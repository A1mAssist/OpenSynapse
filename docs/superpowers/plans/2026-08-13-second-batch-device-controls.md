# Second-Batch Device Controls Implementation Plan

> **For agentic workers:** Execute each checkbox in order and verify before continuing.

**Goal:** Connect Blade custom performance, logo, and Viper DPI-stage controls to the existing verified hardware setters.

**Architecture:** Extend the existing `MainViewModel` selection-and-apply pattern. Fixed choices remain strong-typed inside the ViewModel; XAML binds display options and editable values; code-behind only passes the window cancellation token. A small row ViewModel represents one editable DPI stage and the existing backend remains the final validator and rollback owner.

**Tech Stack:** C# 14, .NET 10, WinUI 3, Windows App SDK 1.8.

## Global Constraints

- Do not add HID, protocol-builder, polling, DI, or third-party UI code.
- CPU/GPU Boost and Max Fan require a trusted current `Custom` performance-mode readback.
- Logo write targets are only `Off` and `Static`; `Breathing` is read-only.
- DPI stages are submitted once as a complete `ViperDpiStagesTelemetry` table.
- Persist only Max Fan, using the existing Profile field and save flow.
- Do not set `OPENSYNAPSE_HARDWARE_TEST` during tests.

---

### Task 1: Blade second-batch state and operations

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: existing Blade CPU/GPU Boost, Max Fan, and Logo telemetry and setters.
- Produces: option lists, selected values, readback summaries, Custom-aware `CanSet...` properties, and four `Apply...Async` methods.

- [ ] Add fixed strong-typed arrays for CPU Boost, GPU Boost, and Logo write targets.
- [ ] Add editable and confirmed values plus capability flags for CPU Boost, GPU Boost, Max Fan, and Logo.
- [ ] Synchronize each state from telemetry; retain `Breathing` only in the Logo summary and leave its editor unselected.
- [ ] Recalculate Custom-dependent capability properties when performance mode or `IsBusy` changes.
- [ ] Add four operations through `RunDeviceOperationAsync`; save only returned Max Fan state to `_profile.Global.Blade.MaxFanMode`.
- [ ] Clear all state and gates during refresh or disconnect.

### Task 2: Viper DPI-stage editor and operation

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `RazerDeviceTelemetry.ViperDpiStages` and `IRazerDeviceTelemetryReader.SetViperDpiStagesAsync`.
- Produces: `ViperDpiStages`, count/active-stage values, `CanSetViperDpiStages`, `ApplyViperDpiStagesAsync`, and `ViperDpiStageRowViewModel`.

- [ ] Add an observable row collection and a confirmed full-table snapshot.
- [ ] Normalize row X/Y to `100..30000` in steps of 50.
- [ ] Resize only the editing collection when stage count changes, copying the last row on growth and clamping the active stage on shrink.
- [ ] Build one complete `ViperDpiStagesTelemetry` and call the stage setter once.
- [ ] Replace editing and confirmed state with backend readback on success; restore the complete confirmed snapshot on failure.
- [ ] Clear rows and disable editing on refresh or unavailable telemetry.

### Task 3: WinUI controls and click routing

**Files:**
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: Task 1 and Task 2 ViewModel properties and methods.
- Produces: Blade custom-performance setting band, Blade logo control, Viper DPI-stage table, and six lifetime-token click handlers.

- [ ] Add CPU/GPU Boost selectors and Max Fan toggle in one un-nested setting card.
- [ ] Add a separate Logo setting unit inside the Blade lighting card.
- [ ] Add a compact DPI-stage table below the Viper summary grid.
- [ ] Use native `ComboBox`, `ToggleSwitch`, `NumberBox`, and existing icon-button styles with tooltips and automation names.
- [ ] Route all six apply buttons through `_lifetime.Token`.

### Task 4: Regression, visual QA, and adversarial review

**Files:**
- Verify: `tests/OpenSynapse.Core.Tests`
- Verify: `src/OpenSynapse.App`

- [ ] Run `dotnet test .\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore`; expect 325 passing, 9 hardware tests skipped, 0 failing.
- [ ] Run `dotnet build .\src\OpenSynapse.App\OpenSynapse.App.csproj -p:Platform=x64 --no-restore`; require 0 warnings and 0 errors.
- [ ] Launch the app and inspect Blade/Viper pages at normal and narrow widths without clicking hardware write buttons.
- [ ] Attack and fix the five likely failures: stale Custom gates, Logo Breathing leakage, invalid DPI step/count, incomplete DPI rollback, and accidental Profile persistence.

