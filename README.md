# Cutter Studio

Cutter Studio is a lightweight Windows desktop vinyl-cutter application built with C#,
.NET 8, WPF, MVVM, dependency injection, SQLite, and serial-port communication.

## Features

- Paste SVG clipboard data and WPF/XAML vector artwork.
- Import SVG paths, rectangles, circles, ellipses, lines, polylines, and polygons.
- Vector preview with selection, mouse-wheel zoom, middle/right-button pan, and fit-to-screen.
- Dimensions displayed in millimeters.
- Local SQLite project storage with recent-project loading.
- HPGL generation using `IN`, `PU`, `PD`, plus `VS`, `FS`, and absolute positioning.
- Configurable speed, pressure, passes, mirroring, 90-degree rotation, and scaling.
- COM-port detection, configurable baud rate, transfer progress, cancellation, and error handling.
- Weed borders, multiple copies, row-based automatic nesting, material-width validation,
  copy spacing, and cutting-time estimation.
- HPGL export for inspection or use with an external spooler.

## Build and test

Requirements:

- Windows 10/11
- .NET 8 SDK or newer

```powershell
Set-Location F:\Cutter
dotnet restore .\CutterStudio.sln
dotnet build .\CutterStudio.sln -c Release
dotnet test .\CutterStudio.sln -c Release
```

Run the compiled application:

```powershell
& "F:\Cutter\CutterStudio\bin\Release\net8.0-windows\CutterStudio.exe"
```

Create a self-contained Windows x64 build:

```powershell
dotnet publish .\CutterStudio\CutterStudio.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o .\publish\win-x64
```

## Cutter setup

1. Install the USB serial driver supplied with the cutter.
2. Connect and power on the cutter.
3. Start Cutter Studio and click **Refresh**.
4. Select the cutter COM port and its configured baud rate.
5. Import or paste artwork, configure the job, and click **Cut**.

Many desktop cutters use 9600 baud, 8 data bits, no parity, one stop bit, and no
handshake. If the cutter uses different serial framing or plotter units, adjust
`SerialCutterService` or `PlotterUnitsPerMillimeter` in `HpglService`.

## Data locations

Projects and preferences are stored per Windows user:

- `%LOCALAPPDATA%\CutterStudio\projects.db`
- `%LOCALAPPDATA%\CutterStudio\settings.json`

## Architecture

- `Models`: project, artwork, HPGL job, and cutter settings data.
- `Services`: SVG/clipboard import, SQLite repository, HPGL generation, serial transfer,
  dialogs, and user preferences.
- `ViewModels`: observable state and commands.
- `Views`: WPF shell and view-only interaction code.
- `Controls`: interactive vector preview canvas.
- `CutterStudio.Tests`: SVG normalization and HPGL generation tests.

## SVG support notes

The importer is intentionally cutter-focused. It reads vector geometry and transforms,
not visual effects. Text should be converted to paths in the source design program.
Raster images, masks, filters, gradients, and externally referenced content are not
converted into cut paths. SVG clipboard formats and SVG text are supported directly;
clipboard formats that expose only an EMF or bitmap should be exported as SVG first.
