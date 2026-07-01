using CutterStudio.Models;

namespace CutterStudio.Services;

public interface IVectorArtworkService
{
    Task<ArtworkDocument> ImportSvgAsync(string filePath, CancellationToken cancellationToken = default);
    ArtworkDocument ParseSvg(string svg, string sourceName = "Artwork");
    ArtworkDocument PasteFromClipboard();
    ArtworkDocument Merge(ArtworkDocument current, ArtworkDocument inserted);
}
