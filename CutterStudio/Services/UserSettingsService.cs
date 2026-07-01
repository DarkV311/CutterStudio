using System.Text.Json;
using CutterStudio.Models;

namespace CutterStudio.Services;

public sealed class UserSettingsService : IUserSettingsService
{
    private readonly string _filePath;

    public UserSettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CutterStudio");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");
    }

    public CutterSettings Load()
    {
        try
        {
            var settings = File.Exists(_filePath)
                ? JsonSerializer.Deserialize<CutterSettings>(File.ReadAllText(_filePath)) ?? new CutterSettings()
                : new CutterSettings();
            ApplyRuntimeConfig(settings);
            return settings;
        }
        catch
        {
            // Invalid user preferences should never prevent the application from starting.
            var settings = new CutterSettings();
            ApplyRuntimeConfig(settings);
            return settings;
        }
    }

    public void Save(CutterSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, true);
    }

    private static void ApplyRuntimeConfig(CutterSettings settings)
    {
        foreach (var path in RuntimeConfigPaths())
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("licenseServerUrl", out var camel) &&
                    !string.IsNullOrWhiteSpace(camel.GetString()))
                {
                    settings.LicenseServerUrl = camel.GetString()!.Trim();
                    return;
                }
                if (document.RootElement.TryGetProperty("LicenseServerUrl", out var pascal) &&
                    !string.IsNullOrWhiteSpace(pascal.GetString()))
                {
                    settings.LicenseServerUrl = pascal.GetString()!.Trim();
                    return;
                }
            }
            catch
            {
                // Bad runtime config should not block startup; the license check will show a clear failure.
            }
        }
    }

    private static IEnumerable<string> RuntimeConfigPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "license-server.json");
        yield return Path.Combine(AppContext.BaseDirectory, "CutterStudio.license.json");
    }
}
