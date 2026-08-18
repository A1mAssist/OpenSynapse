# Product 710 AMD Curve Optimizer

## Result

Product 710 explicitly enables `deviceUI.performance.hasAMDOverclock`. Its
voltage control is AMD Curve Optimizer, not a millivolt voltage override and
not voltage telemetry.

The Product 710 write path is:

```text
ON_SET_AMD_OVERCLOCKING
  payload.cpuVoltageOptimizer
  -> RzAMDOverClock.setCurveOptimizerValueAsync(value)
  -> RzAMDOverClock_v1.1.15.0.dll
  -> RzDLLService_v1.0.29.0.exe
  -> RzAMDOverClockDLL_v1.1.15.0.dll
  -> AMD Ryzen Master driver
```

The implementation range recovered from `SetCurveOptimizerValue` is exactly
`-30..0`. `0` means disabled or all offsets zero. Native GET also uses `-100`
as a special non-all-core/error state; OpenSynapse must reject it and must not
offer it as a writable value.

## Sources

| Source | Evidence |
| --- | --- |
| Product 710 bundle `f_000233` | `performance.hasAMDOverclock=true` |
| Shared Product task bundle `f_00022a` | Product event, GET/SET task, async wrapper API and RzDLLService startup |
| `RzAMDOverClock_v1.1.15.0.dll` | Async client exports and named-pipe/service process wrapper |
| `RzAMDOverClockDLL_v1.1.15.0.dll` | Synchronous JSON ABI, range checks, GET/SET/reset/enable and `FreeMalloc` |
| `RzAMDOverClockDLL.log` | Driver install/uninstall and privilege behavior on this Product 710 machine |

Installed paths used for this audit:

```text
%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Apps\Common\RzDLLService\
  RzAMDOverClock_v1.1.15.0.dll
  RzAMDOverClockDLL_v1.1.15.0.dll
  RzDLLService_v1.0.29.0.exe

C:\Program Files\Razer\RazerAMDOverClock\
  AMDRyzenMasterDriver.sys
  Device.dll
  Platform.dll
```

The admitted synchronous DLL SHA-256 is
`7966CF2F6DEAF18A277ACAFED433063AFEC7DB9F5C4275729601AAB40B54EB9E`.
It lives under a user-writable directory, so an elevated host must verify this
exact hash before loading it. A future Razer update requires a new static audit
and explicit hash admission.

The admitted async client SHA-256 is
`9F0EE89C1E003D2990880BABC7612FA76D0259A716B955FBC0A7D34D5DE8A418`.
Its file is `RzAMDOverClock_v1.1.15.0.dll`; it is distinct from the
service-loaded synchronous DLL above.

## Official async ABI

The Product 710 chunk cached as
`https://apps.razer.com/synapse/products/710/mw/6737.05cc203cffd1a7a12564.js`
contains Synapse's exact `ffi-napi-rz` table:

```text
DeviceInit:                    bool()
DeviceTerminate:               bool()
SetNodeFFIEvent:               bool(pointer)  // void callback(string UTF-8 JSON)
RegisterFFIEvent:              bool(string event/container ID)
UnRegisterFFIEvent:            bool(string event/container ID)
IsAPPServerRunning:            bool()
SetServerDLLPath:              bool(string)
SetExternalDLLPath:            bool(string)
GetCurveOptimizerValueAsync:   bool()
SetCurveOptimizerValueAsync:   bool(int)
```

`llvm-readobj --coff-exports` independently confirms every named export in
the fixed-hash x64 client. Synapse registers
`{00000000-0000-0000-FFFF-FFFFFFFFFFFF}`, passes the synchronous DLL path to
`SetServerDLLPath`, passes `C:\Program Files\Razer\RazerAMDOverClock` to
`SetExternalDLLPath`, and receives completion through the callback. The
successful GET event recorded in `main2.log` is:

```json
{"curveOptimizer":-1,"dllFunc":"GetCurveOptimizerValue","dllName":"RzAMDOverClockDLL","error":"","event":"{00000000-0000-0000-FFFF-FFFFFFFFFFFF}","status":"SUCCESS"}
```

`AmdCurveOptimizerAsyncController` implements this ABI without UAC. It only
connects to an already-running `RzDLLService`; it never starts, stops, or owns
that process. Both DLL hashes and the external `Platform.dll` / `Device.dll`
presence are checked before load. The read-only validation command is
`--amd-curve-async-read`.

## Synchronous ABI

Exports used by the minimal backend:

```text
Initialize() -> bool
DeviceInit() -> bool
GetCurveOptimizerValue(jsonUtf8) -> malloc JSON UTF-8 pointer
SetCurveOptimizerValue(jsonUtf8) -> malloc JSON UTF-8 pointer
FreeMalloc(pointer)
DeviceTerminate() -> bool
Terminate() -> bool
```

Input fields are `relateDllPath`, `dllName`, `dllFunc`, and, for SET,
`curveOptimizer`. Results contain `status`, `error`, `curveOptimizer`,
`dllName`, and `dllFunc`. Every non-null result pointer must be released by
the DLL's own `FreeMalloc` export.

Ghidra decompilation of `SetCurveOptimizerValue` at RVA `0x7EB00` shows:

```c
if ((value < -0x1e) || (0 < value)) {
    status = "FAIL";
    error = "Error! the curveOptimizer value out of range ! ";
}
```

GET accepts an all-core negative value other than `-100`, returns `0` for the
disabled/all-zero state, and returns failure for other states.

## Privilege boundary

The synchronous target creates and starts a temporary AMD Ryzen Master driver
service. Local logs from successful Synapse use show `check admin: true`,
driver service creation, `InitTune`, GET, and later service removal. A direct
read from the normal, non-elevated OpenSynapse validation process reached the
native GET and failed with `SEHException`; teardown logged `check admin:
false` and `OpenSCManager failed: 0x5`.

Therefore the WinUI process must not load the synchronous DLL. The recovered
official async client instead delegates the privileged call to RzDLLService
and does not require an elevated OpenSynapse process. Production integration
still remains disabled until that async GET is physically validated.

## Current implementation status

`AmdCurveOptimizerController` and `--amd-curve` retain the failed synchronous
research path. `AmdCurveOptimizerAsyncController` now implements the exact
official event ABI, strict routing/JSON/range checks, serialized GET, and a
GET/SET/GET transaction that only restores after a confirmed mismatch. A
missing SET response or missing readback is reported as indeterminate and
does not issue another write. No native export was executed during this
static implementation pass. Physical async GET, then reversible SET and
restore, remain required before the production UI is enabled.
