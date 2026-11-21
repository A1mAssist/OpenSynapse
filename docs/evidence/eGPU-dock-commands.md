# eGPU / dock command evidence

## 0F51 Razer Core X V2

Evidence:

- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\0F51\main.072111f727c767458aea.js`
- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\0F51\rzDevice25FiverV2.699c898515afbaf0aacb.js`

The bundle selects `rzDevice25FiverV2`, `isEGPUV2`, product id `3921` (`0x0F51`)
and a 91-byte protocol-25 feature report. `p.Rt` resolves to `M = 6`, and the
main image uses `u.Ih.Fan2 = 6`; therefore the Core X V2 thermal id is 6.

| Method | Wire command | Data size | Payload / response |
|---|---|---:|---|
| `setThermalFanSpeed` | `0D:01` | 6 | Request `[classId=0, thermalId=6, RPM_hi, RPM_lo]`; response `[classId, thermalId, RPM_hi, RPM_lo]`. |
| `getThermalFanSpeed` | `0D:81` | 6 | Request `[classId=0, thermalId=6]`; response `[classId, thermalId, RPM_hi, RPM_lo]`. |
| `setThermalFanMode` | `0D:02` | 6 | Request `[classId=0, thermalId=6, mode]`; `Auto=0`, `Manual=1`, `ForceAuto=2`. |
| `getThermalFanMode` | `0D:82` | 4 | Request `[classId=0, thermalId=6]`; response `[classId, thermalId, mode]`. |
| `getThermalFanInformation` | `0D:83` | 12 | Request `[thermalId=6]`; bundle reads `data[6] * 100` as minimum RPM and `data[10] * 100` as maximum RPM. |
| `getThermalReading` | `0D:85` | 80 | Response count at `data[0]`, temperature values at `data[1..count]`; the first value is the chassis temperature. |

The `rzDevice25FiverV2` override is explicit for the two-byte big-endian RPM:

```js
new Uint8Array([6,13,1])   // setThermalFanSpeed
new Uint8Array([6,13,129]) // getThermalFanSpeed
new Uint8Array([6,13,2])   // setThermalFanMode
```

### Curve model

The bundle keeps `fiverFanController.manualPresets` in the host profile. The
default `activeTemperatureMode` is `"gpu"`; each preset carries a GPU curve and a
chassis curve. The curves are:

| Preset | GPU points (C, RPM) |
|---|---|
| Quiet | `(50,1000),(60,1200),(65,1400),(70,1650),(75,1900),(80,2200),(85,2500)` |
| Balanced | `(45,1050),(55,1300),(60,1600),(65,1900),(70,2200),(75,2450),(80,2650),(85,2800)` |
| Performance | `(40,1200),(50,1600),(60,2000),(65,2300),(70,2500),(75,2650),(80,2750),(85,2800)` |

| Preset | Chassis points (C, RPM) |
|---|---|
| Quiet | `(30,1000),(35,1150),(40,1300),(45,1500),(50,1750),(55,2000),(60,2250)` |
| Balanced | `(25,1000),(30,1150),(35,1300),(40,1550),(45,1800),(50,2050),(55,2300),(60,2550)` |
| Performance | `(20,1000),(25,1150),(30,1300),(35,1550),(40,1800),(45,2050),(50,2300),(55,2550),(60,2800)` |

For GPU temperature the bundle adds an implicit `(30 C, 1000 RPM)` lower point;
for chassis temperature it uses `(20 C, 1000 RPM)`. It linearly interpolates
between points, rounds to the nearest RPM, and clamps above the last point. It
uses `setThermalFanMode(0, 6, Manual)` and then `setThermalFanSpeed(0, 6, rpm)`;
the stock loop evaluates the selected curve every 2.5 seconds.

The GPU temperature comes from the shared `rzSystemMon` hardware monitor
(`GPU_Info`), while the chassis temperature comes from `0D:85`. OpenSynapse maps
those two sources to the existing `IPerformanceMonitor` and
`getThermalReading` paths and exposes the source in the Core X V2 card.

### 0F51 volume and interface status

The default host profile contains `volume:{isEnabled:true,value:50}`, but the
0F51 bundle has no device-specific volume read/write binding. No 0F51-specific
`class:id:size` command for volume, HDMI/USB interface status, or GPU-card
presence is bound by `rzDevice25FiverV2`; its specialized methods are limited
to the thermal commands above. The generic LED table does contain a candidate
`getLedGPUCardDetection` (`03:A0`, request data size `1`) with a
`ledClassId/enabled/status` response parser, but the 0F51 application has no
call site for it and the bundle does not establish that its `status` field is a
USB/HDMI interface state. These controls are therefore
`Blocked: no evidence` rather than inferred from the generic profile schema.

| Method | Wire command | Request data size | Decision |
|---|---|---:|---|
| `getLedGPUCardDetection` | `03:A0` | 1 | Generic candidate only; no 0F51 call site or verified interface semantics. |
| `setLedGPUCardDetection` | `03:20` | 3 | Generic candidate only; not exposed. |

## 0F52 / 0F59 / 0F5A / 0F5C

Evidence:

- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\0F52\`
- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\0F59\`
- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\0F5A\`
- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\0F5C\`

The bundles select the generic `rzDeviceType = rzDevice25`; none selects a
device-specific `rzDevice25FiverV2` or another accessory control class. Their
product records only define Chroma metadata (`productId` 3922/3929/3930/3932,
`category:"ACCESSORY"`) and a default profile. The latter can contain a generic
`volume` field, but that is host profile state, not a wire command.

The strings `getVolume`, `getMute`, `getHDMIStatus`, `setHDMIVolume`, and
`setPCVolume` occur in the shared `87887` UI-helper module. The helper calls
`rzDevice.getVolume()` / `getHDMIStatus()` only if that UI path is reached, but
the selected `rzDevice25` base class in the same bundle defines neither method,
and no PID-specific command table is supplied. The generic audio/HDMI code is
therefore not a usable device command for these PIDs. No `class:id:size` triple
is available for fan, volume, or interface status.

Blocked: no evidence for fan curve, volume, or interface-status commands.

## 030A

Evidence:

- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\030A\main.b298b272c43153c266ff.js`
- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\030A\rzASRock.3f22b127c7b5e26b8d94.js`

The ASRock bundle controls lighting through `RzASRock` / `RzDLLService`
(`SetCustomizedEffect`, `SetAllChannelEffectToBlack`, `SetStaticEffect`) and
does not expose a direct HID `class:id:size` triple. Its fixed stripe ids are
`[0x80000003, 0x80000004, 0x80000001, 0x80000002, 0x80000005]` for edition 0
and `[0x80000003, 0x80000004, 0x80000008, 0x80000001, 0x80000005]` for edition
129. Those are vendor-service region ids, not a HID command surrogate.

Blocked: no direct HID command evidence.
