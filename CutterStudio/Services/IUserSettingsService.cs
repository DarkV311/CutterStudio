using CutterStudio.Models;

namespace CutterStudio.Services;

public interface IUserSettingsService
{
    CutterSettings Load();
    void Save(CutterSettings settings);
}
