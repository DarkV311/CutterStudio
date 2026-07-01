using System.Globalization;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CutterStudio.Models;

namespace CutterStudio.Services;

/// <summary>
/// Creates a printable vector layout with registration marks. The marks are printed
/// with the artwork so the same origin can be used later for contour-cut correction.
/// </summary>
public sealed class PrintCutService : IPrintCutService
{
    public string GeneratePrintableSvg(ArtworkDocument document, CutterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);

        var bounds = document.BoundsMm;
        if (bounds.IsEmpty)
            throw new InvalidOperationException("There is no artwork to export.");

        var margin = Math.Max(2, settings.RegistrationMarkMarginMm);
        var mark = Math.Max(2, settings.RegistrationMarkSizeMm);
        var pageWidth = bounds.Width + margin * 2;
        var pageHeight = bounds.Height + margin * 2;
        var matrix = Matrix.Identity;
        matrix.Translate(-bounds.X + margin, -bounds.Y + margin);

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{pageWidth:0.###}mm\" height=\"{pageHeight:0.###}mm\" viewBox=\"0 0 {pageWidth:0.###} {pageHeight:0.###}\">");
        builder.Append("<rect x=\"0\" y=\"0\" width=\"100%\" height=\"100%\" fill=\"white\"/>");
        builder.Append("<g fill=\"none\" stroke=\"black\" stroke-width=\"0.2\">");
        foreach (var geometry in document.Geometries)
        {
            var transformed = ApplyTransform(geometry, new MatrixTransform(matrix));
            var data = transformed.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute)
                .ToString(CultureInfo.InvariantCulture);
            builder.Append("<path d=\"").Append(SecurityElement.Escape(data)).Append("\"/>");
        }
        builder.Append("</g>");

        builder.Append("<g id=\"registration-marks\" stroke=\"black\" fill=\"black\">");
        DrawMark(builder, margin, margin, mark, settings.RegistrationMarkStyle);
        DrawMark(builder, pageWidth - margin, margin, mark, settings.RegistrationMarkStyle);
        DrawMark(builder, pageWidth - margin, pageHeight - margin, mark, settings.RegistrationMarkStyle);
        DrawMark(builder, margin, pageHeight - margin, mark, settings.RegistrationMarkStyle);
        builder.Append("</g>");
        if (settings.CutContourBox)
        {
            var gap = Math.Max(0, settings.ContourGapMm);
            builder.Append(CultureInfo.InvariantCulture,
                $"<rect id=\"cut-contour-box\" x=\"{margin - gap:0.###}\" y=\"{margin - gap:0.###}\" width=\"{bounds.Width + gap * 2:0.###}\" height=\"{bounds.Height + gap * 2:0.###}\" fill=\"none\" stroke=\"#ff00ff\" stroke-width=\"0.2\" stroke-dasharray=\"1 1\"/>");
        }
        builder.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{margin:0.###}\" y=\"{pageHeight - 2:0.###}\" font-family=\"Arial\" font-size=\"3\" fill=\"black\">Cutter Studio Print &amp; Cut - artwork {bounds.Width / 10:0.##} x {bounds.Height / 10:0.##} cm</text>");
        builder.Append("</svg>");
        return builder.ToString();
    }

    private static void DrawMark(
        StringBuilder builder,
        double x,
        double y,
        double size,
        RegistrationMarkStyle style)
    {
        var half = size / 2;
        if (style == RegistrationMarkStyle.CircleCross)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"<g stroke=\"black\" stroke-width=\"0.35\" fill=\"none\"><line x1=\"{x - half:0.###}\" y1=\"{y:0.###}\" x2=\"{x + half:0.###}\" y2=\"{y:0.###}\"/><line x1=\"{x:0.###}\" y1=\"{y - half:0.###}\" x2=\"{x:0.###}\" y2=\"{y + half:0.###}\"/><circle cx=\"{x:0.###}\" cy=\"{y:0.###}\" r=\"{half:0.###}\"/></g>");
            return;
        }

        // High-contrast corner target: a black square with white center notch.
        // It is easier for low-cost CCD cameras to lock onto than thin outlines.
        builder.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{x - half:0.###}\" y=\"{y - half:0.###}\" width=\"{size:0.###}\" height=\"{size:0.###}\"/>");
        var notch = size * 0.38;
        builder.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{x - notch / 2:0.###}\" y=\"{y - notch / 2:0.###}\" width=\"{notch:0.###}\" height=\"{notch:0.###}\" fill=\"white\"/>");
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
}
