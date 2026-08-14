# Blade First-Batch Controls Implementation Plan

> **For agentic workers:** Execute each checkbox in order and verify before continuing.

**Goal:** Connect Blade performance mode, charge limit, and internal-display refresh-rate controls to the existing verified backends.

**Architecture:** Extend the existing `MainViewModel` selection-and-apply pattern used by Viper polling rate. Keep protocol values in the ViewModel, bind only display options and selected indexes in XAML, and route clicks through the window lifetime cancellation token.

**Tech Stack:** C# 14, .NET 10, WinUI 3, Windows App SDK 1.8.

## Global Constraints

- Do not add HID, protocol-builder, polling, dependency-injection, or third-party UI code.
- Only expose values accepted by the existing strong-typed setters.
- Persist only actual read-back values returned by the backend.
- Restore the last confirmed selection after a failed write.
- Do not set `OPENSYNAPSE_HARDWARE_TEST` during tests.

---

### Task 1: ViewModel control state and operations

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IRazerDeviceTelemetryReader.SetBladePerformanceModeAsync`, `IRazerDeviceTelemetryReader.SetBladeChargeLimitAsync`, `RunDeviceOperationAsync`.
- Produces: `BladePerformanceModeOptions`, `BladePerformanceModeIndex`, `CanSetBladePerformanceMode`, `ApplyBladePerformanceModeAsync`, `BladeChargeLimitOptions`, `BladeChargeLimitIndex`, `CanSetBladeChargeLimit`, `ApplyBladeChargeLimitAsync`.

- [ ] Add fixed strong-typed mode and charge arrays plus Chinese display options.
- [ ] Add selected index, confirmed index, and capability state for both settings.
- [ ] On telemetry read, synchronize summary, selected index, confirmed index, and capability state.
- [ ] On reset, clear both selections and disable both operations.
- [ ] Implement both apply methods through `RunDeviceOperationAsync`; store returned mode/percentage in `_profile.Global.Blade` and call `SaveProfileAsync`.
- [ ] Build the App project and require 0 warnings and 0 errors.

### Task 2: Native WinUI controls and routing

**Files:**
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: Task 1 ViewModel properties and methods; existing internal-display refresh-rate properties and method.
- Produces: Three `ComboBox` controls and three icon apply buttons with tooltips and automation names.

- [ ] Replace the Blade performance and charge summary cards with setting cards that retain the current read-back text and add bound selectors.
- [ ] Extend the display card with a supported-refresh-rate selector and apply button.
- [ ] Add click handlers that pass `_lifetime.Token` into the three ViewModel apply methods.
- [ ] Build the App project and require 0 warnings and 0 errors.

### Task 3: Regression and visual verification

**Files:**
- Verify: `tests/OpenSynapse.Core.Tests`
- Verify: `src/OpenSynapse.App`

- [ ] Run `dotnet test .\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore` with no hardware-test environment variable.
- [ ] Run `dotnet build .\src\OpenSynapse.App\OpenSynapse.App.csproj -p:Platform=x64 --no-restore` and require 0 warnings and 0 errors.
- [ ] Launch `OpenSynapse.App.exe`, inspect the Blade device page at normal and narrow window widths, and verify text is not clipped.
- [ ] Verify unavailable controls remain disabled when their corresponding telemetry read fails.
- [ ] Review the five highest-risk points: index/value mapping, busy-state notifications, telemetry reset, failure rollback, and Profile persistence.

