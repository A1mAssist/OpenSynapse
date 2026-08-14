# Blade Software Quick Effects Implementation Plan

> **For agentic workers:** Implement each checked task with its focused test before moving on.

**Goal:** Connect evidence-backed Blade keyboard quick effects to a production backend using the existing matrix pipeline.

**Architecture:** Pure renderers produce complete `6 x 17` frames. A single Windows controller validates the manifest-backed HID path, owns the existing runtime/pump, and restores a persistent matrix frame on stop.

**Tech Stack:** .NET 10, Windows HID feature reports, xUnit.

## Global Constraints

- No Razer DLL, service, Chroma SDK, administrator, or new package dependency.
- Use 25 FPS and the existing `03/0B` matrix protocol.
- Never treat a HID ACK as visual validation.

---

### Task 1: Quick-effect frame renderers

**Files:**
- Modify: `src/OpenSynapse.Windows/Lighting/QuickLightingEngine.cs`
- Test: `tests/OpenSynapse.Core.Tests/QuickLightingEngineTests.cs`

- [ ] Add failing tests for Off/Static, 7-second Breathing, Spectrum Cycling, and directional Wave.
- [ ] Implement only the formulas required by those tests.
- [ ] Run the focused renderer tests.

### Task 2: Production lighting controller

**Files:**
- Create: `src/OpenSynapse.Windows/Lighting/BladeLightingController.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeLightingControllerTests.cs`

- [ ] Add failing tests for device validation, first-frame writes, replacement, and persistent restore.
- [ ] Implement a serialized single-session controller using the existing runtime and pump.
- [ ] Run the focused controller and pump/runtime tests.

### Task 3: Lifetime and contract

**Files:**
- Modify: `src/OpenSynapse.App/App.xaml.cs`
- Modify: `docs/frontend-handoff.md`
- Modify: `docs/device-capability-matrix.md`

- [ ] Own one controller for the app lifetime and dispose it on actual exit.
- [ ] Document the strong backend contract and the evidence-gated modes.
- [ ] Build the solution and run all tests.
