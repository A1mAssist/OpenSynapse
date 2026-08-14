# RzLightingEngine algorithm reconstruction

Target: `RzLightingEngineApi_v4.0.55.0.dll` (`SHA-256 CD9FC2A61FF920B9B73E1F5E27632020E2F8586F443E426A875AD3B042B714FA`).
This pass was static only. It did not start Synapse, open a HID device, or send a report.

## Shared color-stop renderer

`FUN_1800692e0(effect, 10, positions, colors, frameCount, output)` is the common renderer used by Spectrum, Breathing, Wave, and Fire colorization.

1. Positions are integer percentages in the range `0..100`.
2. Adjacent positions are raised when necessary so that two stops cannot map to the same output frame.
3. The output is cleared, then every interval is interpolated independently across all four bytes of the packed color.
4. Interpolation uses float increments followed by truncation and clamping to `0..255`.
5. Processing stops at the first position equal to `100`; unused entries in the ten-slot arrays are ignored.

This is linear packed-channel interpolation, not HSV interpolation and not an easing curve.

## Recovered defaults

The packed colors below use the DLL's low-byte-first channel convention.

| Effect | Positions | Colors | Static confidence |
|---|---|---|---|
| Spectrum | `0, 33, 66, 100` | red, green, blue, red | High |
| Breathing | `0, 25, 50, 75, 100` | black, black, selected color, black, black | High |
| Wave | `0, 33, 66, 100` | red, green, blue, red | High for color stops only |

`CSpectrumEffect::slot2` copies one packed color to every output cell and advances a single frame index. Therefore Synapse Spectrum Cycling is a spatially uniform keyboard color that changes over time. The DLL fallback duration is `0x936c = 37740 ms`.

`CBreathingEffect::slot2` uses the same precomputed sequence. The stop layout proves that the second half fades down from 50% to 75% and remains black from 75% to 100%; there is no 50%-75% peak hold. The DLL fallback duration is `14000 ms`, while the previously observed active Blade request uses `7000 ms`.

## Wave

`FUN_180065c90` computes a projected path length from the effect angle and device geometry, converts speed and pause to frame counts, builds the same RGB stop sequence, and stores it at `this+0x198`.

`FUN_180065f50` maps each matrix coordinate through the angle vector into that sequence. `FUN_180066160` handles perimeter/region modes. The right/left Blade quick-effect path can reuse the recovered RGB stops, but its exact active speed still depends on request/LedData fields that are not present in the installed directory. Wave timing is not yet proven 1:1.

## Fire

`FUN_1800625d0` is a precomputed cellular renderer, not independent pixel noise:

- work rows contain `0xa1 = 161` cells arranged as 7 x 23;
- the CRT linear-congruential generator uses multiplier `0x343fd` and increment `0x269ec3`;
- static 23-byte masks at `0x1801f5333` and `0x1801f534a` shape source intensity and decay;
- seven 23-byte lookup rows at `0x1801f5370..0x1801f5410` colorize the propagated heat field;
- generated work frames are copied/resampled into the configured device rectangle before `CFireEffect::slot2` returns them.

The masks and control flow are recovered, but the current Blade LedData mapping and active Fire rate are still missing. OpenSynapse's simple deterministic noise renderer remains an approximation and was not relabeled as equivalent.

## Production changes from this pass

- Spectrum now renders one uniform color using the native red/green/blue/red linear stops and `37740 ms` fallback period.
- Breathing now uses the native black/black/color/black/black curve and channel truncation.
- Wave now uses the native RGB stop interpolation instead of HSV; its existing movement period remains provisional.
- Fire was intentionally left unchanged until its Blade mapping/rate can be validated.

## Adversarial review

1. RGB order could be misread from a packed integer. Cross-check: the native default sequence is the canonical red/green/blue/red cycle and matches the low-byte-first Razer color convention used by the existing transport.
2. `37740 ms` is a fallback, not necessarily every request's duration. The local quick-effect conversion supplies no Spectrum parameters, so it is the best current static value; a captured native frame timeline would still be stronger evidence.
3. Breathing has a `14000 ms` native fallback but the active Blade path was previously observed at `7000 ms`. Production retains the observed Blade value.
4. Wave color stops are proven; speed, angle scaling, and pause are not. Production does not claim full Wave parity.
5. Fire's work grid is not the Blade output grid. Directly treating the 7 x 23 state as keyboard coordinates would be wrong; production keeps the old renderer until the mapping is recovered.

