# Blade Product 710 current-bundle capability delta

## Result

The current Product 710 bundle changes four conclusions from the older audit:

1. `bladeSeries` is `11`, not `10`.
2. The user-visible Battery Saver maps to firmware performance mode `3`.
3. Firmware mode `6` is Balanced DC and is selected automatically while on
   battery; it is not the Product 710 Battery Saver UI mode.
4. Speaker and microphone mute events are mirrored to function-lighting status
   through an exact set-only HID report.

`needHyperboostRecheck`, `useGetDockEditionAPI`, and the profile field
`powerSavingMode` do not introduce new Product 710 HID protocols.

No HID report or native setter was invoked during this audit.

## Sources

| Source | SHA-256 |
|---|---|
| Product 710 `4520b62b873ad8c3_0` | `AD12E1536ED95E4652EFD38A9E4358CDD202266B531F809030A86E308FDC7258` |
| Shared MW `a2cb83e8876bf761_0` | `8E04D33B17C5C16ACA94D0546F53C47D85E200DF458125DB50C1E77804237153` |
| `rzDevice25LedMatrixBlade2` `df1819fc34cb9bfa_0` | `03DA2AAFC966F08839C624EA5C1DF79FD35883F5778C8AE1E1AB22B5B0B1851A` |

## Performance and battery policy

The Product enum and Product-used conversion are exact:

```text
Balanced=0
Performance=2
BatterySaver=3
Custom=4
Silent=5
BalancedDC=6
HyperBoost=7

"batterySaver" -> BatterySaver_Ultrabook_LowPowerMode -> 3
```

Product 710 exposes only Balanced, Performance, Custom, Silent, HyperBoost, and
Battery Saver as selectable modes. `onBatteryBatterySaver` checks minimum BIOS
`2.02`, EC `1.09`, and model-specific RTX 5080/5090 VBIOS versions before the UI
advertises Battery Saver. The final write reuses the existing two-zone thermal
GET `0D/82` and SET `0D/02` path with mode value `3`; there is no new report.

`isBalancedDCSupported` changes a requested Balanced mode `0` to mode `6` for
each fan zone while Windows reports battery power. The same substitution is
prepared for suspend. OpenSynapse must treat `6` as readback state, not as a
manual Battery Saver choice.

`needHyperboostRecheck` reads `systemSKU.charAt(9)` and hides HyperBoost for SKU
types `5` and `6`. It is host eligibility policy only.

## Local dimming on series 11

The current runner explicitly groups series `10`, `11`, and `12` together:

```text
ON_ENABLE_LOCAL_DIMMING
  -> getPowerModeControl()
  -> replace only localDimming bit 3
  -> setPowerModeControl(cpuBoost | localDimming | maxFan | oneTimeOverride)
```

Therefore Product 710 series 11 still uses GET `07/8F` and SET `07/0F`. The
older series-10 label was stale, but the recovered report and sibling-bit
preservation rule remain correct.

## Function-lighting mute synchronization

Correction after tracing the Product 710 handler rather than the shared generic
function-lighting branch: Product 710 declares:

```text
functionLighting = { gamingMode:true, micMute:true, functionKey:true }
```

The loaded Product 710 MW handles Core Audio events as follows:

```text
speakervolumechanged    -> update UI state only
microphonevolumechanged -> setAudioMuteStatus(2, event.muted)
```

The loaded `rzDevice25LedMatrixBlade2` implementation sends:

```text
data size 03, class 18, command 04
args [00, type, muted]
type 2 = microphone
muted 0 = unmuted
muted 1 = muted
```

This mirrors OS microphone mute state to the M5 function-light indicator. The
shared protocol enum contains speaker target `1`, but Product 710 does not call
it, so it is not production evidence for F3. This is not a microphone-volume
controller, and no separate state GET exists in the Product-used path.
OpenSynapse now has the persistent session, strict SET/ACK path, and Core Audio
lifecycle enabled for Product 710. The 2026-08-17 controlled run visibly lit
M5 for 10 seconds, acknowledged Off, and restored Normal mode.

## Non-protocol selectors

- `useGetDockEditionAPI`: edition/layout fallback when ordinary edition lookup
  fails; no user-facing device control.
- `powerSavingMode`: local profile/runtime flag; direct Product code only stores
  it and does not send a new HID command.
- `onBatteryBatterySaver`: firmware eligibility gate around the existing thermal
  mode report, not a native or HID protocol of its own.

## Adversarial review

1. Product selectors were followed to their consumers before admission.
2. The current cache hashes are recorded and are not mixed with stale bundles.
3. Firmware mode `3` and automatic Balanced DC mode `6` are kept distinct.
4. Series 11 Local Dimming preserves all sibling bits; a direct literal `08`
   write would be unsafe.
5. The mute report is set-only and is not misrepresented as readback-capable.
