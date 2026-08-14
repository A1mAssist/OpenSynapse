# RzLightingEngineApi static function map

Read-only Ghidra 12.1.2 analysis of the locally installed `RzLightingEngineApi_v4.0.55.0.dll`.
The original binary was imported in place; it was not copied into the repository and no HID/device process was started.

## Evidence

- Binary: `%LOCALAPPDATA%\\Razer\\RazerAppEngine\\User Data\\Apps\\Common\\RzLightingEngineApi_v4.0.55.0.dll`
- File/Product version: `4.0.55.0`
- SHA-256: `CD9FC2A61FF920B9B73E1F5E27632020E2F8586F443E426A875AD3B042B714FA`
- Size: `2,153,672` bytes; PE `IMAGE_FILE_MACHINE_AMD64`; image base `0x180000000`
- Ghidra project: disposable; moved outside the repository after exporting evidence
- Raw outputs: `ghidra/targeted-call-chains.md`, `ghidra/msvc-rtti.tsv`, `ghidra/effect-vftables.tsv`, `ghidra/effect-method-decompilations.md`, `ghidra/llvm-pe.txt`

## Exported API

| Export | RVA |
|---|---:|
| `CreateLightingDevice` | `0x45170` |
| `DestroyLightingDevice` | `0x45210` |
| `CreateLightingEngine` | `0x45230` |
| `DestroyLightingEngine` | `0x452d0` |
| `FreeMalloc` | `0x340c0` |
| `GetDLLVersion` | `0x33cf0` |
| `GetFeatureSet` | `0x331b0` |
| `PollEvents` | `0x32d90` |
| `RzLightingApi` | `0x33490` |
| `RzLightingApiNoReturn` | `0x33b60` |
| `SetAllocator` | `0x322d0` |
| `SetEventInterface` | `0x32250` |
| `SetNodeFFIEvent` | `0x340d0` |
| `SetOperatingMode` | `0x32d00` |
| `UsePollingMode` | `0x32d80` |

`RzLightingApi`/`RzLightingApiNoReturn` are dispatch wrappers. `CreateLightingEngine` allocates a `0x160`-byte engine object and calls `FUN_18004b6c0(engine, effectId)`; `CreateLightingDevice` allocates a `0xe778`-byte device object. The native API loads `LedData\\PID%04x_EID%02x_%02x.json` (or `C:\\Windows\\System32\\LedData`) before constructing an engine/device.

## Effect vftables and methods

The vftable slot 0 is the destructor. Slots 1-3 are the effect lifecycle/frame methods; slot 9 is a shared tail method or destructor variant. Exact parameter types are not recovered by this pass.

| Effect | vftable | slot 1 | slot 2 | slot 3 | slot 9 |
|---|---:|---:|---:|---:|---:|
| `RippleEffect` | `0x1801aae78` | `0x180053690` (0x38e) | `0x180053a20` (0x238) | `0x1800541d0` (0x190) | `0x180069290` (0x22) |
| `StarlightEffect` | `0x1801ac5a8` | `0x1800603b0` (0x3de) | `0x180060790` (0x15e) | `0x180069240` (0x3) | `0x180069290` (0x22) |
| `SpectrumEffect` | `0x1801ac698` | `0x180061e90` (0x293) | `0x180062130` (0x99) | `0x1800621d0` (0x39) | `0x180069290` (0x22) |
| `FireEffect` | `0x1801ac758` | `0x1800624a0` (0x123) | `0x1800633b0` (0xa4) | `0x180069240` (0x3) | `0x1800625d0` (0xd96) |
| `ReactiveEffect` | `0x1801ac828` | `0x180063890` (0x298) | `0x180063b30` (0x182) | `0x180063e30` (0x5d2) | `0x180069290` (0x22) |
| `BreathingEffect` | `0x1801ac928` | `0x1800651c0` (0x3a2) | `0x180065570` (0x1b3) | `0x180065730` (0x62) | `0x180069290` (0x22) |
| `WaveEffect` | `0x1801aca28` | `0x180065a80` (0x209) | `0x1800666b0` (0x11a) | `0x1800667d0` (0x80) | `0x180065c90` (0x2b2) |

Sizes are Ghidra function-body address counts, not source-line sizes.

## Algorithm evidence

### Spectrum

`0x180061e90` initializes the effect and allocates a `uint32` frame-history buffer at `this+0x198`. Buffer length is derived from `Duration`/`FPS` (`1000 / *(uint32*)(this+0x38)`), using `0x936c` ms when no explicit duration is present. It initializes color stops at `this+0x40` and `this+0x68`, then calls the shared renderer `FUN_1800692e0(this, 10, ...)`.

`0x180062130` is the frame callback: it copies the current 32-bit color into the caller's output buffer, advances `this+0x188` modulo `this+0x184`, and decrements the remaining-cycle counter at `this+0x18c`. `0x1800621d0` restarts the cycle and resets the frame index. This is a precomputed frame sequence, not a per-pixel HSV loop in the callback.

### Breathing

`0x1800651c0` follows the same precompute model as Spectrum. It selects a mode from the ordered color stops at offsets `0x44..0x60`, falls back to default colors when the stop chain is invalid, allocates a frame buffer at `this+0x198`, and calls `FUN_1800692e0(this, 10, this+0x40, this+0x68, frameCount, buffer)`.

`0x180065570` copies one precomputed frame and, when the frame index wraps, refreshes randomized color stops via `FUN_180069600` for the mode flags `0x1/0x8/0x10`. `0x180065730` changes the active mode/restart state. Therefore the current OpenSynapse breathing curve is not proven 1:1 until `FUN_1800692e0` and its stop interpolation are recovered.

### Wave

`0x180065a80` validates the color-stop ordering and stores the Wave mode/flags. `0x180065c90` computes spatial width and speed from geometry (`this+0x30`, `this+0x34`, `this+0x38`), duration and configured rate (`this+0xa8`), allocates a frame buffer, then calls `FUN_180069590` and `FUN_1800692e0`.

`0x1800666b0` is the frame callback. It chooses between two internal paths (`FUN_180065f50` and `FUN_180066160`) based on flags `0x400/0x800`, writes the current frame, advances the combined pause/active index, and decrements cycles. `0x1800667d0` restarts the sequence. The native implementation has explicit width, speed, pause and angle state; the current renderer only approximates these parameters.

### Fire

`0x1800624a0` initializes Fire defaults: minimum rate `0x28`, default frame count `0x32`, and a time-derived seed at `this+0x178`.

`0x1800625d0` is the real Fire frame generator (0xd96 bytes). It allocates a history buffer and auxiliary `0x400`-byte state, uses a `0x17`-column, `0xa1`-cell (7x23) working frame, mirrors/copies rows with `FUN_180194710`, seeds values through `FUN_1801551d0`, and applies per-channel decay and neighbor propagation before calling `FUN_1800692e0` for color interpolation. This is a stateful cellular/decay algorithm with random seeding, not the simple noise implementation currently in OpenSynapse.

`0x1800633b0` copies generated frames to the output. The generator's device dimensions are taken from the engine's LedData configuration; the constants above are not evidence that every device uses the Blade 6x17 matrix directly.

## What is and is not recovered

- Recovered with high confidence: exported API RVAs, LedData loading path, effect class identities, vftable addresses, lifecycle/frame function boundaries, frame-buffer allocation/index/cycle behavior, and Fire's internal state dimensions.
- Not recovered: exact semantics of the shared renderer `FUN_1800692e0`, color-stop interpolation tables, the helper functions `FUN_180069590`, `FUN_180065f50`, `FUN_180066160`, and the final device-layout conversion. Calling conventions and parameter types remain Ghidra defaults.
- Do not replace OpenSynapse renderers from this map alone. A one-time frame capture or deeper decompilation of the shared helpers is still required for visual parity; ACKs cannot validate parity.
