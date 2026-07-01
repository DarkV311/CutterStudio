using System.Windows;
using CutterStudio.Models;
using CutterStudio.Services;

namespace CutterStudio.Views;

public partial class LicenseWindow : Window
{
    private readonly IUserSettingsService _settingsService;
    private readonly ILicenseUpdateService _licenseUpdate;
    private readonly CutterSettings _settings;

    public LicenseWindow(IUserSettingsService settingsService, ILicenseUpdateService licenseUpdate)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _licenseUpdate = licenseUpdate;
        _settings = settingsService.Load();
        LicenseKeyBox.Text = _settings.LicenseKey;
        StatusText.Text = _settings.LicenseStatus;
    }

    public static bool IsActivated(CutterSettings settings) =>
        settings.LicenseStatus.StartsWith("Active", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(settings.LicenseKey);

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateButton.IsEnabled = false;
        StatusText.Text = "Activating...";
        try
        {
            if (string.IsNullOrWhiteSpace(_settings.LicenseServerUrl))
                _settings.LicenseServerUrl = "http://localhost:5080";
            var result = await _licenseUpdate.ActivateAsync(_settings.LicenseServerUrl, LicenseKeyBox.Text);
            _settings.LicenseKey = LicenseKeyBox.Text.Trim();
            _settings.LicenseStatus = result.Valid
                ? $"Active ({result.ActivationsUsed}/{result.MaxActivations})"
                : $"Invalid: {result.Status}";
            _settings.LicenseExpiresUtc = result.ExpiresUtc;
            _settings.LicenseLastCheckedUtc = DateTime.UtcNow;
            _settingsService.Save(_settings);

            if (result.Valid)
            {
                MessageBox.Show(
                    "License activated successfully. Cutter Studio will open now.",
                    "License Activated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                Close();
                return;
            }

            StatusText.Text = result.Message;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message.Contains("localhost:5080", StringComparison.OrdinalIgnoreCase)
                ? "License service is not running. Open CutterStudio.Admin.exe first, then try again."
                : ex.Message;
        }
        finally
        {
            ActivateButton.IsEnabled = true;
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
