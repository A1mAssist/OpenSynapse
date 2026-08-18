# Blade Product 710 mapping, HyperShift, and Snap Tap

## Result

The Product 710 implementation does not use an on-board mapping engine. Its
ordinary key remapping, HyperShift layer, and Snap Tap configuration are
persisted into the Synapse profile and compiled into MappingEngine JSON.

This resolves the earlier classification error: these capabilities are not
blocked on an unknown `02C6` feature report. They are blocked on replacing the
closed MappingEngine/filter-driver runtime safely, or on deliberately using
that installed runtime. They must not be represented as firmware persistence.

## Source files

| Role | Local file | SHA-256 |
| --- | --- | --- |
| Product 710 MW main | `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000233` | `A0F0AF07C6CFBD1C6F03516ED63C93858F5CD0FF2E17B6A3B6D0DAFB4608A79B` |
| Shared MW chunk `737` | `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00022a` | `00E18FD4D8C4F5C20E31308160C6D8599997FE26BF3C41CEF4A076440708D749` |
| Product 710 UI main | `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000266` | `8D27E266EE06C7954D9F1187159CE75DFC0A46AAA20659E743B0273954C27A78` |
| Generic `rzDevice25` chunk | `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000081` | `14BC86FAE481354C3F9593973C636103027E29F9AC76B7813B8E5E6AF5559C48` |
| Native MappingEngine | `C:\Program Files\Razer\RazerAppEngine\app-4.0.698\CommonDLL\mapping_engine.dll` | `82CF78080C78EB7092A12BEC89421E00AAC5A1047F41AF3D205ECE806980A15B` |

The minified bundles are single-line files. The offsets below are zero-based
character offsets in the decoded JavaScript and are included so every claim is
reproducible without relying on renamed symbols.

## Product capability boundary

Product module `26061` starts near offset `130969` in `f_000233`.

- `DeviceInfo` declares Product `710`, category `SYSTEM`, and interface `2`.
- The product local-data object has `profiles`, `defaultMappings`, and
  `reportIDs`, but no `obm` object or `OBMSpecs` declaration.
- Product startup loads chunks `63`, `737`, and `364`; it does not load chunk
  `396` (`obmEngineKeyboard`).
- The shared feature derivation code creates `isObm` only when the product data
  contains a truthy `obm` property.

The generic state machine still contains an `obm: "init"` state for all
products. That generic state name is not evidence that Product 710 owns an OBM
profile.

## Mapping and HyperShift contract

Product 710 stores each UI assignment in this shape:

```json
{
  "inputType": "KeyInput | DKMInput",
  "inputID": "KEY_* | DKM_*",
  "isHyperShift": false,
  "outputType": "<mapping group>",
  "<mapping group>": {}
}
```

`isHyperShift:false` selects the normal layer and `isHyperShift:true` selects
the HyperShift layer. They are separate assignments for the same `inputID`,
not a modifier bit added to the output key.

Product module `26061`, offsets `137510..141450`, proves the following
device-specific inputs and defaults:

| Input | Razer key byte | Default behavior |
| --- | ---: | --- |
| `DKM_03` | `0x03` | HyperShift while held |
| `DKM_D2` | `0xD2` | normal: Application key; HyperShift: `Win+Shift+F23` |
| `DKM_D3` | `0xD3` | next performance mode |
| `DKM_D4` | `0xD4` | microphone mute |

The same default list contains normal and HyperShift assignments for ordinary
keyboard keys. Examples include HyperShift `B` for battery override, `T` for
trackpad, `P` for performance, `R` for refresh rate, `F1..F11` for media,
display and keyboard controls, and the arrow-key navigation layer.

Shared module `76743` in `f_00022a` contains the complete write path:

```text
ON_SET_KEYMAPPING payload
  { mappingList, isResetKeybinds?, version? }
        |
        v
taskMakerSetKeyMapping / jA
  -> activeProfile.mappings = mappingList
  -> activeProfile.appEngine = QA(mappingList, profile)
  -> persist profile
  -> sync profile JSON through MappingEngine localStorageSetItem
```

The state machine near offset `1452332` has two explicit branches:

- OBM ready: `updateLocalStorage -> generateAppEngine -> updateObm`
- otherwise: `updateLocalStorage -> generateAppEngine`

Product 710 takes the second branch. Module `76743` function `QA`, near offset
`1060886`, calls the common mapping generator and writes:

```json
{
  "mappings": [],
  "hash": "<mapping hash>"
}
```

The generated engine records use paired press/release inputs and outputs. The
native MappingEngine consumes this JSON; it is not a Razer feature report.
See `docs/reverse-engineering/mapping-engine-input-io.md` for the confirmed
filter-driver and hardware-event input paths.

For example, the Product 710 logs contain both normal and HyperShift records
for `DKM_D2` (`0xD2`). A HyperShift-layer press compiles to the following
shape; the release is a second record with the paired input/output flags:

```json
{
  "input": {
    "type": "razerKey",
    "key": 210,
    "hypershift": true,
    "flag": 0
  },
  "output": {
    "type": "keyboard",
    "scancode": 93,
    "flag": 2
  }
}
```

The HyperShift activator itself compiles to an output with
`type:"hypershift"`. This confirms that HyperShift is MappingEngine state,
not a keyboard HID modifier and not a Product 710 feature report.

The persisted locations are:

```text
RzLockableStorage key: synapse_710
  profiles[].mappings
  profiles[].appEngine
  profiles[].snapTap
  defaultMappings.appEngine

MappingEngine local key:
  synapse_710_{containerId}

task resume key:
  usb_5426_710
  taskMaker.keyMapping.setKeyMapping
```

`usb_5426_710` contains task resume/version information. It is not a board
profile and is not the source of truth for mappings.

## Snap Tap contract

The Product 710 profile default at offset `147272` is:

```json
{
  "snapTap": {
    "isEnabled": false,
    "keyList": [
      { "key1": "KEY_A", "key2": "KEY_D", "id": 1 }
    ]
  }
}
```

The product default mappings also assign HyperShift + left Shift to:

```json
{
  "inputType": "KeyInput",
  "inputID": "KEY_LEFT_SHIFT",
  "isHyperShift": true,
  "outputType": "snapTapGroup",
  "snapTapGroup": { "snapTapAssignment": "toggle" }
}
```

UI module `99857` in `f_000266`, near offset `565411`, posts the complete
state whenever either the key pair or enable switch changes:

```text
ON_SET_SNAP_TAP payload
  { isEnabled, keyList: [{ key1, key2, id }] }
```

The UI event visualizer reads `keyList[0]`, matching the one-pair Product 710
profile. No evidence in this product bundle supports multiple simultaneous
pairs.

Shared module `76743` handles `ON_SET_SNAP_TAP` as follows:

1. Replace `activeProfile.snapTap` with `{isEnabled,keyList}`.
2. Regenerate `activeProfile.appEngine` with `QA`.
3. Persist the profile.
4. Only when `isObm` is true and
   `DeviceInfo.OBMSpecs.supportedFeature.killSwitch` is true, enqueue a hardware
   kill-switch write for each OBM profile.

Product 710 does not satisfy step 4. Its Snap Tap behavior therefore comes
from MappingEngine JSON, not from a Product 710 firmware command.

During MappingEngine generation, the configured pair id is injected into the
matching input records as `snaptapId`. If the pair has no existing user
mapping, the generator synthesizes pass-through press/release mappings first.
The `registerSnaptapKeyEvent` and ordinary input-event callbacks feed only the
UI pressed-key visualizer; the executable Snap Tap rule is the generated
MappingEngine record carrying `snaptapId`.

The generic `rzDevice25` implementation does reveal the optional OBM command,
but it is not used by Product 710:

```text
GET: dataSize=4, class=0x02, id=0xA1, payload=[profileId]
SET: dataSize=4, class=0x02, id=0x21,
     payload=[profileId, enabled, analogKeyId1, analogKeyId2]
```

Generic module offsets are approximately `7818` for the command constants and
`128547` for the builders. This is evidence for compatible analog/OBM
keyboards only. It must not be added to the `02C6` manifest without a Product
710 capability declaration and a current-device read/write/readback/restore
test.

## Implementation consequence

OpenSynapse has two honest implementation choices for Blade:

1. Build an owned Windows input-remapping runtime with device-origin
   discrimination, injection tagging, loop prevention, per-device normal and
   HyperShift state, Snap Tap last-input priority, and failure-safe release of
   every synthetic key.
2. Depend on the installed Razer MappingEngine/filter driver and reproduce its
   profile JSON contract. This is unsuitable for a standalone distributable
   build and couples OpenSynapse to a closed, versioned Razer component.

The first path is the standalone product path. The second is useful only as an
opt-in compatibility probe. A raw `02C6` feature-report setter is not a valid
third path for these Product 710 capabilities.

## Remaining verification

- Exact ordinary-key, DKM, HyperShift activator, Snap Tap pass-through/toggle,
  and canonical graph-hash generation is recovered and covered by
  `BladeMappingEngineProtocolTests`. See
  `artifacts/reverse-engineering/2026-08-16/blade-product710-mapping-engine-compiler-and-driver.md`.
- Confirm the owned runtime can distinguish the internal Blade keyboard from
  an external keyboard before suppressing or injecting any input.
- Validate Snap Tap last-input priority for A/D press, overlap, release, and
  focus/session transitions.
- Confirm that process exit, crash recovery, device removal, sleep/resume, UAC
  desktop transitions, and profile switches never leave a synthetic key held.
- Keep the generic `02/21` kill-switch command disabled for Product 710 unless
  the device itself proves support through GET and a reversible physical test.
