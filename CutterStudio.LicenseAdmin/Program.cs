using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CutterStudio.LicenseAdmin;

internal static class Program
{
    private const string ServerUrl = "http://0.0.0.0:5080";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var appDirectory = AppContext.BaseDirectory;
        var repository = new LicenseRepository(appDirectory);
        repository.InitializeAsync().GetAwaiter().GetResult();

        var (webHost, apiStatus) = StartActivationApi(repository);
        Application.Run(new MainForm(repository, apiStatus));
        if (webHost is not null)
        {
            webHost.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            webHost.Dispose();
        }
    }

    private static (IHost? Host, string StatusText) StartActivationApi(LicenseRepository repository)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseUrls(ServerUrl);
        builder.Services.AddSingleton(repository);

        var app = builder.Build();
        app.MapGet("/", () => Results.Text("Cutter Studio License API is running."));
        app.MapPost("/api/licenses/activate", async (LicenseActivationRequest request, LicenseRepository repo) =>
            Results.Json(await repo.ActivateAsync(request)));
        app.MapGet("/api/releases/latest", () =>
            Results.Json(new LatestReleaseResponse(false, "", "stable", "", "", "Use GitHub Releases for updates.", DateTime.MinValue)));

        try
        {
            app.Start();
            return (app, "Activation service is running in the background.");
        }
        catch (IOException ex) when (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Warning: activation port 5080 is already in use by another process.");
        }
        catch (Exception ex)
        {
            return (null, "Warning: activation service could not start: " + ex.Message);
        }
    }
}
