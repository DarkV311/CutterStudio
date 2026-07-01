using System.Diagnostics;

static string? Arg(string[] args, string name)
{
    var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

if (args.Contains("--help") || args.Length == 0)
{
    Console.WriteLine("CutterStudio.Updater");
    Console.WriteLine("Usage:");
    Console.WriteLine("  CutterStudio.Updater --source <newExe> --target <installedExe> [--pid <processId>] [--restart]");
    return 0;
}

var source = Arg(args, "--source");
var target = Arg(args, "--target");
var restart = args.Contains("--restart");
if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
{
    Console.Error.WriteLine("--source and --target are required.");
    return 2;
}

if (int.TryParse(Arg(args, "--pid"), out var pid))
{
    try
    {
        var process = Process.GetProcessById(pid);
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10000);
        }
    }
    catch
    {
        // Process may already be closed.
    }
}

var backup = target + ".bak";
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
    if (File.Exists(backup))
        File.Delete(backup);
    if (File.Exists(target))
        File.Move(target, backup);
    File.Copy(source, target, overwrite: true);

    if (restart)
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    Console.WriteLine("Update applied successfully.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    try
    {
        if (!File.Exists(target) && File.Exists(backup))
            File.Move(backup, target);
    }
    catch
    {
        // Best-effort rollback.
    }
    return 1;
}
