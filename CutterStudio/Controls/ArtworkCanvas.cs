using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CutterStudio.Models;

namespace CutterStudio.Controls;

public sealed class ArtworkMovedEventArgs(double deltaXmm, double deltaYmm) : EventArgs
{
    public double DeltaXmm { get; } = deltaXmm;
    public double DeltaYmm { get; } = deltaYmm;
}

/// <summary>
/// Lightweight retained-mode vector preview with mouse-wheel zoom, pointer-centered
/// zooming, middle/right-button panning, fit-to-screen, and whole-artwork selection.
/// </summary>
public sealed class ArtworkCanvas : FrameworkElement
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(ArtworkDocument),
        typeof(ArtworkCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, DocumentChanged));

    private double _zoom = 1;
    private Vector _pan;
    private Point _dragStart;
    private Vector _panStart;
    private bool _isPanning;
    private bool _isMovingArtwork;
    private bool _isSelected;
    private Point _moveStart;
    private Vector _moveOffsetMm;

    public ArtworkDocument? Document
    {
        get => (ArtworkDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom => _zoom;
    public event EventHandler? ZoomChanged;
    public event EventHandler<ArtworkMovedEventArgs>? ArtworkMoved;

    public ArtworkCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
        Loaded += (_, _) => FitToScreen();
        SizeChanged += (_, _) =>
        {
            if (Document is not null && _zoom <= 0)
                FitToScreen();
        };
    }

    public void FitToScreen()
    {
        if (Document is null || Document.BoundsMm.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        const double margin = 55;
        var availableWidth = Math.Max(1, ActualWidth - margin * 2);
        var availableHeight = Math.Max(1, ActualHeight - margin * 2);
        _zoom = Math.Clamp(
            Math.Min(availableWidth / Document.BoundsMm.Width, availableHeight / Document.BoundsMm.Height),
            0.02,
            100);
        _pan = default;
        NotifyZoomChanged();
    }

    public void ZoomBy(double factor)
    {
        SetZoomAround(new Point(ActualWidth / 2, ActualHeight / 2), _zoom * factor);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 14, 18)), null,
            new Rect(0, 0, ActualWidth, ActualHeight));
        DrawGrid(drawingContext);

        if (Document is null)
        {
            var text = new FormattedText(
                "Paste vector artwork or import an SVG",
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                16,
                new SolidColorBrush(Color.FromRgb(135, 143, 158)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(text,
                new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
            return;
        }

        var matrix = GetViewMatrix();
        drawingContext.PushTransform(new MatrixTransform(matrix));

        if (Document.MaterialBoundsMm is { } materialBounds)
        {
            var materialFill = new SolidColorBrush(Color.FromArgb(28, 145, 154, 168));
            var materialPen = new Pen(new SolidColorBrush(Color.FromRgb(92, 101, 116)), 1 / _zoom)
            {
                DashStyle = new DashStyle(new[] { 6 / _zoom, 4 / _zoom }, 0)
            };
            drawingContext.DrawRectangle(materialFill, materialPen, materialBounds);
        }

        drawingContext.PushTransform(new TranslateTransform(_moveOffsetMm.X, _moveOffsetMm.Y));
        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(225, 232, 239)), 1.15 / _zoom);
        stroke.Freeze();
        foreach (var geometry in Document.Geometries)
            drawingContext.DrawGeometry(Brushes.Transparent, stroke, geometry);

        if (_isSelected)
        {
            var selectionPen = new Pen(new SolidColorBrush(Color.FromRgb(48, 183, 163)), 1.5 / _zoom)
            {
                DashStyle = new DashStyle(new[] { 5 / _zoom, 3 / _zoom }, 0)
            };
            drawingContext.DrawRectangle(null, selectionPen, Document.ArtworkBoundsMm ?? Document.BoundsMm);
        }
        drawingContext.Pop();
        drawingContext.Pop();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        SetZoomAround(e.GetPosition(this), _zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _isPanning = true;
            _dragStart = e.GetPosition(this);
            _panStart = _pan;
            Cursor = Cursors.Hand;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && Document is not null)
        {
            var inverse = GetViewMatrix();
            if (inverse.HasInverse)
            {
                inverse.Invert();
                var point = inverse.Transform(e.GetPosition(this));
                var artworkBounds = Document.ArtworkBoundsMm ?? Document.BoundsMm;
                _isSelected = artworkBounds.Contains(point);
                if (_isSelected && Document.MaterialBoundsMm is not null)
                {
                    _isMovingArtwork = true;
                    _moveStart = e.GetPosition(this);
                    _moveOffsetMm = default;
                    Cursor = Cursors.SizeAll;
                    CaptureMouse();
                }
                InvalidateVisual();
                e.Handled = _isSelected;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isPanning)
        {
            if (!_isMovingArtwork)
                return;
            var delta = e.GetPosition(this) - _moveStart;
            _moveOffsetMm = new Vector(delta.X / _zoom, delta.Y / _zoom);
            InvalidateVisual();
            return;
        }
        var current = e.GetPosition(this);
        _pan = _panStart + (current - _dragStart);
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_isMovingArtwork)
        {
            _isMovingArtwork = false;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
            var delta = _moveOffsetMm;
            _moveOffsetMm = default;
            if (Math.Abs(delta.X) > 0.01 || Math.Abs(delta.Y) > 0.01)
                ArtworkMoved?.Invoke(this, new ArtworkMovedEventArgs(delta.X, delta.Y));
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private Matrix GetViewMatrix()
    {
        if (Document is null)
            return Matrix.Identity;

        var bounds = Document.BoundsMm;
        var matrix = Matrix.Identity;
        matrix.Translate(-bounds.X, -bounds.Y);
        matrix.Scale(_zoom, _zoom);
        matrix.Translate(
            (ActualWidth - bounds.Width * _zoom) / 2 + _pan.X,
            (ActualHeight - bounds.Height * _zoom) / 2 + _pan.Y);
        return matrix;
    }

    private void SetZoomAround(Point cursor, double requestedZoom)
    {
        if (Document is null)
            return;
        var oldZoom = _zoom;
        var newZoom = Math.Clamp(requestedZoom, 0.02, 100);
        if (Math.Abs(newZoom - oldZoom) < 0.0001)
            return;

        // Preserve the document point under the cursor by adjusting screen-space pan.
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var relative = cursor - center - _pan;
        _pan += relative - relative * (newZoom / oldZoom);
        _zoom = newZoom;
        NotifyZoomChanged();
    }

    private void DrawGrid(DrawingContext context)
    {
        const double spacing = 25;
        var minor = new Pen(new SolidColorBrush(Color.FromArgb(42, 95, 103, 118)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromArgb(70, 95, 103, 118)), 1);
        for (double x = _pan.X % spacing; x < ActualWidth; x += spacing)
            context.DrawLine(((int)(x / spacing) % 4 == 0) ? major : minor,
                new Point(x, 0), new Point(x, ActualHeight));
        for (double y = _pan.Y % spacing; y < ActualHeight; y += spacing)
            context.DrawLine(((int)(y / spacing) % 4 == 0) ? major : minor,
                new Point(0, y), new Point(ActualWidth, y));
    }

    private void NotifyZoomChanged()
    {
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void DocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (ArtworkCanvas)d;
        canvas._isSelected = e.NewValue is not null;
        canvas._moveOffsetMm = default;
        canvas.Dispatcher.BeginInvoke(canvas.FitToScreen);
    }
}
