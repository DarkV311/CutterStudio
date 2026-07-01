using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CutterStudio.Models;
using Microsoft.Win32;

namespace CutterStudio.Services;

public sealed class LicenseUpdateService : ILicenseUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(5) };

    public string MachineId { get; } = BuildMachineId();
    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task<LicenseActivationResponse> ActivateAsync(
        string serverUrl,
        string licenseKey,
        CancellationToken cancellationToken = default)
    {
        var request = new LicenseActivationRequest(licenseKey.Trim(), MachineId, AppVersion);
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await _client.PostAsync(
            Combine(serverUrl, "/api/licenses/activate"),
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<LicenseActivationResponse>(json, JsonOptions)
               ?? new LicenseActivationResponse(false, "invalid_response", null, 0, 0, "Invalid server response.");
    }

    public async Task<LatestReleaseResponse> GetLatestReleaseAsync(
        string serverUrl,
        string channel = "stable",
        CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(
            Combine(serverUrl, $"/api/releases/latest?channel={Uri.EscapeDataString(channel)}"),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<LatestReleaseResponse>(json, JsonOptions)
               ?? new LatestReleaseResponse(false, "", channel, "", "", "", DateTime.MinValue);
    }

    public Task<LatestReleaseResponse> GetLatestReleaseAsync(
        CutterSettings settings,
        string channel = "stable",
        CancellationToken cancellationToken = default) =>
        settings.UpdateSource switch
        {
            UpdateSourceKind.GitHubReleases => GetGitHubLatestReleaseAsync(settings, cancellationToken),
            UpdateSourceKind.DirectManifest => GetDirectManifestReleaseAsync(settings, cancellationToken),
            _ => GetLatestReleaseAsync(settings.LicenseServerUrl, channel, cancellationToken)
        };

    public string ResolveDownloadUrl(LatestReleaseResponse release, CutterSettings settings)
    {
        if (release.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return release.DownloadUrl;

        return settings.UpdateSource switch
        {
            UpdateSourceKind.DirectManifest => CombineRelativeTo(settings.DirectManifestUrl, release.DownloadUrl),
            _ => Combine(settings.LicenseServerUrl, release.DownloadUrl)
        };
    }

    public bool IsNewerVersion(string candidateVersion)
    {
        if (!Version.TryParse(NormalizeVersion(candidateVersion), out var candidate))
            return false;
        if (!Version.TryParse(NormalizeVersion(AppVersion), out var current))
            return false;
        return candidate > current;
    }

    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string Combine(string serverUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new InvalidOperationException("Enter the license server URL first.");
        return serverUrl.TrimEnd('/') + path;
    }

    private async Task<LatestReleaseResponse> GetDirectManifestReleaseAsync(
        CutterSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.DirectManifestUrl))
            throw new InvalidOperationException("Enter the direct update manifest URL first.");

        using var response = await _client.GetAsync(settings.DirectManifestUrl.Trim(), cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var release = JsonSerializer.Deserialize<LatestReleaseResponse>(json, JsonOptions)
                      ?? new LatestReleaseResponse(false, "", "stable", "", "", "", DateTime.MinValue);

        if (!release.Available || string.IsNullOrWhiteSpace(release.DownloadUrl) ||
            release.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return release;
        }

        return release with { DownloadUrl = CombineRelativeTo(settings.DirectManifestUrl, release.DownloadUrl) };
    }

    private async Task<LatestReleaseResponse> GetGitHubLatestReleaseAsync(
        CutterSettings settings,
        CancellationToken cancellationToken)
    {
        var owner = settings.GitHubOwner.Trim();
        var repo = settings.GitHubRepo.Trim();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            throw new InvalidOperationException("Enter GitHub owner and repository first.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest");
        request.Headers.UserAgent.ParseAdd("CutterStudio-Updater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new LatestReleaseResponse(false, "", "github", "", "", "", DateTime.MinValue);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var github = JsonSerializer.Deserialize<GitHubReleaseResponse>(json, JsonOptions)
                     ?? throw new InvalidOperationException("Invalid GitHub release response.");

        var asset = SelectInstallerAsset(github.Assets);
        var downloadUrl = asset?.BrowserDownloadUrl ?? github.HtmlUrl;
        var notes = string.IsNullOrWhiteSpace(github.Body)
            ? $"GitHub release {github.TagName}"
            : github.Body;

        return new LatestReleaseResponse(
            !string.IsNullOrWhiteSpace(github.TagName) && !string.IsNullOrWhiteSpace(downloadUrl),
            NormalizeVersion(github.TagName),
            "github",
            downloadUrl ?? "",
            "",
            notes,
            github.PublishedAt ?? github.CreatedAt ?? DateTime.UtcNow);
    }

    private static GitHubReleaseAsset? SelectInstallerAsset(IReadOnlyList<GitHubReleaseAsset> assets)
    {
        var preferredExtensions = new[] { ".zip", ".exe", ".msi" };
        return assets.FirstOrDefault(asset =>
                   preferredExtensions.Any(ext => asset.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) &&
                   asset.Name.Contains("CutterStudio", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(asset =>
                   preferredExtensions.Any(ext => asset.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
               ?? assets.FirstOrDefault();
    }

    private static string NormalizeVersion(string value)
    {
        var version = value.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = version[1..];
        var dash = version.IndexOfAny(['-', '+']);
        return dash > 0 ? version[..dash] : version;
    }

    private static string CombineRelativeTo(string baseUrl, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Enter the update manifest URL first.");
        return new Uri(new Uri(baseUrl.Trim()), relativePath).ToString();
    }

    private static string BuildMachineId()
    {
        var machineGuid = TryGetMachineGuid();
        var raw = $"{Environment.MachineName}|{Environment.UserName}|{machineGuid}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32];
    }

    private static string TryGetMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return Convert.ToString(key?.GetValue("MachineGuid")) ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private sealed record GitHubReleaseResponse(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("created_at")] DateTime? CreatedAt,
        [property: JsonPropertyName("published_at")] DateTime? PublishedAt,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset> Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
