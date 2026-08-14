# Viper DPI Stages Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the Viper V3 HyperSpeed `00B8` persistent DPI-stage SET contract with a reversible hardware test before production integration.

**Architecture:** Reuse `RazerFeatureReport`, `IRazerFeatureTransport`, the existing Product 184 protocol class, and the hardware-validation executable. The validator reads the current table, writes the same table, changes one non-active stage by exactly 50 DPI, and always restores and reads back the original table in `finally`.

**Tech Stack:** .NET 10, C#, Windows HID feature reports, xUnit.

## Global Constraints

- Synapse must be closed and the Viper must be physically awake before hardware validation.
- The official Product 184 source is authoritative: SET class/command `04/06`, payload size `3 + 7 * count`, persistent storage `01`, one-based active stage, zero-based SET stage IDs, big-endian X/Y/Z, and Z fixed to zero.
- Never overwrite an existing evidence artifact.
- Never execute a hardware write without the operator's explicit confirmation in the current session.
- Low-battery threshold, calibration, mapping, HyperShift, and profile work are outside this task.
- The workspace has no Git metadata; do not add commit steps.

---

### Task 1: Add the Product 184 DPI-stage SET builder

**Files:**
- Modify: `src/OpenSynapse.Windows/Protocols/ViperProduct184Protocol.cs`
- Modify: `src/OpenSynapse.Windows/Protocols/ViperDpiStagesProtocol.cs`
- Test: `tests/OpenSynapse.Core.Tests/ViperProduct184ProtocolTests.cs`

**Interfaces:**
- Consumes: `ViperDpiStagesState` and its ordered `ViperDpiStage` records.
- Produces: `ViperProduct184Protocol.CreateSetDpiStagesRequest(ViperDpiStagesState state)`.

- [x] Add a failing byte-level test for the five-stage table `400/800/1600/3200/6400`, requiring header `1F/26/04/06` and arguments `[01,03,05] + 5 * [zeroBasedId,X_BE,Y_BE,00,00]`.
- [x] Run `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --no-restore --filter ViperProduct184ProtocolTests`; expected: failure because the builder is absent.
- [x] Implement the builder with no new abstraction. Reject null state, counts outside `1..5`, active stages outside `1..count`, non-contiguous public stage numbers, DPI outside `100..30000`, and values not divisible by `50`.
- [x] Run the focused test again; expected: PASS (`15/15`).

### Task 2: Add the reversible hardware validator

**Files:**
- Create: `tools/OpenSynapse.HardwareValidation/ViperDpiStagesValidation.cs`
- Modify: `tools/OpenSynapse.HardwareValidation/Program.cs`
- Create: `tests/OpenSynapse.Core.Tests/ViperDpiStagesValidationSafetyTests.cs`

**Interfaces:**
- Consumes: one available `1532:00B8`, usage `0001:0002`, 91-byte feature collection and `--viper-dpi-stages --output <new-json-path>`.
- Produces: redacted JSON containing original, same-value readback, target, target readback, restoration readback, operation error, and restoration error.

- [x] Add failing fake-transport tests for the successful command sequence, target readback mismatch, SET exception, and restoration failure.
- [x] Implement `ExecuteAsync` as GET original -> same-value SET/GET -> one non-active stage `+50` or `-50` SET/GET -> `finally` original SET/GET. Compare active stage, count, stage numbers, X, and Y exactly after every GET.
- [x] Make restoration errors independent from operation errors, and reject output paths that already exist before device discovery or writes.
- [x] Run `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --no-restore --filter ViperDpiStagesValidationSafetyTests`; expected: PASS (`4/4`).

### Task 3: Verify software and stop at the hardware gate

**Files:**
- No additional files.

**Interfaces:**
- Consumes: Tasks 1-2.
- Produces: a build/test result and an explicit operator checkpoint.

- [x] Run `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --no-restore`; result: `295 passed / 9 skipped / 0 failed`.
- [x] Run `dotnet build OpenSynapse.slnx --configuration Debug --no-restore`; result: `0 errors / 0 warnings`.
- [x] Adversarially verify the most likely failures: wrong protocol family, one-based SET IDs, active-stage mutation, missing restore after exception, and artifact overwrite.
- [x] Stop and ask the operator to close Synapse, wake the mouse, and confirm readiness. Do not execute the validator yet.

### Task 4: Run hardware validation and promote only on proof

**Files:**
- Create at runtime: `artifacts/protocol/2026-08-13/viper-dpi-stages-write-readback-restore.json`
- Modify only after success: `docs/protocol/capability-ledger.md`
- Modify only after success: `docs/device-capability-matrix.md`

**Interfaces:**
- Consumes: explicit operator confirmation and the validator from Task 2.
- Produces: same-value, minimal-change, and restoration readbacks from the current device.

- [x] Run the validator once with a new evidence path while the mouse remains awake.
- [x] Require exact same-value, target, and final restoration matches; any restoration failure keeps the capability blocked.
- [x] Validation passed; evidence status is promoted and the telemetry-reader production method is path-gated with exact restoration. Stage editor UI remains intentionally deferred.

## Self-Review

- The plan uses Product 184 `04/06`, not the unrelated generic `0B/04` implementation.
- The only intentional hardware mutation is one non-active stage by 50 DPI.
- Original state is obtained from the current device and restored from that exact parsed state.
- Hardware execution remains behind explicit operator confirmation.
