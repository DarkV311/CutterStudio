namespace CutterStudio.LicenseAdmin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var appDirectory = AppContext.BaseDirectory;
        var repository = new LicenseRepository(appDirectory);
        repository.InitializeAsync().GetAwaiter().GetResult();

        Application.Run(new MainForm(repository, $"Connected to license server: {repository.ServerUrl}"));
    }
}
