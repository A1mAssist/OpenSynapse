# Backend Completion Implementation Plan

> **For agentic workers:** Execute the independent tasks in parallel and keep every unverified hardware write fail closed.

**Goal:** Close the remaining safe backend gaps for fixed fan targets, software quick-lighting runtime orchestration, and protocol evidence without inventing device commands.

**Architecture:** Keep HID transport and protocol builders in `OpenSynapse.Windows`. Add small runtime interfaces around software-lighting inputs so scheduling and restoration can be tested without hardware; keep native capture/audio adapters independent. Fixed-fan support remains diagnostic-only until an out-of-process recovery owner exists.

**Tech Stack:** .NET 10, Windows 11, C#, WinUI 3, Windows HID Feature Reports, xUnit.

## Global Constraints

- Advanced lighting editor, macros, and Viper calibration remain deferred.
- No hardware write is promoted without read, minimal change, readback, and deterministic restoration evidence.
- Do not terminate Synapse.
- A mismatch between CPU and GPU fan state or target must fail closed.
- Software lighting must use a bounded latest-frame pipeline and restore a known persistent effect on stop, cancellation, and failure.

### Task 1: Fixed-Fan Safety Boundary

- [x] Add fake-transport tests for two-zone disagreement and either-zone range failure.
- [x] Keep the production controller free of a manual fan setter unless recovery survives process termination, disconnect, and sleep.
- [x] Document stored target RPM versus live tachometer RPM.

### Task 2: Software Quick-Lighting Runtime

- [x] Add a cancellation-aware render scheduler that consumes a testable frame-source interface.
- [x] Route complete `6 x 17` frames to `BladeMatrixFramePump` without unbounded queues.
- [x] Add deterministic tests for cadence, cancellation, input failure, and restoration.
- [x] Keep production UI disabled until sustained hardware and visual checks pass.

### Task 3: Remaining Blade Protocol Evidence

- [x] Search exact `02C6` or `RZ09-05286` public implementations and local Synapse artifacts for manual fan curve, GPU mode, one-time full charge, Gaming Mode, Snap Tap, Fn primary, and mappings.
- [x] Record exact source and readback contract where one exists.
- [x] Leave all features without an exact command and restore path Blocked; do not create speculative builders.

### Task 4: Integration and Adversarial Verification

- [x] Run the full unit test project and solution build.
- [x] Verify display topology checks, fan-zone agreement, matrix restoration, profile persistence after readback only, and Viper evidence status.
- [x] Update README, capability matrix, and ledger to match executable behavior.
