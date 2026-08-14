# OpenSynapse Backend Completion Design

Date: 2026-08-14

## Goal

Complete the remaining backend work for the supported Blade 16 2025 (`1532:02C6`, family `blade-710`) and Viper V3 HyperSpeed (`1532:00B8`, family `viper-184`): persist and automatically apply every existing verified control, allow safe external manifests for new PIDs in an existing protocol family, finish evidence-backed hardware paths, and prove disconnect/resume/failure recovery.

GPU MUX, macros, the advanced lighting editor, and Viper surface calibration remain outside this work because the device or user explicitly excluded or deferred them.

## Non-Negotiable Safety Rules

- Every hardware SET remains unavailable until the matching current HID path has completed its required GET.
- A successful transport acknowledgement is not physical validation.
- Every state-changing validation captures the original state, reads the target back, restores the original state in `finally`, and reads the restored state back.
- External manifests cannot define raw reports, command classes, command IDs, argument payloads, or new protocol families.
- Manual fan control and Logo Breathing do not enter the production interface until their hardware completion gates below pass.
- Unknown fields, duplicate device identities, conflicting built-in devices, invalid ranges, and unsupported capabilities fail closed.

## 1. Complete Profile Application

### Persisted values

Extend the existing Profile model rather than introducing another store:

- `BladeProfileSettings`: add CPU Boost, GPU Boost, and Logo mode.
- `ViperProfileSettings`: add the complete DPI-stage table, including active stage and ordered X/Y values.
- `LightingProfile`: retain effect and parameters, but validate the supported effect names and typed parameters at the application boundary.
- `DeviceProfileSettings`: include lighting so the existing global -> device -> power precedence applies consistently.

The existing JSON version remains `1` because all new properties are optional and old documents deserialize safely. Clone and safe-default logic must deep-copy DPI stages and lighting parameters.

### Resolution and ordering

`ProfileResolver` continues to resolve power override, then device override, then global value. The apply order is:

1. Blade performance mode.
2. Blade CPU/GPU Boost and Max Fan, which depend on Custom mode.
3. Blade brightness, charge limit, and Logo.
4. Viper DPI stages, current DPI, polling rate, and idle timeout.
5. App-owned Blade software lighting after the verified hardware applier completes.

An invalid value produces one visible profile error and does not start later dependent writes. Independent Viper work may continue after a Blade-only failure; failures are grouped by device instead of globally suppressing the other device.

### Lighting shadow state

Software lighting has no trustworthy GET. `MainViewModel` therefore owns a shadow fingerprint derived from the resolved effect, validated parameters, active Profile, power state, and current Blade device path. It reapplies only when that fingerprint changes. Disconnect, resume, controller fault, or device-path change clears the fingerprint. Off, Static, Breathing, Spectrum, Wave, Fire, Starlight, Reactive, Ripple, Audio Meter, and Ambient Awareness are accepted after their individual evidence gates pass; unknown effects and parameters fail closed.

User-initiated setters save the read-back value into the active Profile only after the hardware operation succeeds. A save failure remains visible and restores the in-memory Profile snapshot without pretending persistence succeeded.

## 2. Safe External Device Manifests

### Source and precedence

Load `*.json` files from `%LocalAppData%\OpenSynapse\devices` after embedded manifests. The directory is optional. Loading is bounded to 64 files of at most 64 KiB each and uses `System.Text.Json` with the same strict schema as built-ins.

Embedded manifests are authoritative. An external manifest is rejected if any VID/PID overlaps a built-in or previously accepted manifest. One bad external file is reported in discovery diagnostics but does not disable built-in devices or other valid external manifests.

### Allowlist

External manifests may declare only an existing family:

- `blade-710`
- `viper-184`

For that family, every capability name, direction, report header, accepted response exception, and transport delay must be equal to or stricter than the static family contract already compiled into `RazerDeviceRegistry`. External JSON may provide identity, display name, product IDs, and collection selectors. It cannot add a capability or relax a family contract.

This supports a new same-family PID by copying and reviewing a manifest, without rebuilding OpenSynapse. Supporting a genuinely new packet format still requires a strongly typed parser/setter and a new compiled family contract.

### Runtime behavior

The registry is immutable after startup. Discovery and every controller receive the same registry instance so enumeration, telemetry, lighting, and writes cannot disagree about an external device. The UI exposes manifest load errors through existing diagnostics; no arbitrary manifest editor is added.

## 3. Remaining Hardware Paths

### Manual fixed fan target and fan curve

Add one transactional production method that accepts Automatic mode or Manual mode with a shared `2000..5000 RPM`, `100 RPM` step target. It must:

1. Read both thermal zones, both stored targets, and current tachometers from the current path.
2. Reject mismatched zone modes or targets.
3. For Manual, write both targets and then switch both zones to Manual while preserving the performance mode.
4. For Automatic, switch both zones to Automatic without inventing a target.
5. Read both zones and targets back.
6. On cancellation or failure, restore both targets and both zone modes using a non-cancelable restore path, then verify restoration.

The production method remains internal to the backend until the validation tool passes write/readback/restore, process-exit restore, USB disconnect/reconnect, and sleep/resume checks on the target Blade.

The fan-curve path is a separate protocol-recovery task; a fixed RPM target is not presented as a curve. Capture Synapse while opening the Product 710 custom fan page and changing exactly one temperature node on one zone. Correlate the Product 710 call site with its `rzDevice25` dependency and Native/service transfer, then establish the complete GET and SET envelopes, node ordering, temperature/RPM bounds, zone identity, readback, and restore sequence. Only that exact contract may become `BladeFanCurveProtocol`; promotion requires same-value and one-node-change write/readback/restore on both zones.

If manual fan state persists after the controlling process closes, production ownership includes a small watchdog process launched only while manual control is active. It receives the original two-zone state over an inherited anonymous pipe, restores Automatic/original state if the App exits unexpectedly, and requires no service installation or administrator rights. If hardware validation proves the firmware itself reverts safely on handle close, the watchdog is omitted.

### Logo Breathing

Do not promote the existing generic effect packet. First recover the exact Synapse Native/service sequence that changes a powered-off target into sustained Breathing, including any profile, power, or operating-mode commands. Add a validation action only after that sequence is deterministic. Production promotion requires visible sustained breathing, electronic readback where available, and exact Off/Static restoration.

### Blade battery and sleep telemetry

Add a GET-only validation action for wired battery level, charging status, automatic sleep, and time-to-sleep. It records decoded values and raw response envelopes without HID paths or serial numbers. Promotion to verified read-only telemetry requires two successful reads separated by a wake/sleep or power transition and plausible value/range checks.

### Software lighting parity and remaining Basic effects

Preserve the current matrix transport and layout. Static reverse-engineering may change Wave or Fire only when it proves an exact constant, state transition, native grid mapping, or timing rule. Visual validation must run each effect long enough to observe a full cycle and record mode, duration, direction, color, frame cadence, and restore result. Until exact Wave timing/angle scaling and Fire `7 x 23` -> `6 x 17` mapping are proven, their public evidence status remains `Approximate`.

Complete the remaining Basic effects without introducing the advanced editor:

- Starlight uses recovered Basic Lighting Engine constants and random scheduling only after its exact constructor/state evidence is established.
- Reactive and Ripple share a Windows low-level keyboard event source. Events are projected through `BladeLightingLayout`; injected events are ignored, hook callbacks never perform HID I/O, and a bounded channel transfers events to the renderer.
- Audio Meter uses Windows WASAPI loopback capture for the current default render endpoint. Audio callbacks publish normalized level bands to the renderer; endpoint changes restart capture and silence renders an unlit frame.
- Ambient Awareness uses Windows Graphics Capture for the selected display and reduces sampled edge regions to the keyboard layout. Capture permission denial disables the mode with a visible error and no fallback animation.

All input/audio/capture adapters have explicit start/stop ownership under `SoftwareLightingRuntime`; effect switching, device disconnect, suspend, and App exit dispose the old adapter before restoring the keyboard frame. No effect is described as Synapse-equivalent until a side-by-side visual run proves its timing, color behavior, and spatial mapping.

## 4. Lifecycle and Failure Recovery

### Deterministic tests

Add focused tests for:

- Profile deep clone, precedence, invalid values, dependent ordering, and complete DPI-stage equality.
- Per-device error isolation in Profile application.
- Lighting shadow reapply and stale-runtime fault filtering.
- External manifest directory absence, valid same-family PID, duplicate PID, unknown family/capability, relaxed contract, malformed JSON, file count, and file size limits.
- Manual fixed-target and curve validation, write ordering, partial-write failure, cancellation, watchdog handoff when required, aggregate restore failure, and post-restore readback.
- Reactive/Ripple event filtering and layout projection, Audio Meter endpoint restart/silence behavior, and Ambient Awareness permission/capture teardown.
- Device path change and disconnect clearing every write gate.

### Hardware validation

Hardware validation remains opt-in through `OPENSYNAPSE_HARDWARE_TEST=1` or an explicit validation-tool action. The final checklist covers:

- Synapse closed and Synapse contending for the collection.
- USB disconnect/reconnect during idle and during a write.
- Windows sleep/resume.
- App exit during software lighting and manual fan validation.
- Automatic Profile switching while one device is unavailable.
- At least ten minutes of Wave/Fire runtime with successful restore.

No validation step terminates Synapse automatically.

## 5. Diagnostics and Documentation

Diagnostics identify the manifest source by filename but never emit a HID path, serial number, username, or raw credential. Capability and frontend handoff documents distinguish `Verified`, `SourceBacked`, `Approximate`, and `Blocked`; only completed hardware gates can change those labels.

## Acceptance Criteria

- All existing verified setters are persisted and automatically reapplied, including CPU/GPU Boost, Logo Off/Static, Viper DPI stages, and the six current software-lighting modes.
- A valid external manifest can discover and control a new PID in an existing family without rebuilding the app, while unsafe or conflicting manifests fail closed and remain diagnosable.
- Manual fixed fan, manual fan curve, and Logo Breathing have the stated exact protocol and current-device evidence before production promotion; a failed gate keeps the overall completion goal open rather than relabeling source-backed work as finished.
- Blade battery/sleep reads have reproducible GET-only evidence across a power transition.
- Starlight, Reactive, Ripple, Audio Meter, and Ambient Awareness have owned Windows input sources, deterministic teardown tests, and recorded side-by-side visual validation. Wave and Fire have exact recovered timing/mapping evidence and no longer rely on an `Approximate` completion claim.
- Unit/integration tests and a Release build pass with zero warnings and zero errors.
- Required hardware lifecycle checks are recorded, original state is restored, and the capability matrix matches the evidence.
