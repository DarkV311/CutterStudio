namespace CutterStudio.Services;

public interface IDialogService
{
    string? PickSvgFile();
    string? PickHpglSavePath(string suggestedName);
    string? PickSvgSavePath(string suggestedName);
    bool Confirm(string message, string title);
    void ShowError(string message, string title = "Error");
    void ShowInfo(string message, string title = "Cutter Studio");
}
