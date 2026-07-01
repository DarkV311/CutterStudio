using CutterStudio.Models;

namespace CutterStudio.Services;

public interface ISerialCutterService
{
    IReadOnlyList<string> GetAvailablePorts();
    Task SendAsync(
        CutterSettings settings,
        string hpgl,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
