param(
    [Parameter(Mandatory = $true)]
    [string]$NgrokDomain,

    [string]$LicenseAdminPath = "F:\Cutter\release\LicenseAdmin\CutterStudio.Admin.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command ngrok -ErrorAction SilentlyContinue)) {
    throw "ngrok is not installed or not available in PATH. Restart PowerShell or install ngrok first."
}

if (-not (Test-Path -LiteralPath $LicenseAdminPath)) {
    throw "License admin EXE was not found: $LicenseAdminPath"
}

$existingAdmin = Get-Process CutterStudio.Admin -ErrorAction SilentlyContinue
if (-not $existingAdmin) {
    Start-Process -FilePath $LicenseAdminPath -WindowStyle Normal
    Start-Sleep -Seconds 3
}

Start-Process -FilePath "ngrok" -ArgumentList @("http", "--domain=$NgrokDomain", "5080") -WindowStyle Normal

$publicUrl = "https://$NgrokDomain"
$clientConfigPath = "F:\Cutter\release\CutterStudio\license-server.json"
@{
    licenseServerUrl = $publicUrl
} | ConvertTo-Json | Set-Content -LiteralPath $clientConfigPath -Encoding UTF8

Write-Host "Public license URL: $publicUrl"
Write-Host "Client config written: $clientConfigPath"
