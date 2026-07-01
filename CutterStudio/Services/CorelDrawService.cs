using System.Runtime.InteropServices;
using Corel.Interop.VGCore;

namespace CutterStudio.Services;

/// <summary>
/// Late-bound CorelDRAW automation. Late binding keeps Cutter Studio deployable without
/// shipping Corel's interop assembly and supports compatible CorelDRAW versions.
/// </summary>
public sealed class CorelDrawService : ICorelDrawService
{
    public string? TryExportSelectionToSvg()
    {
        Application? application = null;
        Document? document = null;
        ExportFilter? exportFilter = null;
        var outputPath = Path.Combine(Path.GetTempPath(), $"CutterStudio_Corel_{Guid.NewGuid():N}.svg");

        try
        {
            var type = Type.GetTypeFromProgID("CorelDRAW.Application.25")
                       ?? Type.GetTypeFromProgID("CorelDRAW.Application");
            if (type is null)
                return null;

            // CorelDRAW is a single-instance automation server, so activation attaches to
            // the currently running application when one is open.
            application = Activator.CreateInstance(type) as Application;
            if (application is null)
                return null;

            document = application.ActiveDocument;
            if (document is null)
                return null;
            if (document.SelectionRange?.Count < 1)
                return null;

            exportFilter = document.ExportEx(
                outputPath,
                cdrFilter.cdrSVG,
                cdrExportRange.cdrSelection);
            exportFilter.Finish();

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                return null;
            return File.ReadAllText(outputPath);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                "CorelDRAW is open, but its selected objects could not be exported. " +
                "Select the artwork in CorelDRAW, press Ctrl+C, then return here and press Ctrl+V.", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            var reason = GetDeepestMessage(ex);
            throw new InvalidOperationException(
                $"CorelDRAW could not export the selected artwork: {reason}. " +
                "Convert text to curves with Ctrl+Q, then copy and paste again.", ex);
        }
        finally
        {
            TryDelete(outputPath);
            ReleaseCom(exportFilter);
            ReleaseCom(document);
            ReleaseCom(application);
        }
    }

    private static string GetDeepestMessage(Exception exception)
    {
        while (exception.InnerException is not null)
            exception = exception.InnerException;
        return string.IsNullOrWhiteSpace(exception.Message)
            ? "unknown CorelDRAW automation error"
            : exception.Message;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temporary file cleanup is best-effort and must not hide the import result.
        }
    }
}
