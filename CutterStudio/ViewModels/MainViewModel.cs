using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using CutterStudio.Models;
using CutterStudio.Services;

namespace CutterStudio.ViewModels;

/// <summary>
/// Main application state and workflows. The view only handles canvas-specific pointer gestures.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IProjectRepository _projects;
    private readonly IVectorArtworkService _artworkService;
    private readonly ICorelDrawService _corelDraw;
    private readonly IHpglService _hpgl;
    private readonly ICutLayoutService _layout;
    private readonly ISerialCutterService _serial;
    private readonly ICutterProfileService _profiles;
    private readonly IPrintCutService _printCut;
    private readonly ILicenseUpdateService _licenseUpdate;
    private readonly IDialogService _dialogs;
    private readonly IUserSettingsService _userSettings;

    private ArtworkDocument? _artwork;
    private ArtworkDocument? _previewArtwork;
    private CutLayoutMetrics? _layoutMetrics;
    private RecentProject? _selectedRecentProject;
    private long _projectId;
    private DateTime _createdUtc;
    private string _projectName = "Untitled Project";
    private string _statusText = "Ready";
    private string _dimensionsText = "No artwork";
    private string _estimatedTimeText = "Estimated time: —";
    private string _requiredVinylText = "Required vinyl: —";
    private double _transferProgress;
    private bool _isBusy;
    private bool _applyingProfile;

    public MainViewModel(
        IProjectRepository projects,
        IVectorArtworkService artworkService,
        ICorelDrawService corelDraw,
        IHpglService hpgl,
        ICutLayoutService layout,
        ISerialCutterService serial,
        ICutterProfileService profiles,
        IPrintCutService printCut,
        ILicenseUpdateService licenseUpdate,
        IDialogService dialogs,
        IUserSettingsService userSettings)
    {
        _projects = projects;
        _artworkService = artworkService;
        _corelDraw = corelDraw;
        _hpgl = hpgl;
        _layout = layout;
        _serial = serial;
        _profiles = profiles;
        _printCut = printCut;
        _licenseUpdate = licenseUpdate;
        _dialogs = dialogs;
        _userSettings = userSettings;

        Settings = userSettings.Load();
        ApplyProfile(_profiles.Get(Settings.CutterProfile), false);
        Settings.PropertyChanged += SettingsOnPropertyChanged;

        PasteCommand = new AsyncRelayCommand(PasteAsync, CanEdit, HandleError);
        ImportCommand = new AsyncRelayCommand(ImportAsync, CanEdit, HandleError);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => Artwork is not null && !IsBusy, HandleError);
        OpenCommand = new AsyncRelayCommand(OpenSelectedAsync,
            () => SelectedRecentProject is not null && !IsBusy, HandleError);
        InsertProjectCommand = new AsyncRelayCommand(InsertSelectedProjectAsync,
            () => Artwork is not null && SelectedRecentProject is not null && !IsBusy, HandleError);
        CutCommand = new AsyncRelayCommand(CutAsync,
            () => Artwork is not null && !IsBusy && !string.IsNullOrWhiteSpace(Settings.PortName), HandleError);
        TestCutterCommand = new AsyncRelayCommand(TestCutterAsync,
            () => !IsBusy && !string.IsNullOrWhiteSpace(Settings.PortName), HandleError);
        AreaTestCommand = new AsyncRelayCommand(AreaTestAsync,
            () => Artwork is not null && !IsBusy && !string.IsNullOrWhiteSpace(Settings.PortName), HandleError);
        ExportPrintCutCommand = new AsyncRelayCommand(ExportPrintCutAsync,
            () => Artwork is not null && !IsBusy, HandleError);
        ActivateLicenseCommand = new AsyncRelayCommand(ActivateLicenseAsync, () => !IsBusy, HandleError);
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, () => !IsBusy, HandleError);
        ExportCommand = new AsyncRelayCommand(ExportHpglAsync,
            () => Artwork is not null && !IsBusy, HandleError);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        DetectCutterCommand = new RelayCommand(DetectCutter, () => !IsBusy);
        CancelTransferCommand = new RelayCommand(CancelTransfer, () => IsBusy && _transferCancellation is not null);
    }

    private CancellationTokenSource? _transferCancellation;

    public CutterSettings Settings { get; }
    public ObservableCollection<RecentProject> RecentProjects { get; } = [];
    public ObservableCollection<string> AvailablePorts { get; } = [];
    public IReadOnlyList<string> CutterProfiles => _profiles.Profiles.Select(profile => profile.Name).ToArray();
    public IReadOnlyList<string> FlowControlModes { get; } = ["RTS/CTS", "None"];
    public IReadOnlyList<int> BaudRates { get; } = [4800, 9600, 19200, 38400, 57600, 115200];
    public IReadOnlyList<AreaTestMode> AreaTestModes { get; } = [AreaTestMode.BladeUp, AreaTestMode.BladeDown];
    public IReadOnlyList<RegistrationMarkStyle> RegistrationMarkStyles { get; } =
        [RegistrationMarkStyle.SquareCorners, RegistrationMarkStyle.CircleCross];
    public IReadOnlyList<UpdateSourceKind> UpdateSources { get; } =
        [UpdateSourceKind.LocalServer, UpdateSourceKind.GitHubReleases, UpdateSourceKind.DirectManifest];

    public AsyncRelayCommand PasteCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand InsertProjectCommand { get; }
    public AsyncRelayCommand CutCommand { get; }
    public AsyncRelayCommand TestCutterCommand { get; }
    public AsyncRelayCommand AreaTestCommand { get; }
    public AsyncRelayCommand ExportPrintCutCommand { get; }
    public AsyncRelayCommand ActivateLicenseCommand { get; }
    public AsyncRelayCommand CheckUpdateCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand DetectCutterCommand { get; }
    public RelayCommand CancelTransferCommand { get; }

    public ArtworkDocument? Artwork
    {
        get => _artwork;
        private set
        {
            if (!SetProperty(ref _artwork, value))
                return;
            DimensionsText = value is null
                ? "No artwork"
                : $"{value.WidthMm:0.##} × {value.HeightMm:0.##} mm";
            UpdateEstimate();
            UpdatePreview();
            NotifyCommands();
        }
    }

    public ArtworkDocument? PreviewArtwork
    {
        get => _previewArtwork;
        private set => SetProperty(ref _previewArtwork, value);
    }

    public CutLayoutMetrics? LayoutMetrics
    {
        get => _layoutMetrics;
        private set => SetProperty(ref _layoutMetrics, value);
    }

    public string RequiredVinylText
    {
        get => _requiredVinylText;
        private set => SetProperty(ref _requiredVinylText, value);
    }

    public RecentProject? SelectedRecentProject
    {
        get => _selectedRecentProject;
        set
        {
            if (SetProperty(ref _selectedRecentProject, value))
            {
                OpenCommand.NotifyCanExecuteChanged();
                InsertProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DimensionsText
    {
        get => _dimensionsText;
        private set => SetProperty(ref _dimensionsText, value);
    }

    public string EstimatedTimeText
    {
        get => _estimatedTimeText;
        private set => SetProperty(ref _estimatedTimeText, value);
    }

    public double TransferProgress
    {
        get => _transferProgress;
        private set => SetProperty(ref _transferProgress, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;
            NotifyCommands();
            CancelTransferCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task InitializeAsync()
    {
        RefreshPorts();
        ApplyRememberedDevice();
        await ReloadRecentProjectsAsync();
        StatusText = "Ready";
        _ = CheckUpdateAsync(true);
    }

    public void PersistUserSettings() => _userSettings.Save(Settings);

    private Task PasteAsync()
    {
        try
        {
            Artwork = _artworkService.PasteFromClipboard();
        }
        catch (InvalidOperationException)
        {
            var corelSvg = _corelDraw.TryExportSelectionToSvg();
            if (string.IsNullOrWhiteSpace(corelSvg))
            {
                throw new InvalidOperationException(
                    "No supported vector data was found. In CorelDRAW, select the artwork, " +
                    "press Ctrl+C, then return to Cutter Studio and press Ctrl+V.");
            }
            Artwork = _artworkService.ParseSvg(corelSvg, "CorelDRAW selection");
        }
        StartNewProject(Artwork.SourceName);
        StatusText = Artwork.SourceName.Contains("CorelDRAW", StringComparison.OrdinalIgnoreCase)
            ? "CorelDRAW selection imported as vector artwork."
            : "Vector artwork pasted from the clipboard.";
        return Task.CompletedTask;
    }

    private async Task ImportAsync()
    {
        var path = _dialogs.PickSvgFile();
        if (path is null)
            return;

        IsBusy = true;
        StatusText = "Importing SVG…";
        try
        {
            Artwork = await _artworkService.ImportSvgAsync(path);
            StartNewProject(Path.GetFileNameWithoutExtension(path));
            StatusText = $"Imported {Path.GetFileName(path)}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (Artwork is null)
            return;
        if (string.IsNullOrWhiteSpace(ProjectName))
            throw new InvalidOperationException("Enter a project name before saving.");

        IsBusy = true;
        StatusText = "Saving project…";
        try
        {
            var now = DateTime.UtcNow;
            var record = new ProjectRecord
            {
                Id = _projectId,
                Name = ProjectName.Trim(),
                CreatedUtc = _createdUtc == default ? now : _createdUtc,
                ModifiedUtc = now,
                ArtworkSvg = Artwork.SvgData,
                SettingsJson = JsonSerializer.Serialize(Settings)
            };
            _projectId = await _projects.SaveAsync(record);
            _createdUtc = record.CreatedUtc;
            await ReloadRecentProjectsAsync();
            StatusText = $"Saved “{ProjectName}”.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenSelectedAsync()
    {
        if (SelectedRecentProject is null)
            return;

        IsBusy = true;
        StatusText = "Opening project…";
        try
        {
            var record = await _projects.GetAsync(SelectedRecentProject.Id)
                         ?? throw new InvalidOperationException("The selected project no longer exists.");
            Artwork = _artworkService.ParseSvg(record.ArtworkSvg, record.Name);
            _projectId = record.Id;
            _createdUtc = record.CreatedUtc;
            ProjectName = record.Name;
            ApplySavedSettings(record.SettingsJson);
            StatusText = $"Opened “{record.Name}”.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InsertSelectedProjectAsync()
    {
        if (Artwork is null || SelectedRecentProject is null)
            return;

        IsBusy = true;
        StatusText = "Inserting project artwork...";
        try
        {
            var record = await _projects.GetAsync(SelectedRecentProject.Id)
                         ?? throw new InvalidOperationException("The selected project no longer exists.");
            var inserted = _artworkService.ParseSvg(record.ArtworkSvg, record.Name);
            Artwork = _artworkService.Merge(Artwork, inserted);
            StatusText = $"Inserted artwork from “{record.Name}” into “{ProjectName}”.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CutAsync()
    {
        if (Artwork is null || string.IsNullOrWhiteSpace(Settings.PortName))
            return;

        var job = _hpgl.Generate(Artwork, Settings);
        if (!_dialogs.Confirm(
                $"Send {ProjectName} to {Settings.PortName}?\n\n" +
                $"Cut distance: {job.CuttingDistanceMm / 1000:0.##} m\n" +
                $"Estimated cutting time: {FormatDuration(job.EstimatedCutDuration)}\n" +
                $"Expected finish: {DateTime.Now.Add(job.EstimatedCutDuration + EstimateTransferDuration(job)).ToShortTimeString()}",
                "Confirm cut"))
            return;

        IsBusy = true;
        TransferProgress = 0;
        StatusText = $"Sending to {Settings.PortName}…";
        _transferCancellation = new CancellationTokenSource();
        CancelTransferCommand.NotifyCanExecuteChanged();
        try
        {
            var progress = new Progress<double>(value => TransferProgress = value * 100);
            await _serial.SendAsync(
                Settings,
                job.Commands,
                progress,
                _transferCancellation.Token);
            TransferProgress = 100;
            StatusText = "Cut job transferred successfully.";
        }
        finally
        {
            _transferCancellation.Dispose();
            _transferCancellation = null;
            IsBusy = false;
        }
    }

    private async Task TestCutterAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.PortName))
            return;

        if (!_dialogs.Confirm(
                $"The cutter head will move 10 mm and return with the blade raised.\n\n" +
                $"Port: {Settings.PortName}\nProfile: {Settings.CutterProfile}\n\nContinue?",
                "Safe movement test"))
            return;

        var dmpl = Settings.CutterProfile.Contains("DMPL", StringComparison.OrdinalIgnoreCase);
        var testUnits = Math.Max(1, (int)Math.Round(Settings.UnitsPerMm * 10));
        var command = dmpl
            ? $";:H A L0 ECN U U0,0;U{testUnits},0;U0,0;@;"
            : $"IN;PA;PU0,0;PU{testUnits},0;PU0,0;";

        IsBusy = true;
        TransferProgress = 0;
        StatusText = $"Testing {Settings.PortName} with blade raised...";
        try
        {
            var progress = new Progress<double>(value => TransferProgress = value * 100);
            await _serial.SendAsync(Settings, command, progress);
            TransferProgress = 100;
            StatusText = "Movement test sent. Confirm that the head moved and returned.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AreaTestAsync()
    {
        if (Artwork is null || string.IsNullOrWhiteSpace(Settings.PortName))
            return;

        var job = _hpgl.GenerateAreaTest(Artwork, Settings, Settings.AreaTestMode);
        var warning = Settings.AreaTestMode == AreaTestMode.BladeDown
            ? "\n\nBlade Down will cut the boundary rectangle."
            : "\n\nBlade Up only moves around the boundary.";

        if (!_dialogs.Confirm(
                $"Send area test to {Settings.PortName}?{warning}\n\n" +
                $"Boundary travel: {(job.CuttingDistanceMm + job.TravelDistanceMm) / 1000:0.##} m",
                "Area Test"))
            return;

        IsBusy = true;
        TransferProgress = 0;
        StatusText = $"Sending area test ({Settings.AreaTestMode})...";
        try
        {
            var progress = new Progress<double>(value => TransferProgress = value * 100);
            await _serial.SendAsync(Settings, job.Commands, progress);
            TransferProgress = 100;
            StatusText = $"Area test sent ({Settings.AreaTestMode}).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportHpglAsync()
    {
        if (Artwork is null)
            return;
        var path = _dialogs.PickHpglSavePath(ProjectName);
        if (path is null)
            return;

        var job = _hpgl.Generate(Artwork, Settings);
        await File.WriteAllTextAsync(path, job.Commands, System.Text.Encoding.ASCII);
        StatusText = $"Exported {Path.GetFileName(path)}.";
    }

    private async Task ExportPrintCutAsync()
    {
        if (Artwork is null)
            return;
        var path = _dialogs.PickSvgSavePath(ProjectName);
        if (path is null)
            return;
        var svg = _printCut.GeneratePrintableSvg(Artwork, Settings);
        await File.WriteAllTextAsync(path, svg, System.Text.Encoding.UTF8);
        StatusText = $"Exported print-and-cut SVG: {Path.GetFileName(path)}.";
    }

    private async Task ActivateLicenseAsync()
    {
        IsBusy = true;
        StatusText = "Activating license...";
        try
        {
            var result = await _licenseUpdate.ActivateAsync(Settings.LicenseServerUrl, Settings.LicenseKey);
            Settings.LicenseStatus = result.Valid
                ? $"Active ({result.ActivationsUsed}/{result.MaxActivations})"
                : $"Invalid: {result.Status}";
            Settings.LicenseExpiresUtc = result.ExpiresUtc;
            Settings.LicenseLastCheckedUtc = DateTime.UtcNow;
            PersistUserSettings();
            _dialogs.ShowInfo(result.Message, "License");
            StatusText = $"License status: {Settings.LicenseStatus}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task CheckUpdateAsync() => CheckUpdateAsync(false);

    private async Task CheckUpdateAsync(bool automatic)
    {
        IsBusy = true;
        StatusText = "Checking for updates...";
        try
        {
            var release = await _licenseUpdate.GetLatestReleaseAsync(Settings);
            if (!release.Available)
            {
                if (!automatic)
                    _dialogs.ShowInfo("No published release was found from the selected update source.", "Updates");
                StatusText = "No update release found.";
                return;
            }

            if (!_licenseUpdate.IsNewerVersion(release.Version))
            {
                if (!automatic)
                    _dialogs.ShowInfo($"You are already on version {_licenseUpdate.AppVersion}.\nLatest: {release.Version}", "Updates");
                StatusText = "Application is up to date.";
                return;
            }

            var absoluteUrl = _licenseUpdate.ResolveDownloadUrl(release, Settings);
            if (_dialogs.Confirm(
                    $"New version available: {release.Version}\n" +
                    $"Current version: {_licenseUpdate.AppVersion}\n" +
                    $"Source: {Settings.UpdateSource}\n\n{release.Notes}\n\nDownload and install now?",
                    "Update available"))
            {
                await DownloadAndInstallUpdateAsync(release, absoluteUrl);
            }
            StatusText = $"Update available: {release.Version}";
        }
        catch (Exception ex)
        {
            StatusText = automatic ? "Update check skipped." : ex.Message;
            if (!automatic)
                _dialogs.ShowError(ex.Message, "Updates");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DownloadAndInstallUpdateAsync(LatestReleaseResponse release, string downloadUrl)
    {
        TransferProgress = 0;
        StatusText = $"Downloading update {release.Version}...";

        var workDirectory = Path.Combine(Path.GetTempPath(), "CutterStudioUpdate", Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(workDirectory, $"CutterStudio-{release.Version}.zip");
        var extractDirectory = Path.Combine(workDirectory, "extract");
        Directory.CreateDirectory(workDirectory);
        Directory.CreateDirectory(extractDirectory);

        await DownloadFileAsync(downloadUrl, zipPath, new Progress<double>(value =>
        {
            TransferProgress = value * 85;
            StatusText = $"Downloading update {release.Version}: {TransferProgress:0}%";
        }));

        if (!string.IsNullOrWhiteSpace(release.Sha256))
            await VerifySha256Async(zipPath, release.Sha256);

        StatusText = "Preparing update...";
        ZipFile.ExtractToDirectory(zipPath, extractDirectory, true);

        var newExe = Directory.GetFiles(extractDirectory, "CutterStudio.exe", SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The downloaded update does not contain CutterStudio.exe.");
        var sourceDirectory = Path.GetDirectoryName(newExe)
            ?? throw new InvalidOperationException("Could not locate extracted update directory.");

        var installScript = WriteInstallScript(workDirectory, sourceDirectory, AppContext.BaseDirectory);
        StatusText = "Installing update. Cutter Studio will restart...";
        TransferProgress = 100;

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{installScript}\" -ProcessId {Environment.ProcessId}",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        await Task.Delay(300);
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown(0));
    }

    private static async Task DownloadFileAsync(string url, string targetPath, IProgress<double> progress)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(targetPath);
        var buffer = new byte[1024 * 128];
        long readTotal = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer);
            if (read == 0)
                break;
            await output.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total is > 0)
                progress.Report(Math.Clamp(readTotal / (double)total.Value, 0, 1));
        }
        progress.Report(1);
    }

    private static async Task VerifySha256Async(string path, string expectedSha256)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        if (!actual.Equals(expectedSha256.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Downloaded update checksum did not match.");
    }

    private static string WriteInstallScript(string workDirectory, string sourceDirectory, string targetDirectory)
    {
        var scriptPath = Path.Combine(workDirectory, "InstallCutterStudioUpdate.ps1");
        var script = $$"""
param([int]$ProcessId)
$ErrorActionPreference = "Stop"
$source = "{{EscapePowerShell(sourceDirectory)}}"
$target = "{{EscapePowerShell(targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}}"
try {
    Wait-Process -Id $ProcessId -Timeout 30 -ErrorAction SilentlyContinue
} catch {}
Start-Sleep -Milliseconds 700
Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
    if ($_.Name -ieq "license-server.json") {
        return
    }
    $destination = Join-Path $target $_.Name
    if ($_.PSIsContainer) {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    } else {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }
}
Start-Process -FilePath (Join-Path $target "CutterStudio.exe")
Start-Sleep -Seconds 2
try { Remove-Item -LiteralPath "{{EscapePowerShell(workDirectory)}}"" -Recurse -Force } catch {}
""";
        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        return scriptPath;
    }

    private static string EscapePowerShell(string value) => value.Replace("`", "``").Replace("\"", "`\"");

    private void RefreshPorts()
    {
        var selected = Settings.PortName;
        AvailablePorts.Clear();
        foreach (var port in _serial.GetAvailablePorts())
            AvailablePorts.Add(port);

        if (selected is not null && AvailablePorts.Contains(selected))
            Settings.PortName = selected;
        else
            Settings.PortName = AvailablePorts.FirstOrDefault();

        StatusText = AvailablePorts.Count == 0
            ? "No serial cutters detected."
            : $"Detected {AvailablePorts.Count} COM port(s).";
        NotifyCommands();
    }

    private void CancelTransfer() => _transferCancellation?.Cancel();

    private void StartNewProject(string suggestedName)
    {
        _projectId = 0;
        _createdUtc = DateTime.UtcNow;
        ProjectName = string.IsNullOrWhiteSpace(suggestedName) ? "Untitled Project" : suggestedName;
    }

    private async Task ReloadRecentProjectsAsync()
    {
        var recent = await _projects.GetRecentAsync();
        RecentProjects.Clear();
        foreach (var project in recent)
            RecentProjects.Add(project);
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CutterSettings.CutterProfile) && !_applyingProfile)
        {
            ApplyProfile(_profiles.Get(Settings.CutterProfile));
        }
        UpdateEstimate();
        UpdatePreview();
        CutCommand.NotifyCanExecuteChanged();
        TestCutterCommand.NotifyCanExecuteChanged();
    }

    private void ApplySavedSettings(string json)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<CutterSettings>(json);
            if (saved is null)
                return;
            Settings.BaudRate = saved.BaudRate;
            Settings.CutterProfile = saved.CutterProfile;
            Settings.FlowControl = saved.FlowControl;
            Settings.Speed = saved.Speed;
            Settings.Pressure = saved.Pressure;
            Settings.Passes = saved.Passes;
            Settings.Mirror = saved.Mirror;
            Settings.Rotate90 = saved.Rotate90;
            Settings.ScalePercent = saved.ScalePercent;
            Settings.WeedBorder = saved.WeedBorder;
            Settings.WeedBorderMarginMm = saved.WeedBorderMarginMm;
            Settings.Copies = saved.Copies;
            Settings.AutomaticNesting = saved.AutomaticNesting;
            Settings.MaterialWidthMm = saved.MaterialWidthMm;
            Settings.CopySpacingMm = saved.CopySpacingMm;
            Settings.OffsetXmm = saved.OffsetXmm;
            Settings.OffsetYmm = saved.OffsetYmm;
            Settings.UnitsPerMm = saved.UnitsPerMm <= 0 ? 40 : saved.UnitsPerMm;
            Settings.DeviceId = saved.DeviceId;
            Settings.VinylMarginMm = saved.VinylMarginMm;
            Settings.AreaTestMode = saved.AreaTestMode;
            Settings.ContourCorrectionEnabled = saved.ContourCorrectionEnabled;
            Settings.ContourOffsetXmm = saved.ContourOffsetXmm;
            Settings.ContourOffsetYmm = saved.ContourOffsetYmm;
            Settings.ContourRotationDeg = saved.ContourRotationDeg;
            Settings.ContourScaleXPercent = saved.ContourScaleXPercent <= 0 ? 100 : saved.ContourScaleXPercent;
            Settings.ContourScaleYPercent = saved.ContourScaleYPercent <= 0 ? 100 : saved.ContourScaleYPercent;
            Settings.RegistrationMarkMarginMm = saved.RegistrationMarkMarginMm <= 0 ? 10 : saved.RegistrationMarkMarginMm;
            Settings.RegistrationMarkSizeMm = saved.RegistrationMarkSizeMm <= 0 ? 5 : saved.RegistrationMarkSizeMm;
            Settings.RegistrationMarkStyle = saved.RegistrationMarkStyle;
            Settings.CutContourBox = saved.CutContourBox;
            Settings.ContourGapMm = saved.ContourGapMm;
            // License identity belongs to the local machine, not to project files.
            // Do not restore saved project license fields over the user's active license.
            Settings.UpdateSource = saved.UpdateSource;
            Settings.GitHubOwner = saved.GitHubOwner;
            Settings.GitHubRepo = saved.GitHubRepo;
            Settings.DirectManifestUrl = saved.DirectManifestUrl;
        }
        catch (JsonException)
        {
            StatusText = "Project opened; its old cutter settings could not be restored.";
        }
    }

    private void UpdateEstimate()
    {
        if (Artwork is null)
        {
            EstimatedTimeText = "Estimated time: —";
            return;
        }
        try
        {
            var job = _hpgl.Generate(Artwork, Settings);
            var transfer = EstimateTransferDuration(job);
            var total = job.EstimatedCutDuration + transfer;
            EstimatedTimeText =
                $"Cut: {FormatDuration(job.EstimatedCutDuration)}  |  " +
                $"Transfer: {FormatDuration(transfer)}  |  " +
                $"Finish about {DateTime.Now.Add(total):t}";
        }
        catch (Exception ex)
        {
            EstimatedTimeText = $"Layout issue: {ex.Message}";
        }
    }

    private void UpdatePreview()
    {
        if (Artwork is null)
        {
            PreviewArtwork = null;
            LayoutMetrics = null;
            RequiredVinylText = "Required vinyl: —";
            return;
        }

        try
        {
            PreviewArtwork = _layout.CreatePreview(Artwork, Settings);
            LayoutMetrics = _layout.CalculateMetrics(Artwork, Settings);
            RequiredVinylText =
                $"Required vinyl: {LayoutMetrics.VinylWidthMm / 10:0.##} × " +
                $"{LayoutMetrics.VinylLengthMm / 10:0.##} cm  |  " +
                $"Artwork: {LayoutMetrics.ArtworkWidthMm / 10:0.##} × " +
                $"{LayoutMetrics.ArtworkHeightMm / 10:0.##} cm";
        }
        catch
        {
            PreviewArtwork = Artwork;
            LayoutMetrics = null;
            RequiredVinylText = "Required vinyl cannot be calculated with the current layout.";
        }
    }

    private void HandleError(Exception ex)
    {
        StatusText = ex.Message;
        _dialogs.ShowError(ex.Message);
        IsBusy = false;
    }

    private bool CanEdit() => !IsBusy;

    public void MoveArtwork(double deltaXmm, double deltaYmm)
    {
        Settings.OffsetXmm = Math.Max(0, Settings.OffsetXmm + deltaXmm);
        Settings.OffsetYmm = Math.Max(0, Settings.OffsetYmm + deltaYmm);
    }

    private void DetectCutter()
    {
        var detections = _profiles.Detect();
        if (detections.Count == 0)
        {
            _dialogs.ShowInfo(
                "No cutter interface was detected. Connect and power on the cutter, install its USB driver, then try again.",
                "Detect Cutter");
            return;
        }

        var detection = detections[0];
        ApplyProfile(_profiles.Get(detection.ProfileName));
        if (!string.IsNullOrWhiteSpace(detection.PortName))
            Settings.PortName = detection.PortName;
        Settings.DeviceId = detection.DeviceId;
        PersistUserSettings();

        var portText = detection.PortName ?? "No COM port exposed";
        _dialogs.ShowInfo(
            $"Detected device: {detection.DeviceDescription}\n" +
            $"Suggested profile: {detection.ProfileName}\n" +
            $"Port: {portText}\n" +
            $"Confidence: {detection.ConfidencePercent}%\n\n" +
            $"{detection.Explanation}\n\n" +
            "Use Test Cutter with the blade raised before cutting.",
            "Cutter Detection");
        StatusText = $"Detected {detection.ProfileName} on {portText}.";
    }

    private void ApplyRememberedDevice()
    {
        if (string.IsNullOrWhiteSpace(Settings.DeviceId))
            return;
        var match = _profiles.Detect().FirstOrDefault(result =>
            result.DeviceId.Equals(Settings.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return;
        if (!string.IsNullOrWhiteSpace(match.PortName))
            Settings.PortName = match.PortName;
        StatusText = $"Recognized saved cutter on {match.PortName ?? "USB"}";
    }

    private void ApplyProfile(CutterProfile profile, bool applyDefaultWidth = true)
    {
        _applyingProfile = true;
        try
        {
            Settings.CutterProfile = profile.Name;
            Settings.BaudRate = profile.BaudRate;
            Settings.FlowControl = profile.FlowControl;
            Settings.UnitsPerMm = profile.UnitsPerMm;
            if (applyDefaultWidth)
                Settings.MaterialWidthMm = profile.DefaultWidthMm;
        }
        finally
        {
            _applyingProfile = false;
        }
    }

    private void NotifyCommands()
    {
        PasteCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        InsertProjectCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        TestCutterCommand.NotifyCanExecuteChanged();
        AreaTestCommand.NotifyCanExecuteChanged();
        DetectCutterCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ExportPrintCutCommand.NotifyCanExecuteChanged();
        ActivateLicenseCommand.NotifyCanExecuteChanged();
        CheckUpdateCommand.NotifyCanExecuteChanged();
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                : $"{Math.Max(1, duration.Seconds)}s";

    private TimeSpan EstimateTransferDuration(HpglJob job)
    {
        // 8-N-1 serial framing sends approximately 10 bits per byte.
        var baudRate = Math.Max(1200, Settings.BaudRate);
        return TimeSpan.FromSeconds(job.Commands.Length * 10.0 / baudRate);
    }
}
