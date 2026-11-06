# Productivity Tracker

Productivity Tracker is a standalone WinUI desktop app that tracks foreground applications and distinguishes active and inactive time.

## Features

- Active and inactive totals for any selected day
- Canvas-based seven-day column and selected-day hourly line charts, with gaps for unrecorded periods
- A native chart-range selector in the same chart area
- A read-only ListView timeline with start time, end time, application, duration, and state
- Responsive dashboard cards grouped into selected-day insights and 28-day weekday patterns
- Activity-based insights: active share, peak and quiet recorded hours, longest active/inactive stretches, and peak/quiet weekdays
- A most-used-app card with selected-day active duration and share of active time
- A configurable inactive threshold
- Separate idle, locked, sleeping and confirmed powered-off states, with an inactive-time breakdown
- English, Hindi, Simplified Chinese, Arabic, Hebrew, Spanish, French, Korean and Japanese resources
- Separate default-on settings to keep tracking after closing and start a headless collector at sign-in
- Local JSON storage only

## Run

The default build uses stable `Microsoft.WindowsAppSDK` version `2.6.5`, with
Canvas charts and a ListView timeline. It does not compile or load experimental
Chart/TableView types, resource dictionaries or component PRIs.

The native Chart and TableView implementation is retained in
`MainWindow.ExperimentalControls.cs`, excluded from compilation by default.
For preview-only development, `-p:UseExperimentalActivityControls=true` selects
that implementation and the pinned `2.5.4-experimental` SDK. When these controls
reach GA, update that SDK selection before enabling the native implementation
for production. Rebuild when switching paths to replace stale runtime binaries.

Install the .NET 8 SDK or newer and the Visual Studio Windows app development tools.
Build the unpackaged C# project from its own directory; the WinUI repository is not required:

```powershell
# From the repository root
Set-Location .\ProductivityTracker
dotnet build .\ProductivityTracker.csproj -p:Platform=x64
```

Run the dashboard:

```powershell
.\bin\stable\self-contained\x64\Debug\net8.0-windows10.0.17763.0\win-x64\ProductivityTracker.exe
```

Packaged, framework-dependent and self-contained builds have separate `bin` and
`obj` folders so Visual Studio deployment cannot reuse an incompatible startup
initializer. The packaging project rebuilds its framework-dependent app variant.

Release publishing uses Native AOT for x64 and ARM64. Install the Visual Studio
C++ build tools and Windows SDK, then publish from a matching developer terminal:

```powershell
dotnet publish .\ProductivityTracker.csproj -c Release -r win-x64 -p:Platform=x64
```

The standalone native executable is under
`bin\stable\self-contained\aot\x64\Release\net8.0-windows10.0.17763.0\win-x64\publish`.
Native AOT and managed outputs are isolated. Debug builds remain managed for
debugging; x86 remains managed because .NET Native AOT does not support Windows x86.
Use `-p:PublishAot=false` for a managed Release publish.

The window requests Windows-managed rounded corners on activation and resizing.
No custom clipping region is applied; Windows can keep corners square when
maximized, snapped, or running in a remote/virtual-machine session.

The same executable accepts `--worker` to collect activity without opening a window.
The interface follows Windows language preferences, with English as the fallback.
To select a supported language for a launch, use `--language=hi-IN`, `--language=zh-CN`,
`--language=ar-SA`, `--language=he-IL`, `--language=es-ES`, `--language=fr-FR`,
`--language=ko-KR`, `--language=ja-JP`, or `--language=en-US`.
Arabic and Hebrew use a right-to-left dashboard; chart time axes retain chronological
left-to-right order. Resources live in `Strings\<language>\Resources.resw`.
The original clock-and-checkmark artwork is in `Assets`, including an editable SVG,
64px and 256px PNGs, and a multi-resolution ICO used by the executable, title bar,
taskbar, and dashboard.

## Privacy and tracking behavior

The app uses Windows idle-input and foreground-window APIs. It stores process names, activity state, and timestamps under `%LOCALAPPDATA%\ProductivityTracker`. It does not record window titles, keystrokes, typed content, URLs, screenshots, or clipboard data.

Tracking begins when the dashboard launches if the windowless collector is not
already running. Pause or resume foreground-application collection from the toolbar.
**Keep tracking after closing** is enabled by default: closing the dashboard
flushes its final samples and hands recording to a windowless `--worker` collector.
The dashboard waits for the collector to become ready before closing. If hand-off
fails, it stays open and explains the error rather than silently stopping tracking.
A storage lease prevents the dashboard and collector from writing competing history.

While collection is running, Windows session notifications identify lock/unlock
without treating recent input on the lock screen as active application time.
Suspend/resume notifications delimit sleep. The collector flushes on suspend and
session end, and reconciles readable Windows System events on start, resume and
periodically. Confirmed sleep/Modern Standby pairs and clean shutdown/startup pairs
can fill power-state periods even when the process was not executing. Intervals are
clipped to when this app first observed usage and merged without double-counting.

A process cannot collect while the PC is asleep or switched off. Recovered intervals
therefore depend on retained, accessible Windows events. An unexplained restart,
power loss, cleared/inaccessible log, or ordinary app-closed gap is **not** guessed
to be inactive or powered off. Locks while the collector is not running are not
reconstructed from privileged Security logs. No administrator access or OS audit
policy changes are requested.

Open **Tracking settings** (the gear button) to change the inactivity threshold in
seconds and configure **Keep tracking after closing** and **Run at sign-in**.
Both switches are enabled by default and are saved independently.
**Run at sign-in** creates a per-user `ProductivityTracker`
scheduled task. The task starts `ProductivityTracker.exe --worker` without opening
the WinUI window. Installed packages use the registered Windows execution alias
so the collector retains package identity and framework access. Turning the setting
off removes the startup entry without stopping an already-running collector.

Turning **Keep tracking after closing** off makes closing the dashboard stop the
collector as well. If **Run at sign-in** remains on, collection starts again at the
next sign-in. Turn both off and close the dashboard to stop recording and prevent
automatic restart. No Windows service or administrator privileges are required;
the collector runs in your signed-in desktop session so foreground-app APIs work.

If Windows denies scheduled-task creation, the app falls back to a per-user
Startup-folder launcher named `ProductivityTrackerWorker.cmd`.

## Reading the insights

Use the calendar picker, Previous/Next, or **Today** to select a day. The **7 days /
By hour** selector inside the Activity card changes only the chart and keeps the
selected date. Unrecorded hours are gaps, not zeros. Hours use the PC's current local
time zone; repeated daylight-saving hours are combined.

The six selected-day cards and two weekday-pattern cards reflow across the
dashboard instead of hiding in a sidebar. The chart and timeline share a
row on desktop widths and stack on narrow windows; the timeline keeps its own
bounded scrolling area. Empty days have explicit messages rather than bare axes.
Use **About these insights** for definitions, Alt+Left/Right to navigate dates, and
F5 to refresh.

Idle, locked, sleeping and confirmed powered-off periods appear in inactive totals,
charts and the timeline. Sleep and powered-off time are **excluded** from active-share
percentages and hour/weekday rankings; locked time remains awake inactivity.
The inactive breakdown shows each category so overnight power-off time is not
mistaken for an unproductive working hour.

Hour rankings compare active time as a share of recorded awake time. Weekday patterns
use the 28 calendar days ending on the selected date, compare the same share across
recorded awake time, and show recorded-day counts and average durations for context.
Missing days and hours are excluded. Identical shares do not produce a made-up
"least active" ranking.

Active stretches can span application switches and sampling gaps of at most three
seconds; observed inactivity or longer unrecorded gaps break them. All stretches
are clipped to the selected day. Active interaction is only a productivity proxy:
the app does not classify applications or measure work output.

Run the focused analytics tests without starting WinUI:

```powershell
dotnet test .\Tests\ProductivityTracker.Tests\ProductivityTracker.Tests.csproj
```
