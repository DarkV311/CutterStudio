using System.Windows;
using CutterStudio.Models;
using CutterStudio.Services;
using CutterStudio.ViewModels;
using CutterStudio.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CutterStudio;

/// <summary>
/// Application composition root. The host owns dependency injection and service lifetimes.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton<IProjectRepository, SqliteProjectRepository>();
            services.AddSingleton<IVectorArtworkService, VectorArtworkService>();
            services.AddSingleton<ICorelDrawService, CorelDrawService>();
            services.AddSingleton<IHpglService, HpglService>();
            services.AddSingleton<ICutLayoutService, CutLayoutService>();
            services.AddSingleton<IPrintCutService, PrintCutService>();
            services.AddSingleton<ILicenseUpdateService, LicenseUpdateService>();
            services.AddSingleton<ISerialCutterService, SerialCutterService>();
            services.AddSingleton<ICutterProfileService, CutterProfileService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IUserSettingsService, UserSettingsService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddTransient<LicenseWindow>();
        })
        .Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await _host.StartAsync();
            await _host.Services.GetRequiredService<IProjectRepository>().InitializeAsync();

            var settingsService = _host.Services.GetRequiredService<IUserSettingsService>();
            var settings = settingsService.Load();
            var licenseUpdate = _host.Services.GetRequiredService<ILicenseUpdateService>();
            if (!await ValidateSavedLicenseAsync(settingsService, licenseUpdate, settings))
            {
                var licenseWindow = _host.Services.GetRequiredService<LicenseWindow>();
                if (licenseWindow.ShowDialog() != true)
                {
                    Shutdown(0);
                    return;
                }
            }

            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Cutter Studio could not start.\n\n{ex.Message}",
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync(TimeSpan.FromSeconds(3));
        _host.Dispose();
        base.OnExit(e);
    }

    private static async Task<bool> ValidateSavedLicenseAsync(
        IUserSettingsService settingsService,
        ILicenseUpdateService licenseUpdate,
        CutterSettings settings)
    {
        if (!LicenseWindow.IsActivated(settings))
            return false;

        try
        {
            var result = await licenseUpdate.ActivateAsync(settings.LicenseServerUrl, settings.LicenseKey);
            settings.LicenseStatus = result.Valid
                ? $"Active ({result.ActivationsUsed}/{result.MaxActivations})"
                : $"Invalid: {result.Status}";
            settings.LicenseExpiresUtc = result.ExpiresUtc;
            settings.LicenseLastCheckedUtc = DateTime.UtcNow;
            settingsService.Save(settings);
            return result.Valid;
        }
        catch
        {
            settings.LicenseStatus = "Invalid: server_unreachable";
            settingsService.Save(settings);
            return false;
        }
    }
}
