param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseRoot = Join-Path $root "release"
$versionParts = $Version.Split('.', [StringSplitOptions]::RemoveEmptyEntries)
if ($versionParts.Length -lt 2) {
    throw "Version must be like 0.1, 0.2, or 1.0.0"
}
$assemblyVersion = if ($versionParts.Length -eq 2) {
    "$Version.0.0"
} elseif ($versionParts.Length -eq 3) {
    "$Version.0"
} else {
    $Version
}

$clientOut = Join-Path $releaseRoot "CutterStudio"
$adminOut = Join-Path $releaseRoot "LicenseAdmin"
$updaterOut = Join-Path $releaseRoot "Updater"

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

foreach ($path in @($clientOut, $adminOut, $updaterOut)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

dotnet publish (Join-Path $root "CutterStudio\CutterStudio.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -o $clientOut

dotnet publish (Join-Path $root "CutterStudio.LicenseAdmin\CutterStudio.LicenseAdmin.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -o $adminOut

dotnet publish (Join-Path $root "CutterStudio.UpdatePublisher\CutterStudio.UpdatePublisher.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -o $updaterOut

$clientZip = Join-Path $releaseRoot "CutterStudio-win-x64-v$Version.zip"
if (Test-Path -LiteralPath $clientZip) {
    Remove-Item -LiteralPath $clientZip -Force
}
Compress-Archive -Path (Join-Path $clientOut "*") -DestinationPath $clientZip -Force

Write-Host ""
Write-Host "Release output is ready:"
Write-Host "  Version: $Version"
Write-Host "  Client:  $clientOut\CutterStudio.exe"
Write-Host "  Admin:   $adminOut\CutterStudio.Admin.exe"
Write-Host "  Updater: $updaterOut\CutterStudio.Updater.exe"
Write-Host "  ZIP:     $clientZip"
