# 0F2E method index

Evidence:

- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\0F2E\main.df6f64c941b9efded61e.js`
- `D:\Workspaces\OpenSynapse\artifacts\upstream-reference\razer-mw-full-cdn\bundles\0F2E\manifest.json`

The bundle is the 2.57 MB minified `Razer Chroma Wireless ARGB Controller`
application. It contains the full `rzDevice25GlitterWireless` implementation
rather than a separately authored device module.

## Device configuration

| Item | Evidence |
|---|---|
| Product id | `productId:3886` (`0x0F2E`) |
| Device class | `rzDeviceType = rzDevice25GlitterWireless` |
| Port count | `isMultiLightingDevice.yes.stripeIDNumber = 3` |
| Chroma frame payload | `reportMaxDataSize = 380` |
| HID report buffer | `new Uint8Array(reportMaxDataSize + 1)` = 381 bytes |

## Methods

| Method | Evidence / behavior |
|---|---|
| `getChromaStripeLEDNumber(portCount)` | Sends `Get Chroma Stripe LED Number`; response port ids start at `LED_Port_Stripe1` (26). |
| `setChromaStripeLEDNumber(portCount, data)` | Sends `Set Chroma Stripe LED Number`; paired `[regionId, ledCount]` entries. |
| `setChromaFrame(...)` | Builds a per-row RGB frame through the shared Chroma path. |
| `_setChromaFrameGlitter(...)` | Builds the 381-byte report described below. |
| `setChromaFrameLightingEngine(...)` | Iterates `numRow`, slices `numCol * 3` bytes, and sends one glitter frame per row. |
| `setChromaEffectCommon(...)` | Shared effect dispatcher. |
| `setBrightness(...)` | Shared brightness command path. |
| `getBrightness()` | Shared brightness read path. |
| `sendCommand(...)` | Shared class/id command transport. |
| `sendCommand_chromaFrame(...)` | HID feature-report transport for the 381-byte frame (`hid.sendFeatureReport`). |
| `getDeviceMode(...)` | Shared device mode reader. |
| `setDeviceMode(...)` | Shared device mode setter. |

## Port mapping

The ARGB controller setup helper writes region ids directly:

```js
n[2 * e] = 26 + e;
n[2 * e + 1] = t[e];
```

The shared LED enum also defines:

```text
LED_Port_Stripe1 = 26
LED_Port_Stripe2 = 27
LED_Port_Stripe3 = 28
```

Together with `stripeIDNumber = 3`, this is sufficient evidence for ports
`0..2` mapping to region ids `26..28`.

## Frame layout

`_setChromaFrameGlitter` allocates 381 bytes and writes:

```text
[0]       regionId (26, 27, 28)
[1]       0x04 for rows 0/1, 0x84 for the last row
[2]       row (the port index)
[3]       row (the port index)
[4]       start LED index
[5]       end LED index
[6..380]  RGB triples
```

`setChromaFrameLightingEngine` sends one frame per row, so a static per-port
color is one 381-byte feature report per port.

## Decision

`Implemented`: the method index, fixed `26 + port` mapping, and frame layout are
all present in the bundle. 0F2E shares the 0F2C frame implementation and is
enabled for the same three-port color UI.
