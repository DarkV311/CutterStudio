using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CutterStudio.Models;

namespace CutterStudio.Controls;

/// <summary>
/// SignMaster-style visual showing the cutter, the minimum vinyl piece, artwork,
/// and physical dimensions. This is a preview only; cut geometry remains unchanged.
/// </summary>
public sealed class CutterMachinePreview : FrameworkElement
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(ArtworkDocument), typeof(CutterMachinePreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MetricsProperty = DependencyProperty.Register(
        nameof(Metrics), typeof(CutLayoutMetrics), typeof(CutterMachinePreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProfileNameProperty = DependencyProperty.Register(
        nameof(ProfileName), typeof(string), typeof(CutterMachinePreview),
        new FrameworkPropertyMetadata("Cutter", FrameworkPropertyMetadataOptions.AffectsRender));

    public ArtworkDocument? Document
    {
        get => (ArtworkDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public CutLayoutMetrics? Metrics
    {
        get => (CutLayoutMetrics?)GetValue(MetricsProperty);
        set => SetValue(MetricsProperty, value);
    }

    public string ProfileName
    {
        get => (string)GetValue(ProfileNameProperty);
        set => SetValue(ProfileNameProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(235, 237, 240)), null,
            new Rect(0, 0, ActualWidth, ActualHeight));
        if (Document is null || Metrics is null)
            return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawCenteredText(dc, $"{ProfileName}   {Metrics.CutterWidthMm / 10:0.##} cm",
            18, Brushes.Black, new Point(ActualWidth / 2, 28), dpi);

        var machineLeft = 70.0;
        var machineRight = ActualWidth - 105;
        var machineTop = 95.0;
        var machineWidth = Math.Max(250, machineRight - machineLeft);

        var bodyBrush = new LinearGradientBrush(
            Color.FromRgb(218, 225, 232), Color.FromRgb(151, 164, 177), 90);
        dc.DrawRoundedRectangle(bodyBrush, new Pen(Brushes.Gray, 1),
            new Rect(machineLeft, machineTop, machineWidth, 72), 9, 9);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(45, 50, 55)), null,
            new Rect(machineLeft + 25, machineTop + 25, machineWidth - 50, 15));

        for (var index = 0; index < 5; index++)
        {
            var x = machineLeft + 70 + index * (machineWidth - 140) / 4;
            dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(x, machineTop + 18, 22, 26), 3, 3);
        }

        var panel = new Rect(machineRight - 26, machineTop + 8, 34, 58);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(26, 35, 48)), Brushes.DarkSlateGray is SolidColorBrush
            ? new Pen(Brushes.DarkSlateGray, 1) : null, panel, 4, 4);
        dc.DrawRectangle(Brushes.DodgerBlue, null, new Rect(panel.X + 7, panel.Y + 7, 20, 13));

        var maxVinylWidth = machineWidth - 120;
        var vinylPixelWidth = maxVinylWidth * Math.Min(1, Metrics.VinylWidthMm / Metrics.CutterWidthMm);
        var vinylLeft = machineLeft + 55;
        var vinylTop = machineTop + 47;
        var vinylHeight = Math.Clamp(vinylPixelWidth * Metrics.VinylLengthMm / Metrics.VinylWidthMm, 75, 235);
        var vinylRect = new Rect(vinylLeft, vinylTop, vinylPixelWidth, vinylHeight);
        dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(205, 208, 213)), 1), vinylRect);

        DrawArtwork(dc, vinylRect);
        DrawDimensions(dc, vinylRect, dpi);
    }

    private void DrawArtwork(DrawingContext dc, Rect vinylRect)
    {
        if (Document?.ArtworkBoundsMm is not { } artworkBounds || artworkBounds.IsEmpty)
            return;

        var margin = 12.0;
        var target = new Rect(
            vinylRect.X + margin,
            vinylRect.Y + margin,
            Math.Max(1, vinylRect.Width - margin * 2),
            Math.Max(1, vinylRect.Height - margin * 2));
        var scale = Math.Min(target.Width / artworkBounds.Width, target.Height / artworkBounds.Height);
        var matrix = Matrix.Identity;
        matrix.Translate(-artworkBounds.X, -artworkBounds.Y);
        matrix.Scale(scale, scale);
        matrix.Translate(
            target.X + (target.Width - artworkBounds.Width * scale) / 2,
            target.Y + (target.Height - artworkBounds.Height * scale) / 2);

        dc.PushTransform(new MatrixTransform(matrix));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(40, 45, 52)), 1 / scale);
        foreach (var geometry in Document.Geometries)
            dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }

    private void DrawDimensions(DrawingContext dc, Rect vinyl, double dpi)
    {
        var pen = new Pen(Brushes.Black, 1.3);
        var widthY = vinyl.Bottom + 34;
        dc.DrawLine(pen, new Point(vinyl.Left, vinyl.Bottom + 6), new Point(vinyl.Left, widthY + 9));
        dc.DrawLine(pen, new Point(vinyl.Right, vinyl.Bottom + 6), new Point(vinyl.Right, widthY + 9));
        DrawArrowLine(dc, pen, new Point(vinyl.Left, widthY), new Point(vinyl.Right, widthY));
        DrawCenteredText(dc, $"{Metrics!.VinylWidthMm / 10:0.##} cm",
            15, Brushes.Black, new Point(vinyl.Left + vinyl.Width / 2, widthY + 18), dpi);

        var heightX = vinyl.Right + 35;
        dc.DrawLine(pen, new Point(vinyl.Right + 6, vinyl.Top), new Point(heightX + 9, vinyl.Top));
        dc.DrawLine(pen, new Point(vinyl.Right + 6, vinyl.Bottom), new Point(heightX + 9, vinyl.Bottom));
        DrawArrowLine(dc, pen, new Point(heightX, vinyl.Top), new Point(heightX, vinyl.Bottom));
        DrawCenteredText(dc, $"{Metrics.VinylLengthMm / 10:0.##} cm",
            15, Brushes.Black, new Point(heightX + 30, vinyl.Top + vinyl.Height / 2), dpi, 90);
    }

    private static void DrawArrowLine(DrawingContext dc, Pen pen, Point start, Point end)
    {
        dc.DrawLine(pen, start, end);
        if (Math.Abs(start.Y - end.Y) < 0.1)
        {
            dc.DrawLine(pen, start, new Point(start.X + 8, start.Y - 5));
            dc.DrawLine(pen, start, new Point(start.X + 8, start.Y + 5));
            dc.DrawLine(pen, end, new Point(end.X - 8, end.Y - 5));
            dc.DrawLine(pen, end, new Point(end.X - 8, end.Y + 5));
        }
        else
        {
            dc.DrawLine(pen, start, new Point(start.X - 5, start.Y + 8));
            dc.DrawLine(pen, start, new Point(start.X + 5, start.Y + 8));
            dc.DrawLine(pen, end, new Point(end.X - 5, end.Y - 8));
            dc.DrawLine(pen, end, new Point(end.X + 5, end.Y - 8));
        }
    }

    private static void DrawCenteredText(
        DrawingContext dc, string text, double size, Brush brush, Point center, double dpi, double rotation = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, dpi);
        dc.PushTransform(new RotateTransform(rotation, center.X, center.Y));
        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
        dc.Pop();
    }
}
