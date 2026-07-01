using System.Windows;
using System.Windows.Media;

namespace CutterStudio.Models;

/// <summary>
/// Normalized vector artwork. Geometry coordinates are always millimeters.
/// </summary>
public sealed class ArtworkDocument
{
    public required string SvgData { get; init; }
    public required IReadOnlyList<Geometry> Geometries { get; init; }
    public required Rect BoundsMm { get; init; }
    public string SourceName { get; init; } = "Artwork";
    public Rect? MaterialBoundsMm { get; init; }
    public Rect? ArtworkBoundsMm { get; init; }

    public double WidthMm => BoundsMm.IsEmpty ? 0 : BoundsMm.Width;
    public double HeightMm => BoundsMm.IsEmpty ? 0 : BoundsMm.Height;
}
