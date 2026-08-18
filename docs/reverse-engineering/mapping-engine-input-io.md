# mapping_engine input I/O evidence

## Target binary

- File: `C:\Program Files\Razer\RazerAppEngine\app-4.0.698\CommonDLL\mapping_engine.dll`
- Version: `1.3.15.8`
- SHA-256: `82CF78080C78EB7092A12BEC89421E00AAC5A1047F41AF3D205ECE806980A15B`
- Image base used below: `0x180000000`

The Ghidra extraction script is
`tools/OpenSynapse.HardwareValidation/ghidra/DumpMappingIo.java`. Generated
decompiler output is intentionally kept under untracked `artifacts/ghidra/`.

## Two independent input paths

`mapping_engine.dll` has two different input paths. They must not be treated as
one protocol.

### Filter-driver input notification

`DriverImpl::ConnectToDriver` at `0x18010e030` opens its supplied ANSI path with:

```text
CreateFileA(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
            OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL)
```

On this machine that endpoint is the `Razer Control Device`:

```text
Instance: RZCONTROL\VID_1532&PID_02C6&MI_00\9&350DFF7&0
Interface GUID: {e3be005d-d130-4910-88ff-09ae02f680e9}
Service: RzCommon
Interface path:
\\?\RZCONTROL#VID_1532&PID_02C6&MI_00#9&350dff7&0#{e3be005d-d130-4910-88ff-09ae02f680e9}
```

The generic overlapped IOCTL wrapper is `0x18010eaea`. Initial connection and
each completion submit:

```text
DeviceIoControl(handle,
                0x88883018,
                NULL, 0,
                context + 0x20, 0x130,
                NULL,
                context /* OVERLAPPED */)
```

Two contexts are kept at `DriverImpl + 0x200` and `DriverImpl + 0x350` and are
alternated by `0x180112140` (`DriverImpl::OnIOCompleted`). Relevant output
offsets, relative to `context + 0x20`, are:

| Offset | Width | Meaning established by use |
| --- | ---: | --- |
| `+0x08` | 4 | notification route; accepted values are `1`, `2`, `4` |
| `+0x0c` | variable | input payload passed to the keyboard/mouse parsers |
| `+0x10` | 4 | input kind; `1` keyboard, `2` mouse, `4` direct two-byte event |
| `+0x29` | 1 | first byte of the direct event when input kind is `4` |
| `+0x2a` | 1 | second byte of the direct event when input kind is `4` |

Keyboard payload parsing at `0x180112027` consumes 16-bit fields at payload
offsets `+0x08`, `+0x0a`, and `+0x0c`. Mouse payload parsing at `0x1801120a9`
consumes 16-bit fields at `+0x08`, `+0x0c`, and `+0x0e`.

Other observed driver IOCTLs include `0x8888301c`, `0x88883020`,
`0x8888302c`, `0x88883030`, `0x88883034`, `0x88883038`, `0x88883180`, and
`0x88883184`. They are not required for the dedicated-key listener and are not
specified here beyond the evidence.

### Hardware-event HID input

This is the path relevant to the Blade dedicated keys. The endpoint constructor
at `0x180120a78` opens each supplied HID path with:

```text
CreateFileA(path, GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
            OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL)
```

The read helper at `0x180120cb8` alternates two `OVERLAPPED` objects and issues
`ReadFile(handle, buffer, 100, NULL, overlapped)`. The completion path forwards
the exact received bytes; it does not transform the report through the RzCommon
IOCTL structure.

Existing physical Raw Input evidence from Blade `MI_01&Col05` is limited to
generic notification report `0x05`:

```text
\\?\HID#VID_1532&PID_02C6&MI_01&Col05#<redacted>#{4d1e55b2-f16f-11cf-88cb-001111000030}

05 33 0C 0C 00 00 00 00 00 00 00 00 00 00 00 00
05 08 FF 00 00 00 00 00 00 00 00 00 00 00 00 00
```

The official Protocol 2.5 parser identifies these as
`POWER_WATTAGE_EVENT (0x33)` with two `200W (0x0c)` values and
`BRIGHTNESS_CHANGE_EVENT (0x08)` with value `0xff`. They happened during the
key test but are not M3 or M4. Source artifact:
`artifacts/protocol/2026-08-15/blade-raw-m3-m4-log-02.json`.

Two controlled direct reads of `MI_01&Col04` in Normal mode produced no
completed reports, including after M3/M4/M5 presses:

```text
artifacts/protocol/2026-08-15/blade-col04-m345-01.json
artifacts/protocol/2026-08-15/blade-col04-m345-02.json
```

After switching Product 710 to Software/Driver mode with Protocol 2.5 command
class `0x00`, command `0x04`, arguments `03 00`, both a direct asynchronous read
and an independent Windows Raw Input capture received the same sequence from
`MI_01&Col04` while M3, M4, and M5 were pressed in that order:

```text
04 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00
04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
04 D3 00 00 00 00 00 00 00 00 00 00 00 00 00 00
04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
04 D4 00 00 00 00 00 00 00 00 00 00 00 00 00 00
04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
```

Sources:

```text
artifacts/protocol/2026-08-15/blade-col04-m345-software-mode-01.json
artifacts/protocol/2026-08-15/blade-reactive-m345-rawinput-02.json
```

The collection suffix and HID report byte remain different namespaces, but the
endpoint and bytes are now independently physically verified. For these three
physical keys the verified values are `0x03`, `0xD3`, and `0xD4`, not
`0xD2`, `0xD3`, and `0xD4`.

## Official Product 710 dedicated-key path

Static analysis of the official Product 710 bundles and `mapping_engine.dll`
establishes the following path:

1. Product 710 calls `addUsbDevice`.
2. It loads and migrates the active device profile, then persists the complete
   product object through MappingEngine `localStorageSetItem`.
3. That object declares `4: "razerKeyReportID"` and
   `5: "hardwareEventReportID"`. Its complete dedicated-key table is
   `DKM_03 -> 3`, `DKM_D2 -> 210`, `DKM_D3 -> 211`, and `DKM_D4 -> 212`.
4. The native report dispatcher maps incoming report byte `0x04` to internal
   case `6`; `FUN_18007ED66` treats the remaining non-zero bytes as the current
   RazerKey set and differences it against the previous set.
5. A newly present key becomes `{type:"razerKey", key, flag:0}` and a removed
   key becomes the same input with `flag:1`.
6. MappingEngine resolves that input against the active `appEngine` mappings.
   Native-supported outputs are executed inside the engine. An output the
   engine cannot execute is surfaced through `unsupportedmapping` when that
   callback is registered.
7. Product 710 always calls `UnsupportedMappingEventHandler.init`, but the
   handler registers only when the injected `handleUnsupportedMappingEvents`
   flag is enabled and at least one short- or long-press mapping exists. It then
   parses `event.input`, looks up the key in `DKMKEYS`, and dispatches that
   browser-side assignment.

The Product 710 bundle does not call `enableRazerKeyInputRedirect`; its only
occurrences are generic API wrapper definitions. Native
`RegisterUnsupportedMapping` (`FUN_18006B600`) only atomically sets the device
flag at offset `+0x250`, while `SetUnsupportedMappingCallbackOnDeviceThread`
stores the callback at `+0x2e0`. Neither operation sends `DeviceIoControl`,
`WriteFile`, or a HID feature report. Input redirect is therefore a separate
facility and not how Product 710 consumes M3/M4/M5.

Official startup order around the relevant calls is:

```text
addUsbDevice
  -> load/migrate active profile
  -> localStorageSetItem(full Product 710 data and appEngine mappings)
  -> register input-event manager
  -> UnsupportedMappingEventHandler.init
       -> conditional registerUnsupportedMapping
       -> conditional listen for unsupportedmapping
```

The Lighting Engine observation path is separate from assignment execution and
is visible in both its compiled JavaScript and log:

```text
Product 710 DeviceInfo.isRegisterInputEvents = true
  -> rzInputEvents.registerDevice(usbDevice)
  -> RzMappingEngine.addUsbDevice(usbDevice, skipFilterDriver:false)
  -> RzMappingEngine.registerInputNotification(usbDevice)
  -> native setInputNotificationCallback
  -> callback event type 1
  -> Electron IPC "inputnotified"
  -> rzInputEvents.oninputevent
  -> emit("inputevent", {
       input: JSON.parse(event.input),
       productId, containerId, vendorId, timeTick
     })
```

For RazerKey report `0x04`, `event.input` has this native-generated shape:

```json
{"type":"razerKey","key":3,"flag":0}
```

`flag:0` is press and `flag:1` is release. The key can likewise be `211` or
`212` for the other two physically tested buttons. The Lighting Engine log also
shows `setDeviceMode() mode:3, param:0` before the software effect takes over,
which matches the Protocol 2.5 `03 00` mode arguments used by the direct test.
This explains why Normal-mode direct reads were empty while the Software-mode
captures contained the dedicated-key reports.

Supporting artifacts:

- `artifacts/reverse-engineering/2026-08-15/product710-m345-report-routing.md`
- `artifacts/ghidra/mapping-product710-report-path.txt`
- `artifacts/ghidra/mapping-io-decompile.txt`

## Minimum official DLL call surface

Synapse's Windows wrapper loads the following C exports from
`mapping_engine.dll`. The signatures are confirmed by both the PE export names
and the extracted `ffi-napi-rz` declarations:

```c
void mappingEngineInitialize(void (*completed)(void));

void addUsbDevice(
    const char *deviceInfoJson,
    void (*deviceEvent)(const char *deviceInfoJson,
                        int eventType,
                        const char *eventJson,
                        unsigned long long timeTick),
    void (*completed)(bool ok, const char *reason, const char *deviceInfoJson));

void localStorageSetItem(
    const char *key,
    const char *valueJson,
    void (*completed)(bool ok, const char *reason));

void enableMapping(void (*completed)(bool ok, const char *reason));
void removeUsbDevice(
    const char *deviceInfoJson,
    void (*completed)(bool ok, const char *reason, const char *deviceInfoJson));
void mappingEngineShutdown(void (*completed)(void));
```

For Product 710 the device object supplied to the native DLL has this minimum
shape:

```json
{
  "vendorId": 5426,
  "containerId": "{00000000-0000-0000-FFFF-FFFFFFFFFFFF}",
  "productId": 710,
  "guid": "a fresh UUID"
}
```

The full Product 710 configuration is then written under:

```text
synapse_710_{00000000-0000-0000-FFFF-FFFFFFFFFFFF}
```

It must contain the `reportIDs` table and active `appEngine` graph. The observed
standalone probe sequence is:

```text
mappingEngineInitialize
addUsbDevice
wait for device event type 5: {"type":"info","info":"driver ready"}
localStorageSetItem
enableMapping
```

`registerInputNotification` plus `setInputNotificationCallback` is an optional
UI-observation surface, not the native mapping executor. Their event callback is
`(deviceInfoJson, eventType, inputJson, timeTick)` and Synapse accepts only event
type `1` as `inputnotified`. `registerUnsupportedMapping` plus
`setUnsupportedMappingCallback` is required only for outputs deliberately
executed by browser-side Product code; Synapse accepts event type `2` there.

Product 710 passes `skipFilterDriver:false`, so its official path calls
`addUsbDevice`, whose internal option is `{"type":"usb"}` and connects the
RzCommon filter driver. `addUsbDeviceWithoutFilterDriver` exists and passes
`{"type":"usb","connectFilterDriver":false}`, but Product 710 does not use it
and it has not been physically validated for these keys. Direct standalone DLL
probes succeed without `RazerAppEngine.exe`, but the kernel drivers `RzCommon`
and `RzDev_02c6` were installed and running. Therefore the evidence supports
"no Razer user-mode service process required", not "no Razer driver required".

## Standalone listener decision

A standalone native listener is physically established without loading or
distributing `mapping_engine.dll`:

1. Put Product 710 into Software/Driver mode for the lifetime of the listener.
2. Register Windows Raw Input for HID usage pages `0x01` and `0x0c`, or open
   `VID_1532&PID_02C6&MI_01&Col04` for shared asynchronous reads.
3. Filter the source path to Product 710 `MI_01&Col04`.
4. Accept only report ID `0x04` and treat non-zero trailing bytes as the current
   RazerKey set.
5. Difference the current set against the previous report. Newly present keys
   are presses; removed keys are releases.
6. Translate the physically confirmed `0x03`, `0xD3`, and `0xD4` values for the
   three tested keys.
7. Restore Normal mode when the software-lighting/input session ends.

Do not infer M-key identity from report `0x05`, and do not add a dependency on
the RzCommon `0x88883018` notification path for this listener. Synapse Product
710 does use `addUsbDevice` with `skipFilterDriver:false`, but the physically
verified standalone path receives the dedicated-key reports directly from the
HID collection. `DKM_D2 -> 0xD2` remains part of the official Product 710 table,
but it was not emitted by the three-key physical test and must not be substituted
for the verified `0x03` value.
