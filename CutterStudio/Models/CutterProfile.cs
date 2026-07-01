namespace CutterStudio.Models;

public sealed record CutterProfile(
    string Name,
    string Protocol,
    int BaudRate,
    string FlowControl,
    double UnitsPerMm,
    double DefaultWidthMm,
    string[] ModelAliases);

public sealed record CutterDetectionResult(
    string ProfileName,
    string? PortName,
    string DeviceId,
    string DeviceDescription,
    int ConfidencePercent,
    string Explanation);
