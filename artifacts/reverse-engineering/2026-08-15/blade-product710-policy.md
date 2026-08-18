# Blade 16 2025 (Product 710) policy and logo reverse

## Result

The remaining Product 710 policy paths are not one undifferentiated HID
feature:

| Capability | Product evidence | Boundary | OpenSynapse admission |
| --- | --- | --- | --- |
| Gaming Mode | `rzDevice.setGameModeState(state)` and `getGameModeState()` are called by the Product 710 middleware | Device report plus host-side MappingEngine suppression flags | Source-backed builders/parsers only; no production SET until current-path readback and recovery validation |
| Fn primary (`func`/`multi`) | `rzDevice.setFnKeyState(0, alternateState)` is called when `functionKeyPrimary` changes | Device SET changes the native Fn interpretation; profile mappings are regenerated in the host | Source-backed SET builder/parser only; no GET exists in the product call and no production SET |
| Trackpad toggle | `getTouchPadEnableStatus()` and `toggleTouchPadEnableStatus()` are called by the `bladeTrackpad` event handler | Windows/system helper, not a Razer HID report | Native/system boundary; do not invent a `02C6` packet |
| Lid Logo Off/Static/Breathing | `taskRunnerSetLogoEffect` sends `setLedEffect(0, LogoLED, ...)` followed by `setLedState(0, LogoLED, ...)` | Generic LED command path; the Synapse report transaction is `0` in its logged `dataSend` envelope | Source-backed sequence recorded; current OpenSynapse production path remains the independently verified OpenRazer `FF` Off/Static path. Breathing remains disabled pending current-device visual/readback validation |
| Startup animation | No Product 710 startup-animation call or product-specific command was found in the loaded Product 710 module | Shared `Startup_Animation_Mode` strings belong to OLED-capable generic code | Not admitted for Product 710 |

## Sources

| Local source | SHA-256 | Relevant evidence |
| --- | --- | --- |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00023d` | `0E386F87468BC19CBCB52E434CF0C3737A74D5483D6BC7201EA40B5A5096B985` | Product 710 device descriptors and `setGameModeState` |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000233` | `A0F0AF07C6CFBD1C6F03516ED63C93858F5CD0FF2E17B6A3B6D0DAFB4608A79B` | Product 710 profile defaults, `functionKeyPrimary`, Logo effect IDs, HyperShift+T trackpad mapping |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\data_3` | `4C50EE335DEE89148EE2B98889B8337A1459185A4B66CAE79BE66D056C186EEF` | Module `48967`, including the Fn SET descriptor |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00022a` | `00E18FD4D8C4F5C20E31308160C6D8599997FE26BF3C41CEF4A076440708D749` | Policy event handlers, system TouchPad helper, Logo sequence |

## Exact Product 710 descriptors

The Product 710 device-descriptor module (`73932`) defines:

```text
Game Mode GET: dataSize=04, class=00, command=88, no arguments
Game Mode SET: dataSize=04, class=00, command=08, arguments=[state]
```

Its parser reads `data[0]` as `gameMode`, `data[1]` as `keyCover`, and
`data[2]` as `lifted`. The middleware writes only `gamingMode.state`. The
profile's `isWindowsKeyDisabled`, `isAltTabDisabled`, and `isAltF4Disabled`
are host policy values; they must not be represented as additional firmware
bytes without a separate call proving that ownership.

The Product 710-loaded device module imports module `48967` for Fn control.
That module's comma-expression resolves the SET descriptor to:

```text
Fn primary SET: dataSize=02, class=02, command=06, arguments=[classId, alternateState]
```

Product middleware calls `setFnKeyState(0, 0)` for `functionKeyPrimary="func"`
and `setFnKeyState(0, 1)` for `"multi"`. It has no Fn GET method. The profile
mapping layer is then inverted/regenerated in AppEngine, so the device SET is
only one half of the behavior.

## Exact Logo sequence

The Product 710 profile exports:

```text
Static     = effectId 1
Breathing  = effectId 2
Off        = effectId 0
Logo LED   = region/LED id 4
```

The shared task runner function `Ao` receives `effectId=o` and executes:

```text
setLedEffect(0, LogoLED, 2 == o ? 2 : 0)
setLedState (0, LogoLED, o ? 1 : 0)
```

The generic LED descriptors are `03/03/02` (Set LED Effect) and `03/03/00`
(Set LED State), each with three argument bytes `[0, 4, value]`. The local
Product 710 MW log records the corresponding report envelope as
`[0,6,0,0,0,3,3,command,...]`; therefore the Synapse transaction byte for
this sequence is `0`, not the OpenRazer Blade transaction `0xFF` used by the
separately validated Off/Static path.

This explains why the earlier standalone `0302 [01,04,02]` experiment was
not a valid reproduction of Synapse's Breathing operation: it used the wrong
transaction/object prefix and omitted the required state SET. It is still
negative evidence for that standalone report, not for the two-step sequence.

## Trackpad ownership

The `bladeTrackpad` MappingEngine output emits
`ON_SWITCH_TRACKPAD_MODE_BY_DEVICE`. The Product 710 event task calls:

```text
getTouchPadEnableStatus()
toggleTouchPadEnableStatus()
setMemoryStorageItem(...)
```

No `sendCommand` or HID descriptor is involved. The exact helper implementation
is vendor/system-owned and is not reproduced by a fabricated feature report.

## Validation boundary

The source establishes exact construction, but not current-device safety for
Gaming Mode, Fn SET, or Synapse Logo Breathing. A future validation run must
capture the current read (where available), perform one reversible change,
verify response and visible behavior, restore the baseline in a non-cancelable
path, and verify again from a fresh process. Until then these paths remain
SourceBacked and have no production/UI sender.
