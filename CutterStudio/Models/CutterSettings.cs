using CutterStudio.ViewModels;

namespace CutterStudio.Models;

/// <summary>
/// Cutter and layout options. Values are constrained again by the HPGL service.
/// </summary>
public sealed class CutterSettings : ObservableObject
{
    private string? _portName;
    private string _cutterProfile = "Bascocut CCD Tool 2 (DMPL)";
    private string _flowControl = "RTS/CTS";
    private int _baudRate = 38400;
    private int _speed = 20;
    private int _pressure = 80;
    private int _passes = 1;
    private bool _mirror;
    private bool _rotate90;
    private double _scalePercent = 100;
    private bool _weedBorder;
    private double _weedBorderMarginMm = 3;
    private int _copies = 1;
    private bool _automaticNesting = true;
    private double _materialWidthMm = 600;
    private double _copySpacingMm = 5;
    private double _offsetXmm;
    private double _offsetYmm;
    private double _unitsPerMm = 40;
    private string? _deviceId;
    private double _vinylMarginMm = 5;
    private AreaTestMode _areaTestMode = AreaTestMode.BladeUp;
    private bool _contourCorrectionEnabled;
    private double _contourOffsetXmm;
    private double _contourOffsetYmm;
    private double _contourRotationDeg;
    private double _contourScaleXPercent = 100;
    private double _contourScaleYPercent = 100;
    private double _registrationMarkMarginMm = 10;
    private double _registrationMarkSizeMm = 5;
    private RegistrationMarkStyle _registrationMarkStyle = RegistrationMarkStyle.SquareCorners;
    private bool _cutContourBox;
    private double _contourGapMm = 2;
    private string _licenseServerUrl = "http://localhost:5080";
    private string _licenseKey = "";
    private string _licenseStatus = "Not activated";
    private DateTime? _licenseExpiresUtc;
    private DateTime? _licenseLastCheckedUtc;
    private UpdateSourceKind _updateSource = UpdateSourceKind.GitHubReleases;
    private string _githubOwner = "DarkV311";
    private string _githubRepo = "CutterStudio";
    private string _directManifestUrl = "";

    public string? PortName { get => _portName; set => SetProperty(ref _portName, value); }
    public string CutterProfile { get => _cutterProfile; set => SetProperty(ref _cutterProfile, value); }
    public string FlowControl { get => _flowControl; set => SetProperty(ref _flowControl, value); }
    public int BaudRate { get => _baudRate; set => SetProperty(ref _baudRate, value); }
    public int Speed { get => _speed; set => SetProperty(ref _speed, value); }
    public int Pressure { get => _pressure; set => SetProperty(ref _pressure, value); }
    public int Passes { get => _passes; set => SetProperty(ref _passes, value); }
    public bool Mirror { get => _mirror; set => SetProperty(ref _mirror, value); }
    public bool Rotate90 { get => _rotate90; set => SetProperty(ref _rotate90, value); }
    public double ScalePercent { get => _scalePercent; set => SetProperty(ref _scalePercent, value); }
    public bool WeedBorder { get => _weedBorder; set => SetProperty(ref _weedBorder, value); }
    public double WeedBorderMarginMm { get => _weedBorderMarginMm; set => SetProperty(ref _weedBorderMarginMm, value); }
    public int Copies { get => _copies; set => SetProperty(ref _copies, value); }
    public bool AutomaticNesting { get => _automaticNesting; set => SetProperty(ref _automaticNesting, value); }
    public double MaterialWidthMm { get => _materialWidthMm; set => SetProperty(ref _materialWidthMm, value); }
    public double CopySpacingMm { get => _copySpacingMm; set => SetProperty(ref _copySpacingMm, value); }
    public double OffsetXmm { get => _offsetXmm; set => SetProperty(ref _offsetXmm, value); }
    public double OffsetYmm { get => _offsetYmm; set => SetProperty(ref _offsetYmm, value); }
    public double UnitsPerMm { get => _unitsPerMm; set => SetProperty(ref _unitsPerMm, value); }
    public string? DeviceId { get => _deviceId; set => SetProperty(ref _deviceId, value); }
    public double VinylMarginMm { get => _vinylMarginMm; set => SetProperty(ref _vinylMarginMm, value); }
    public AreaTestMode AreaTestMode { get => _areaTestMode; set => SetProperty(ref _areaTestMode, value); }
    public bool ContourCorrectionEnabled { get => _contourCorrectionEnabled; set => SetProperty(ref _contourCorrectionEnabled, value); }
    public double ContourOffsetXmm { get => _contourOffsetXmm; set => SetProperty(ref _contourOffsetXmm, value); }
    public double ContourOffsetYmm { get => _contourOffsetYmm; set => SetProperty(ref _contourOffsetYmm, value); }
    public double ContourRotationDeg { get => _contourRotationDeg; set => SetProperty(ref _contourRotationDeg, value); }
    public double ContourScaleXPercent { get => _contourScaleXPercent; set => SetProperty(ref _contourScaleXPercent, value); }
    public double ContourScaleYPercent { get => _contourScaleYPercent; set => SetProperty(ref _contourScaleYPercent, value); }
    public double RegistrationMarkMarginMm { get => _registrationMarkMarginMm; set => SetProperty(ref _registrationMarkMarginMm, value); }
    public double RegistrationMarkSizeMm { get => _registrationMarkSizeMm; set => SetProperty(ref _registrationMarkSizeMm, value); }
    public RegistrationMarkStyle RegistrationMarkStyle { get => _registrationMarkStyle; set => SetProperty(ref _registrationMarkStyle, value); }
    public bool CutContourBox { get => _cutContourBox; set => SetProperty(ref _cutContourBox, value); }
    public double ContourGapMm { get => _contourGapMm; set => SetProperty(ref _contourGapMm, value); }
    public string LicenseServerUrl { get => _licenseServerUrl; set => SetProperty(ref _licenseServerUrl, value); }
    public string LicenseKey { get => _licenseKey; set => SetProperty(ref _licenseKey, value); }
    public string LicenseStatus { get => _licenseStatus; set => SetProperty(ref _licenseStatus, value); }
    public DateTime? LicenseExpiresUtc { get => _licenseExpiresUtc; set => SetProperty(ref _licenseExpiresUtc, value); }
    public DateTime? LicenseLastCheckedUtc { get => _licenseLastCheckedUtc; set => SetProperty(ref _licenseLastCheckedUtc, value); }
    public UpdateSourceKind UpdateSource { get => _updateSource; set => SetProperty(ref _updateSource, value); }
    public string GitHubOwner { get => _githubOwner; set => SetProperty(ref _githubOwner, value); }
    public string GitHubRepo { get => _githubRepo; set => SetProperty(ref _githubRepo, value); }
    public string DirectManifestUrl { get => _directManifestUrl; set => SetProperty(ref _directManifestUrl, value); }
}
