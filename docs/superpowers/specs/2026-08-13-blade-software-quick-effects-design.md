# Blade Software Quick Effects Design

## Goal

Make the Blade 16 2025 keyboard effects that can run without external input adapters available through a production backend.

## Evidence

Synapse registers PID 710 as a `6 x 17` matrix using `rzDevice25LedMatrixSkipSetEffect`, creates a Basic Lighting Engine at 25 FPS, and sends 306-byte RGB frames through `hid.sendFeatureReportInBatch`. Its one-shot `03/0A` firmware effect path is not the production path for this Blade and has already ACKed without visible change.

## Design

- Reuse `QuickLightingEngine`, `SoftwareLightingRuntime`, and `BladeMatrixFramePump`.
- Add deterministic matrix renderers for Off, Static, Breathing, Spectrum Cycling, and Wave. Reuse the existing Fire renderer.
- Add one `BladeLightingController` that validates the current HID path with a brightness read, owns at most one runtime, and serializes effect changes.
- Use the Synapse-observed 25 FPS cadence and the existing six row writes.
- Restore a complete persistent matrix frame after stop or failure. Do not restore through the ineffective firmware Static command.
- Do not expose Reactive, Ripple, or Audio Meter until Windows keyboard/WASAPI adapters exist. Do not expose Starlight until the Blade Lighting Engine mapping is evidenced.

## Failure Handling

Starting a new effect stops the old session first. Missing, busy, unsupported, or unvalidated devices fail before any matrix write. Transport failures stop the runtime, reject subsequent frames, and attempt one persistent-frame restore.

## Verification

Unit tests cover frame dimensions, deterministic color behavior, controller device gating, 25 FPS startup, mode replacement, and restoration. The final gate is a real-device visual run because ACK alone is not proof of visible lighting.
