using CutterStudio.Models;

namespace CutterStudio.Services;

public interface IHpglService
{
    HpglJob Generate(ArtworkDocument document, CutterSettings settings);
    HpglJob GenerateAreaTest(ArtworkDocument document, CutterSettings settings, AreaTestMode mode);
}
