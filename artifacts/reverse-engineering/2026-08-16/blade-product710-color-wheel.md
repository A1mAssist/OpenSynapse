# Blade Product 710 Color Wheel reconstruction

Date: 2026-08-16

This is a static reconstruction. It did not start Synapse, open a HID device,
or send a report.

## Sources

| Source | Role | SHA-256 |
| --- | --- | --- |
| Product 710 main MW cache file `4520b62b873ad8c3_0` | Product effect declaration and default direction | `AD12E1536ED95E4652EFD38A9E4358CDD202266B531F809030A86E308FDC7258` |
| Lighting Engine cache file `b02e3dede0db631c_0` | Exact Wheel request construction and center resolution | `A29EC8AB0BE588E1B6F54A9CA6E2A240DDB3B40D9E232BDF65EDB849D2236097` |
| `RzLightingEngineApi_v4.0.55.0.dll` | `CColorWheelEffect` implementation | `CD9FC2A61FF920B9B73E1F5E27632020E2F8586F443E426A875AD3B042B714FA` |

Raw Ghidra output is retained in:

```text
artifacts/reverse-engineering/2026-08-16/ghidra/color-wheel-direct.md
artifacts/reverse-engineering/2026-08-16/ghidra/color-wheel-render-asm.md
```

## Product 710 request

Product 710 defaults Wheel direction to `1`. The UI converter maps that to
`counterclockwise=false`; the Lighting Engine then builds:

```json
{
  "Mode": 1024,
  "ColorStops": [
    {"Stop": 0, "Color": 16718079},
    {"Stop": 18, "Color": 16715792},
    {"Stop": 28, "Color": 16744448},
    {"Stop": 35, "Color": 15728384},
    {"Stop": 42, "Color": 14679808},
    {"Stop": 49, "Color": 65280},
    {"Stop": 52, "Color": 65535},
    {"Stop": 64, "Color": 1595647},
    {"Stop": 77, "Color": 1595647},
    {"Stop": 100, "Color": 16718079}
  ],
  "Speed": 360,
  "Cycles": -1,
  "CenterRow": 3,
  "CenterCol": 8
}
```

`CenterRow` and `CenterCol` use `LedData.wheel.centerRow/centerCol` when the
manifest provides them. Otherwise the apply path uses `floor(rows/2)` and
`floor(cols/2)`, which is row `3`, column `8` for Product 710's `6 x 17`
matrix. As with other effects, the wrapper swaps RGB to BGR immediately before
the DLL call; the numbers above are the pre-swap UI RGB values.

## DLL algorithm

`CColorWheelEffect::vftable` is at `0x1801ACB18`. Initialization performs these
steps:

```text
periodMs    = 360000 / Speed
periodFrames= floor(periodMs / (1000 / fps))
paletteSize = 1024
palette     = sharedColorStopRenderer(ColorStops, paletteSize)
```

For Product 710's `Speed=360` and 25 FPS, the period is `1000 ms` or `25`
frames. The center coordinates are translated through the device's 40-unit LED
geometry and origin offsets before rendering.

The raw instruction listing proves the decompiler's missing second argument:
the angle helper receives the row delta in `XMM0` and the column delta in
`EDX`, converts both to doubles, and calls the DLL's `atan2` implementation.
For each output cell:

```text
rowDelta = row + deviceTop  - scaledCenterRow
colDelta = col + deviceLeft - scaledCenterCol
angle    = atan2(rowDelta, colDelta)          // -PI .. PI

phase = floor(currentFrame * 1024 / periodFrames)
if (Mode & 0x400) != 0:
    phase = 1024 - phase

paletteIndex = (floor(1024 * (angle + PI) / (2 * PI)) + phase) % 1024
output[row, col] = palette[paletteIndex]
```

After the frame is emitted, `currentFrame` advances modulo `periodFrames`.
Finite `Cycles` decrement on wrap; Product 710 uses `-1` for infinite cycling.
The opposite direction uses the other playback-direction mode bit and therefore
does not apply the `1024 - phase` reversal.

## Admission result

The Wheel request, palette, center fallback, timing, direction and per-cell
polar projection are statically closed. OpenSynapse's current generic rotating
HSV approximation should not be labelled equivalent until it uses this exact
10-stop, 1024-sample `atan2` renderer and passes a physical comparison.

## Adversarial review

1. The second `atan2` argument is proven from the raw register setup, not
   inferred only from decompiler pseudocode.
2. `Speed=360` means a one-second period through `360000 / Speed`; it is not a
   direct four-second UI period.
3. The 10 Product color stops are preserved. The DLL's four-stop fallback is
   not substituted for a valid Product request.
4. Center row/column are manifest-aware; `3,8` is only the `6 x 17` fallback.
5. Static equivalence does not constitute current-device visual validation.
