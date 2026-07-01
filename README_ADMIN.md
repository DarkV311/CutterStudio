# Cutter Studio Licensing and Updates

This folder now includes three complete executables under `F:\Cutter\dist`:

- Main client: `F:\Cutter\dist\CutterStudio\CutterStudio.exe`
- License/update admin: `F:\Cutter\dist\LicenseAdmin\CutterStudio.Admin.exe`
- Standalone updater helper: `F:\Cutter\dist\Updater\CutterStudio.Updater.exe`

The admin server source is:

- Project: `CutterStudio.Admin`
- Published output: `publish\admin`
- Database: `publish\admin\App_Data\admin.db`
- Uploaded releases / update files: `F:\Cutter\Updates`

## Run locally

```powershell
Set-Location F:\Cutter\dist\LicenseAdmin
$env:CUTTER_ADMIN_PASSWORD = "change-this-password"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5080"
.\CutterStudio.Admin.exe
```

Open:

```text
http://localhost:5080/admin
```

## Admin features

- Create license keys.
- Block/unblock licenses.
- Limit activation count per license.
- Upload release files.
- Publish release metadata.
- Serve update downloads from `/downloads/...`.

## Local update server from your PC

The admin EXE stores uploaded update files in:

```text
F:\Cutter\Updates
```

Run the server on your PC:

```powershell
Set-Location F:\Cutter\dist\LicenseAdmin
$env:CUTTER_ADMIN_PASSWORD = "change-this-password"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5080"
.\CutterStudio.Admin.exe
```

Clients on the same network should use your PC IP:

```text
http://YOUR-PC-IP:5080
```

If Windows Firewall blocks access, allow inbound TCP port `5080`.

To use another updates folder:

```powershell
$env:CUTTER_UPDATES_DIR = "D:\Some\Other\Updates"
```

## Client configuration

In Cutter Studio, open `Licensing & Updates` and set:

```text
Server URL: http://your-server:5080
License key: CUT-....
```

Then use:

- `Activate License`
- `Check Update`

The main app now shows the license activation window before opening the editor
when no activated license is saved.

## GitHub Releases updates

You can use GitHub as the public update host instead of keeping your PC online.

1. Create a GitHub repository, for example `CutterStudio`.
2. Build/publish the client EXE.
3. Zip the publish folder, for example `CutterStudio-win-x64.zip`.
4. Create a GitHub Release with a version tag such as `v1.0.1`.
5. Upload the zip/exe as a Release asset.

In Cutter Studio, open `Licensing & Updates` and set:

```text
Update source: GitHubReleases
GitHub owner: DarkV311
GitHub repo: CutterStudio
```

`Check Update` reads `https://api.github.com/repos/<owner>/<repo>/releases/latest`,
selects the first `.zip`, `.exe`, or `.msi` release asset, compares the release
tag with the app version, then opens the download link if a newer version exists.

## Direct manifest updates

For any static host, put a JSON file online using this format:

```json
{
  "available": true,
  "version": "1.0.1",
  "channel": "stable",
  "downloadUrl": "https://example.com/CutterStudio-win-x64.zip",
  "sha256": "",
  "notes": "Bug fixes and cutter profile updates.",
  "createdUtc": "2026-07-01T12:00:00Z"
}
```

Then set:

```text
Update source: DirectManifest
Direct manifest URL: https://example.com/latest.json
```

## Public API

Activation:

```http
POST /api/licenses/activate
Content-Type: application/json

{
  "licenseKey": "CUT-....",
  "machineId": "...",
  "appVersion": "1.0.0"
}
```

Latest update:

```http
GET /api/releases/latest?channel=stable
```

## Production notes

- Put the server behind HTTPS.
- Set a strong `CUTTER_ADMIN_PASSWORD`.
- Back up `App_Data\admin.db` and `App_Data\releases`.
- Use a reverse proxy such as IIS, Nginx, or Caddy if hosting publicly.
- The client currently opens the download link; it does not yet replace the executable automatically.
