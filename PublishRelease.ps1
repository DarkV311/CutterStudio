param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.2"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseRoot = Join-Path $root "release"

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
    -o $clientOut

dotnet publish (Join-Path $root "CutterStudio.Admin\CutterStudio.Admin.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $adminOut

dotnet publish (Join-Path $root "CutterStudio.Updater\CutterStudio.Updater.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $updaterOut

$clientZip = Join-Path $releaseRoot "CutterStudio-win-x64-v$Version.zip"
if (Test-Path -LiteralPath $clientZip) {
    Remove-Item -LiteralPath $clientZip -Force
}
Compress-Archive -Path (Join-Path $clientOut "*") -DestinationPath $clientZip -Force

Write-Host ""
Write-Host "Release output is ready:"
Write-Host "  Client:  $clientOut\CutterStudio.exe"
Write-Host "  Admin:   $adminOut\CutterStudio.Admin.exe"
Write-Host "  Updater: $updaterOut\CutterStudio.Updater.exe"
Write-Host "  ZIP:     $clientZip"
