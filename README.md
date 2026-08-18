# OpenSynapse

OpenSynapse is a Windows 11 desktop controller for the verified Razer devices in the design spec.

## Run the release

Extract the complete release archive, then start `OpenSynapse.exe` in the root folder. Keep the adjacent `resources` folder intact; it contains the self-contained WinUI runtime and application files.

## Current slice

The current build is a WinUI 3 desktop application. It ships strict embedded manifests for `1532:02C6` (Razer Blade 16 2025) and `1532:00B8` (Razer Viper V3 HyperSpeed), and selects each manifest's declared HID control collection (`UsagePage 0001 / Usage 0002 / Feature 91 B` for both built-ins).

Implemented device paths:

- Enumerates HID interfaces through native `SetupAPI` and `hid.dll`, then watches path/access changes every three seconds so disconnects and reconnects clear stale controls.
- Blade: reads keyboard brightness, performance mode, automatic/manual fan state, manual fan target, CPU/GPU Boost, battery charge limit, Product 710 power telemetry, current CPU/GPU fan speed, advanced fan mode, and lid-logo state.
- Blade: writes keyboard brightness only after the matching read succeeds on the current device path, followed by an immediate GET readback.
- Blade lighting: the production UI exposes Off, Static, Breathing, Spectrum, Wave, Fire, Reactive, Ripple, Audio Meter, Ambient, Wheel, Starlight, and two-color Tidal through complete `6 x 17` frames. Reactive/Ripple own the verified internal-key input path; Audio Meter owns recoverable WASAPI loopback; Ambient owns permission-gated internal-display capture. Wave and Fire use recovered algorithms but remain approximate pending exact visual parity.
- Display: identifies the active internal panel through Windows display topology, reads its current resolution/rate, enumerates supported rates for that resolution, and applies a selected/profile rate with validation, readback, and restore. External and clone topologies fail closed.
- Blade Logo: writes verified Off/Static states only after a current-path read; writes mode then power, reads both values back, and restores the original state after failure or cancellation. Breathing stays evidence-gated.
- Viper: reads battery estimate, polling rate, current DPI, idle timeout, the complete DPI-stage table, and the validated low-battery raw value.
- Viper: writes polling rate, current DPI (`100..30000`), idle timeout, and complete DPI-stage tables with readback; DPI-stage failure/cancellation restores and re-reads the original table.
- Viper: reads all 16 Profile 1 Normal/HyperShift assignments and exposes only the verified `Off` and mouse-button codes `1,2,3,4,5,9,10` for readback/restored writes. Current hardware polling is limited to `125/500/1000 Hz`.
- Shows live CPU, NVIDIA GPU, memory, and fixed-drive telemetry. Missing data is displayed as `--`; sampler failures remain visible and do not silently terminate the refresh loop.

The Blade performance/fan/boost/charge protocol is backed by exact `02C6` / `RZ09-05286` public implementations and deterministic GET/parser tests. Performance mode, Custom-only CPU/GPU Boost and Max Fan, and charge-limit writes have passed local hardware write/readback/restore verification. Fixed-RPM target protocol builders/parsers now cover `0D01`/`0D81` with a strict `2000..5000 RPM` range and `100 RPM` step; production manual writes remain gated until thermal, process-exit, device-disconnect, and sleep/wake recovery tests are complete.

Manual fixed/curve fan writes, firmware quick-effect writes without visible confirmation, Viper extended button functions, one-time full charge, and battery-type calibration are not presented as finished features. GPU MUX is explicitly excluded for this Optimus-only Blade; OpenSynapse leaves GPU mode under the Windows/Razer platform path. The fixed target reader fails closed if CPU/GPU stored targets disagree or either zone is outside the verified range. The app implements local named Profile selection, create/clone/rename/delete, atomic JSON import/export, executable binding/unbinding with foreground switching, user-level startup registration, resume refresh, second-instance activation, and tray residency. Profile changes roll back their in-memory state when persistence fails. Global/device/power-scope editing remains a backend-only capability. Blade mapping/HyperShift/Snap Tap remain opt-in; the physically verified M5 microphone mute indicator follows the Blade control-collection lifecycle by default. Synapse can still contend for the same feature collection; a failed query is logged instead of silently using an old value.

M5 microphone mute-indicator synchronization starts automatically for Product 710 and stops on disconnect or App exit. Software lighting defers its Normal-mode restoration while this session is active, and App shutdown restores Normal exactly once after lighting stops. F3 speaker indication is excluded because the Product 710 Synapse path does not call that target.

Local diagnostics are appended to `%LocalAppData%\OpenSynapse\logs\opensynapse.log`. The file rotates at 1 MiB and keeps one `opensynapse.log.previous`; logging failures never interrupt device control.

Production writes currently verified: Blade keyboard brightness, performance mode, Custom-only CPU/GPU Boost, Custom-only Max Fan, charge limit, Logo Off/Static, and the Windows internal-panel refresh rate; Viper current DPI, complete DPI stages, 125/500/1000 Hz polling, idle timeout, and restricted Profile 1 button mappings. Blade fixed/manual fan and battery policy remain evidence-gated. Blade software quick effects are production-connected, but Wave and Fire remain approximate pending the exact parameter, geometry-mapping, actual-refresh-rate, and current-device visual evidence described above. Advanced lighting editor, macros, and Viper calibration are deferred. THX, EQ, volume leveling, and voice clarity are explicitly outside OpenSynapse's product scope; reverse-engineering notes are retained as archival evidence only.

Device identity, collection matching, transport delay, and admitted request headers live in strict embedded JSON manifests under `src/OpenSynapse.Windows/Devices/Manifests`. A same-family device still requires a reviewed built-in manifest and hardware evidence; unknown fields, families, capabilities, duplicate VID/PIDs, malformed hex, oversized arguments, and unapproved transport exceptions fail closed. Dynamic values, parsers, current-path write gates, readback, cancellation, and restoration remain in strongly typed C# handlers. External drop-in manifests and arbitrary raw reports are intentionally unsupported.

## Front end / back end

- `src/OpenSynapse.App`: WinUI 3 window, bindings, refresh action, and user-facing states.
- `src/OpenSynapse.Core`: device identity, performance snapshots, and hardware telemetry contracts.
- `src/OpenSynapse.Windows`: Windows HID discovery, feature-report transport, evidence-gated Razer queries and writes, system telemetry, Core Audio M5 mute synchronization, and opt-in Blade mapping input hosts.
- `tests/OpenSynapse.Core.Tests`: identity parsing, report validation, protocol replay, write/readback mismatch, boundary, and NVIDIA parser tests.

The WinUI project consumes `IDeviceDiscovery` and snapshots. It does not call `SetupAPI`, `hid.dll`, or any hardware API directly.

## Build and test

```powershell
dotnet restore OpenSynapse.slnx
dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj
dotnet build OpenSynapse.slnx
dotnet build src/OpenSynapse.App/OpenSynapse.App.csproj -p:Platform=x64
```

Run the opt-in hardware smoke tests. They read Blade platform state, change Blade brightness by one raw step, and temporarily change the three supported Viper settings. Every changed value is read back and restored:

```powershell
$env:OPENSYNAPSE_HARDWARE_TEST='1'
dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --filter 'Category=Hardware'
```

Run the debug executable after the build:

```powershell
& .\src\OpenSynapse.App\bin\x64\Debug\net10.0-windows10.0.19041.0\OpenSynapse.App.exe
```

Close Razer Synapse during the first hardware probe so the access state can be observed without a competing owner. The app never terminates Synapse itself.

Run the GET-only protocol probe. By default it uses only locally verified reads; `--include-source-backed` adds the compiled source-backed GET list. The probe accepts no arbitrary class, command, argument, or SET input and omits HID paths from JSON:

```powershell
dotnet run --project tools/OpenSynapse.ProtocolProbe/OpenSynapse.ProtocolProbe.csproj -- `
  --output artifacts/protocol/2026-08-12/verified.json

dotnet run --project tools/OpenSynapse.ProtocolProbe/OpenSynapse.ProtocolProbe.csproj -- `
  --include-source-backed `
  --output artifacts/protocol/2026-08-12/source-backed.json
```

Probe exit codes are `0` for successful results, `1` for query errors, `2` when no matching available device/result exists, and `64` for invalid options. Hardware smoke tests require `OPENSYNAPSE_HARDWARE_TEST=1`, and every supported write test restores the original value.

Run the read-only Core Audio mute source check. It does not open Razer HID handles, send feature reports, or require OpenSynapse to be closed:

```powershell
dotnet run --project tools/OpenSynapse.HardwareValidation/OpenSynapse.HardwareValidation.csproj -c Release -- `
  --core-audio-mute-read
```

## Protocol sources

The 91-byte Windows feature-report layout, CRC, response states, and device commands are based on protocol facts documented and implemented by OpenRazer, cross-checked against OpenRGB, and verified against the two local HID collections. Relevant upstream sources:

- OpenRazer `razercommon.h` and `razercommon.c`: report layout, CRC, retry, and response status.
- OpenRazer `razerchromacommon.c`: Blade brightness and Viper battery, polling-rate, DPI, and idle-timeout commands.
- OpenRazer device support PRs `#2450` (`02C6`) and `#2149` (`00B8`).
- `blauzim/razer-ctl` (MIT): exact `RZ09-05286`, PID `02C6` performance, fan-state, and charge-limit commands and readbacks.
- `encomjp/razer-control-revived` (GPL-2.0): independent `02C6` device table and fan bounds used only as corroborating protocol evidence.

OpenRazer is GPL-2.0-or-later. This repository does not yet declare a distribution license; do not redistribute binaries until the project license and attribution requirements are finalized.
