param(
    [string]$HostName = "69.169.109.119",
    [string]$UserName = "root",
    [string]$PasswordFile = "F:\Cutter\secrets\vps-root-password.txt",
    [string]$HostKey = "ssh-ed25519 255 SHA256:eR08OKKhkbkTDHzKOks/9e8XbISTXy/Sm+kYXvzeeso",
    [string]$PublishDir = "F:\Cutter\server-publish\LicenseServer",
    [string]$RemoteDir = "/opt/cutter/license-server",
    [string]$AdminPasswordFile = "F:\Cutter\secrets\license-admin-password.txt"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $PasswordFile)) {
    throw "Password file was not found: $PasswordFile"
}

if (-not (Test-Path -LiteralPath $PublishDir)) {
    throw "Publish directory was not found: $PublishDir"
}

$plink = "C:\Program Files\PuTTY\plink.exe"
$pscp = "C:\Program Files\PuTTY\pscp.exe"

if (-not (Test-Path -LiteralPath $plink)) {
    throw "PuTTY plink.exe was not found at $plink"
}

if (-not (Test-Path -LiteralPath $pscp)) {
    throw "PuTTY pscp.exe was not found at $pscp"
}

$rootPassword = Get-Content -LiteralPath $PasswordFile -Raw

if (-not (Test-Path -LiteralPath $AdminPasswordFile)) {
    $chars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%^&*()-_=+"
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $adminPassword = -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $AdminPasswordFile) | Out-Null
    Set-Content -LiteralPath $AdminPasswordFile -Value $adminPassword -NoNewline -Encoding UTF8
} else {
    $adminPassword = Get-Content -LiteralPath $AdminPasswordFile -Raw
}

Write-Host "Creating remote directories..."
& $plink -batch -ssh -hostkey $HostKey -l $UserName -pw $rootPassword $HostName "mkdir -p '$RemoteDir' /opt/cutter/updates && rm -rf '$RemoteDir'/*"
if ($LASTEXITCODE -ne 0) { throw "Failed to prepare remote directory." }

Write-Host "Uploading server files..."
& $pscp -batch -scp -hostkey $HostKey -l $UserName -pw $rootPassword "$PublishDir\*" "${HostName}:$RemoteDir/"
if ($LASTEXITCODE -ne 0) { throw "Failed to upload server files." }

$service = @"
[Unit]
Description=Cutter Studio License Server
After=network.target

[Service]
WorkingDirectory=$RemoteDir
ExecStart=$RemoteDir/CutterStudio.Admin
Restart=always
RestartSec=5
Environment=ASPNETCORE_URLS=http://0.0.0.0:5080
Environment=CUTTER_ADMIN_PASSWORD=$adminPassword
Environment=CUTTER_UPDATES_DIR=/opt/cutter/updates

[Install]
WantedBy=multi-user.target
"@

$encodedService = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($service))

Write-Host "Installing systemd service..."
$remoteInstall = "echo $encodedService | base64 -d > /etc/systemd/system/cutter-license.service && chmod +x '$RemoteDir/CutterStudio.Admin' && systemctl daemon-reload && systemctl enable --now cutter-license && (ufw allow 5080/tcp || true) && systemctl --no-pager --full status cutter-license"
& $plink -batch -ssh -hostkey $HostKey -l $UserName -pw $rootPassword $HostName $remoteInstall
if ($LASTEXITCODE -ne 0) { throw "Failed to install/start service." }

$clientConfigPath = "F:\Cutter\release\CutterStudio\license-server.json"
Set-Content -LiteralPath $clientConfigPath -Value (@{
    licenseServerUrl = "http://$HostName`:5080"
} | ConvertTo-Json) -Encoding UTF8

Write-Host "Done."
Write-Host "Admin URL: http://$HostName`:5080/admin"
Write-Host "Client config updated: $clientConfigPath"
Write-Host "Admin password file: $AdminPasswordFile"

