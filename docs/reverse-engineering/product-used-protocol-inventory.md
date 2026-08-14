# Product-Used Protocol Inventory

Scope is deliberately limited to Synapse Product 710 (`1532:02C6`) and Product 184 (`1532:00B8`). A method existing only in the shared `rzDevice25` bundle is not admitted.

Evidence states:

- `Verified`: current-device read/write/readback/restore evidence permits production use.
- `SourceBacked`: exact product call and wire layout are known; only strict builders/parsers or opt-in GET probes are admitted.
- `Blocked`: product capability exists, but its exact safe device contract or recovery path is incomplete.
- `Excluded`: explicitly outside this device/configuration scope.

## Blade 16 2025 / Product 710

| Product call | Wire `[size,class,command]` | Arguments / response | State in OpenSynapse |
|---|---|---|---|
| Keyboard brightness | GET `02/0E/84`, SET `02/0E/04` | profile, brightness | Verified read/write |
| Thermal fan mode | GET `04/0D/82`, SET `04/0D/02` | profile, fan ID, mode, value | Verified performance-mode write; exact two-zone read |
| Stored fan target | GET `03/0D/81`, SET `03/0D/01` | profile, fan ID, RPM / 100 | SourceBacked read; write Blocked by recovery requirements |
| Fan ID list | GET `50/0D/80` | response count, fan IDs | SourceBacked opt-in probe |
| Current fan RPM | GET `03/0D/88` | profile, fan ID; response raw RPM / 100 | SourceBacked opt-in probe |
| Advanced fan mode / wattage | GET `03/0D/87`, SET `03/0D/07` | profile, CPU/GPU fan ID, value | SourceBacked GET; no production write |
| CPU/GPU Boost | GET `03/0D/87`, SET `03/0D/07` | cluster and enum value | Verified read/write/restore |
| Power mode control | GET `01/07/8F`, SET `01/07/0F` | bit 0 CPU Boost, bit 1 Max Fan, bit 2 one-time override, bit 3 local dimming | Max Fan Verified; setter preserves sibling bits. Other flags not exposed |
| Charge limiter | GET `01/07/92`, SET `01/07/12` | encoded limit byte | Verified read/write/restore |
| LED brightness/state/effect/RGB | Blade Logo state: GET `03/03/80`, SET `03/03/00`; args `[01,04,state]`; transaction `FF` | OpenRazer `razerchromacommon.c` and Blade laptop branch in `razerkbd_driver.c`; LED state, not effect, for on/off | Keyboard brightness Verified; Logo Off/Static Verified on current hardware; quick effects SourceBacked; Logo Breathing SourceBacked pending visual validation |
| Function-key swap / game mode / device game-mode selection / auto sleep | Product 710 call exists | complete safe `02C6` mapping/policy contract not established | Blocked |
| BIOS/EC/firmware/display/OLED reads | Product 710 call exists | mixed native/vendor paths | Not admitted to HID backend without exact product contract |
| VGA configure / GPU mode | GET `02/0D/89`, SET `02/0D/09` | mode byte | Excluded: this Blade configuration is Optimus-only |

`getThermalFanInformation` and thermal fan-table helpers exist in the dependency, but the ordinary Product 710 flow does not require a table write; they are not admitted as production features.

## Viper V3 HyperSpeed / Product 184

| Product call | Wire `[size,class,command]` | Arguments / response | State in OpenSynapse |
|---|---|---|---|
| Battery | GET `02/07/80` | raw battery value | Verified read-only |
| Current X/Y DPI | GET `07/04/85`, SET `07/04/05` | profile, X/Y big-endian | Verified read/write |
| Polling rate | GET `01/00/85`, SET `01/00/05` | `08/02/01` = `125/500/1000 Hz` | Verified read/write |
| Idle timeout | GET `02/07/83`, SET `02/07/03` | seconds big-endian | Verified read/write |
| DPI stages | GET `26/04/86` | persistent store `01`, active/count/stage records | SourceBacked opt-in GET only |
| Low-battery threshold | GET `01/07/81` | raw threshold | SourceBacked opt-in GET only |
| Battery chemistry | SET `01/07/14` | alkaline/NiMH/lithium | SourceBacked builder only; no sender without GET/restore |
| Button mapping / HyperShift / profile | Product data is stored through AppEngine JSON | no demonstrated board-profile HID write | Blocked |
| Macros / lighting / surface calibration | Product flags or project scope exclude them | none admitted | Excluded |

## Admission Rule

The probe catalog and production backend accept only entries above. The other methods in the shared 416-method `rzDevice25` surface remain out of scope unless Product 710 or 184 actually calls them and the exact request, response, device ownership, and recovery behavior are proven.
