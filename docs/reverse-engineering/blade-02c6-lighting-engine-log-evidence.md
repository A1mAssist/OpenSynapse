# Blade 16 PID 02C6 Lighting Engine Log Evidence

Date: 2026-08-12

Scope: read-only analysis of Synapse logs for decimal `productId/PID 710` (`0x02C6`). Device paths, serial numbers, GUIDs, profile identifiers, and unrelated profile fields are omitted.

## Sources

- `lighting-engine4.log`, SHA-256 `4D7D2C877F44893410728454F70774CCB43DA0DE45F19458B1B30F13BCD59E0F`, evidence timestamps `2026/08/12 20:58:07.274` through `20:58:07.280` and `2026/08/12 21:52:28.273` through `21:52:29.222`.
- `lighting-engine1.log`, SHA-256 `3795525D050CEED4EA18BE22B1A5A70F31421067A868B0EBA601FE81F2FB45A3`, evidence timestamps `2026/08/04 14:57:53.206` through `14:57:53.220`.
- `lighting_driver.log` was searched for the Logo-change interval. It contained no matching `2026/08/12 21:52:28-29` entries and no useful Logo/effect call record.

Hashes identify the rotating files as inspected. They will change if Synapse appends or rotates logs.

## Observed Action schema

These are JSON messages passed to `RzLightingAPI`, not HID reports.

| Action | Logged name | Request fields | Observed response |
|---:|---|---|---|
| 1 | `LightingEngine_CreateDevice` | `Action`, `config` | `Status`, `device_handle` |
| 2 | `LightingEngine_DestroyDevice` | `Action`, `device_handle` | `Status` |
| 3 | `LightingEngine_CreateEngine` | `Action`, `fps`, `type` | `Status`, `engine_handle` |
| 4 | `LightingEngine_DestroyEngine` | `Action`, `engine_handle` | `Status` |
| 17 | `LightingEngine_AddDevice` | `Action`, `engine_handle`, `device_handle`, `region`, `orientation` | `Status` |
| 33 | `LightingEngine_AddEffect` | `Action`, `engine_handle`, `effect`, `EffectParam` | `Status`, `effect_handle` |
| 34 | `LightingEngine_RemoveEffect` | `Action`, `engine_handle`, `effect_handle` | `Status` |
| 49 | `LightingEngine_EnableEngine` | `Action`, `engine_handle`, `enable`, `device_handle`, `region`, `clearFrame` | `Status` |
| 61447 | `LightingDevice_SetBrightnessState` | `Action`, `device_handle`, `region`, `state` | `Status` |
| 65520 | `LightingEngine_Terminate` | `Action` | `Status` |

`CreateDevice.config` is truncated by Synapse itself in the log. The visible PID 710 fields are:

```json
{
  "Version": 2,
  "VID": 5426,
  "PID": 710,
  "EID": 0,
  "Layout": 1,
  "MatrixMaxRow": 7,
  "MatrixMaxCol": 16,
  "DeviceMaxRow": 6,
  "DeviceMaxCol": 17,
  "LedWidth": 40,
  "LedHeight": 40,
  "LedWidthOffset": 0,
  "LedHeightOffset": 0,
  "LedLeftOffset": 200,
  "LedRightOffset": 2
}
```

## PID 710 creation and apply sequence

Observed in `lighting-engine4.log` at `2026/08/12 20:58:07.274-20:58:07.280`:

1. Action 3 creates a Basic engine: `{"Action":3,"fps":25,"type":"Basic"}` and returns `engine_handle=1`.
2. Action 1 creates the PID 710/Layout 1 device and returns `device_handle=1`.
3. Action 17 attaches device 1 to engine 1 at `region=0`, `orientation=0`.
4. Action 33 adds quick effect 2. Its visible effect parameters are `Mode=0`, four color stops, `Duration=0`, `Width=100`, `Speed=25`, `Pause=0`, `Angle=90`, and `Cycles=-1`; it returns `effect_handle=1`.
5. Action 49 enables engine 1 with `enable=1`, `device_handle=0`, `region=0`, `clearFrame=1`.

The in-memory bookkeeping observed alongside this sequence is:

```json
{
  "productId": 710,
  "engineHandle": 1,
  "deviceHandle": 1,
  "deviceHandles": [{"engineHandle": 1, "deviceHandle": 1}],
  "effectHandles": [{"regionId": 0, "effectHandle": 1}]
}
```

This is the complete logged creation/apply chain for the PID 710 region engine in that session. No separate logged `Apply` action exists; `AddEffect` followed by Action 49 is the observed apply/activation boundary.

## Static effectId 1 replacement sequence

Observed in `lighting-engine1.log` at `2026/08/04 14:57:53.206-14:57:53.220` for input:

```json
{"productId":710,"effectId":1,"params":{"color":"0x00ff00"},"regionId":0}
```

The existing engine/device are reused, then:

1. Action 49 enables the existing engine.
2. Action 34 removes existing `effect_handle=1`.
3. Effect parameters are normalized to:

```json
{
  "Mode": 4096,
  "ColorStops": [
    {"Stop": 0, "Color": 65280},
    {"Stop": 100, "Color": 65280}
  ],
  "Width": 200,
  "Speed": 25,
  "Duration": 0,
  "Cycles": 1,
  "Pause": 0
}
```

4. Action 33 adds `effect=1` and returns `effect_handle=2`.
5. Action 49 enables engine 1 again with `clearFrame=1`.

Fact: this proves the Lighting Engine JSON schema for a PID 710, region 0 static quick effect. It does **not** prove that Action 33/effect 1 controls the physical lid Logo.

## Logo configuration adjacency

The stored profile schema is consistently:

```json
{
  "logoLighting": {
    "plugged": {"effectId": 1},
    "battery": {"effectId": 1},
    "twoMode": {"effectId": 1}
  }
}
```

At `2026/08/12 21:52:28.274`, the plugged Logo value changed to `effectId=2`; at `21:52:29.222` it changed back to `effectId=1`. In both storage callbacks:

- `quickEffects.selectedEffectId` remained 4;
- `applyRegionQuickEffect` ran for effect 4;
- it logged `same effect:4, skip`;
- no `RzLightingAPI Action` occurred in the interval;
- no corresponding entry appeared in `lighting_driver.log`.

Therefore, based on these logs alone, `logoLighting.effectId` is profile state consumed by another module/path, or a path not logged at the Lighting Engine Action layer. The PID 710 Action sequence above is the keyboard/region lighting path and must not be promoted to a Logo protocol implementation without dynamic evidence from the actual Logo-changing process.

## Next dynamic breakpoint candidates

These are candidates, not protocol facts:

1. Break where the Synapse product module writes `logoLighting` before the storage event; the Lighting Engine listener demonstrably ignores that field.
2. Trace whichever process changes the physical Logo during the `effectId 2 -> 1` transition; do not restrict attachment to the lighting-engine process.
3. Use Action 49 and Action 33 only as negative controls: if they do not fire during a confirmed Logo change, the Logo path is separate.

## Keyboard matrix driver result

The local `lighting_driver` logs close the engine-to-HID gap for PID 710:

```text
claimInterface: 2
protocol: rzDevice25LedMatrixSkipSetEffect
write_function: hid.sendFeatureReportInBatch
batch_processing: true
maxRows: 6
rowDelayMs: 1
RGB frame: rows 0..5, columns 0..16, 306 bytes
```

`SkipSetEffect` means the driver does not send the firmware `03/0A` effect command before matrix output. OpenSynapse therefore starts PID 710 software effects by writing complete `03/0B` matrix rows directly and treats six acknowledged rows, not `03/0A`, as its electronic apply boundary.
