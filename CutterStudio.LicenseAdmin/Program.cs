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

        var webHost = StartActivationApi(repository);
        Application.Run(new MainForm(repository, "http://localhost:5080"));
        webHost.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        webHost.Dispose();
    }

    private static IHost StartActivationApi(LicenseRepository repository)
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

        app.Start();
        return app;
    }
}
