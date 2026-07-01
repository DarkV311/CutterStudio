using Microsoft.Win32;
using System.Windows;

namespace CutterStudio.Services;

public sealed class DialogService : IDialogService
{
    public string? PickSvgFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import SVG artwork",
            Filter = "Scalable Vector Graphics (*.svg)|*.svg|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickHpglSavePath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export HPGL",
            Filter = "HPGL plotter file (*.plt;*.hpgl)|*.plt;*.hpgl|All files (*.*)|*.*",
            FileName = $"{SanitizeFileName(suggestedName)}.plt",
            AddExtension = true,
            DefaultExt = ".plt"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSvgSavePath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export print layout SVG",
            Filter = "Scalable Vector Graphics (*.svg)|*.svg|All files (*.*)|*.*",
            FileName = $"{SanitizeFileName(suggestedName)}-print-cut.svg",
            AddExtension = true,
            DefaultExt = ".svg"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message, string title = "Error") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message, string title = "Cutter Studio") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "cut-job" : value;
    }
}
