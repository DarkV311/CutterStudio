using System.Diagnostics;

namespace CutterStudio.UpdatePublisher;

public static class GitCredentialReader
{
    public static async Task<string> GetGitHubTokenAsync()
    {
        var git = FindGit();
        var start = new ProcessStartInfo
        {
            FileName = git,
            Arguments = "credential fill",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start git credential helper.");
        await process.StandardInput.WriteAsync("protocol=https\nhost=github.com\n\n");
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException("GitHub credential is not available. Open GitHub Desktop and sign in first. " + error);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("password=", StringComparison.Ordinal))
                return line["password=".Length..].Trim();
        }

        throw new InvalidOperationException("GitHub token was not found. Open GitHub Desktop and sign in first.");
    }

    private static string FindGit()
    {
        var desktopGit = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"GitHubDesktop\app-3.6.1\resources\app\git\cmd\git.exe");
        if (File.Exists(desktopGit))
            return desktopGit;

        return "git";
    }
}
