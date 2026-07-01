namespace CutterStudio.Models;

public sealed record LicenseActivationRequest(
    string LicenseKey,
    string MachineId,
    string AppVersion);

public sealed record LicenseActivationResponse(
    bool Valid,
    string Status,
    DateTime? ExpiresUtc,
    int ActivationsUsed,
    int MaxActivations,
    string Message,
    string CustomerName = "",
    string LicenseType = "");

public sealed record LatestReleaseResponse(
    bool Available,
    string Version,
    string Channel,
    string DownloadUrl,
    string Sha256,
    string Notes,
    DateTime CreatedUtc);
