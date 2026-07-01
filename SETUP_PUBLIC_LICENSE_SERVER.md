# Public license server setup

GitHub is used for update files only. License activation needs a live API.

The free practical option is ngrok:

1. Create/sign in to an ngrok account.
2. From the ngrok dashboard, copy your auth token and run:

```powershell
ngrok config add-authtoken YOUR_TOKEN
```

3. In the ngrok dashboard, copy your free Dev Domain.
   It looks similar to:

```text
example-name.ngrok-free.app
```

4. Start your public license server:

```powershell
Set-Location F:\Cutter
.\StartPublicLicenseServer.ps1 -NgrokDomain example-name.ngrok-free.app
```

5. Ship this folder to the customer:

```text
F:\Cutter\release\CutterStudio
```

Make sure this file exists beside `CutterStudio.exe`:

```text
F:\Cutter\release\CutterStudio\license-server.json
```

It should contain:

```json
{
  "licenseServerUrl": "https://example-name.ngrok-free.app"
}
```

The customer does not run `CutterStudio.Admin.exe`. Only you run the license admin.
