namespace CutterStudio.LicenseAdmin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var appDirectory = AppContext.BaseDirectory;
            var repository = new LicenseRepository(appDirectory);
            repository.InitializeAsync().GetAwaiter().GetResult();

            Application.Run(new MainForm(repository, $"Connected to license server: {repository.ServerUrl}"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Cutter Studio License Admin could not start.\n\n{ex.Message}",
                "License Admin startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
