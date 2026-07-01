using CutterStudio.Models;

namespace CutterStudio.Services;

public interface ICutLayoutService
{
    ArtworkDocument CreatePreview(ArtworkDocument source, CutterSettings settings);
    CutLayoutMetrics CalculateMetrics(ArtworkDocument source, CutterSettings settings);
}
