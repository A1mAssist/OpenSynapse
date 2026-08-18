# Product 710 host mapping runtime and trackpad boundary

## Result

Product 710 ordinary mapping, HyperShift and Snap Tap are host-side
MappingEngine features. They are not Product `02C6` SET reports. Their static
configuration and IPC lifecycle are closed; the remaining work is behavioral
validation of an owned replacement runtime.

The Product 710 trackpad action is also host-side. Its exact implementation is
ordinary Windows registry and keyboard-input behavior, so it can be reproduced
without Synapse, a Razer service, or a Product 710 HID command.

No native call, injected key event, registry write, driver IOCTL, or hardware
SET was executed during this audit.

## Sources

| Source | SHA-256 | Evidence |
| --- | --- | --- |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000233` | `A0F0AF07C6CFBD1C6F03516ED63C93858F5CD0FF2E17B6A3B6D0DAFB4608A79B` | Product 710 inputs, defaults and feature selection |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00022a` | `00E18FD4D8C4F5C20E31308160C6D8599997FE26BF3C41CEF4A076440708D749` | Mapping task graph and Product trackpad caller |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000005` | `9E80EDD2D8C2628614E0823FDA13967D2AA4AE66503FE828992A0E7EF640EB05` | Renderer-to-Electron system-utils action wrapper |
| `C:\Program Files\Razer\RazerAppEngine\app-4.0.698\CommonDLL\mapping_engine.dll` | `82CF78080C78EB7092A12BEC89421E00AAC5A1047F41AF3D205ECE806980A15B` | Host mapping executor and filter-driver client |
| `C:\Program Files\Razer\RazerAppEngine\app-4.0.698\CommonDLL\SysUtilsNative.dll` | `0FBC869A793AF9BE581AF7103FF517B35598F85A2C9F34FE060C1E22C2C07507` | Trackpad GET/toggle implementation, version `1.0.94.0` |

Detailed compiler evidence is already recorded in
`blade-product710-mapping-snaptap.md`; exact device input and native DLL I/O is
in `docs/reverse-engineering/mapping-engine-input-io.md`. This document closes
their runtime boundary without duplicating their generic inventories.

## Minimum Product 710 mapping graph

The profile source of truth is:

```text
profiles[].mappings
profiles[].snapTap
profiles[].appEngine
```

An ordinary assignment and a HyperShift assignment use the same input schema.
`isHyperShift` selects the layer:

```json
{
  "inputType": "KeyInput | DKMInput",
  "inputID": "KEY_* | DKM_*",
  "isHyperShift": false,
  "outputType": "<mapping group>",
  "<mapping group>": {}
}
```

The selected Product 710 task graph is:

```text
ON_SET_KEYMAPPING {mappingList}
  -> replace activeProfile.mappings
  -> compile activeProfile.appEngine = QA(mappingList, profile)
  -> persist the complete profile
  -> MappingEngine.localStorageSetItem(
       "synapse_710_{containerId}", completeProductJson)
```

Product 710 has no `obm` declaration, does not load the keyboard OBM engine,
and takes the state-machine branch that ends after `generateAppEngine`. The
generic `updateObm` branch is therefore not a Product 710 capability.

The compiler emits paired press/release records. Normal records have the
ordinary layer, while HyperShift records carry `hypershift:true`. The
HyperShift activator compiles to an output of `type:"hypershift"`; it is not a
Windows modifier and is not written to firmware.

Snap Tap persists exactly:

```json
{
  "isEnabled": false,
  "keyList": [{ "key1": "KEY_A", "key2": "KEY_D", "id": 1 }]
}
```

When enabled, the compiler adds the pair id as `snaptapId` on the matching
MappingEngine input records. If no user mapping exists for a member, it first
creates pass-through press/release records. The Product 710 state machine does
not call the generic OBM kill-switch SET because the product has no OBM
capability declaration.

## Input, executor and IPC lifecycle

Product 710 uses `addUsbDevice(..., skipFilterDriver:false)`. Its selected
runtime sequence is:

```text
renderer Product 710 module
  -> Electron MappingEngine wrapper
  -> mapping_engine.dll mappingEngineInitialize()
  -> addUsbDevice(deviceInfoJson, deviceEventCallback, completedCallback)
  -> wait for device event type 5 {type:"info",info:"driver ready"}
  -> localStorageSetItem(productKey, completeProductJson)
  -> enableMapping()

RzCommon filter input / Product 710 hardware-event HID input
  -> mapping_engine.dll input record
  -> normal/HyperShift/Snap Tap resolution
  -> native-supported output execution
  -> optional event type 2 "unsupportedmapping" for browser-owned outputs

teardown
  -> removeUsbDevice(deviceInfoJson, completedCallback)
  -> mappingEngineShutdown(completedCallback)
```

The minimum native exports and callback signatures are already statically
closed in `mapping-engine-input-io.md`. `registerInputNotification` and event
type `1` are observation/UI-lighting callbacks, not the mapping executor.
`registerUnsupportedMapping` is needed only for outputs the native engine
deliberately hands back to Product JavaScript.

Static analysis and existing physical captures establish dedicated-key report
press/release parsing, but they do not prove the complete behavioral semantics
of arbitrary ordinary mappings, HyperShift transitions or Snap Tap priority.
An OpenSynapse-owned runtime must validate source-device discrimination,
injection tagging, loop prevention, last-input priority and fail-safe release
on device removal, sleep, profile switch, secure desktop and process failure.

## Exact Product 710 trackpad caller

The Product 710 `bladeTrackpad` MappingEngine output raises
`ON_SWITCH_TRACKPAD_MODE_BY_DEVICE`. The task executes:

```text
before = !!getTouchPadEnableStatus()
if assignment == "toggle":
    toggleTouchPadEnableStatus()
    after = !!getTouchPadEnableStatus()
publish/store after
```

The renderer methods contain no payload. They send Electron actions named
exactly `getTouchPadEnableStatus` and `toggleTouchPadEnableStatus`.

## SysUtilsNative GET

`SysUtilsNative.dll` exports `getTouchPadEnableStatus` at RVA `0x66DD0`.
The function is equivalent to:

```text
RegGetValueW(
  HKEY_CURRENT_USER,
  "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\PrecisionTouchPad\\Status",
  "Enabled",
  RRF_RT_REG_DWORD,
  NULL,
  &value,
  &sizeof(value))
```

It returns the DWORD on success and `-1` on failure. Synapse immediately
coerces that return to boolean, so an error (`-1`) is displayed as enabled.
OpenSynapse must not copy that error handling; missing/invalid data is Unknown,
not `true`.

## SysUtilsNative toggle

`toggleTouchPadEnableStatus` is exported at RVA `0x67AB0`. It does not write
the registry or call a device driver. It calls the legacy Windows
`keybd_event` API with this exact sequence, always using scan code equal to the
virtual key and `dwExtraInfo=0`:

| Order | Virtual key | Flags |
| ---: | --- | --- |
| 1 | `VK_F24 (0x87)` | `KEYEVENTF_EXTENDEDKEY (1)` |
| 2 | `VK_CONTROL (0x11)` | `KEYEVENTF_EXTENDEDKEY (1)` |
| 3 | `VK_LWIN (0x5B)` | `KEYEVENTF_EXTENDEDKEY (1)` |
| 4 | `VK_F24 (0x87)` | `KEYEVENTF_EXTENDEDKEY (1)` |
| 5 | `VK_F24 (0x87)` | `KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP (3)` |
| 6 | `VK_CONTROL (0x11)` | `KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP (3)` |
| 7 | `VK_LWIN (0x5B)` | `KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP (3)` |
| 8 | `VK_F24 (0x87)` | `KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP (3)` |

This can be reproduced with documented Windows APIs. Use `RegGetValueW` (or a
.NET registry API) for the state and `SendInput` for the exact key sequence;
`keybd_event` is superseded. A Razer DLL/service and Product 710 HID access are
not required for the toggle itself.

## Admission boundary

| Capability | Statically closed | Physical/runtime validation still required | Razer dependency |
| --- | --- | --- | --- |
| Profile persistence/compiler routing | Yes | Schema round-trip against current Product profile | No for an owned compiler/runtime |
| Ordinary mapping | Config and event graph | Internal-vs-external device isolation, press/release and injected-event loop safety | Current Synapse implementation uses `mapping_engine.dll` + RzCommon; owned runtime can replace it |
| HyperShift | Layer representation and activator output | Hold/release ordering, repeats and profile/device transitions | Same as ordinary mapping |
| Snap Tap | One-pair config and `snaptapId` graph | Last-input priority for every overlap/release sequence and crash cleanup | Same as ordinary mapping |
| Trackpad state GET | Exact registry value | Missing key, policy-disabled device and real hardware state agreement | None |
| Trackpad toggle | Exact Windows key sequence | One reversible toggle/readback/restore test on Product 710 | None |

There is no reason to reverse or fabricate a Product 710 trackpad HID report.
Using the installed MappingEngine remains an opt-in compatibility path only;
it is not a standalone distribution solution because it depends on a closed,
versioned DLL and installed RzCommon driver.
