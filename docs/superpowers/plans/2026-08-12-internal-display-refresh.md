# Internal Display Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fail-closed Windows backend for the built-in panel's refresh-rate state, supported-rate enumeration, explicit switching, readback, and profile persistence without ever targeting an external monitor.

**Architecture:** Keep Windows display topology separate from Razer HID telemetry. `OpenSynapse.Core` owns the small `IInternalDisplayController` contract and immutable snapshot; `OpenSynapse.Windows` owns the P/Invoke implementation. The application reads display state independently from HID refreshes and applies only a profile rate proven to exist for the current internal source.

**Tech Stack:** .NET 10, C# BCL P/Invoke, Windows Display Configuration API, `EnumDisplaySettingsExW`/`ChangeDisplaySettingsExW`, existing `System.Text.Json` profiles and xUnit tests.

## Global Constraints

- Only the active target technology values `LVDS=6`, `DISPLAYPORT_EMBEDDED=11`, `UDI_EMBEDDED=13`, and `INTERNAL=0x80000000` count as an internal panel.
- Query flags are `QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE | QDC_VIRTUAL_REFRESH_RATE_AWARE`.
- `EnumDisplaySettingsExW` and `ChangeDisplaySettingsExW` always receive the non-empty GDI source name returned by `DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME`; never pass `null` or infer identity from the primary/window display.
- Zero or multiple active internal paths, a shared source with an external path, a missing source name, an unsupported rate, or a topology race fails closed and performs no display write.
- Use `CDS_TEST` before applying. After applying, query the same source and require readback. On failure, attempt the original mode once and report the original error.
- Add `BladeProfileSettings.RefreshRateHertz` as nullable JSON v1 data; old profile files remain valid.
- Display query/application errors are isolated from HID device discovery and do not clear the device list.
- No NuGet package, Windows service, vendor DLL, or guessed Razer command.

---

### Task 1: Add the Core display contract and profile field

**Files:**
- Create: `src/OpenSynapse.Core/Displays/InternalDisplay.cs`
- Modify: `src/OpenSynapse.Core/Profiles/ProfileModels.cs`
- Modify: `src/OpenSynapse.Core/Profiles/ProfileResolver.cs`
- Test: `tests/OpenSynapse.Core.Tests/ProfileResolverTests.cs`
- Test: `tests/OpenSynapse.Core.Tests/ProfileStoreTests.cs`

**Interfaces:**
- `InternalDisplaySnapshot(int Width, int Height, int RefreshRateHertz, IReadOnlyList<int> SupportedRefreshRates)`.
- `IInternalDisplayController.Read()` returns a snapshot or throws an expected Windows error.
- `IInternalDisplayController.SetRefreshRate(int refreshRateHertz)` validates and applies one enumerated rate, then returns a read-back snapshot.

- [x] Add the nullable `RefreshRateHertz` field to `BladeProfileSettings` and copy it in `CloneBlade`.
- [x] Resolve it with existing precedence `power > device > global`; leave it null when the power state is unknown.
- [x] Add tests for plugged-in and battery overrides, unknown power state, profile JSON round-trip, and cloned-profile independence.
- [x] Run `dotnet test .\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProfileResolver|FullyQualifiedName~ProfileStore|FullyQualifiedName~ProfileCatalog'` and require PASS.

### Task 2: Implement fail-closed Windows topology and mode switching

**Files:**
- Create: `src/OpenSynapse.Windows/Displays/WindowsInternalDisplayController.cs`
- Test: `tests/OpenSynapse.Core.Tests/WindowsInternalDisplayControllerTests.cs`

**Interfaces:**
- The controller accepts an optional internal native API seam in its non-public constructor for deterministic tests; production uses the real P/Invoke implementation.
- The topology helper returns exactly one validated source name plus current width/height and unique rates for that resolution.

- [x] Implement bounded `GetDisplayConfigBufferSizes`/`QueryDisplayConfig` retry on `ERROR_INSUFFICIENT_BUFFER`.
- [x] Select only active paths whose output technology is one of the four internal values.
- [x] Reject zero/multiple internal paths, empty GDI source names, and any external active path sharing the internal `(adapterId, sourceId)`.
- [x] Enumerate `DEVMODEW` modes for the selected source, retain the current source resolution, and de-duplicate positive refresh rates.
- [x] For a set, resolve the requested rate from the current mode list, preserve the original mode, call `CDS_TEST`, apply using `CDS_UPDATEREGISTRY`, read back, and restore once if anything fails.
- [x] Add fake-native tests for technology filtering, clone rejection, resolution filtering, de-duplication, unsupported-rate no-write, test/apply failure, and readback mismatch recovery.
- [x] Run the focused controller tests and require PASS.

### Task 3: Isolate display refresh in the running application

**Files:**
- Modify: `src/OpenSynapse.App/App.xaml.cs`
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- `MainViewModel` receives `IInternalDisplayController?` after existing optional dependencies, preserving existing test construction.
- It keeps display state/error independently from `RazerDeviceTelemetry` and `VerifiedProfileApplier`.

- [x] Read display state during initialization and every explicit/full refresh, with a separate expected-error catch.
- [x] On power-state transitions, resolve the Blade profile's nullable rate, require it in `SupportedRefreshRates`, set it, save the actual read-back rate only after success, and update the confirmed value on failure without changing the profile.
- [x] Update `_lastPowerState` even when display read/apply fails so a failure cannot cause a repeated write every watch-loop tick.
- [x] Inject `WindowsInternalDisplayController` from `App.OnLaunched`.
- [x] Keep display failures from clearing HID descriptors or suppressing existing device errors.

### Task 4: Verify, document, and adversarially audit

**Files:**
- Modify: `docs/device-capability-matrix.md`
- Modify: `README.md`

- [x] Run full `dotnet test .\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore` and `dotnet build .\OpenSynapse.slnx --no-restore`.
- [x] Run a local read-only probe and record the current internal source, resolution, current rate, and supported rates without changing the user's display.
- [x] Manually inspect the external-primary and clone-topology failure paths; no write may occur.
- [x] Review for five failure modes: null/primary display targeting, stale topology after buffer resize, duplicate mode ambiguity, display failure clearing HID state, and profile mutation before readback.
- [x] Mark only the documented Windows display portions as Native; do not claim vendor-only GPU/display controls are complete.

Verification record (2026-08-12): the full Core test run passed 183 tests with 9 opt-in hardware tests skipped, and the solution build completed with 0 warnings and 0 errors. The dedicated internal-panel hardware test changed the nearest supported rate and restored the original rate in `finally` (1 passed). The GET-only protocol probe was saved to `artifacts/protocol/2026-08-12/current-recheck.json`; it is a HID artifact and does not include display state. The read-only Windows display probe reported the active built-in panel as 2560 x 1600 at 240 Hz with supported rates 48, 60, 75, 100, 120, and 240 Hz; no external display was written.
