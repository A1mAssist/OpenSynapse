# Blade Product 710 MappingEngine compiler and driver evidence

Date: 2026-08-16

Scope: Blade 16 2025, USB `1532:02C6`, Synapse Product ID `710`.

This artifact separates three contracts that Synapse combines at runtime:

1. Product profile JSON and its AppEngine compiler.
2. `mapping_engine.dll` graph parsing and execution.
3. The installed `RzCommon` filter-driver transport.

None of these records is a Product 710 HID feature report. OpenSynapse must not
advertise key remapping, HyperShift, or Snap Tap as firmware capabilities.

## Source identity

Product compiler chunk:

```text
C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000225
SHA-256 B74E320D2C156D109C35C446CC64F517321001A8B602EB3F1911C2C42BEE001C
Length 108890 bytes
```

Shared profile transformation chunk:

```text
C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00022a
SHA-256 00E18FD4D8C4F5C20E31308160C6D8599997FE26BF3C41CEF4A076440708D749
Length 1600985 bytes
```

Native engine:

```text
C:\Program Files\Razer\RazerAppEngine\app-4.0.698\CommonDLL\mapping_engine.dll
Version 1.3.15.8
SHA-256 82CF78080C78EB7092A12BEC89421E00AAC5A1047F41AF3D205ECE806980A15B
```

## Exact compiler records

Webpack module `70873` in `f_000225` contains the input generators and event
pair assembler. A normal keyboard input compiles to:

```json
{
  "type": "keyboard",
  "scancode": 30,
  "hypershift": false,
  "flag": 0
}
```

`modifiers` is present only when the source mapping supplies an input modifier
bitmask. Release uses the same object with `flag + 1`; extended keys therefore
retain `2/3` rather than being flattened to `0/1`.

A Product 710 dedicated key compiles to:

```json
{
  "type": "razerKey",
  "key": 3,
  "hypershift": false,
  "flag": 0
}
```

The release record uses `flag:1`. The Product 710 table is:

| Input ID | Key byte | Product default |
| --- | ---: | --- |
| `DKM_03` | `0x03` | HyperShift while held |
| `DKM_D2` | `0xD2` | Application / HyperShift assignment |
| `DKM_D3` | `0xD3` | performance action |
| `DKM_D4` | `0xD4` | microphone mute action |

Module `38833` proves the HyperShift activator outputs. The exact Product 710
`DKM_03` graph is:

```json
[
  {
    "input": {"type":"razerKey","key":3,"hypershift":false,"flag":0},
    "output": {"type":"hypershift","flag":0}
  },
  {
    "input": {"type":"razerKey","key":3,"hypershift":false,"flag":1},
    "output": {"type":"hypershift","flag":1}
  }
]
```

Normal and HyperShift-layer assignments are independent graph entries. The
layer is selected by the input's `hypershift` boolean; it is not an output
modifier bit.

## Exact Snap Tap compilation

Module `4753` in `f_00022a` transforms the active profile only when
`activeProfile.snapTap.isEnabled` is true. It looks up both configured keys,
adds the same numeric `snaptapId` to all matching press/release inputs, and
synthesizes pass-through pairs when no user mapping exists.

For the Product 710 default A/D pair (`id:1`), A press/release has this form:

```json
[
  {
    "input": {"type":"keyboard","scancode":30,"flag":0,"snaptapId":1},
    "output": {"type":"keyboard","scancode":30,"flag":0}
  },
  {
    "input": {"type":"keyboard","scancode":30,"flag":1,"snaptapId":1},
    "output": {"type":"keyboard","scancode":30,"flag":1}
  }
]
```

D uses scan code `0x20` with the same ID. Module `80427` proves that the
HyperShift + left Shift toggle is disabled on press and toggles on release:

```json
{
  "downOutput": {"type":"disable"},
  "upOutput": {"type":"snapTap","id":"toggle"}
}
```

## Exact graph hash

Module `63739` in `f_000225` computes the graph hash as follows:

1. Shallow-copy the graph.
2. Delete root properties `hash` and `gamemode`.
3. Serialize with recursively lexicographically sorted object keys; preserve
   array order.
4. Replace literal `<` with uppercase `\u003C`. Do not additionally escape
   `>`, `&`, or other printable characters.
5. Compute lowercase MD5 hex over the UTF-8 bytes.

This is now implemented as a pure compiler in
`src/OpenSynapse.Windows/Protocols/BladeMappingEngineProtocol.cs`. It does not
load Razer code or write hardware state.

## Native Snap Tap parser boundary

Read-only Ghidra analysis of `mapping_engine.dll` establishes:

- `FUN_1800eb965 @ 0x1800EB965` parses input `hypershift`, `gamemode`, and
  `dkpId`; when `dkpId` is absent it also parses `snaptapId` and
  `snaptapPrioritize`.
- `FUN_1800e600e @ 0x1800E600E` parses top-level `mappings`, `actuations`,
  `rapidTriggers`, and optional `snaptaps`.
- `FUN_1800ea3bb @ 0x1800EA3BB` requires every `snaptaps` value to be numeric
  and no greater than `3`.
- `FUN_18006836c @ 0x18006836C` dispatches Snap Tap numeric types `0`, `1`, and
  `3` to different handlers. Other values are logged as unsupported.

No Product 710 JavaScript emits a top-level `snaptaps` value or assigns names
to these native numeric types. Their semantics remain unnamed. Treating them
as user-facing modes would be invention, not reverse engineering.

## RzCommon IOCTL evidence

`artifacts/ghidra/mapping-io-decompile.txt` recovers the following names and
wire sizes from `DriverImpl`:

| IOCTL | Function | Static input/output evidence |
| --- | --- | --- |
| `0x88883018` | asynchronous input notification | no input; output buffer `0x130` bytes |
| `0x8888301C` | `EnableInputRedirect` | 5-byte input: little-endian `uint32` kind plus one enable byte |
| `0x88883020` | `SendInput` | 32-byte input union |
| `0x88883024` | `SetInputHook` | `0x124`-byte input union |
| `0x8888302C` | `ClearInputHook` | 32-byte input union |
| `0x88883030` | `EnumerateInputHook` | 4-byte index input; `0x124`-byte output |
| `0x88883034` | `EnableInputHooks` | 4-byte zero/one input |
| `0x88883038` | `EnableInputNotify` | 4-byte zero/one input |
| `0x88883180` | set sensor rotation angle | 4-byte angle input |
| `0x88883184` | get sensor rotation angle | no input; 4-byte angle output |

`FUN_18010eef0 @ 0x18010EEF0` maps native input runtime tags to redirect kinds:

```text
native tag 0x15 -> driver kind 1
native tag 0x1B -> driver kind 2
native tag 0x1C -> driver kind 4
```

`FUN_180110800 @ 0x180110800` and `FUN_1801100e0 @ 0x1801100E0` prove that
keyboard and mouse objects are translated into the 32-byte send union and
`0x124`-byte hook union before the IOCTL. The C++ object offsets and several
union subtypes are still internal implementation details. No production
OpenSynapse IOCTL wrapper is added until those fields and lifecycle cleanup are
fully named and independently validated.

This driver path creates a non-distributable dependency on the installed,
closed `RzCommon` driver. It is evidence for Synapse parity, not an acceptable
claim that OpenSynapse can ship the driver.

## Product-used path and explicit exclusions

The Product 710 path is:

```text
addUsbDevice(skipFilterDriver:false)
  -> localStorageSetItem(Product 710 profile + appEngine graph)
  -> enableMapping
  -> native graph execution through the installed filter driver
```

The following shared methods are present but are not Product 710 protocol:

- `enableRazerKeyInputRedirect`: Product 710 never calls it. Dedicated keys
  enter through report `0x04` after Software/Driver mode and MappingEngine
  consumes them as `razerKey` inputs.
- `addUsbDeviceWithoutFilterDriver`: exported by the DLL, but Product 710 uses
  `addUsbDevice` with the filter driver connected.
- Generic OBM Snap Tap GET/SET `02/A1` and `02/21`: Product 710 declares no
  `obm`, no `OBMSpecs`, and never takes this branch.
- Shared OLED `Startup_Animation_Mode`: not attributable to Product 710 OLED.
  Product 710 does have a separate firmware-gated startup-animation handler;
  its exact `0F/98` GET and `0F/18` SET are documented separately.
- Shared generic HDR SET helpers: Product 710 series 11 uses Power Mode Control
  bit 3 for Local Dimming instead.

Other closed boundaries are not missing packets:

- Trackpad toggle is a Windows Precision Touchpad state path plus synthetic
  F24/Ctrl/Win/F24 sequence; it sends no Razer HID report.
- THX settings call the installed proprietary THX APO/`ThxV4Native` property
  contract. There is no replaceable Product 710 feature report in the bundle.
- Smart fan curves are a Synapse software loop over the already recovered
  fixed fan target calls; there is no firmware curve-table command.

## Remaining work after static reverse engineering

Static Product 710 compiler uncertainty is closed for keyboard input pairs,
DKM pairs, HyperShift activation, Snap Tap pass-through/toggle, and graph hash.
The remaining mapping work is runtime engineering and physical validation:

- distinguish internal Blade input from external keyboards before suppression;
- tag injected input and prevent loops;
- release every synthetic key on crash, disconnect, sleep/resume, secure
  desktop, and profile switch;
- validate A/D last-input priority and all overlap/release transitions;
- either own the complete Windows runtime or explicitly require the installed
  Razer driver. Do not silently mix the two models.
