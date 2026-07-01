using CutterStudio.Models;

namespace CutterStudio.Services;

public interface ILicenseUpdateService
{
    string MachineId { get; }
    string AppVersion { get; }
    Task<LicenseActivationResponse> ActivateAsync(string serverUrl, string licenseKey, CancellationToken cancellationToken = default);
    Task<LatestReleaseResponse> GetLatestReleaseAsync(string serverUrl, string channel = "stable", CancellationToken cancellationToken = default);
    Task<LatestReleaseResponse> GetLatestReleaseAsync(CutterSettings settings, string channel = "stable", CancellationToken cancellationToken = default);
    string ResolveDownloadUrl(LatestReleaseResponse release, CutterSettings settings);
    bool IsNewerVersion(string candidateVersion);
}
