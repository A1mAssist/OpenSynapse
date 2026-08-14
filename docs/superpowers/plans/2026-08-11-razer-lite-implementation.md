# RazerLite Foundation Implementation Plan

> Superseded by `docs/superpowers/specs/2026-08-11-opensynapse-functional-parity-design.md`. Retained as historical context only.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build the first runnable RazerLite vertical slice: a WinUI 3 desktop shell that discovers the two supported Razer HID identities and exposes a truthful, read-only device snapshot.

**Architecture:** Keep one process with a WinUI 3 front end and separate .NET projects for domain contracts and Windows device discovery. The UI consumes immutable snapshots and never calls HID/WinRT APIs directly. This phase intentionally stops before guessed write reports, fan control, battery writes, tray residency, and sensors that have no verified source.

**Tech Stack:** Visual Studio 2026, .NET 10 SDK already installed, WinUI 3/Windows App SDK, Windows.Devices.Enumeration, xUnit, C# nullable reference types.

## Global Constraints

- Windows 11 only; supported VID/PIDs are `1532:02C6` (Blade 16 2025) and `1532:00B8` (Viper V3 HyperSpeed).
- Unknown PIDs are ignored; no write report is sent until a real device response is recorded and verified.
- Unavailable values are represented as unavailable and displayed as `--`; no fabricated zeroes or stale values.
- The app runs as a normal user process and does not terminate Razer Synapse or install a service.
- Hardware access stays behind the Windows discovery/transport boundary; the UI binds snapshots only.

## File Map

- Create: `RazerLite.sln` - solution entry point.
- Create: `src/RazerLite.Core/RazerLite.Core.csproj` - domain contracts and snapshot state.
- Create: `src/RazerLite.Core/Devices/DeviceDescriptor.cs` - supported device identity and capability state.
- Create: `src/RazerLite.Core/Devices/IDeviceDiscovery.cs` - discovery abstraction.
- Create: `src/RazerLite.Core/Devices/DeviceSnapshot.cs` - immutable UI-facing snapshot.
- Create: `src/RazerLite.Windows/RazerLite.Windows.csproj` - Windows implementation project.
- Create: `src/RazerLite.Windows/Devices/WindowsHidDiscovery.cs` - native SetupAPI/HID enumeration and VID/PID matching.
- Create: `src/RazerLite.Windows/Devices/DeviceIdParser.cs` - deterministic VID/PID parsing.
- Create: `src/RazerLite.App/RazerLite.App.csproj` - WinUI 3 unpackaged app with `Microsoft.WindowsAppSDK` `1.8.260710003`.
- Create: `src/RazerLite.App/App.xaml`, `src/RazerLite.App/App.xaml.cs` - WinUI application bootstrap.
- Create: `src/RazerLite.App/MainWindow.xaml`, `src/RazerLite.App/MainWindow.xaml.cs` - shell and refresh action.
- Create: `src/RazerLite.App/ViewModels/MainViewModel.cs` - snapshot loading and UI state.
- Create: `tests/RazerLite.Core.Tests/RazerLite.Core.Tests.csproj` - xUnit test project.
- Create: `tests/RazerLite.Core.Tests/DeviceIdParserTests.cs` - parser boundary tests.
- Create: `app.manifest` - Windows 10+ compatibility and DPI declaration.
- Create: `global.json` - pin the installed .NET 10 SDK family.

### Task 1: Create solution and core contracts

**Files:**
- Create: `RazerLite.sln`
- Create: `global.json`
- Create: `src/RazerLite.Core/RazerLite.Core.csproj`
- Create: `src/RazerLite.Core/Devices/DeviceDescriptor.cs`
- Create: `src/RazerLite.Core/Devices/IDeviceDiscovery.cs`
- Create: `src/RazerLite.Core/Devices/DeviceSnapshot.cs`

**Interfaces:**
- Produces `DeviceDescriptor`, `DeviceSnapshot`, and `IDeviceDiscovery` for the Windows and UI projects.

- [ ] **Step 1: Create the solution and projects**

Run:

```powershell
dotnet new sln -n RazerLite
dotnet new classlib -n RazerLite.Core -o src/RazerLite.Core --framework net10.0
dotnet sln RazerLite.sln add src/RazerLite.Core/RazerLite.Core.csproj
```

- [ ] **Step 2: Define the identity and snapshot contracts**

`DeviceDescriptor` stores only verified identity data. `DeviceSnapshot` carries a read-only list and a timestamp; no UI type is used here.

- [ ] **Step 3: Build the core project**

Run `dotnet build src/RazerLite.Core/RazerLite.Core.csproj` and expect `Build succeeded`.

### Task 2: Implement and test VID/PID discovery parsing

**Files:**
- Create: `src/RazerLite.Windows/RazerLite.Windows.csproj`
- Create: `src/RazerLite.Windows/Devices/DeviceIdParser.cs`
- Create: `src/RazerLite.Windows/Devices/WinRtDeviceDiscovery.cs`
- Create: `tests/RazerLite.Core.Tests/RazerLite.Core.Tests.csproj`
- Create: `tests/RazerLite.Core.Tests/DeviceIdParserTests.cs`

**Interfaces:**
- `DeviceIdParser.TryParse(string?, out ushort vid, out ushort pid)` returns `false` for missing or malformed IDs.
- `WindowsHidDiscovery : IDeviceDiscovery` returns only the two supported PIDs and never writes to a device.

- [ ] **Step 1: Add the failing parser tests**

Test valid uppercase/lowercase IDs, an unrelated PID, and malformed input. Run `dotnet test tests/RazerLite.Core.Tests/RazerLite.Core.Tests.csproj`; the tests should fail because the parser is absent.

- [ ] **Step 2: Implement the smallest parser**

Use one compiled regular expression for `VID_####` and `PID_####`, parse hexadecimal values with `NumberStyles.HexNumber`, and reject values outside `ushort`.

- [ ] **Step 3: Implement WinRT enumeration**

Call `DeviceInformation.FindAllAsync(DeviceClass.HumanInterfaceDevice)`, parse each `DeviceInformation.Id`, map supported PIDs to friendly names, and return a snapshot with an `Unsupported` capability state for unverified writes.

- [ ] **Step 4: Run the focused tests and build**

Run `dotnet test tests/RazerLite.Core.Tests/RazerLite.Core.Tests.csproj` and `dotnet build src/RazerLite.Windows/RazerLite.Windows.csproj`.

### Task 3: Create the WinUI 3 unpackaged shell

**Files:**
- Create: `src/RazerLite.App/RazerLite.App.csproj`
- Create: `src/RazerLite.App/app.manifest`
- Create: `src/RazerLite.App/App.xaml`
- Create: `src/RazerLite.App/App.xaml.cs`
- Create: `src/RazerLite.App/MainWindow.xaml`
- Create: `src/RazerLite.App/MainWindow.xaml.cs`
- Create: `src/RazerLite.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- `MainViewModel.RefreshAsync()` calls `IDeviceDiscovery.DiscoverAsync(CancellationToken)` and exposes `Devices`, `LastRefreshText`, and `ErrorText`.
- The app project references `RazerLite.Core` and `RazerLite.Windows` and targets `net10.0-windows10.0.19041.0` with `UseWinUI=true`.

- [ ] **Step 1: Add the WinUI project and package reference**

Create `src/RazerLite.App/RazerLite.App.csproj` with `Microsoft.NET.Sdk`, `OutputType=WinExe`, `TargetFramework=net10.0-windows10.0.19041.0`, `TargetPlatformMinVersion=10.0.19041.0`, `UseWinUI=true`, `Platforms=x64`, `RuntimeIdentifiers=win-x64`, and an explicit `<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.260710003" />`; add `RazerLite.Core` and `RazerLite.Windows` project references, then run `dotnet restore src/RazerLite.App/RazerLite.App.csproj`.

- [ ] **Step 2: Add the shell layout**

Use a compact navigation layout with Performance, Lighting, Mouse, and Profiles sections. The first three sections show capability status; only Performance is populated by real discovery data in this phase.

- [ ] **Step 3: Wire the view model**

Construct `WinRtDeviceDiscovery` in `App`, inject it into `MainViewModel`, refresh on window load, and expose a manual refresh button. No UI event handler accesses WinRT directly.

- [ ] **Step 4: Build and run the app**

Run `dotnet build src/RazerLite.App/RazerLite.App.csproj -p:Platform=x64` and launch the generated executable. With no supported hardware connected, the window must show an empty device state and `--`/unavailable text rather than zeroes.

### Task 4: Adversarial verification and handoff

**Files:**
- Modify: `2026-08-11-razer-lite-design.md` only if the implementation reveals a factual correction.
- Create: `README.md` with setup, build, and hardware-test commands.

- [ ] **Step 1: Run all automated checks**

Run `dotnet test` and `dotnet build RazerLite.sln -p:Platform=x64`.

- [ ] **Step 2: Check the failure paths**

Verify malformed IDs, no matching devices, WinRT enumeration exceptions, and cancellation all leave the UI in a truthful unavailable state.

- [ ] **Step 3: Review the top failure risks**

Check that no unknown PID is shown as supported, no write report exists, no stale snapshot survives a failed refresh, and the UI has no direct hardware dependency.

- [ ] **Step 4: Document the current ceiling**

README must state that fan, battery, performance-mode, lighting, DPI, polling-rate, tray, profiles, and sensors are not yet enabled until real protocol evidence exists.
