namespace CutterStudio.Models;

public sealed record HpglJob(
    string Commands,
    double CuttingDistanceMm,
    double TravelDistanceMm,
    int PenLifts,
    TimeSpan EstimatedCutDuration);
