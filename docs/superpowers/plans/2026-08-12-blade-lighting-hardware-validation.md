# Blade Lighting Hardware Validation Implementation Plan

> **For agentic workers:** Implement task-by-task with a review after each task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a bounded, restoration-first hardware validator for the current Blade `02C6` keyboard Static effect and `6 x 17` matrix.

**Architecture:** Keep the existing Logo validator unchanged and dispatch keyboard-specific arguments to a separate `KeyboardLightingValidation` class. Reuse `BladeLightingProtocol`, `RazerFeatureTransport`, `WindowsHidDiscovery`, and `BladeMatrixFramePump`; add no protocol builders or dependencies.

**Tech Stack:** .NET 10, C#, Windows HID feature reports, xUnit.

## Global Constraints

- Target exactly one available `1532:02C6`, usage `0001:0002`, 91-byte feature collection.
- Restore Static `#99DD72` after success, cancellation, or a write that may have reached the device.
- Do not write brightness, Logo, performance, fan, battery, mappings, or Viper state.
- Do not expose arbitrary reports, colors, effects, or a leave-target option.
- Do not treat a successful SET response as visual readback.
- Refuse to overwrite artifacts and omit HID paths and serials from artifacts.
- The workspace has no Git repository; do not add commit steps or claim commits.

---

### Task 1: Keyboard Lighting Validator

**Files:**
- Create: `tools/OpenSynapse.HardwareValidation/KeyboardLightingValidation.cs`
- Modify: `tools/OpenSynapse.HardwareValidation/Program.cs`
- Test: `tests/OpenSynapse.Core.Tests/KeyboardLightingValidationTests.cs`

**Interfaces:**
- Consumes: `KeyboardLightingValidation.Options.Parse(string[])`, `IRazerFeatureTransport`, `BladeLightingProtocol`, and `BladeMatrixFramePump`.
- Produces: `KeyboardLightingValidation.RunAsync(string[])`, a JSON artifact, and internal `ExecuteAsync` suitable for fake-transport tests.

- [ ] **Step 1: Add parser and execution tests**

Test that only `static-red` and `matrix-locator` are accepted; reject Logo arguments, `--leave-target`, missing/new-invalid output paths, and hold values outside `5..60`. With a fake transport, assert Static sends target `030A` then restore `030A`; matrix sends custom mode `030A`, exactly six `030B` rows, then restore `030A`. Inject target and row failures and assert restoration is still attempted.

- [ ] **Step 2: Run the focused tests and confirm failure**

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~KeyboardLightingValidationTests'
```

Expected: compilation fails because `KeyboardLightingValidation` does not exist.

- [ ] **Step 3: Implement the restricted validator**

Dispatch from `Program.cs` when the first argument is `--keyboard-lighting`; otherwise preserve the exact existing Logo path. Discover exactly one available Blade collection. Static sends pure red through `BladeLightingProtocol.CreateStaticRequest`. Matrix creates a fixed 102-zone locator frame and sends it through `BladeMatrixFramePump`. Restoration always sends `BladeLightingProtocol.CreateStaticRequest(new RazerRgb(0x99, 0xDD, 0x72))` with `CancellationToken.None`. Write artifact atomically and fail when target or restoration errors exist.

- [ ] **Step 4: Run focused tests**

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~KeyboardLightingValidationTests|FullyQualifiedName~BladeMatrixFramePumpTests|FullyQualifiedName~LogoValidationSafetyTests'
```

Expected: all selected tests pass.

- [ ] **Step 5: Build the validator**

```powershell
dotnet build '.\tools\OpenSynapse.HardwareValidation\OpenSynapse.HardwareValidation.csproj' --no-restore
```

Expected: zero warnings and zero errors.

### Task 2: Static Hardware Run

**Files:**
- Create at runtime: `artifacts/protocol/2026-08-12/keyboard-static-red-visual.json`

**Interfaces:**
- Consumes: operator-confirmed Static `#99DD72` baseline and released control collection.
- Produces: bounded Static-red acknowledgement/restoration evidence plus separate operator visual confirmation.

- [ ] **Step 1: Reconfirm ownership with GET-only probe**

Require successful Blade brightness GET and no Synapse process. If either fails, send nothing.

- [ ] **Step 2: Run Static red for 30 seconds**

```powershell
dotnet run --project '.\tools\OpenSynapse.HardwareValidation\OpenSynapse.HardwareValidation.csproj' -- --keyboard-lighting static-red --hold-seconds 30 --output '.\artifacts\protocol\2026-08-12\keyboard-static-red-visual.json'
```

Expected: target acknowledgement, 30-second hold, restoration acknowledgement, exit code `0`.

- [ ] **Step 3: Ask for two visual facts**

Record whether the keyboard became pure red and whether it returned to Static `#99DD72`. Do not promote on acknowledgement alone.

### Task 3: Matrix Hardware Run

**Files:**
- Create at runtime after Static passes: `artifacts/protocol/2026-08-12/keyboard-matrix-locator-visual.json`
- Modify after visual confirmation: `docs/protocol/capability-ledger.md`
- Modify after visual confirmation: `docs/device-capability-matrix.md`

**Interfaces:**
- Consumes: the same operator-confirmed restore baseline and successful Static hardware run.
- Produces: one complete matrix observation and restoration evidence.

- [ ] **Step 1: Run the locator frame for 30 seconds**

```powershell
dotnet run --project '.\tools\OpenSynapse.HardwareValidation\OpenSynapse.HardwareValidation.csproj' -- --keyboard-lighting matrix-locator --hold-seconds 30 --output '.\artifacts\protocol\2026-08-12\keyboard-matrix-locator-visual.json'
```

Expected: custom-mode acknowledgement, six row acknowledgements, restoration acknowledgement, exit code `0`.

- [ ] **Step 2: Ask for matrix and restoration confirmation**

Require an inspectable multi-color matrix and restored Static `#99DD72`. Reject promotion if rows are shifted, missing, reordered, or restoration is wrong.

- [ ] **Step 3: Run adversarial review and update evidence**

Check: exact PID/collection, no Synapse ownership, full six-row send, SET acknowledgement not mislabeled as readback, visual target confirmation, visual restoration confirmation. Update ledger/matrix only for observations that passed all applicable checks.

## Plan Self-Review

- Spec coverage: Static, matrix, cancellation/failure restoration, artifact privacy, and visual gates all have explicit steps.
- No placeholders or speculative production UI work remain.
- All interfaces reuse existing concrete types; no new abstraction or dependency is introduced.
