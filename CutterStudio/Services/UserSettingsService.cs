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
            if (!File.Exists(_filePath))
                return new CutterSettings();
            return JsonSerializer.Deserialize<CutterSettings>(File.ReadAllText(_filePath))
                   ?? new CutterSettings();
        }
        catch
        {
            // Invalid user preferences should never prevent the application from starting.
            return new CutterSettings();
        }
    }

    public void Save(CutterSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, true);
    }
}
