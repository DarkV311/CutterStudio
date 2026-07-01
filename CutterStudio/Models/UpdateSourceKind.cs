namespace CutterStudio.Models;

/// <summary>
/// Defines where Cutter Studio should look for application updates.
/// Licensing still uses the configured license server URL.
/// </summary>
public enum UpdateSourceKind
{
    LocalServer,
    GitHubReleases,
    DirectManifest
}
