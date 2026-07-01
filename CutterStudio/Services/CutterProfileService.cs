using System.Management;
using System.Text.RegularExpressions;
using CutterStudio.Models;

namespace CutterStudio.Services;

/// <summary>
/// Cutter profile catalog and conservative Windows device detection.
/// Generic USB bridge chips are reported as likely matches, never as certain models.
/// </summary>
public sealed partial class CutterProfileService : ICutterProfileService
{
    public IReadOnlyList<CutterProfile> Profiles { get; } =
    [
        new("Bascocut CCD Tool 2 (DMPL)", "DMPL", 38400, "RTS/CTS", 40, 390,
            ["Bascocut CCD Tool 2", "Teneth CCD Tool 2"]),
        new("Refine MH/EH 721", "HPGL", 9600, "RTS/CTS", 39.5, 630,
            ["Refine MH721", "Refine EH721", "USCutter MH721", "MH-721", "EH-721"]),
        new("Refine MH 721 MK2 (4800)", "HPGL", 4800, "RTS/CTS", 40, 630,
            ["Refine MH721 MK2", "USCutter MH721 MK2", "MH-721 MK2"]),
        new("Refine MH/EH 365", "HPGL", 9600, "RTS/CTS", 39.5, 305,
            ["Refine MH365", "Refine EH365", "MH-365", "EH-365"]),
        new("Refine MH/EH 871", "HPGL", 9600, "RTS/CTS", 39.5, 780,
            ["Refine MH871", "Refine EH871", "MH-871", "EH-871"]),
        new("Refine MH/EH 1351", "HPGL", 9600, "RTS/CTS", 39.5, 1220,
            ["Refine MH1351", "Refine EH1351", "MH-1351", "EH-1351"]),
        new("Generic Refine HPGL", "HPGL", 9600, "RTS/CTS", 39.5, 630,
            ["Refine", "USCutter MH", "USCutter EH"]),
        new("Generic HPGL", "HPGL", 9600, "None", 40, 600,
            ["HPGL Plotter", "Cutting Plotter"])
    ];

    public CutterProfile Get(string name) =>
        Profiles.FirstOrDefault(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? Profiles[^1];

    public IReadOnlyList<CutterDetectionResult> Detect()
    {
        var results = new List<CutterDetectionResult>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, PNPDeviceID, Manufacturer FROM Win32_PnPEntity " +
            "WHERE Name LIKE '%(COM%' OR Name LIKE '%USB Printing Support%'");

        foreach (ManagementObject device in searcher.Get())
        {
            var name = Convert.ToString(device["Name"]) ?? "Unknown USB device";
            var deviceId = Convert.ToString(device["PNPDeviceID"])
                           ?? Convert.ToString(device["DeviceID"])
                           ?? name;
            var manufacturer = Convert.ToString(device["Manufacturer"]) ?? string.Empty;
            var port = PortRegex().Match(name) is { Success: true } match ? match.Value : null;
            results.Add(DetectDevice(name, manufacturer, deviceId, port));
        }
        return results.OrderByDescending(result => result.ConfidencePercent).ToArray();
    }

    private CutterDetectionResult DetectDevice(
        string name,
        string manufacturer,
        string deviceId,
        string? port)
    {
        var searchable = $"{name} {manufacturer} {deviceId}";
        foreach (var profile in Profiles)
        {
            var alias = profile.ModelAliases.FirstOrDefault(value =>
                searchable.Contains(value, StringComparison.OrdinalIgnoreCase));
            if (alias is not null)
            {
                return new CutterDetectionResult(
                    profile.Name, port, deviceId, name, 95,
                    $"Windows device identity matched “{alias}”.");
            }
        }

        if (searchable.Contains("VID_1A86", StringComparison.OrdinalIgnoreCase)
            || searchable.Contains("CH340", StringComparison.OrdinalIgnoreCase)
            || searchable.Contains("CH341", StringComparison.OrdinalIgnoreCase)
            || name.Contains("USB Printing Support", StringComparison.OrdinalIgnoreCase))
        {
            return new CutterDetectionResult(
                "Generic Refine HPGL", port, deviceId, name, 70,
                "CH340/CH341 or USB-printing interfaces are commonly used by Refine MH/EH cutters. Confirm the model once.");
        }

        if (searchable.Contains("VID_0403", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Contains("FTDI", StringComparison.OrdinalIgnoreCase))
        {
            return new CutterDetectionResult(
                "Generic HPGL", port, deviceId, name, 35,
                "FTDI is a generic serial bridge used by many cutter brands, so the exact model cannot be identified safely.");
        }

        return new CutterDetectionResult(
            "Generic HPGL", port, deviceId, name, 20,
            "A communication device was found, but Windows does not expose a cutter model.");
    }

    [GeneratedRegex(@"COM\d+", RegexOptions.IgnoreCase)]
    private static partial Regex PortRegex();
}
