using CutterStudio.Models;

namespace CutterStudio.Services;

public interface ICutterProfileService
{
    IReadOnlyList<CutterProfile> Profiles { get; }
    CutterProfile Get(string name);
    IReadOnlyList<CutterDetectionResult> Detect();
}
