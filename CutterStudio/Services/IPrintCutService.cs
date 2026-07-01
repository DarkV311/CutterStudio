using CutterStudio.Models;

namespace CutterStudio.Services;

public interface IPrintCutService
{
    string GeneratePrintableSvg(ArtworkDocument document, CutterSettings settings);
}
