# Product 710 M3/M4/M5 report routing

## Result

Product 710 declares real HID report bytes, not abstract category numbers:

```json
{"4":"razerKeyReportID","5":"hardwareEventReportID"}
```

Therefore report `0x04` enters the RazerKey collection-difference path (internal
case `6`), while report `0x05` enters the generic hardware-event path (internal
case `8`). M3/M4/M5 are configured as RazerKey IDs `210`, `211`, and `212`.

## Evidence

- Product bundle:
  `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000233`
  - byte offsets `137518`, `137545`, `137572`: `key:210`, `key:211`, `key:212`
  - byte offsets `147608`, `147728`: `4:"razerKeyReportID",5:"hardwareEventReportID"`
- Runtime product log:
  `%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Logs\products_710_ui {00000000-0000-0000-FFFF-FFFFFFFFFFFF}4.log`
  - line `1371`: `reportIDs {"4":"razerKeyReportID","5":"hardwareEventReportID"}`
  - line `7450`: the persisted Product 710 object repeats that map and includes
    default mappings for RazerKey IDs `210`, `211`, and `212`
- Ghidra export:
  `D:\Workspaces\OpenSynapse\artifacts\ghidra\mapping-product710-report-path.txt`
  - lines `3-12`: the native string-to-enum table maps
    `razerKeyReportID -> 6` and `hardwareEventReportID -> 8`
  - `FUN_1800e4970 @ 0x1800E4970`: parses each JSON object key as a report byte,
    converts its string value through `FUN_1800E1888`, and stores the enum at
    lookup-node offset `+0x14`
  - `FUN_1800e345c @ 0x1800E345C`: looks up the incoming report byte and returns
    that stored enum
  - `FUN_1800a7860 @ 0x1800A7860`: internal case `6` calls
    `FUN_18007ED66` (RazerKey collection difference); case `8` falls through to
    `FUN_18007E356` (generic hardware-event callback)
- Cached official Protocol 2.5 parser:
  `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\data_3`
  - byte offset `9017353`: module `79885` uses byte 0 as `recordId`
  - record `0x05` is `NOTIFICATION_EVENTS`; byte 1 is its event ID

## Correction to the Col05 interpretation

The previously observed reports are not M-key reports:

- `05 33 0C 0C`: notification event `0x33` (`POWER_WATTAGE_EVENT`), with both
  wattage enums `0x0C` (`200W`)
- `05 08 FF`: notification event `0x08` (`BRIGHTNESS_CHANGE_EVENT`), value
  `0xFF`

They were unrelated notifications arriving during the key test. The expected
M-key transport is report `0x04` carrying a current-key set that contains
`0xD2`, `0xD3`, or `0xD4`; the native difference handler emits press flag `0`
and release flag `1`. This final byte-level shape still needs a physical report
capture or a successful MappingEngine callback to be marked physically verified.
