# BLE protocol 30 coverage

Source of truth: `artifacts/razer-mw-full-cdn/bundles/00AA/rzDevice30.*.js`
(Razer Basilisk V3 Pro, the device class the BLE page uses). Every frame below
was taken from that file, not guessed. No hardware has run any of it yet.

## Covered

| Group | Commands | Where |
|---|---|---|
| Device | `00:81` firmware, `01:82/02` mode, `01:83` serial, `01:84/04` polling | `RazerBleProtocol30.cs` |
| Battery | `05:81` level, `05:85` charging, `05:82/02` critical, `05:87/07` auto sleep, `05:84/04` sleep time, `05:8A/0A` time to dim, `05:8F/0F` chemistry, `05:8D/0D` smart dimming | `RazerBleProtocol30*.cs` |
| Profiles | `03:80` id list, `03:82/02` active profile | same |
| DPI | `0B:81/01` DPI, `0B:84/04` stages | same |
| Input | `08:80` button list, `08:84/04` assignment, `07:8A/0A` Snap Tap, `07:82/02` debounce | same |
| Scroll | `08:85/05` free-notched, `08:87/07` smart reel, `08:8A/0A` threshold, `08:86/06` acceleration, `08:89/09` acceleration level, `08:88/08` masking time, `08:B0/30` tactile mode | same |
| Haptics | `15:80` active stage, `15:81` max stage, `15:00` set stage | `RazerBleProtocol30.Extra.cs` |
| Lighting | `10:80` region list, `10:85/05` brightness, `10:83/03` effect | same |

Each settings read probes on load and the card stays hidden when the device
answers "not supported". Each write reads the old value, writes, reads back,
and restores the old value on mismatch.

## Not covered, and why

| Group | Reason |
|---|---|
| `03:04/05/06` profile name, create, delete; `04:01..04/80..85` macros | Onboard memory. `sendCommand` in the BLE class is `(cmd, desc, param6, param7, payload, packetSize)`. `getMacroData` calls it as `sendCommand(cmd, desc, idArray, offset)` - the two-byte id lands in `param6`, which `_createDataSend` writes into one byte and coerces to 0. The macro id never reaches the device, so the reference read path is malformed and cannot be copied. Creating a profile also requires writing the 250-byte structure right after `03:05`, which needs hardware to verify a rollback. |
| `15:02..0B` haptic stage tables | 22-40 byte tick/generator blocks plus a tuning UI. Stage switching is wired; editing the feel of each stage is a feature, not a command. |
| `0F:xx` proximity sensor | Calibration of the lift-off sensor, not a user setting. |
| `06:xx` legacy LED family | The mouse's lighting runs through `10:xx` Chroma; `06:0B/0C` (dock sync, charging effect) are not called by this device's BLE page. |
| `11:01` thermal fan, `17:xx` OLED, `06:0D/0E` knob | Other device classes. |
| `01:xx` pairing, HW info, wireless status, dock status | Read-only diagnostics; not a setting. |
