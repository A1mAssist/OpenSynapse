# Blade Lighting Hardware Validation Design

Date: 2026-08-12

## Goal

Validate the existing Blade `02C6` firmware-effect and `6 x 17` matrix builders on
the current keyboard without exposing them in production. Every run must restore
the operator-confirmed persistent effect even after cancellation or transport
failure.

## Scope

- Target exactly one available `1532:02C6`, usage `0001:0002`, 91-byte feature
  collection.
- Validate a pure-red Static firmware effect first.
- Validate one fixed `6 x 17` locator frame only after Static succeeds.
- Restore Static `#99DD72` after every target hold, cancellation, or failure.
- Preserve the current brightness; the validator sends no brightness command.
- Never send lid-logo, fan, performance, battery, mapping, or mouse commands.
- Never expose raw report input or arbitrary colors/effects through this tool.

## Command Surface

Extend `OpenSynapse.HardwareValidation` with a mutually exclusive lighting mode:

```text
--keyboard-lighting static-red|matrix-locator
--hold-seconds 5..60
--output <new-json-file>
```

The existing `--logo` mode remains unchanged. Lighting validation has no
`--leave-target` equivalent.

## Execution

1. Discover all HID collections and require exactly one available Blade control
   collection.
2. Build the target only through `BladeLightingProtocol`.
3. Send Static red directly, or publish one locator frame through
   `BladeMatrixFramePump`.
4. Hold for the requested visual-check interval.
5. In `finally`, send `BladeLightingProtocol.CreateStaticRequest(#99DD72)` with a
   non-cancelled token.
6. Record timestamps, target name, hold duration, target acknowledgements,
   restoration acknowledgement, and errors. Do not record HID paths or serials.

The matrix locator uses stable, inspectable bands: red top row, green middle
rows, blue bottom row, and white corner keys. It is a complete 102-zone frame;
partial frames are rejected by the existing pump.

## Failure Rules

- If collection ownership is ambiguous or unavailable, send nothing.
- Treat SET success only as transport acknowledgement, not visual verification.
- Attempt restoration if the first target SET might have reached the device.
- A restoration failure makes the run fail even if the target was visible.
- Refuse to overwrite an existing evidence file.

## Verification

- Parser tests reject mixed Logo/keyboard modes, unknown targets, missing output,
  invalid hold durations, and any leave-target flag for keyboard lighting.
- Fake-transport tests prove command order and restoration after success,
  cancellation, target failure, and matrix-row failure.
- Build the validator and run its focused non-hardware tests before execution.
- Hardware promotion remains visual: the operator confirms the target and the
  restored `#99DD72` state separately.

## Self-Review

- No current-effect GET is invented; restoration is the explicit operator-set
  baseline.
- No SET acknowledgement is called a readback.
- The test cannot affect the lid logo or hardware performance controls.
- The matrix path reuses the existing bounded pump and protocol builders.
- Production/UI exposure remains out of scope until current-device visual
  evidence is complete.
