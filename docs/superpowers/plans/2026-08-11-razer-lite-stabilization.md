# RazerLite Stabilization and Blade Controls Implementation Plan

> Superseded by `docs/superpowers/specs/2026-08-11-opensynapse-functional-parity-design.md`. Retained as historical context only.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the existing WinUI 3 probe into a stable daily-use slice with truthful device lifecycle handling and readback-verified Blade performance and battery controls.

**Architecture:** Keep hardware access in `RazerLite.Windows` behind `IRazerDeviceTelemetryReader`. Extend the immutable telemetry contract in `RazerLite.Core`, let the ViewModel enable each control only after the matching read succeeds, and make every supported write perform an immediate GET readback. A lightweight discovery loop detects path/access changes without repeatedly sending feature reports.

**Tech Stack:** C# 14, .NET 10, WinUI 3 / Windows App SDK 1.8, native Windows HID feature reports, xUnit.

## Global Constraints

- Windows 11 only; supported VID/PIDs remain `1532:02C6` and `1532:00B8`.
- Do not send commands to unknown PIDs or a HID collection other than Usage Page `0001`, Usage `0002`, Feature Report length `91`.
- A write control stays disabled until its exact GET command succeeds on the current device path.
- Every exposed write requires immediate readback; a mismatch is an error and must not update the displayed state.
- Manual fan RPM writes remain out of this milestone even though public `02C6` implementations exist; first verify recovery and thermal safety on this machine.
- Missing data is `null` and displays as `--`, never a fabricated zero.
- Automated verification must not send hardware writes unless `RAZERLITE_HARDWARE_TEST=1` is explicitly set.

---

### Task 1: Correct report matching and input boundaries

**Files:**
- Modify: `src/RazerLite.Windows/Protocols/RazerFeatureReport.cs`
- Modify: `src/RazerLite.Windows/Protocols/RazerFeatureTransport.cs`
- Modify: `src/RazerLite.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Modify: `src/RazerLite.App/MainWindow.xaml`
- Modify: `tests/RazerLite.Core.Tests/DeviceIdParserTests.cs`

**Interfaces:**
- `RazerFeatureReport.Matches(request, response, allowRemainingPacketsMismatch)` validates transaction ID, command, CRC, and normally the remaining-packet field.
- Viper DPI accepts only `100..30000` on both UI and protocol boundaries.

- [ ] **Step 1: Add failing tests for mismatched transaction IDs and DPI values below 100.**
- [ ] **Step 2: Run `dotnet test tests\RazerLite.Core.Tests\RazerLite.Core.Tests.csproj` and verify the new assertions fail.**
- [ ] **Step 3: Match byte 2 transaction IDs, keep the explicit `0x0792` read exception narrow, and change both DPI bounds to 100.**
- [ ] **Step 4: Re-run the focused tests and verify they pass.**

### Task 2: Add exact Blade platform telemetry and readback controls

**Files:**
- Modify: `src/RazerLite.Core/Devices/RazerDeviceTelemetry.cs`
- Modify: `src/RazerLite.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Create: `tests/RazerLite.Core.Tests/RazerDeviceTelemetryReaderTests.cs`

**Interfaces:**
- `BladePerformanceMode` represents the observed firmware values `0, 2, 4, 5, 6, 7`.
- `RazerDeviceTelemetry` exposes nullable Blade performance mode, fan mode/setpoint, and charge limit.
- `SetBladePerformanceModeAsync` writes both thermal zones and then reads both zones back.
- `SetBladeChargeLimitAsync` writes only `50, 55, 60, 65, 70, 75, 80, 100` and then reads command `0x0792` back.

- [ ] **Step 1: Add queued fake-transport tests for successful reads, mismatched fan zones, charge-limit mapping, and write/readback mismatch.**
- [ ] **Step 2: Run the focused tests and verify they fail before the interfaces exist.**
- [ ] **Step 3: Implement GET `0x0D82`, optional GET `0x0D81`, GET `0x0792`, SET `0x0D02`, and SET `0x0712` using the existing serialized transport.**
- [ ] **Step 4: Re-run tests and verify no test requires connected hardware.**

### Task 3: Make device and sampler lifecycle resilient

**Files:**
- Modify: `src/RazerLite.App/ViewModels/MainViewModel.cs`
- Modify: `src/RazerLite.App/MainWindow.xaml.cs`

**Interfaces:**
- `RunDeviceWatchLoopAsync` performs discovery-only polling and refreshes telemetry only when path/access state changes.
- Background loop failures become visible error state instead of terminating an unobserved task.
- Percentage formatting accepts nullable percentages so unavailable memory/storage remains `--`.

- [ ] **Step 1: Track a stable device fingerprint after every discovery result.**
- [ ] **Step 2: Add a cancellation-aware discovery loop and start it beside the performance loop.**
- [ ] **Step 3: Catch expected sampler failures, retain other UI state, and surface the error.**
- [ ] **Step 4: Change unavailable percentage calculations from `0` to `null`.**

### Task 4: Expose only proven Blade controls in WinUI

**Files:**
- Modify: `src/RazerLite.App/ViewModels/MainViewModel.cs`
- Modify: `src/RazerLite.App/MainWindow.xaml`
- Modify: `src/RazerLite.App/MainWindow.xaml.cs`

**Interfaces:**
- The Performance page shows Blade mode and fan state; mode apply is enabled only after a valid mode read.
- The Lighting/device area shows the charge limit; apply is enabled only after a valid `0x0792` read.
- Manual fan remains read-only in this milestone.

- [ ] **Step 1: Add ViewModel properties, mapping helpers, and readback operation handlers.**
- [ ] **Step 2: Add compact mode and charge-limit controls with fixed enumerated choices.**
- [ ] **Step 3: Wire event handlers without calling Windows HID APIs from code-behind.**
- [ ] **Step 4: Build the x64 app and correct all XAML binding/compiler errors.**

### Task 5: Verify and document the truthful ceiling

**Files:**
- Modify: `README.md`
- Modify: `tests/RazerLite.Core.Tests/DeviceIdParserTests.cs`
- Modify: `tests/RazerLite.Core.Tests/RazerDeviceTelemetryReaderTests.cs`

- [ ] **Step 1: Run `dotnet test tests\RazerLite.Core.Tests\RazerLite.Core.Tests.csproj` and record pass/skip counts.**
- [ ] **Step 2: Run `dotnet build RazerLite.slnx` and `dotnet build src\RazerLite.App\RazerLite.App.csproj -p:Platform=x64`.**
- [ ] **Step 3: Launch the app only for a read-only startup check; do not click apply controls.**
- [ ] **Step 4: Review the most likely failures: stale HID response, hot-unplug during a write, split-zone Blade state, unsupported firmware mode, and inaccurate documentation.**
- [ ] **Step 5: Update README so implemented, hardware-verified, protocol-evidenced, and blocked features are not conflated.**
