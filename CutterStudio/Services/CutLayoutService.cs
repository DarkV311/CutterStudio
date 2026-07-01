using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CutterStudio.Models;

namespace CutterStudio.Services;

/// <summary>
/// Builds the exact visual arrangement that will be sent to the cutter.
/// It includes transforms, copies, nesting, weed borders, and the material boundary.
/// </summary>
public sealed class CutLayoutService : ICutLayoutService
{
    public CutLayoutMetrics CalculateMetrics(ArtworkDocument source, CutterSettings settings)
    {
        var preview = CreatePreview(source, settings);
        var artwork = preview.ArtworkBoundsMm ?? Rect.Empty;
        var margin = Math.Max(0, settings.VinylMarginMm);
        var vinylWidth = Math.Min(settings.MaterialWidthMm, artwork.Width + margin * 2);
        var vinylLength = artwork.Height + margin * 2;
        return new CutLayoutMetrics(
            artwork.Width,
            artwork.Height,
            Math.Max(1, vinylWidth),
            Math.Max(1, vinylLength),
            settings.MaterialWidthMm);
    }

    public ArtworkDocument CreatePreview(ArtworkDocument source, CutterSettings settings)
    {
        var scale = Math.Clamp(settings.ScalePercent, 1, 1000) / 100.0;
        var copies = Math.Clamp(settings.Copies, 1, 999);
        var spacing = Math.Max(0, settings.CopySpacingMm);
        var materialWidth = Math.Max(1, settings.MaterialWidthMm);

        var baseMatrix = CreateArtworkTransform(source.BoundsMm, settings, scale);
        var itemBounds = TransformBounds(source.BoundsMm, baseMatrix);
        baseMatrix.Translate(-itemBounds.X, -itemBounds.Y);
        itemBounds = new Rect(0, 0, itemBounds.Width, itemBounds.Height);

        if (itemBounds.Width > materialWidth)
            throw new InvalidOperationException(
                $"Artwork width {itemBounds.Width:0.##} mm exceeds material width {materialWidth:0.##} mm.");

        var positions = ArrangeCopies(
            copies,
            itemBounds.Width,
            itemBounds.Height,
            spacing,
            materialWidth,
            settings.AutomaticNesting);
        var offsetX = Math.Max(0, settings.OffsetXmm);
        var offsetY = Math.Max(0, settings.OffsetYmm);
        var layoutWidth = positions.Max(point => point.X) + itemBounds.Width;
        if (offsetX + layoutWidth > materialWidth)
            offsetX = Math.Max(0, materialWidth - layoutWidth);

        var geometries = new List<Geometry>();
        var contentBounds = Rect.Empty;
        foreach (var position in positions)
        {
            var matrix = baseMatrix;
            matrix.Translate(position.X + offsetX, position.Y + offsetY);
            ApplyContourCorrection(ref matrix, settings);
            foreach (var sourceGeometry in source.Geometries)
            {
                var geometry = ApplyTransform(sourceGeometry, new MatrixTransform(matrix));
                geometry.Freeze();
                geometries.Add(geometry);
                contentBounds.Union(geometry.Bounds);
            }

            if (settings.WeedBorder)
            {
                var margin = Math.Max(0, settings.WeedBorderMarginMm);
                var border = new RectangleGeometry(new Rect(
                    position.X + offsetX - margin,
                    position.Y + offsetY - margin,
                    itemBounds.Width + margin * 2,
                    itemBounds.Height + margin * 2));
                if (settings.ContourCorrectionEnabled)
                {
                    var correction = CreateContourCorrectionMatrix(settings, border.Bounds);
                    border.Transform = new MatrixTransform(correction);
                }
                border.Freeze();
                geometries.Add(border);
                contentBounds.Union(border.Bounds);
            }
        }

        if (settings.CutContourBox && !contentBounds.IsEmpty)
        {
            var gap = Math.Max(0, settings.ContourGapMm);
            var contour = new RectangleGeometry(new Rect(
                contentBounds.X - gap,
                contentBounds.Y - gap,
                contentBounds.Width + gap * 2,
                contentBounds.Height + gap * 2));
            contour.Freeze();
            geometries.Add(contour);
            contentBounds.Union(contour.Bounds);
        }

        var usedHeight = Math.Max(itemBounds.Height, contentBounds.Bottom) + 10;
        var materialBounds = new Rect(0, 0, materialWidth, usedHeight);

        return new ArtworkDocument
        {
            SourceName = $"{source.SourceName} cut preview",
            SvgData = SerializePreview(geometries, materialWidth, usedHeight),
            Geometries = geometries,
            BoundsMm = materialBounds,
            MaterialBoundsMm = materialBounds,
            ArtworkBoundsMm = contentBounds
        };
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
        var positions = new List<Point>(count);
        var perRow = automaticNesting
            ? Math.Max(1, (int)Math.Floor((materialWidth + spacing) / (width + spacing)))
            : 1;

        for (var index = 0; index < count; index++)
        {
            var column = index % perRow;
            var row = index / perRow;
            positions.Add(new Point(column * (width + spacing), row * (height + spacing)));
        }
        return positions;
    }

    private static void ApplyContourCorrection(ref Matrix matrix, CutterSettings settings)
    {
        if (!settings.ContourCorrectionEnabled)
            return;
        var correction = CreateContourCorrectionMatrix(settings, Rect.Empty);
        matrix.Append(correction);
    }

    private static Matrix CreateContourCorrectionMatrix(CutterSettings settings, Rect centerBounds)
    {
        var matrix = Matrix.Identity;
        var scaleX = Math.Clamp(settings.ContourScaleXPercent, 1, 1000) / 100.0;
        var scaleY = Math.Clamp(settings.ContourScaleYPercent, 1, 1000) / 100.0;
        if (!centerBounds.IsEmpty)
        {
            matrix.Translate(-centerBounds.X - centerBounds.Width / 2, -centerBounds.Y - centerBounds.Height / 2);
            matrix.Scale(scaleX, scaleY);
            matrix.Rotate(settings.ContourRotationDeg);
            matrix.Translate(centerBounds.X + centerBounds.Width / 2, centerBounds.Y + centerBounds.Height / 2);
        }
        else
        {
            matrix.Scale(scaleX, scaleY);
            matrix.Rotate(settings.ContourRotationDeg);
        }
        matrix.Translate(settings.ContourOffsetXmm, settings.ContourOffsetYmm);
        return matrix;
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

    private static string SerializePreview(IEnumerable<Geometry> geometries, double width, double height)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width:0.###}mm\" height=\"{height:0.###}mm\" viewBox=\"0 0 {width:0.###} {height:0.###}\">");
        foreach (var geometry in geometries)
        {
            var data = geometry.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute)
                .ToString(CultureInfo.InvariantCulture);
            builder.Append("<path fill=\"none\" stroke=\"black\" d=\"")
                .Append(System.Security.SecurityElement.Escape(data))
                .Append("\"/>");
        }
        builder.Append("</svg>");
        return builder.ToString();
    }
}
