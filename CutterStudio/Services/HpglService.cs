using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CutterStudio.Models;

namespace CutterStudio.Services;

/// <summary>
/// Converts flattened vector contours to standard absolute-coordinate HPGL.
/// Plotter resolution is 40 units/mm, used by common desktop vinyl cutters.
/// </summary>
public sealed class HpglService : IHpglService
{
    public HpglJob GenerateAreaTest(ArtworkDocument document, CutterSettings settings, AreaTestMode mode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);

        var preview = new CutLayoutService().CreatePreview(document, settings);
        var bounds = preview.ArtworkBoundsMm
                     ?? throw new InvalidOperationException("The artwork bounds could not be calculated.");
        var units = Math.Clamp(settings.UnitsPerMm, 1, 1000);
        var isDmpl = settings.CutterProfile.Contains("DMPL", StringComparison.OrdinalIgnoreCase);
        var speed = Math.Clamp(settings.Speed, 1, 1000);
        var pressure = Math.Clamp(settings.Pressure, 1, 1000);
        var builder = new StringBuilder();

        if (isDmpl)
            builder.Append(CultureInfo.InvariantCulture, $";:H A L0 ECN U U0,0;U0,0;FS{pressure};VS{speed};");
        else
            builder.Append(CultureInfo.InvariantCulture, $"IN;VS{speed};FS{pressure};PA;");

        var points = new[]
        {
            bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft, bounds.TopLeft
        };

        WritePenUp(builder, points[0], isDmpl, units);
        var cutDistance = 0.0;
        var travelDistance = Distance(new Point(0, 0), points[0]);

        if (mode == AreaTestMode.BladeDown)
        {
            builder.Append(isDmpl ? "D" : "PD");
            for (var index = 1; index < points.Length; index++)
            {
                AppendCoordinate(builder, points[index], units);
                builder.Append(index == points.Length - 1 ? ';' : ',');
                cutDistance += Distance(points[index - 1], points[index]);
            }
        }
        else
        {
            for (var index = 1; index < points.Length; index++)
            {
                WritePenUp(builder, points[index], isDmpl, units);
                travelDistance += Distance(points[index - 1], points[index]);
            }
        }

        builder.Append(isDmpl ? "U0,0;@;" : "PU0,0;");
        travelDistance += Distance(points[^1], new Point(0, 0));

        var duration = TimeSpan.FromSeconds(
            cutDistance / Math.Max(1, speed)
            + travelDistance / 150.0);
        return new HpglJob(
            builder.ToString(),
            cutDistance,
            travelDistance,
            mode == AreaTestMode.BladeDown ? 1 : 0,
            duration);
    }

    public HpglJob Generate(ArtworkDocument document, CutterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        if (document.Geometries.Count == 0)
            throw new InvalidOperationException("There is no artwork to cut.");

        var speed = Math.Clamp(settings.Speed, 1, 100);
        var pressure = Math.Clamp(settings.Pressure, 1, 255);
        var passes = Math.Clamp(settings.Passes, 1, 20);
        var copies = Math.Clamp(settings.Copies, 1, 999);
        var scale = Math.Clamp(settings.ScalePercent, 1, 1000) / 100.0;
        var spacing = Math.Max(0, settings.CopySpacingMm);
        var materialWidth = Math.Max(1, settings.MaterialWidthMm);

        var baseMatrix = CreateArtworkTransform(document.BoundsMm, settings, scale);
        var transformedBounds = TransformBounds(document.BoundsMm, baseMatrix);
        baseMatrix.Translate(-transformedBounds.X, -transformedBounds.Y);
        transformedBounds = new Rect(0, 0, transformedBounds.Width, transformedBounds.Height);

        var copyPositions = ArrangeCopies(
            copies,
            transformedBounds.Width,
            transformedBounds.Height,
            spacing,
            materialWidth,
            settings.AutomaticNesting);
        var offsetX = Math.Max(0, settings.OffsetXmm);
        var offsetY = Math.Max(0, settings.OffsetYmm);
        var layoutWidth = copyPositions.Max(point => point.X) + transformedBounds.Width;
        if (offsetX + layoutWidth > materialWidth)
            offsetX = Math.Max(0, materialWidth - layoutWidth);

        var isBascocutDmpl = settings.CutterProfile.Contains("DMPL", StringComparison.OrdinalIgnoreCase);
        var builder = new StringBuilder(capacity: 4096);
        if (isBascocutDmpl)
        {
            // Exact Bascocut/Teneth initialization family used by SignMaster's TN_Tool2CCD driver.
            builder.Append(";:H A L0 ECN U U0,0;U0,0;");
            builder.Append(CultureInfo.InvariantCulture, $"FS{pressure};VS{speed};");
        }
        else
        {
            builder.Append("IN;");
            builder.Append(CultureInfo.InvariantCulture, $"VS{speed};FS{pressure};PA;");
        }

        var cuttingDistance = 0.0;
        var travelDistance = 0.0;
        var penLifts = 0;
        Point? currentPoint = null;

        foreach (var position in copyPositions)
        {
            var copyMatrix = baseMatrix;
            copyMatrix.Translate(position.X + offsetX, position.Y + offsetY);
            ApplyContourCorrection(ref copyMatrix, settings);

            for (var pass = 0; pass < passes; pass++)
            {
                foreach (var sourceGeometry in document.Geometries)
                {
                    var geometry = ApplyTransform(sourceGeometry, new MatrixTransform(copyMatrix));
                    var flattened = geometry.GetFlattenedPathGeometry(0.15, ToleranceType.Absolute);
                    WriteGeometry(
                        builder,
                        flattened,
                        isBascocutDmpl,
                        ref currentPoint,
                        ref cuttingDistance,
                        ref travelDistance,
                        ref penLifts,
                        Math.Clamp(settings.UnitsPerMm, 1, 1000));
                }

                if (settings.WeedBorder)
                {
                    var margin = Math.Max(0, settings.WeedBorderMarginMm);
                    var border = new Rect(
                        position.X + offsetX - margin,
                        position.Y + offsetY - margin,
                        transformedBounds.Width + margin * 2,
                        transformedBounds.Height + margin * 2);
                    if (settings.ContourCorrectionEnabled)
                        border = TransformBounds(border, CreateContourCorrectionMatrix(settings));
                    WriteBorder(
                        builder,
                        border,
                        isBascocutDmpl,
                        ref currentPoint,
                        ref cuttingDistance,
                        ref travelDistance,
                        ref penLifts,
                        Math.Clamp(settings.UnitsPerMm, 1, 1000));
                }
            }
        }

        if (settings.CutContourBox)
        {
            var jobBounds = GetPreviewBounds(document, settings);
            var gap = Math.Max(0, settings.ContourGapMm);
            var contour = new Rect(
                Math.Max(0, jobBounds.X - gap),
                Math.Max(0, jobBounds.Y - gap),
                jobBounds.Width + gap * 2,
                jobBounds.Height + gap * 2);
            WriteBorder(
                builder,
                contour,
                isBascocutDmpl,
                ref currentPoint,
                ref cuttingDistance,
                ref travelDistance,
                ref penLifts,
                Math.Clamp(settings.UnitsPerMm, 1, 1000));
        }

        builder.Append(isBascocutDmpl ? "U0,0;@;" : "PU0,0;");
        if (currentPoint is { } last)
            travelDistance += Distance(last, new Point(0, 0));

        // Bascocut/Teneth defines VS directly in mm/sec. Generic HPGL commonly uses cm/sec.
        var cuttingMmPerSecond = isBascocutDmpl ? speed : speed * 10.0;
        var travelMmPerSecond = isBascocutDmpl
            ? Math.Max(120.0, cuttingMmPerSecond * 1.8)
            : cuttingMmPerSecond * 1.5;
        var seconds = cuttingDistance / cuttingMmPerSecond
                      + travelDistance / travelMmPerSecond
                      + penLifts * 0.08
                      + copies * passes * 0.2;
        return new HpglJob(
            builder.ToString(),
            cuttingDistance,
            travelDistance,
            penLifts,
            TimeSpan.FromSeconds(seconds));
    }

    private static Matrix CreateArtworkTransform(Rect bounds, CutterSettings settings, double scale)
    {
        var matrix = Matrix.Identity;
        matrix.Translate(-bounds.X, -bounds.Y);
        matrix.Scale(scale, scale);

        var width = bounds.Width * scale;
        var height = bounds.Height * scale;
        if (settings.Mirror)
        {
            matrix.Scale(-1, 1);
            matrix.Translate(width, 0);
        }
        if (settings.Rotate90)
        {
            matrix.Rotate(90);
            matrix.Translate(height, 0);
        }
        return matrix;
    }

    private static IReadOnlyList<Point> ArrangeCopies(
        int count,
        double width,
        double height,
        double spacing,
        double materialWidth,
        bool automaticNesting)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Artwork dimensions must be greater than zero.");
        if (width > materialWidth)
            throw new InvalidOperationException(
                $"The transformed artwork is {width:0.##} mm wide, exceeding the {materialWidth:0.##} mm material width.");

        var points = new List<Point>(count);
        if (!automaticNesting)
        {
            for (var index = 0; index < count; index++)
                points.Add(new Point(0, index * (height + spacing)));
            return points;
        }

        var perRow = Math.Max(1, (int)Math.Floor((materialWidth + spacing) / (width + spacing)));
        for (var index = 0; index < count; index++)
        {
            var column = index % perRow;
            var row = index / perRow;
            points.Add(new Point(column * (width + spacing), row * (height + spacing)));
        }
        return points;
    }

    private static void ApplyContourCorrection(ref Matrix matrix, CutterSettings settings)
    {
        if (!settings.ContourCorrectionEnabled)
            return;
        matrix.Append(CreateContourCorrectionMatrix(settings));
    }

    private static Matrix CreateContourCorrectionMatrix(CutterSettings settings)
    {
        var matrix = Matrix.Identity;
        matrix.Scale(
            Math.Clamp(settings.ContourScaleXPercent, 1, 1000) / 100.0,
            Math.Clamp(settings.ContourScaleYPercent, 1, 1000) / 100.0);
        matrix.Rotate(settings.ContourRotationDeg);
        matrix.Translate(settings.ContourOffsetXmm, settings.ContourOffsetYmm);
        return matrix;
    }

    private static void WriteGeometry(
        StringBuilder builder,
        PathGeometry geometry,
        bool dmpl,
        ref Point? currentPoint,
        ref double cuttingDistance,
        ref double travelDistance,
        ref int penLifts,
        double unitsPerMm)
    {
        foreach (var figure in geometry.Figures)
        {
            var points = new List<Point> { figure.StartPoint };
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case PolyLineSegment polyLine:
                        points.AddRange(polyLine.Points);
                        break;
                    case LineSegment line:
                        points.Add(line.Point);
                        break;
                }
            }
            if (figure.IsClosed && points.Count > 1 && points[^1] != points[0])
                points.Add(points[0]);
            if (points.Count < 2)
                continue;

            WritePenUp(builder, points[0], dmpl, unitsPerMm);
            penLifts++;
            if (currentPoint is { } previous)
                travelDistance += Distance(previous, points[0]);

            builder.Append(dmpl ? "D" : "PD");
            for (var index = 1; index < points.Count; index++)
            {
                AppendCoordinate(builder, points[index], unitsPerMm);
                builder.Append(index == points.Count - 1 ? ';' : ',');
                cuttingDistance += Distance(points[index - 1], points[index]);
            }
            currentPoint = points[^1];
        }
    }

    private static void WriteBorder(
        StringBuilder builder,
        Rect border,
        bool dmpl,
        ref Point? currentPoint,
        ref double cuttingDistance,
        ref double travelDistance,
        ref int penLifts,
        double unitsPerMm)
    {
        var points = new[]
        {
            border.TopLeft, border.TopRight, border.BottomRight, border.BottomLeft, border.TopLeft
        };
        WritePenUp(builder, points[0], dmpl, unitsPerMm);
        penLifts++;
        if (currentPoint is { } previous)
            travelDistance += Distance(previous, points[0]);
        builder.Append(dmpl ? "D" : "PD");
        for (var index = 1; index < points.Length; index++)
        {
            AppendCoordinate(builder, points[index], unitsPerMm);
            builder.Append(index == points.Length - 1 ? ';' : ',');
            cuttingDistance += Distance(points[index - 1], points[index]);
        }
        currentPoint = points[^1];
    }

    private static void WritePenUp(StringBuilder builder, Point point, bool dmpl, double unitsPerMm)
    {
        builder.Append(dmpl ? "U" : "PU");
        AppendCoordinate(builder, point, unitsPerMm);
        builder.Append(';');
    }

    private static void AppendCoordinate(StringBuilder builder, Point point, double unitsPerMm)
    {
        var x = Math.Max(0, (int)Math.Round(point.X * unitsPerMm));
        var y = Math.Max(0, (int)Math.Round(point.Y * unitsPerMm));
        builder.Append(CultureInfo.InvariantCulture, $"{x},{y}");
    }

    private static Rect TransformBounds(Rect bounds, Matrix matrix)
    {
        var points = new[]
        {
            matrix.Transform(bounds.TopLeft),
            matrix.Transform(bounds.TopRight),
            matrix.Transform(bounds.BottomLeft),
            matrix.Transform(bounds.BottomRight)
        };
        return new Rect(
            new Point(points.Min(point => point.X), points.Min(point => point.Y)),
            new Point(points.Max(point => point.X), points.Max(point => point.Y)));
    }

    private static Geometry ApplyTransform(Geometry source, Transform transform)
    {
        var copy = source.CloneCurrentValue();
        var group = new TransformGroup();
        if (copy.Transform is not null && !copy.Transform.Value.IsIdentity)
            group.Children.Add(copy.Transform.CloneCurrentValue());
        group.Children.Add(transform);
        copy.Transform = group;
        return copy;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Rect GetPreviewBounds(ArtworkDocument document, CutterSettings settings)
    {
        var preview = new CutLayoutService().CreatePreview(document, settings);
        return preview.ArtworkBoundsMm ?? Rect.Empty;
    }
}
