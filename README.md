# Productivity Tracker

A WinUI desktop app for understanding PC usage through local activity history,
daily timelines, charts, and activity insights.

## Features

- Active and inactive time, foreground application usage, and recorded-day history.
- Weekly and hourly Canvas charts with a read-only ListView timeline.
- Dashboard cards for active share, most-used app, active hours, weekday patterns,
  and longest active/inactive stretches.
- Separate idle, locked, sleeping, and confirmed powered-off intervals.
- Default-on background collection after closing and at sign-in, with separate
  switches and a configurable inactivity threshold.
- English, Hindi, Simplified Chinese, Arabic, Hebrew, Spanish, French, Korean,
  and Japanese, including right-to-left layouts.
- Stable Windows App SDK 2.6.5 with Native AOT Release publishing.

Interaction time is a productivity proxy, not a measure of work output.
Unexplained recording gaps are not treated as inactivity.

## Repository layout

- `ProductivityTracker`: app source, resources, icons, and focused tests.
- `ProductivityTrackerPackage`: MSIX packaging project and Visual Studio solution.

The repository intentionally excludes local usage data, screenshots, logs,
compiled binaries, IDE state, signing certificates, publishing profiles, and
Microsoft Store account association files.

## Build and run

Use Windows with the .NET 8 SDK or newer and Visual Studio Windows application
development tools.

```powershell
git clone https://github.com/godlytalias/ProductivityTracker.git
Set-Location .\ProductivityTracker
dotnet build .\ProductivityTracker\ProductivityTracker.csproj -p:Platform=x64
.\ProductivityTracker\bin\stable\self-contained\x64\Debug\net8.0-windows10.0.17763.0\win-x64\ProductivityTracker.exe
```

Open `ProductivityTrackerPackage\ProductivityTrackerPackage.slnx` in Visual Studio
to work with both projects. Select x64 and set the packaging project as the startup
project for packaged deployment.

The package manifest uses a development identity rather than a production Store
publisher identity. Configure your own identity, Store association, and signing
settings before distribution. Keep account association files and private signing
keys out of Git.

## Native AOT publishing

Release publishing defaults to Native AOT for Windows x64 and ARM64. Install the
matching Visual C++ build tools and Windows SDK and use a developer terminal:

```powershell
dotnet publish .\ProductivityTracker\ProductivityTracker.csproj -c Release -r win-x64 -p:Platform=x64
```

Debug remains managed. Use `-p:PublishAot=false` for a managed Release publish.
Windows x86 remains managed because .NET Native AOT does not support Windows x86.
Build and restore outputs are isolated by deployment mode, AOT mode, and activity
control implementation.

Experimental native Chart/TableView code is preserved behind
`-p:UseExperimentalActivityControls=true`, which selects the pinned experimental
SDK. It is not part of the default stable build.

## Tests

```powershell
dotnet test .\ProductivityTracker\Tests\ProductivityTracker.Tests\ProductivityTracker.Tests.csproj
```

## Privacy

Activity history is stored on the device. The app does not record window titles,
typed text, URLs, screenshots, or clipboard contents, and does not provide a
usage-history upload or cloud-sync service.

**Keep tracking after closing** and **Run at sign-in** are enabled by default.
Change them independently in **Tracking settings**. To stop collection when the
dashboard closes and prevent it restarting at sign-in, turn both off and close
the dashboard.

See the [privacy policy](ProductivityTracker/PRIVACY.md) and
[app documentation](ProductivityTracker/README.md) for collection controls,
storage, platform diagnostics, and limitations.
