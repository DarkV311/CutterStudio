namespace CutterStudio.LicenseAdmin;

public sealed record LicenseRecord(
    long Id,
    string LicenseKey,
    string CustomerName,
    string CustomerEmail,
    DateTime CreatedUtc,
    DateTime? ExpiresUtc,
    int MaxActivations,
    bool IsBlocked,
    string Notes,
    int ActivationsUsed,
    DateTime? LastSeenUtc);

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
    string Message);

public sealed record LatestReleaseResponse(
    bool Available,
    string Version,
    string Channel,
    string DownloadUrl,
    string Sha256,
    string Notes,
    DateTime CreatedUtc);
