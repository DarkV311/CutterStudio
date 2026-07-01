using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;
using CutterStudio.Models;

namespace CutterStudio.Services;

/// <summary>
/// Imports SVG into WPF geometry while normalizing all coordinates to millimeters.
/// Common cutter artwork primitives, nested transforms, viewBox, and SVG paths are supported.
/// </summary>
public sealed partial class VectorArtworkService : IVectorArtworkService
{
    private const double MillimetersPerPixel = 25.4 / 96.0;

    public async Task<ArtworkDocument> ImportSvgAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var svg = await File.ReadAllTextAsync(filePath, cancellationToken);
        return ParseSvg(svg, System.IO.Path.GetFileNameWithoutExtension(filePath));
    }

    public ArtworkDocument PasteFromClipboard()
    {
        var data = Clipboard.GetDataObject()
                   ?? throw new InvalidOperationException("The clipboard is empty.");

        foreach (var format in new[] { "image/svg+xml", "image/svg+xml;charset=utf-8", "application/x-inkscape-svg" })
        {
            if (!data.GetDataPresent(format))
                continue;
            var svg = ReadClipboardText(data.GetData(format));
            if (!string.IsNullOrWhiteSpace(svg))
                return ParseSvg(svg, "Clipboard");
        }

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = data.GetData(DataFormats.UnicodeText) as string;
            if (text?.Contains("<svg", StringComparison.OrdinalIgnoreCase) == true)
                return ParseSvg(text, "Clipboard");
        }

        foreach (var format in new[] { DataFormats.XamlPackage, DataFormats.Xaml })
        {
            if (!data.GetDataPresent(format))
                continue;

            var xaml = ReadClipboardText(data.GetData(format));
            if (string.IsNullOrWhiteSpace(xaml))
                continue;

            try
            {
                var parsed = XamlReader.Parse(xaml);
                var geometries = new List<Geometry>();
                ExtractXamlGeometry(parsed, geometries);
                if (geometries.Count > 0)
                    return CreateFromGeometries(geometries, "Clipboard");
            }
            catch (XamlParseException)
            {
                // Continue to the actionable unsupported-format message below.
            }
        }

        throw new InvalidOperationException(
            "No supported vector artwork was found. Copy SVG data from the design application, " +
            "copy a WPF/XAML vector, or use Import SVG.");
    }

    public ArtworkDocument ParseSvg(string svg, string sourceName = "Artwork")
    {
        if (string.IsNullOrWhiteSpace(svg))
            throw new InvalidDataException("The SVG file is empty.");

        XDocument xml;
        try
        {
            xml = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or ArgumentException)
        {
            throw new InvalidDataException("The selected file is not valid SVG XML.", ex);
        }

        var root = xml.Root;
        if (root is null || !root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The document does not contain an SVG root element.");

        var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
        var widthMm = ParseLengthMm(root.Attribute("width")?.Value);
        var heightMm = ParseLengthMm(root.Attribute("height")?.Value);

        if (widthMm <= 0 && viewBox.Width > 0)
            widthMm = viewBox.Width * MillimetersPerPixel;
        if (heightMm <= 0 && viewBox.Height > 0)
            heightMm = viewBox.Height * MillimetersPerPixel;
        if (viewBox.IsEmpty)
            viewBox = new Rect(0, 0, widthMm / MillimetersPerPixel, heightMm / MillimetersPerPixel);

        var scaleX = widthMm > 0 && viewBox.Width > 0 ? widthMm / viewBox.Width : MillimetersPerPixel;
        var scaleY = heightMm > 0 && viewBox.Height > 0 ? heightMm / viewBox.Height : MillimetersPerPixel;
        var rootMatrix = Matrix.Identity;
        rootMatrix.Translate(-viewBox.X, -viewBox.Y);
        rootMatrix.Scale(scaleX, scaleY);

        var geometries = new List<Geometry>();
        VisitSvgElement(root, rootMatrix, geometries);
        if (geometries.Count == 0)
            throw new InvalidDataException("The SVG does not contain any supported vector paths or shapes.");

        return BuildDocument(svg, sourceName, geometries);
    }

    public ArtworkDocument Merge(ArtworkDocument current, ArtworkDocument inserted)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(inserted);

        var geometries = current.Geometries
            .Concat(inserted.Geometries)
            .Select(geometry =>
            {
                var copy = geometry.CloneCurrentValue();
                copy.Freeze();
                return copy;
            })
            .ToArray();
        var bounds = UnionBounds(geometries);

        return new ArtworkDocument
        {
            SourceName = current.SourceName,
            SvgData = SerializeGeometrySvg(geometries, bounds),
            Geometries = geometries,
            BoundsMm = bounds
        };
    }

    private static void VisitSvgElement(XElement element, Matrix parentMatrix, ICollection<Geometry> output)
    {
        if (IsHidden(element))
            return;

        var matrix = parentMatrix;
        var local = ParseTransform(element.Attribute("transform")?.Value);
        matrix.Append(local);

        Geometry? geometry = element.Name.LocalName.ToLowerInvariant() switch
        {
            "path" => ParsePath(element),
            "rect" => ParseRectangle(element),
            "circle" => ParseCircle(element),
            "ellipse" => ParseEllipse(element),
            "line" => ParseLine(element),
            "polyline" => ParsePoly(element, false),
            "polygon" => ParsePoly(element, true),
            _ => null
        };

        if (geometry is not null)
        {
            var transformed = ApplyTransform(geometry, new MatrixTransform(matrix));
            transformed.Freeze();
            output.Add(transformed);
        }

        foreach (var child in element.Elements())
            VisitSvgElement(child, matrix, output);
    }

    private static Geometry? ParsePath(XElement element)
    {
        var data = element.Attribute("d")?.Value;
        if (string.IsNullOrWhiteSpace(data))
            return null;

        try
        {
            return Geometry.Parse(data);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("An SVG path contains invalid path data.", ex);
        }
    }

    private static Geometry? ParseRectangle(XElement element)
    {
        var x = Number(element, "x");
        var y = Number(element, "y");
        var width = Number(element, "width");
        var height = Number(element, "height");
        if (width <= 0 || height <= 0)
            return null;
        var radiusX = Math.Max(0, Number(element, "rx"));
        var radiusY = Math.Max(0, Number(element, "ry"));
        if (radiusX > 0 && radiusY == 0) radiusY = radiusX;
        if (radiusY > 0 && radiusX == 0) radiusX = radiusY;
        return new RectangleGeometry(new Rect(x, y, width, height), radiusX, radiusY);
    }

    private static Geometry? ParseCircle(XElement element)
    {
        var radius = Number(element, "r");
        return radius > 0
            ? new EllipseGeometry(new Point(Number(element, "cx"), Number(element, "cy")), radius, radius)
            : null;
    }

    private static Geometry? ParseEllipse(XElement element)
    {
        var radiusX = Number(element, "rx");
        var radiusY = Number(element, "ry");
        return radiusX > 0 && radiusY > 0
            ? new EllipseGeometry(new Point(Number(element, "cx"), Number(element, "cy")), radiusX, radiusY)
            : null;
    }

    private static Geometry ParseLine(XElement element)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(Number(element, "x1"), Number(element, "y1")),
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new LineSegment(
            new Point(Number(element, "x2"), Number(element, "y2")), true));
        return new PathGeometry(new[] { figure });
    }

    private static Geometry? ParsePoly(XElement element, bool closed)
    {
        var values = NumberRegex().Matches(element.Attribute("points")?.Value ?? string.Empty)
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length < 4)
            return null;

        var figure = new PathFigure
        {
            StartPoint = new Point(values[0], values[1]),
            IsClosed = closed,
            IsFilled = closed
        };
        for (var index = 2; index + 1 < values.Length; index += 2)
            figure.Segments.Add(new LineSegment(new Point(values[index], values[index + 1]), true));
        return new PathGeometry(new[] { figure });
    }

    private static Matrix ParseTransform(string? value)
    {
        var result = Matrix.Identity;
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (Match match in TransformRegex().Matches(value))
        {
            var args = NumberRegex().Matches(match.Groups[2].Value)
                .Select(number => double.Parse(number.Value, CultureInfo.InvariantCulture))
                .ToArray();
            var transform = Matrix.Identity;

            switch (match.Groups[1].Value.ToLowerInvariant())
            {
                case "matrix" when args.Length >= 6:
                    transform = new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]);
                    break;
                case "translate" when args.Length >= 1:
                    transform.Translate(args[0], args.Length > 1 ? args[1] : 0);
                    break;
                case "scale" when args.Length >= 1:
                    transform.Scale(args[0], args.Length > 1 ? args[1] : args[0]);
                    break;
                case "rotate" when args.Length >= 1:
                    if (args.Length >= 3)
                        transform.RotateAt(args[0], args[1], args[2]);
                    else
                        transform.Rotate(args[0]);
                    break;
                case "skewx" when args.Length >= 1:
                    transform.Skew(args[0], 0);
                    break;
                case "skewy" when args.Length >= 1:
                    transform.Skew(0, args[0]);
                    break;
            }
            result.Append(transform);
        }
        return result;
    }

    private static ArtworkDocument CreateFromGeometries(IReadOnlyList<Geometry> geometries, string sourceName)
    {
        var normalized = geometries.Select(geometry =>
        {
            var copy = geometry.CloneCurrentValue();
            var transform = new ScaleTransform(MillimetersPerPixel, MillimetersPerPixel);
            var result = ApplyTransform(copy, transform);
            result.Freeze();
            return result;
        }).ToArray();

        var bounds = UnionBounds(normalized);
        var svg = SerializeGeometrySvg(normalized, bounds);
        return BuildDocument(svg, sourceName, normalized);
    }

    private static ArtworkDocument BuildDocument(
        string svg,
        string sourceName,
        IReadOnlyList<Geometry> geometries) =>
        new()
        {
            SvgData = svg,
            SourceName = sourceName,
            Geometries = geometries,
            BoundsMm = UnionBounds(geometries)
        };

    private static Rect UnionBounds(IEnumerable<Geometry> geometries)
    {
        var bounds = Rect.Empty;
        foreach (var geometry in geometries)
            bounds.Union(geometry.Bounds);
        return bounds;
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

    private static string SerializeGeometrySvg(IReadOnlyList<Geometry> geometries, Rect bounds)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{bounds.Width:0.###}mm\" height=\"{bounds.Height:0.###}mm\" viewBox=\"{bounds.X:0.###} {bounds.Y:0.###} {bounds.Width:0.###} {bounds.Height:0.###}\">");
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

    private static void ExtractXamlGeometry(object? value, ICollection<Geometry> output)
    {
        switch (value)
        {
            case System.Windows.Shapes.Path path when path.Data is not null:
                output.Add(path.Data.CloneCurrentValue());
                break;
            case Shape shape:
                output.Add(shape.RenderedGeometry.CloneCurrentValue());
                break;
            case Geometry geometry:
                output.Add(geometry.CloneCurrentValue());
                break;
            case GeometryDrawing drawing:
                output.Add(drawing.Geometry.CloneCurrentValue());
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                    ExtractXamlGeometry(child, output);
                break;
            case FrameworkElement element:
                foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<object>())
                    ExtractXamlGeometry(child, output);
                break;
        }
    }

    private static string? ReadClipboardText(object? value) => value switch
    {
        string text => text,
        MemoryStream stream => Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\0'),
        byte[] bytes => Encoding.UTF8.GetString(bytes).TrimEnd('\0'),
        _ => null
    };

    private static bool IsHidden(XElement element)
    {
        var display = element.Attribute("display")?.Value;
        var visibility = element.Attribute("visibility")?.Value;
        var style = element.Attribute("style")?.Value ?? string.Empty;
        return display?.Equals("none", StringComparison.OrdinalIgnoreCase) == true
               || visibility?.Equals("hidden", StringComparison.OrdinalIgnoreCase) == true
               || style.Contains("display:none", StringComparison.OrdinalIgnoreCase);
    }

    private static double Number(XElement element, string attribute)
    {
        var value = element.Attribute(attribute)?.Value;
        var match = NumberRegex().Match(value ?? string.Empty);
        return match.Success
            ? double.Parse(match.Value, CultureInfo.InvariantCulture)
            : 0;
    }

    private static Rect ParseViewBox(string? value)
    {
        var numbers = NumberRegex().Matches(value ?? string.Empty)
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        return numbers.Length >= 4 && numbers[2] > 0 && numbers[3] > 0
            ? new Rect(numbers[0], numbers[1], numbers[2], numbers[3])
            : Rect.Empty;
    }

    private static double ParseLengthMm(string? value)
    {
        var match = LengthRegex().Match(value ?? string.Empty);
        if (!match.Success)
            return 0;
        var number = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value.ToLowerInvariant() switch
        {
            "mm" => number,
            "cm" => number * 10,
            "in" => number * 25.4,
            "pt" => number * 25.4 / 72,
            "pc" => number * 25.4 / 6,
            "q" => number * 0.25,
            _ => number * MillimetersPerPixel
        };
    }

    [GeneratedRegex(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"([a-zA-Z]+)\s*\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformRegex();

    [GeneratedRegex(@"^\s*([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*(px|mm|cm|in|pt|pc|q)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();
}
