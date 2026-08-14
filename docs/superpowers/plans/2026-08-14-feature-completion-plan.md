# OpenSynapse Feature Completion Implementation Plan

> **For agentic workers:** Execute the tasks in order and verify each task before moving on.

**Goal:** Finish the already-supported OpenSynapse device workflow by making lighting runtime failures observable, wiring the existing profile/startup backends into the WinUI surface, and preserving all protocol safety gates.

**Architecture:** Keep the current `MainViewModel` as the single UI boundary. The HID and profile implementations remain behind existing Core/Windows classes; XAML binds only to ViewModel state and click handlers. Lighting runtime errors are converted at the controller boundary into a task-visible completion instead of being silently detached.

**Tech Stack:** .NET 10, WinUI 3, C#, x64, existing MSTest test project.

## Global Constraints

- Do not add new dependencies or a DI container.
- Do not expose unverified fan curves, Logo breathing, GPU MUX, macros, Viper calibration, or raw report editing.
- Every hardware write remains gated by successful read-back and current-device validation.
- CPU/GPU voltage remains removed.
- Do not stop or modify the running OpenSynapse process.

### Task 1: Lighting Runtime Failure Propagation

**Files:**
- Modify: `src/OpenSynapse.Windows/Lighting/BladeLightingController.cs`
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeLightingControllerTests.cs`

- [x] Make the controller retain a runtime completion task and observe it when the runtime stops. Preserve the original HID exception and clear the active runtime before reporting it.
- [x] Ensure `StopCoreAsync` awaits disposal and does not leak an unobserved runtime fault.
- [x] Keep `RunDeviceOperationAsync` handling `AggregateException` by reporting its inner hardware failure message.
- [x] Add one focused test proving a post-first-frame transport failure completes the controller operation with a visible failure path.

### Task 2: Profile and Startup ViewModel Surface

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Modify: `src/OpenSynapse.App/App.xaml.cs`
- Modify: `src/OpenSynapse.Windows/Lifecycle/WindowsStartupManager.cs` only if constructor/path access is needed
- Test: `tests/OpenSynapse.Core.Tests/ProfileCatalogTests.cs` or an existing ViewModel test file

- [x] Expose the active profile name/list, startup-enabled state, and minimal create/select/delete commands through the existing ViewModel.
- [x] Reuse `ProfileCatalog` and `ProfileStore`; do not manipulate JSON in XAML or add a second persistence path.
- [x] Add a startup toggle using the current executable path and surface registry errors through the existing device/application error channel.
- [x] Preserve the invariant that the final profile cannot be deleted.

### Task 3: Profile and Settings XAML

**Files:**
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`

- [x] Replace the one-line profile status card with a compact profile selector, create/delete actions, and startup toggle.
- [x] Keep existing navigation, Mica, theme handling, tray behavior, and stable control dimensions.
- [x] Bind all controls to ViewModel properties and route operations through ViewModel methods.
- [x] Use `AutomationProperties.Name` and tooltips for icon-only actions.

### Task 4: Verification and Adversarial Review

**Files:**
- No production changes unless a verification issue is found.

- [x] Run the full non-hardware test suite.
- [x] Build the solution in Release x64 using an independent output path so the running app is not touched.
- [x] Review five failure risks: detached runtime task, stale profile selection after delete, startup registry failure, invalid DPI/profile values, and accidental exposure of prohibited controls.
- [x] Fix any finding, then rerun tests and build.

Clone/rename, application binding, and profile import/export remain intentionally deferred; the existing Core APIs are unchanged and no misleading partial UI was added.
