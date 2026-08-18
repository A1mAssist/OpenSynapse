# Viper V3 HyperSpeed (Product 184) product scope and OBM capabilities

## Result

The current Product 184 Synapse bundle closes the product-level capability
question that remained open in the generic mapping reverse engineering. The
shared mapping encoder contains functions used by many Razer products, but the
Product 184 manifest exposes only the groups listed in `OBMSpecs` below.

No HID SET was sent while collecting this evidence.

## Current official source

```text
Entry:        https://apps.razer.com/synapse/products/184/mw/index.html
Main bundle:  https://apps.razer.com/synapse/products/184/mw/main.f018391fcce3439e7725.js
Version:      1.0.1
Build:        2606110334
Commit:       8be82843906a38a2e266bcbde028fcc6f6f16906
SHA-256:      07F1219079D629725829AD4E24D510FC998A6134C8065626876E2C9B73ACEDAF
Checked:      2026-08-16
```

The bundle identifies Product `184`, dongle `184`, one onboard-memory slot,
and the following exact product specification:

```js
OBMSpecs: {
  hasHardwareProfileSwitch: true,
  noFirmwareInputId: [],
  supportedFeature: {
    macros: false,
    brightness: false,
    dpi: true,
    pollingRate: true
  },
  supportedMappings: {
    disableGroup: true,
    hyperShiftGroup: true,
    keyboardGroup: true,
    mouseGroup: true,
    multimediaGroup: true,
    sensitivityGroup: [
      "DPI_Clutch",
      "DPI_CycleUp",
      "DPI_CycleDown",
      "DPI_Up",
      "DPI_Down"
    ]
  }
}
```

The product's visible polling-rate list is `125`, `500`, and `1000` Hz. The
bundle also enables the shared `highSpeedPollingRate` feature, but this is a
separate dongle-dependent feature path; it is not evidence that the standard
Product 184 control collection accepts a polling value above 1000 Hz.

## Product-level mapping whitelist

Combining `supportedMappings` with the encoder's product predicates yields the
following Product 184 assignment functions:

| Function | ID | Payload |
| --- | ---: | --- |
| Off | `0` | empty |
| Mouse button | `1` | `[buttonId]` |
| Keyboard key | `2` | `[modifierMask, HID]` |
| Sensitivity | `6` | `[1]`, `[2]`, `[6]`, `[7]`, or `[5, X_BE16, Y_BE16]` |
| Media key | `10` | USB consumer usage LE16 |
| Double click | `11` | `[1]` |
| HyperShift activator | `12` | `[1]` |
| Keyboard turbo | `13` | `[modifierMask, HID, delayLE16]` |
| Mouse turbo | `14` | `[buttonId, delayLE16]` |

Product 184 rejects DoubleClick with turbo, horizontal/repeating scroll turbo,
BossKey, and the MicVolumeUp, MicVolumeDown, MuteAll, and MuteMic multimedia
actions. The UI should offer only its product-filtered action catalog rather
than accepting arbitrary values merely because they fit the wire width.

The following shared IDs are not Product 184 capabilities and remain disabled:

```text
3,4,5,15  Macro
7          Profile navigation
8          Lighting
9          Power keys
16         Controller
17         RazerKey
18         Windows shortcuts
```

In particular, Product 184 HyperShift is function `12`, payload `[1]`. The
shared `RazerKey` representation `17/[89]` belongs to another generic path and
must not replace the product's actual encoding.

## Device profile scope

The local Product 184 service logs repeatedly report:

```text
maxProfilesSupported = 1
numOfProfiles         = 1
profileIdList         = [1]
buttonIdList          = [1,2,3,4,5,9,10,96]
```

There are therefore exactly sixteen assignment records: eight firmware button
IDs times Normal and HyperShift. Production writes must force Profile `1`,
reject any other button ID, read the original target and sibling layer, send a
single `02/0C`, then use `02/8C` to verify the target and layer isolation. A SET
ACK is not assignment state. Failure or cancellation requires a non-cancelable
restore and a second GET verification.

The shared dependency also defines active-Profile GET `05/84`, but the current
Product 184 device rejected it during read-only validation. Production does not
send that command: the fixed Profile invariant is established by maximum `1`,
count `1`, and ID list `[1]`.

## `loadProfilesFromDevice` boundary

Product 184 logs record the effective load options as:

```json
{
  "macros": false,
  "brightness": false,
  "dpi": true,
  "pollingRate": true,
  "analogKeyboard": false,
  "analogKeyboardV3": false,
  "modTap": false,
  "resetProfile": false,
  "killSwitch": false,
  "scrollWheel": false
}
```

Consequently DPI and 125/500/1000 Hz polling are Product 184 OBM profile
features. Brightness, advanced scroll-wheel configuration, debounce, macros,
Profile reset, and kill-switch commands are not missing Product 184 protocol
work; Synapse deliberately does not enable them for this product.
