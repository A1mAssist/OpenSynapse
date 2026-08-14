# Product-Used Protocols Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement only the HID protocols actually exercised by the Blade 16 2025 Product 710 code and Viper V3 HyperSpeed Product 184 code, while preserving the current evidence gates and recovery behavior.

**Architecture:** Keep the existing `RazerFeatureTransport` and product-specific protocol classes. Add only product-backed builders/parsers and telemetry operations; do not expose every method from the shared `rzDevice25` bundle. The WinUI layer continues to consume `IRazerDeviceTelemetryReader` and never opens HID handles.

**Tech Stack:** .NET 10, C# nullable reference types, Windows HID `HidD_SetFeature`/`HidD_GetFeature`, WinUI 3, xUnit.

## Global Constraints

- Target devices are Blade 16 2025 `1532:02C6` and Viper V3 HyperSpeed `1532:00B8` only.
- A protocol is eligible only when its call site exists in the local Product 710 or Product 184 JavaScript and its header/argument/response layout is source-backed.
- Only capabilities with existing read/write/readback/restore evidence may enable production writes.
- Source-backed capabilities may be exposed through opt-in read-only probes and strict parsers only.
- Blocked capabilities remain unavailable; no guessed HID command or fake UI control.
- Do not run keyboard-matrix hardware tests, Viper surface calibration, GPU MUX, macros, or mouse brightness.
- Do not include HID paths, serial numbers, GUIDs, account data, or raw protocol logs in repository artifacts.

---

### Task 1: Freeze Product-Used Protocol Inventory

**Files:**
- Create: `docs/reverse-engineering/product-used-protocol-inventory.md`
- Modify: `docs/protocol/capability-ledger.md`
- Test: `tests/OpenSynapse.Core.Tests/ProbeCatalogTests.cs`

**Interfaces:**
- Consumes: local Product 710 `a2cb83e8876bf761_0`, Product 184 `404e8044ccdb64b6_0`, and their `rzDevice25` dependencies.
- Produces: a redacted inventory mapping each admitted capability to product call site, `[dataSize,class,command]`, argument layout, response parser, and evidence level.

- [ ] Extract only methods called by Product 710 and Product 184 application code, excluding generic class exports that have no product call site.
- [ ] Record the current admitted set: existing Blade performance/fan/power/brightness/charge/boost paths; the Blade lighting and logo paths under their existing evidence gates; existing Viper battery/current DPI/polling/idle paths; and Viper DPI-stage/low-battery/battery-chemistry paths only where the local product code or exact source-backed capture supplies the report layout.
- [ ] Keep Product 184 local AppEngine mappings separate from board-profile HID commands; mark them blocked until a product call chain reaches a device write.
- [ ] Add catalog assertions that every probe command has a product owner (`blade-710` or `viper-184`) and no generic-only command enters the catalog.
- [ ] Run `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --filter ProbeCatalogTests`; expected: PASS.

### Task 2: Consolidate Product-Backed Protocol Builders and Parsers

**Files:**
- Modify: `src/OpenSynapse.Windows/Protocols/BladeLightingProtocol.cs`
- Modify: `src/OpenSynapse.Windows/Protocols/BladeLogoProtocol.cs`
- Modify: `src/OpenSynapse.Windows/Protocols/ViperDpiStagesProtocol.cs`
- Modify: `src/OpenSynapse.Windows/Protocols/ViperLowBatteryThresholdProtocol.cs`
- Modify: `src/OpenSynapse.Windows/Protocols/ViperBatteryChemistryProtocol.cs`
- Create: `src/OpenSynapse.Windows/Protocols/ViperProduct184Protocol.cs`
- Test: `tests/OpenSynapse.Core.Tests/ViperProduct184ProtocolTests.cs`

**Interfaces:**
- Consumes: `RazerFeatureReport`, existing protocol classes, and the inventory from Task 1.
- Produces: strict C# request builders and response parsers for only Product 184 calls not already represented, with exact transaction/data-size/class/command semantics.

- [x] Add `ViperProduct184Protocol` constants/builders and strict parsers for Product 184 battery, polling-rate, current DPI, idle timeout; DPI-stage and low-battery parsers remain separate SourceBacked paths.
- [ ] Keep transaction generation compatible with the official Product 184 `rzDevice25` implementation. Do not reuse a fixed transaction if the product code increments it.
- [ ] Validate exact response status, transaction, command class/id, declared data size, and all product-specific argument fields before returning a value.
- [ ] Preserve SourceBacked status for DPI stages, low-battery threshold, and battery chemistry until current-device read/write/restore evidence exists.
- [ ] Add table-driven tests for every legal enum/value, malformed length, wrong command, wrong transaction, invalid range, and endianness.
- [ ] Run `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --filter ViperProduct184ProtocolTests`; expected: PASS.

### Task 3: Wire Product-Backed Read Paths into Telemetry

**Files:**
- Modify: `src/OpenSynapse.Core/Devices/RazerDeviceTelemetry.cs`
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Modify: `tests/OpenSynapse.Core.Tests/DeviceIdParserTests.cs`
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: protocol builders/parsers from Task 2 and existing `IRazerFeatureTransport`.
- Produces: telemetry for admitted Product 710 and Product 184 queries, with independent error handling per capability and no invented fallback values.

- [x] Add nullable telemetry fields for Product 184 DPI-stage metadata and low-battery threshold; each is populated only after its strict parser succeeds.
- [ ] Query each capability independently so one timeout does not erase successful values from the other device.
- [ ] Set a validated path only after the response parser succeeds for the current device path; production setters must continue to reject stale paths.
- [ ] Keep SourceBacked values read-only and opt-in where current-device query evidence is not yet successful.
- [ ] Add fake-transport tests for success, timeout, malformed response, device removal, and partial success.
- [ ] Run `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --filter RazerDeviceTelemetryReaderTests`; expected: PASS.

### Task 4: Wire Only Verified Writes and Recovery

**Files:**
- Modify: `src/OpenSynapse.Core/Devices/RazerDeviceTelemetry.cs`
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Modify: `src/OpenSynapse.Core/Profiles/VerifiedProfileApplier.cs`
- Modify: `src/OpenSynapse.Core/Profiles/ProfileModels.cs`
- Test: `tests/OpenSynapse.Core.Tests/VerifiedProfileApplierTests.cs`

**Interfaces:**
- Consumes: validated read paths and strict setters from Task 3.
- Produces: production writes only for capabilities already promoted to Verified, with original-state restoration on failure or cancellation.

- [ ] Keep current production setters for Blade keyboard brightness, Blade performance/charge/boost/Max Fan where their ledger says Verified, and Viper current DPI, polling rate, and idle timeout.
- [ ] Do not add production setters for Viper DPI stages, low-battery threshold, battery chemistry, button mappings, HyperShift, profiles, or calibration until their ledger evidence is promoted.
- [ ] If a new Product 710 or 184 setter is promoted later, require current-path read validation, same-value write/readback, minimal-change write/readback, and `finally` restoration before adding it to `VerifiedProfileApplier`.
- [ ] Add tests proving blocked/source-backed settings are skipped and leave no HID calls.
- [ ] Run `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --filter VerifiedProfileApplierTests`; expected: PASS.

### Task 5: Expose Capability State in WinUI Without Fake Controls

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/App.xaml`
- Test: `tests/OpenSynapse.Core.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: telemetry and capability evidence from Tasks 1-4.
- Produces: stable WinUI bindings for Ready, ReadOnly, PendingValidation, Blocked, Busy, Failed, and Restoring states.

- [ ] Add controls only for the admitted and currently usable Product 710/Product 184 capabilities.
- [ ] Render SourceBacked and Blocked capabilities as status rows or disabled state, never as enabled write buttons.
- [ ] Keep protocol terminology confined to diagnostics; normal device pages use user-facing names.
- [ ] Preserve stable control dimensions and existing WinUI 3 Fluent styling; do not add a web layer or third-party UI library.
- [ ] Add view-model tests for connected, unavailable, busy, failed, and restored states.
- [ ] Run `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --filter MainViewModelTests`; expected: PASS.

### Task 6: Verification and Evidence Update

**Files:**
- Modify: `docs/device-capability-matrix.md`
- Modify: `docs/protocol/capability-ledger.md`
- Create: `artifacts/protocol/2026-08-13/product-used-protocol-validation.json`

**Interfaces:**
- Consumes: all implementation and test outputs from Tasks 1-5.
- Produces: redacted evidence showing build/test results and per-capability status; no private HID identifiers.

- [ ] Run the focused protocol tests and the full non-hardware suite.
- [ ] Run `dotnet build OpenSynapse.slnx --configuration Debug --no-restore`; expected: zero errors and zero warnings.
- [ ] Run only safe read-only probes for SourceBacked capabilities while Synapse is closed; do not run keyboard matrix tests or logo visual tests without an explicit controlled hold.
- [ ] For existing Verified writes, run one-device-at-a-time read/write/readback/restore checks and record only redacted status, VID/PID, collection, capability, and outcome.
- [ ] Update the ledger only when the evidence threshold is actually met; otherwise leave the capability SourceBacked or Blocked.
- [ ] Run a self-review against the product-call inventory and confirm no generic-only `rzDevice25` API was added.

## Self-Review Checklist

- [ ] Every admitted protocol has a Product 710 or Product 184 call-site citation.
- [ ] No plan task promotes source-backed or blocked capabilities to production writes without validation.
- [ ] No task adds macros, Viper brightness, calibration, GPU MUX, or generic-only APIs.
- [ ] All types and method names used by later tasks are defined by earlier tasks or already exist in the repository.
- [ ] No raw HID path, serial number, GUID, account data, or unsanitized protocol log is committed.
