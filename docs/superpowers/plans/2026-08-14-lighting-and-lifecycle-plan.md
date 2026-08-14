# Basic Lighting and Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete Basic Lighting Engine parity work and own the Windows input/audio/capture lifetimes for reactive lighting modes.

**Architecture:** Keep `QuickLightingEngine` deterministic and device-free. Add bounded Windows adapters that feed typed events/samples/pixels into `ISoftwareLightingFrameSource`; `SoftwareLightingRuntime` owns adapter start/stop and the existing latest-frame pump. Every transition disposes the previous adapter before restoring the persistent frame.

**Tech Stack:** .NET 10, WinUI 3, Windows low-level keyboard hook, WASAPI/Core Audio, Windows Graphics Capture, xUnit.

## Global Constraints

- No HID I/O from hook, audio, or capture callbacks.
- Use bounded channels and drop stale input rather than growing memory.
- Stop adapters on cancellation, device disconnect, suspend, effect switch, controller fault, and App exit.
- Do not claim Synapse parity without side-by-side visual evidence.
- Keep the advanced lighting editor and arbitrary matrix editing out of production UI.

---

### Task 1: Add a runtime-owned adapter contract

**Files:**
- Modify: `src/OpenSynapse.Windows/Lighting/SoftwareLightingRuntime.cs`
- Create: `src/OpenSynapse.Windows/Lighting/ILightingInputAdapter.cs`
- Test: `tests/OpenSynapse.Core.Tests/SoftwareLightingRuntimeTests.cs`

**Interfaces:**
- Produces: `ILightingInputAdapter : IAsyncDisposable` with `StartAsync`, `StopAsync`, and `ReadAsync` cancellation semantics.

- [ ] **Step 1: Add failing lifetime tests**

Use a fake adapter that records `Start`, `Stop`, and `Dispose`. Assert stop/dispose order on normal stop, cancellation, source fault, and replacement.

- [ ] **Step 2: Implement the adapter-owned runtime loop**

The runtime constructor accepts an optional adapter. Start it before the first frame, pass the same cancellation token to its producer, and dispose it in a `finally` before `_pump.RestoreAsync` completes. Aggregate adapter and pump failures with the existing `AggregateException` behavior.

- [ ] **Step 3: Run focused runtime tests**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~SoftwareLightingRuntimeTests'
```

- [ ] **Step 4: Commit**

```powershell
git add 'src\OpenSynapse.Windows\Lighting\SoftwareLightingRuntime.cs' 'src\OpenSynapse.Windows\Lighting\ILightingInputAdapter.cs' 'tests\OpenSynapse.Core.Tests\SoftwareLightingRuntimeTests.cs'
git commit -m 'feat: own lighting adapter lifetimes'
```

### Task 2: Implement keyboard Reactive and Ripple input

**Files:**
- Create: `src/OpenSynapse.Windows/Lighting/WindowsKeyboardLightingAdapter.cs`
- Modify: `src/OpenSynapse.Windows/Lighting/BladeLightingController.cs`
- Modify: `src/OpenSynapse.Windows/Lighting/QuickLightingEngine.cs`
- Test: `tests/OpenSynapse.Core.Tests/WindowsKeyboardLightingAdapterTests.cs`

**Interfaces:**
- Produces: `QuickLightingKeyEvent` values using `BladeLightingLayout` logical coordinates.

- [ ] **Step 1: Add pure key-event filtering tests**

Reject injected events, unknown scan codes, key-up events, repeated events within the hook debounce window, and coordinates outside the logical matrix. Assert a valid physical key maps to the documented row/column.

- [ ] **Step 2: Implement a low-level hook adapter**

Use `SetWindowsHookEx(WH_KEYBOARD_LL)` on a dedicated message-pump thread. Copy only scan code, flags, and timestamp in the callback; enqueue a bounded `QuickLightingKeyEvent` after mapping. Never call the HID transport or wait in the callback. `UnhookWindowsHookEx` and thread shutdown happen in `DisposeAsync`.

- [ ] **Step 3: Add Reactive/Ripple frame sources**

Read a bounded event buffer and call the existing deterministic `QuickLightingEngine.RenderReactive` or `RenderRipple`. Reuse the existing `BladeLightingLayout`; do not create a second matrix mapping.

- [ ] **Step 4: Run adapter tests**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~WindowsKeyboardLightingAdapterTests'
```

- [ ] **Step 5: Commit**

```powershell
git add 'src\OpenSynapse.Windows\Lighting\WindowsKeyboardLightingAdapter.cs' 'src\OpenSynapse.Windows\Lighting\BladeLightingController.cs' 'src\OpenSynapse.Windows\Lighting\QuickLightingEngine.cs' 'tests\OpenSynapse.Core.Tests\WindowsKeyboardLightingAdapterTests.cs'
git commit -m 'feat: add keyboard reactive lighting input'
```

### Task 3: Implement WASAPI Audio Meter input

**Files:**
- Create: `src/OpenSynapse.Windows/Lighting/WasapiAudioMeterAdapter.cs`
- Modify: `src/OpenSynapse.Windows/Lighting/BladeLightingController.cs`
- Test: `tests/OpenSynapse.Core.Tests/WasapiAudioMeterAdapterTests.cs`

**Interfaces:**
- Produces: normalized RMS/peak samples in `[0,1]` for `QuickLightingEngine.RenderAudioMeter`.

- [ ] **Step 1: Add sample normalization tests**

Cover silence, full-scale samples, clipping, endpoint removal, and restart after endpoint change.

- [ ] **Step 2: Implement Core Audio loopback capture**

Use the Windows Audio Client COM interfaces already available in the Windows SDK. Initialize the default render endpoint in loopback mode, read packets on a dedicated task, compute channel-independent RMS/peak, clamp to `[0,1]`, and publish through a bounded channel. On `AUDCLNT_E_DEVICE_INVALIDATED`, stop and recreate the endpoint.

- [ ] **Step 3: Connect the Audio Meter source and teardown**

No COM object crosses the render loop. Stop capture before disposing the runtime and restore the persistent frame.

- [ ] **Step 4: Run tests/build**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~WasapiAudioMeterAdapterTests'
dotnet build 'OpenSynapse.slnx' -c Release --no-restore
```

- [ ] **Step 5: Commit**

```powershell
git add 'src\OpenSynapse.Windows\Lighting\WasapiAudioMeterAdapter.cs' 'src\OpenSynapse.Windows\Lighting\BladeLightingController.cs' 'tests\OpenSynapse.Core.Tests\WasapiAudioMeterAdapterTests.cs'
git commit -m 'feat: add WASAPI audio meter lighting input'
```

### Task 4: Implement Ambient Awareness capture

**Files:**
- Create: `src/OpenSynapse.Windows/Lighting/WindowsDisplayCaptureAdapter.cs`
- Modify: `src/OpenSynapse.Windows/Lighting/BladeLightingController.cs`
- Test: `tests/OpenSynapse.Core.Tests/WindowsDisplayCaptureAdapterTests.cs`

**Interfaces:**
- Produces: bounded RGB pixel samples for `QuickLightingEngine.RenderAmbientAwareness`.

- [ ] **Step 1: Add pure crop/resample tests**

Cover non-square source frames, a one-pixel source, capture stride, display-edge crop, and frame drop when the consumer is slower.

- [ ] **Step 2: Implement Windows Graphics Capture**

Select the active internal display used by `WindowsInternalDisplayController`, create a `GraphicsCaptureItem`, copy frames to a CPU-readable buffer on a dedicated task, sample only the configured edge band, and publish immutable RGB samples. Permission or topology failure returns a typed adapter error and never starts a fake ambient animation.

- [ ] **Step 3: Add teardown tests**

Assert capture frame pools, sessions, and COM buffers are closed before the matrix pump restore finishes.

- [ ] **Step 4: Commit**

```powershell
git add 'src\OpenSynapse.Windows\Lighting\WindowsDisplayCaptureAdapter.cs' 'src\OpenSynapse.Windows\Lighting\BladeLightingController.cs' 'tests\OpenSynapse.Core.Tests\WindowsDisplayCaptureAdapterTests.cs'
git commit -m 'feat: add ambient display capture lighting input'
```

### Task 5: Recover exact Starlight, Wave, and Fire behavior

**Files:**
- Use: `artifacts/reverse-engineering/2026-08-14/ghidra/*`
- Modify: `src/OpenSynapse.Windows/Lighting/QuickLightingEngine.cs`
- Test: `tests/OpenSynapse.Core.Tests/QuickLightingEngineTests.cs`
- Create: `artifacts/reverse-engineering/2026-08-14/lighting-parity-vectors.json`

**Interfaces:**
- Produces: deterministic frame vectors and exact cadence/mapping constants.

- [ ] **Step 1: Convert recovered constructor/helper evidence to frame vectors**

For each effect, record zero-time, one-period boundary, direction reversal, and color-stop frames. Use the recovered native work-grid dimensions and byte order; do not infer missing values from visual taste.

- [ ] **Step 2: Compare vectors against current renderers**

Any difference must be classified as proven constant, proven mapping, or unknown. Only the first two may change C#.

- [ ] **Step 3: Add deterministic vector tests**

Assert exact RGB cells and frame timestamps for every recovered vector. Keep unknown fields out of the public Profile codec.

- [ ] **Step 4: Run lighting tests**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~QuickLightingEngineTests'
```

- [ ] **Step 5: Commit only proven renderer changes**

```powershell
git add 'src\OpenSynapse.Windows\Lighting\QuickLightingEngine.cs' 'tests\OpenSynapse.Core.Tests\QuickLightingEngineTests.cs' 'artifacts\reverse-engineering\2026-08-14\lighting-parity-vectors.json'
git commit -m 'feat: align basic lighting with recovered engine evidence'
```

### Task 6: Integrate all effects and record visual lifecycle evidence

**Files:**
- Modify: `src/OpenSynapse.Windows/Lighting/BladeLightingController.cs`
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Create: `tools/OpenSynapse.HardwareValidation/KeyboardLightingLifecycleValidation.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeLightingControllerTests.cs`
- Modify: `docs/device-capability-matrix.md`

**Interfaces:**
- Produces: mode selection for all Basic effects with explicit adapter requirements and persistent-frame restoration.

- [ ] **Step 1: Add controller tests for effect replacement/fault/disconnect**

Assert the old adapter is stopped before the new one starts, stale runtime faults do not overwrite the new mode, and every teardown ends with a persistent `#99DD72` frame.

- [ ] **Step 2: Add the lifecycle validation command**

Run each mode for at least 60 seconds, write effect name, parameters, frame count, transport errors, restore readback, and visual-confirmation flag to a new JSON file. Refuse existing output files and omit HID paths.

- [ ] **Step 3: Run full non-hardware tests and Release build**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore
dotnet build 'OpenSynapse.slnx' -c Release --no-restore
```

- [ ] **Step 4: Run current-device visual/lifecycle validation**

With Synapse closed, run all effects; then repeat with Synapse contending, device reconnect, sleep/resume, and App exit. Record every mode's visible result and restoration. A missing adapter or visual mismatch keeps that mode out of `Verified`.

- [ ] **Step 5: Commit evidence and status changes**

```powershell
git add 'src\OpenSynapse.Windows\Lighting\BladeLightingController.cs' 'src\OpenSynapse.App\ViewModels\MainViewModel.cs' 'tools\OpenSynapse.HardwareValidation\KeyboardLightingLifecycleValidation.cs' 'tests\OpenSynapse.Core.Tests\BladeLightingControllerTests.cs' 'docs\device-capability-matrix.md'
git commit -m 'test: verify basic lighting lifecycle recovery'
```
