namespace CutterStudio.Services;

public interface ICorelDrawService
{
    /// <summary>
    /// Exports CorelDRAW's current selection as SVG without modifying the CDR document.
    /// Returns null when CorelDRAW is unavailable or nothing is selected.
    /// </summary>
    string? TryExportSelectionToSvg();
}
