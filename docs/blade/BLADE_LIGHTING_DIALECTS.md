# Blade Lighting Dialects

The manifest is the source of truth for the keyboard-lighting channel:

- `device.rzDeviceType` records the middleware class name from the bundle.
- `lightingDialect` records the report family the controller may use.
- `BladeModelCapabilities` only decides whether the model is admitted; it does not choose a wire report.

## Declared dialects

| PID | Manifest | Dialect | Evidence / path |
|---|---|---|---|
| 023A | `blade-023a.json` | `LedMatrixBlade` | `rzDevice25LedMatrixBlade`; `03:0A` effect id and `03:0B` pattern reports in the 023A bundle; 6x16 geometry from OpenRazer |
| 0234 / 024B / 024C / 024D / 0253 / 0256 / 026D / 026E / 0270 / 0276 / 0279 | `blade-*.json` | `LedMatrixBlade` | **inferred**: local OpenRazer 6x16 matrix + bundle `03:0A` / `03:0B`; firmware acceptance unverified |
| 0239 / 023B / 0246 / 0252 / 0259 / 0268 / 026F / 027A / 028A / 028B / 029E / 02A0 | `blade-*.json` | `FirmwareEffect` | **inferred**: local OpenRazer class + manifest `keyboard-effect.get/set` (`0F:82` / `0F:02`); firmware acceptance unverified |
| 028C / 029F | `blade-028c.json`, `blade-671.json` | `FirmwareEffect` | selector-prefixed `0F:82` / `0F:02` keyboard effects |
| 02E0 | `blade-02e0.json` | `LedMatrix2026` | `rzDevice25LedMatrixBlade2`; `03:0A` effect + `03:0B` frame |
| 02E1 | `blade-02e1.json` | `LedMatrix2026` | `rzDevice25LedMatrixBlade2`; `03:0A` effect + `03:0B` frame |
| 02C6 / 029D / 02B6 / 02B7 / 02B8 / 02C5 / 02C7 | `blade-*.json` | `LedMatrix710` | existing 710 matrix path (`03:0A` effects, `03:0B` frames) |
| 0233 / 0240 / 0245 / 024A / 0255 | `blade-*.json` | `Unknown` | no effect contract (0245 is a read-only probe); brightness only |

Rows marked **inferred** are not hardware-confirmed. 028C and 029F show that the same `rzDevice25LedMatrixBlade` handler can resolve to a different dialect on a particular firmware, so these rows need a runtime `03:0A` / `0F:02` probe before they are treated as confirmed.

At runtime, an explicit `0x03` rejection or `0x05` not-supported answer to the native effect report downgrades that PID to `Unknown` for the rest of the session; keyboard brightness stays available. A timeout or busy response does not downgrade the dialect.

`FirmwareEffect` writes use a 6-byte `0F:02` payload (`profile, region, effect, flags, rate, colorCount`); the `blade-671` manifest maximum is 6, so the configured-request builder accepts the effect frame. The effect *get* report keeps its 3-byte selector.

`BladeLighting2023Admitted` no longer contains a product-id whitelist. It only maps the `FirmwareEffect` dialect to the selector-prefixed effects.

## 023A boundary

The 023A bundle's `rzDevice25LedMatrixBlade` wrapper delegates to the parent `rzDevice25LedMatrix` handler:

- Native effect: `03:0A`, payload starts with the effect id. There is no store/region prefix.
- Frame: `03:0B`, payload `[0xFF, row, start, end, RGB...]`, 70-byte data buffer, followed by a `05 00` latch.
- The matrix geometry is not hard-coded in the bundle. The controller uses the OpenRazer 6x16 dimension for 023A and sends six `03:0B` rows plus the `03:0A` `05 00` latch.
- Admitted effects: Off `0x00`, Static `0x06`, Breathing `0x03`, Spectrum `0x04`, Wave `0x01`, Reactive `0x02`; ChromaFile uses the software frame pump when an animation is loaded.
- Fire, Ripple, AudioMeter, Ambient, Wheel, Starlight and Tidal remain unexposed for 023A because their bundle path is not a proven 023A native effect.

This is a bundle/protocol alignment, not a real-device validation. The 023A hardware still needs the switch, effect, frame, and color paths exercised on a machine.
