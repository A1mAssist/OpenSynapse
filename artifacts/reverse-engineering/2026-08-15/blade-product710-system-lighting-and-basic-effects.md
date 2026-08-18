# Blade Product 710 system lighting and Basic Lighting Engine evidence

Date: 2026-08-15

This pass is static and log-backed only. It did not start Synapse, open a HID
device, or send a feature report. It separates Product 710 product policy from
the shared Basic Lighting Engine so that host automation is not mistaken for a
new device command.

## Evidence sources

| Source | Role | SHA-256 |
| --- | --- | --- |
| Product 710 main MW bundle, cache file `4520b62b873ad8c3_0` | Product effect list, default UI values, power-aware brightness and switch-off configuration | `AD12E1536ED95E4652EFD38A9E4358CDD202266B531F809030A86E308FDC7258` |
| Product 710 shared MW chunk, cache file `a2cb83e8876bf761_0` | Brightness state machine and event registration | `8E04D33B17C5C16ACA94D0546F53C47D85E200DF458125DB50C1E77804237153` |
| Lighting Engine bundle, cache file `b02e3dede0db631c_0` | UI-to-engine conversion and exact `AddEffect` parameter construction | `A29EC8AB0BE588E1B6F54A9CA6E2A240DDB3B40D9E232BDF65EDB849D2236097` |
| `lighting-engine.log` | Product 710 Wave `AddEffect` request | `C962B4291472C551688CF72272F7517B8822A261A38DF8966BE7E2AF176CA84A` |
| `lighting-engine1.log` | Product 710 Wave and Fire `AddEffect` requests | `3795525D050CEED4EA18BE22B1A5A70F31421067A868B0EBA601FE81F2FB45A3` |
| `RzLightingEngineApi_v4.0.55.0.dll` Ghidra reconstruction | Effect algorithms and Fire output projection | `CD9FC2A61FF920B9B73E1F5E27632020E2F8586F443E426A875AD3B042B714FA` |

The cache paths and source URLs are:

```text
%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Default\Service Worker\CacheStorage\3842aea8f625086ac73d0f8e1c00277da03b0e65\66cde48a-183d-4c4f-b70d-36dab581523d\4520b62b873ad8c3_0
https://apps.razer.com/synapse/products/710/mw/main.2c094ef7118d3f0c24c7.js

%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Default\Service Worker\CacheStorage\3842aea8f625086ac73d0f8e1c00277da03b0e65\66cde48a-183d-4c4f-b70d-36dab581523d\a2cb83e8876bf761_0
https://apps.razer.com/synapse/products/710/mw/6737.05cc203cffd1a7a12564.js

%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Default\Service Worker\CacheStorage\3842aea8f625086ac73d0f8e1c00277da03b0e65\fd8bb62d-ff27-48b2-a586-a7ef22a3996e\b02e3dede0db631c_0
https://apps.razer.com/synapse/lighting-engine/static/js/main.ec8d6b1c.js
```

## System lighting is host policy

Product 710 stores separate plugged, battery, and optional two-mode settings:

```text
brightness.plugged / battery / twoMode
switchOffLighting.plugged / battery / twoMode
```

The middleware listens for AC/battery mode, display power, system idle, and
battery-level changes. Its brightness state machine ultimately dispatches the
same two device tasks used by the ordinary brightness control:

```text
setBrightnessOff
setBrightnessOn
```

Observed runtime events and predicates include:

```text
on-ac / on-battery
displaypoweroff / displaypoweron / displaypowerdimmed
systemidle
BatteryManager.onlevelchange
idleTime >= idleMinutes * 60000
batteryPercent <= lowBatteryPercent
```

Therefore the following Synapse controls do not imply additional Product 710
HID commands:

- plugged and battery brightness values;
- turn lighting off when the display turns off;
- turn lighting off after an idle timeout;
- turn lighting off below a configured battery percentage;
- the two-mode selector that chooses which stored policy applies.

OpenSynapse should own these event subscriptions and lifecycle rules, then call
the already verified keyboard-brightness operation. A future implementation
must restore the configured brightness after the suppressing condition clears
and must not stack duplicate event handlers after profile changes.

## Product 710 quick-effect surface

The Product 710 bundle declares these 12 software quick effects:

```text
Ambient
Audio Meter
Breathing
Fire
Reactive
Ripple
Spectrum
Starlight
Static
Wave
Wheel
Tidal
```

The product defaults are:

| Effect | Product 710 default UI parameters |
| --- | --- |
| Static | `color1=#00ff00` |
| Ripple | `color1=#00ff00` |
| Ambient | `screen=full` |
| Audio Meter | `color1=#00ff00`, `colorBoost=1` |
| Breathing | `color1=#00ff00`, `color2=no-color`, `isRandom=false` |
| Reactive | `color1=#00ff00`, `duration=2` |
| Starlight | `color1=#00ff00`, `color2=no-color`, `isRandom=false`, `duration=2` |
| Wave | `direction=2` |
| Wheel | `direction=1` |
| Tidal | `color1=#00ff00`, `color2=#0000FF`, `isRandom=false`, `direction=1` |

`Tidal` is product-declared and was missing from the previous OpenSynapse
capability summary. Its presence is source-backed; that alone does not mean the
current production renderer implements it.

## Exact shared-engine constructors

The Lighting Engine bundle builds these request shapes before calling
`RzLightingEngine.AddEffect`:

| Effect | Engine construction |
| --- | --- |
| Ripple | One-color stops, `Width=200`, `Speed=25`, playback starts on key press, playback ends after one cycle |
| Spectrum | Fixed red/green/blue/red stops, `Duration=37740`, `Cycles=-1`, `Pause=0` |
| Breathing | One-color, two-color, or random stops; default `Duration=7000`; two-color duration is doubled |
| Starlight | One-color, two-color, or random stops; duration comes from UI conversion; default density is `2` |
| Audio Meter | Green/yellow/red stops, UI boost, `AutoBoost=0`, `Decay=1000`, `Auto=0`, default `Flow=0` |
| Ambient | UI screen rectangle and `Blur=1` |
| Wheel | Fixed spectrum stops, `Speed=360`, infinite cycles, manifest center or matrix midpoint |

These are constructor inputs to the proprietary engine. OpenSynapse's software
renderers still need algorithm parity or physical comparison before they can
be labelled visually identical.

## Exact Product 710 Starlight request and spawn model

Product 710's default `duration=2` converts to `1700 ms`. With one green color,
no second color, and `density=2`, the shared constructor produces:

```json
{
  "Mode": 0,
  "ColorStops": [
    {"Stop": 0, "Color": 0},
    {"Stop": 33, "Color": 65280},
    {"Stop": 67, "Color": 0},
    {"Stop": 100, "Color": 0}
  ],
  "Duration": 1700,
  "MaxStar": 20,
  "Regen": 10,
  "Cycles": 1,
  "Pause": 0
}
```

The previous Ghidra pass had already identified the ordered active-node tree
and the per-node color buffers. Combining its field accesses with this exact
request closes the remaining default and spawn-rate semantics:

```text
maxActiveStars = max(1, floor(rows * cols * MaxStar / 100))
effectFrames   = max(colorStopCount, floor(Duration / (1000 / fps)))
regenFrames    = max(floor(effectFrames / 8),
                     floor(Regen / (1000 / fps)))
```

For Product 710 (`6 x 17`, 25 FPS), those values are `20`, `42`, and `5`.
Every five frames, the engine removes exhausted nodes and uses its
`state = state * 0x343FD + 0x269EC3` generator to choose a spawn count from
zero through the currently free capacity. Each accepted star receives:

```text
position       = an unoccupied index in 0 .. rows*cols-1
nodeFrames     = max(colorStopCount, random(effectFrames) + 5)
intensity      = random(80) + 20
cycles         = 1
```

For the default request, `nodeFrames` is `5..46` and intensity is `20..99%`.
The intensity scales each RGB channel before the shared packed-channel stop
renderer builds that node's frame buffer. Each output tick copies the node's
current packed color into its one matrix position, advances modulo its node
frame count, decrements its cycle on wrap, and disables it at zero cycles.

Random-color mode uses the same scheduling but regenerates non-black stop
colors from the engine RNG before rendering a node. Collision-checked position
selection can end a spawn batch early; an implementation should preserve that
bounded behavior rather than loop indefinitely on a full matrix.

## Exact Product 710 Wave request

The Product 710 log records this pre-color-swap request:

```json
{
  "Mode": 0,
  "ColorStops": [
    {"Stop": 0, "Color": 16711680},
    {"Stop": 33, "Color": 65280},
    {"Stop": 67, "Color": 255},
    {"Stop": 100, "Color": 16711680}
  ],
  "Duration": 0,
  "Width": 100,
  "Speed": 25,
  "Pause": 0,
  "Angle": 90,
  "Cycles": -1
}
```

The UI direction conversion is:

```text
right -> Angle 90
left  -> Angle 270
up    -> Angle 0
down  -> Angle 180
```

The API wrapper swaps RGB to BGR immediately before the DLL call. The request
above is UI RGB evidence; the transformed log line must not be reinterpreted as
a different UI palette.

This closes the previous uncertainty around the active Product 710 Wave width,
speed, pause, angle, and cycle values. The Ghidra reconstruction remains the
source for how the DLL projects that request across the matrix.

## Exact Product 710 Fire request and projection

The Product 710 log records:

```json
{
  "Mode": 0,
  "ColorStops": [
    {"Stop": 0, "Color": 16744192},
    {"Stop": 50, "Color": 16711680},
    {"Stop": 100, "Color": 0}
  ],
  "Rate": 160,
  "Seed": 100,
  "Cycles": -1
}
```

The UI RGB colors are orange `0xFF7F00`, red `0xFF0000`, and black. Ghidra
evidence proves that Fire generates a `7 x 23` cellular work field. For Product
710's `6 x 17` output, the active direct-crop branch is:

```text
for outputRow = 0..5:
    sourceRow = 6 - outputRow
    output[outputRow, 0..16] = work[sourceRow, 0..16]
```

Source row zero is intentionally omitted. This closes the previous uncertainty
around Fire rate, seed, stops, and the `7 x 23` to `6 x 17` projection. It does
not by itself prove that the existing OpenSynapse noise approximation matches
the DLL's cellular simulation.

## Product 710 Tidal request and DLL algorithm

Product 710 exposes Tidal in its own effect list. Its default UI values convert
to these Lighting Engine inputs before center-point resolution:

```text
effectId        = 19
angle           = 160
split           = true
counterclockwise= false
color           = 0x00FF00
color2          = 0x0000FF
randomColor     = false
duration        = 0
width           = 100
speed           = 10
pause           = 0
cycles          = -1
centerPointX    = 5 fallback
centerPointY    = 0 fallback
```

The apply path replaces the fallback center with `LedData.tidal.centerCol` and
`centerRow` when present, otherwise with the matrix midpoint. A `6 x 17`
matrix without a manifest override therefore uses column `8`, row `3`.

The resulting non-random `AddEffect` constructor is:

```json
{
  "Mode": 2,
  "ColorStops": [
    {"Stop": 0, "Color": 0},
    {"Stop": 10, "Color": 0},
    {"Stop": 25, "Color": 65280},
    {"Stop": 40, "Color": 0},
    {"Stop": 60, "Color": 0},
    {"Stop": 75, "Color": 255},
    {"Stop": 90, "Color": 0},
    {"Stop": 100, "Color": 0}
  ],
  "Duration": 0,
  "Width": 100,
  "Speed": 10,
  "Pause": 0,
  "Angle": 160,
  "Direction": 1,
  "Cycles": -1,
  "CenterCol": 8,
  "CenterRow": 3
}
```

This request is statically derived from the Product 710 defaults and the
Lighting Engine constructor. Unlike the Wave and Fire requests above, no
Product 710 Tidal `AddEffect` entry was present in the retained logs.

The `CTidalEffect` vftable is at `0x1801ACC48`. Direct decompilation of its
methods and helpers establishes this algorithm:

1. It validates up to ten strictly increasing percentage stops and uses the
   shared packed-channel linear color-stop renderer.
2. It converts `Angle` to radians with `angle * PI / 180`, resolves the center
   and projected row/column extents, and computes the distance along the angle.
3. `Speed` is clamped through the common minimum-rate path. With a non-zero
   speed, spatial step is `((speed * 2) / 100 * distance) / fps`; otherwise it
   uses the shared unit-step fallback divided by `fps`.
4. It derives the active spatial frame count from `distance / step`, converts
   `Pause` to frames, and bounds the generated color-frame count against the
   effect's minimum frame requirement.
5. It allocates three wave-lane records. Their initial phase offsets are `0`,
   `-colorFrameCount`, and `-2 * colorFrameCount`; each lane owns a color frame
   buffer produced by the shared stop renderer.
6. The frame method projects every matrix cell around the selected center onto
   the angle vector, applies `Direction`, finds the active lane/phase interval,
   and copies that lane's packed color. Cells outside all active intervals use
   the configured background value.
7. Every tick advances lane phase, wraps it by the color-frame count, handles
   finite cycles, and regenerates stop colors only when random-color mode is
   active.

The raw decompilation and constants are retained in
`ghidra/tidal-effect-direct.md`. Relevant recovered constants include
`PI`, `180.0`, `100.0`, `1.0`, `-1.0`, and `-2.0`; this avoids treating Ghidra's
overlapping SIMD literals as unexplained magic values.

## Admission result

| Item | Static result | Remaining work |
| --- | --- | --- |
| Power-aware brightness and automatic switch-off | Complete host-policy call chain | Implement Windows power/display/idle/battery subscriptions and lifecycle restoration |
| Product 710 quick-effect list | Complete, including Tidal | Align backend/frontend capability declarations |
| Wave parameters | Complete Product 710 request plus DLL algorithm evidence | Replace provisional renderer timing/projection and compare physically |
| Fire parameters and output crop | Complete Product 710 request plus DLL mapping evidence | Port the cellular algorithm and compare physically |
| Ripple/Spectrum/Breathing/Audio/Ambient/Wheel constructors | Complete shared-engine parameter construction | Verify any remaining software-renderer parity claims individually |
| Starlight | Exact Product 710 request, spawn cadence, capacity, node lifetime/intensity, RNG and output lifecycle recovered | Port the node scheduler/renderer and compare physically |
| Tidal | Product request construction and `CTidalEffect` matrix algorithm recovered | Port the three-lane projected renderer and compare physically; do not call it verified yet |

## Adversarial review

1. A shared Lighting Engine enum is not enough to attribute an effect to Product
   710. The 12-item list above is cross-checked against the Product 710 bundle.
2. A successful `AddEffect` log establishes the native request, not visual parity
   of OpenSynapse's renderer.
3. The wrapper's RGB-to-BGR conversion is kept separate from Product UI RGB.
4. Power/display/idle rules dispatch existing brightness tasks; they are not
   fabricated as another HID command family.
5. Fire's `7 x 23` work field is not a flat Product 710 matrix. The proven
   reverse-row crop is preserved explicitly.
6. The Tidal request is a static Product-default derivation, not a retained
   runtime log. It is labelled separately from the logged Wave and Fire values.
7. Starlight percentages and times are converted using the actual Product 710
   matrix and 25 FPS engine. Generic-device values must be recomputed.
