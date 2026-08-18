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
| Thermal fan mode | GET `04/0D/82`, SET `04/0D/02` | profile, fan ID, mode, value; Product-selectable values are `0,2,3,4,5,7`. Battery Saver is `3`; `6` is automatic Balanced DC while on battery | Verified transaction and exact two-zone read; Battery Saver `3` and automatic Balanced DC `6` need targeted physical confirmation |
| Stored fan target | GET `03/0D/81`, SET `03/0D/01` | profile, fan ID, RPM / 100 | SourceBacked read; the backend now reads and restores CPU/GPU targets independently for the software curve transaction. Ordinary fixed-target writes remain gated by lifecycle evidence |
| Fan ID list | GET `50/0D/80` | response count, fan IDs | SourceBacked opt-in probe |
| Current fan RPM | GET `03/0D/88` | profile, fan ID; response raw RPM / 100 | SourceBacked opt-in probe |
| Advanced fan mode / wattage | GET `03/0D/87`, SET `03/0D/07` | profile, CPU/GPU fan ID, value | SourceBacked GET; no production write |
| CPU/GPU Boost | GET `03/0D/87`, SET `03/0D/07` | cluster and enum value | Verified read/write/restore |
| Power mode control | GET `01/07/8F`, SET `01/07/0F` | bit 0 CPU Boost, bit 1 Max Fan, bit 2 one-time override, bit 3 local dimming | Max Fan Verified. Production now decodes the one-time-full-charge and local-dimming bits from the same read without another report; both remain SourceBacked with no production SET |
| Charge limiter | GET `01/07/92`, SET `01/07/12` | encoded limit byte | Verified read/write/restore |
| System battery / lighting sleep policy | No Product 710 HID report | Product 710 sets `readSystemBattery=true`, keeps its device `battery` state machine at `powerOff`, and implements idle/display/battery lighting rules in host middleware before calling keyboard brightness | Native host policy; shared `02/07/80,84,88,83` commands are explicitly excluded |
| Native display mode | GET `01/0D/8E`, SET `01/0D/0E` | `UHD=0`, `FHD=1`; SKU flags use GET `01/0D/8F` | Verified electronic `UHD -> FHD -> UHD` readback/restore on 2026-08-18; physical switching, UI exposure and automatic restart remain disabled |
| LED brightness/state/effect/RGB | Verified OpenRazer path remains `03/03/80` + `03/03/00`, args `[01,04,state]`, transaction `FF`; Product 710 Synapse Logo path is `03/03/02` then `03/03/00`, args `[00,04,effect/state]`, transaction `00` | OpenRazer path is current-device verified for Logo Off/Static. Local Product 710 task runner proves the separate two-step Synapse sequence (`effectId 2` for Breathing, then state On) | Keyboard brightness Verified; Logo Off/Static Verified; Synapse Logo Breathing SourceBacked pending current-device visual/readback validation |
| Basic Lighting Engine | Host renders through the Verified `6 x 17` custom-matrix path at 25 FPS | Wave ports Product 710 `Width=100/Speed=25/Pause=0/Angle=90\|270`; Fire ports the DLL's 100-keyframe field, Rate-160 five-phase/500-frame cycle, fixed masks/color tables and reverse `7 x 23 -> 6 x 17` crop; Wheel, Starlight and Tidal port the recovered center projection, scheduler and three-lane projection | Wheel, Starlight and two-color Tidal are production-connected. Wave/Fire/Wheel remain Approximate pending exact physical comparison; Starlight/Tidal passed visual and Normal-restoration validation on 2026-08-17 |
| Gaming Mode | GET `04/00/88`, SET `04/00/08` | Product 710 descriptors use data size `04`; SET args `[state]`, GET parser returns `gameMode`, `keyCover`, `lifted`. Host Windows/Alt suppression is MappingEngine policy | Verified production transaction and M3 indicator synchronization; current-device M3 toggle was confirmed on 2026-08-17 |
| Internal display brightness keys | No Razer HID report | Product 710 default MappingEngine graph emits `display/driverBrightnessDown|Up|Stop`; OpenSynapse uses `BrightnessOverride.GetDefaultForSystem` | Native production handler implemented without Razer service or elevation; physical Fn+F7/F8 check remains |
| Fn primary | SET `02/02/06` | Product 710 module `48967` resolves data size `02`, args `[classId,alternateState]`; middleware calls class `0`, state `0=func` or `1=multi`; no GET | SourceBacked builder/parser; no production SET |
| M5 microphone mute indicator sync | SET/GET `03/18/04` | Product 710 opens the persistent handle with a full 91-byte report beginning `02 00 00 00 00 00 02 00 81`, enters Driver Mode, sends `[00,02,muted]`, waits 5 ms, then reads report ID `2`; no Product 710 call to speaker target `1` was found | Verified 2026-08-17: tx `2` On visibly lit M5 for 10 seconds, tx `3` Off returned success, and Normal was restored. Production starts on device discovery and coordinates mode ownership with software lighting |
| Trackpad toggle | No HID report | Product `bladeTrackpad` handler calls system `getTouchPadEnableStatus` / `toggleTouchPadEnableStatus` helper | Native/system path; no fabricated HID command |
| Startup animation | GET `01/0F/98`, SET `02/0F/18` | GET `[00]`; SET `[profileId=00,disableAnimation=0\|1]`; Product 710 requires firmware `>=1.08.00` | Verified electronic `true -> false -> true` readback/restore on 2026-08-18; reboot-time visual validation still gates the UI setter |
| OLED | No Product 710 call | Generic OLED strings belong to other products | Excluded |
| Key mapping / HyperShift | No Product 710 board report | Normal and HyperShift assignments share `profiles[].mappings`; exact keyboard/DKM pairs, HyperShift outputs, extended flags and canonical graph MD5 are recovered. Product 710 has no `OBMSpecs` | SourceBacked compiler, session, Raw Input host, SendInput sink, and explicit opt-in window host are implemented; physical suppression/injection is still unvalidated |
| Snap Tap | No Product 710 board report | `profiles[].snapTap = {isEnabled,keyList}`; exact pass-through compilation attaches one `snaptapId` to both keys, and the HyperShift toggle runs on release. Generic OBM kill-switch is not enabled for Product 710 | SourceBacked compiler, owned arbitration, explicit host and failure-safe cleanup are implemented; real A/D behavior remains unvalidated |
| System detail export | Native service action `GetAllBladeInfo`, no arguments | Product 710 uses it only to save a diagnostic `.synapse4` JSON file | Native diagnostic export; not a HID capability |
| Display/firmware inventory | Native service actions `GetBladeDisplayData`, `GetBladeFirmwareInfo`, `GetBladeDriverInfo`, `GetBladeScreenResolution`, no arguments | Read-only inventory returned by the proprietary Blade helper; screen/HDR/ICC user-facing equivalents are documented separately | No unknown `02C6` report; vendor-only inventory stays out of the hardware-control backend |
| Panel vendor / USB4 updater checks | Native actions `GetBladeLEDPanelProducerShortName` and `GetBladeUSB4RootRouterFWVersion([vid,pid])` | Used only by Product 710 firmware-update eligibility checks | Excluded with the vendor firmware updater |
| Smart fan curve runtime | No separate HID table | Product 710 persists `smartFanCurve.cpu/gpu` nodes and software-interpolates CPU/GPU outputs before calling `setThermalFanSpeed` for fan IDs 1 and 2; observed target conversion is `floor(value/100)` | SourceBacked runtime with serialized Stop/Dispose, non-cancelable restoration and transient sensor-loss tests; no guessed table packet or production UI, pending physical disconnect/sleep/hard-exit validation |
| VGA configure / GPU mode | GET `02/0D/89`, SET `02/0D/09` | mode byte | Excluded: this Blade configuration is Optimus-only |

`getThermalFanInformation` and thermal fan-table helpers exist in the dependency, but the ordinary Product 710 flow does not require a table write; they are not admitted as production features.

## Viper V3 HyperSpeed / Product 184

| Product call | Wire `[size,class,command]` | Arguments / response | State in OpenSynapse |
|---|---|---|---|
| Battery | GET `02/07/80` | raw battery value | Verified read-only |
| Current X/Y DPI | GET `07/04/85`, SET `07/04/05` | profile, X/Y big-endian | Verified read/write |
| Polling rate | GET `01/00/85`, SET `01/00/05` | `08/02/01` = `125/500/1000 Hz` | Verified read/write |
| Idle timeout | GET `02/07/83`, SET `02/07/03` | seconds big-endian | Verified read/write |
| DPI stages | GET `26/04/86`, SET `26/04/06` | persistent store `01`, active/count/stage records | Verified read/write/readback/restore on 2026-08-13 |
| Low-battery threshold | GET `01/07/81` | raw threshold | SourceBacked opt-in GET only |
| Battery chemistry | SET `01/07/14` | alkaline/NiMH/lithium | SourceBacked builder only; no sender without GET/restore |
| Maximum / count / Profile IDs | GET `01/05/8A`, `01/05/80`, `50/05/81` | scalar responses or `[count,...ids]`; Product 184 reports exactly fixed Profile `1`. Generic active-Profile GET `01/05/84` is rejected by this device | SourceBacked strict GET builders/parsers and dynamic validator |
| Button ID list | GET `50/02/84` | `[count,...buttonIds]`; local Product 184 logs returned `1,2,3,4,5,9,10,96` | SourceBacked strict GET builder/parser |
| Single button assignment / HyperShift layer | GET `50/02/8C`, SET `50/02/0C` | GET args `[profileId,buttonId,mode]`; SET args `[profileId,buttonId,mode,functionId,functionDataSize,...functionData]`; request mode `0=Normal`, `1=HyperShift`. Product 184 returns actual size `0A` and echoes mode `1` for both, so request context is authoritative | Board-layer transaction Verified. Production SET admits `Off`, known mouse `ButtonCode`, `KeyboardKey`, `DoubleClick`, DPI, media, HyperShift, keyboard Turbo and mouse Turbo. The five latter families passed target readback, sibling isolation and restoration on 2026-08-18 |
| HyperShift activator | SET is the same `50/02/0C` assignment transaction | Product 184 UI encoder emits function `12 (ModeButtonkey)`, size `1`, data `[1]`. Generic function `17/[89]` is not product-used evidence | Verified electronic write/readback/isolation/restore on 2026-08-18 and admitted by production SET; physical button behavior is not claimed |
| Macros / lighting / surface calibration | Product flags or project scope exclude them | none admitted | Excluded |

The Product 184 bundle also contains an archival `00/C0` / `00/40` HyperPolling contract, but the current `00B8` hardware supports only the verified normal `125/500/1000 Hz` path. HyperPolling is not admitted to the OpenSynapse backend or UI.

## Admission Rule

The probe catalog and production backend accept only entries above. The other methods in the shared 416-method `rzDevice25` surface remain out of scope unless Product 710 or 184 actually calls them and the exact request, response, device ownership, and recovery behavior are proven.
