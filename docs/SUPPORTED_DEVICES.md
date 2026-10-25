# Supported devices

OpenSynapse is not limited to the product-specific Blade and Viper implementations. The support surface currently includes:

- **259 OpenRazer catalog models**. Eight Blade PIDs use product-specific manifests: `0255`, `029D`, `02B6`, `02B7`, `02B8`, `02C5`, `02C6`, and `02C7`; the Razer Leviathan V2 X `054A` is also locally admitted.
- **4 product-specific control sets**: Blade, Viper, BlackShark V3 Pro, and Leviathan V2 X audio features that need dedicated protocols.
- **6 Kraken headsets** with lighting control.
- **BlackShark V3 Pro** with battery, sidetone, THX, ANC, and game/chat balance controls.
- **Chroma REST** for compatible games and integrations.

The OpenRazer portion is pinned to OpenRazer commit `477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d`. OpenSynapse creates a page only for a connected, recognized device and shows a control only after resolving its Windows HID endpoint and transaction.

## Product-specific controls

| Device | USB VID:PID | Controls |
|---|---|---|
| Razer Blade 16 (2025) | `1532:02C6` | Telemetry, keyboard lighting, performance, fans, display, battery, touchpad, charge limit, and verified Fn/M3/M4/M5 behavior |
| Razer Blade 14 (2023) | `1532:029D` | Telemetry, 6×16 keyboard matrix lighting, performance, fans, Max Fan, CPU levels, and charge limit |
| Razer Blade 14 (2024) | `1532:02B6` | Telemetry, 6×16 keyboard matrix lighting, performance, fans, Max Fan, CPU levels, and charge limit |
| Razer Blade 16 (2024) | `1532:02B7` | Blade 695 middleware control set: telemetry, 6×17 keyboard matrix lighting, performance, fans, display MUX, native panel mode, local dimming, battery, charge limit, and Fn/game-mode reports |
| Razer Blade 18 (2024) | `1532:02B8` | Telemetry, 6×16 keyboard matrix lighting, performance, fans, Max Fan, and charge limit |
| Razer Blade 14 (2025) | `1532:02C5` | Telemetry, 6×16 keyboard matrix lighting, performance, fans, and charge limit |
| Razer Blade 18 (2025) | `1532:02C7` | Telemetry, 6×19 keyboard matrix lighting, performance, fans, and charge limit |
| Razer Blade Base (Early 2020) | `1532:0255` | Telemetry, keyboard brightness writes, performance/fan-mode writes, adapter power, power-mode bitmap, charge limit, and native panel/SKU reads; the percentage-based fan target is not exposed as an RPM target |
| Razer Blade 16 (2023) | `1532:029F` | Telemetry, keyboard brightness and firmware-rendered effects (Off, Breathing, Spectrum, Wave), performance modes, Custom levels including CPU Boost, Max Fan, fixed fan targets (2000-5000 RPM), charge limit, one-time full charge, and Gaming mode |
| Razer Viper V3 HyperSpeed | `1532:00B8` | Battery, DPI, polling, sleep timeout, battery type, and onboard mappings |
| Razer BlackShark V3 Pro | `1532:101B`, `1532:101C` | Battery, charging, sidetone, THX, ANC, and game/chat balance |

Blade Wave and Spectrum use firmware rendering when no palette is saved and software frame rendering when a palette is saved. The Blade 16 (2023) has no per-key matrix channel: it exposes brightness plus the four effects its firmware draws on its own (Off, Breathing, Spectrum, Wave), and its stored effect color is not readable. Custom colors, audio-reactive lighting, frame rendering, Chroma takeover, the wave direction selector, and the .chroma import button are therefore not offered on that model. The Viper page supports 125 / 500 / 1000 Hz polling, X/Y DPI from 100 to 30000, up to five DPI stages, and Normal/HyperShift mappings in fixed Profile 1.

## Razer Leviathan V2 X soundbars (054A)

`1532:054A` maps to Synapse product `1354` and covers the following editions that share one protocol build:

| Edition ID | Product |
|---:|---|
| 0 | Razer Leviathan V2 X |
| 128 | Razer \| Pokémon Pikachu Edition Soundbar |
| 129 | Razer Leviathan V2 X Pokémon AP Edition |
| 131 | Razer \| Kuromi Limited Edition Soundbar |
| 132 | Razer Leviathan V2 X Kuromi Edition |

OpenRazer does not catalogue `054A`, so OpenSynapse admits it locally for firmware, serial, brightness read, and brightness write. The soundbar page adds equalizer presets, a 10-band equalizer, bass boost, input source, and audio I/O status. Preset and bass writes read the current value first, read back after writing, and restore the previous value on failure. Custom EQ writes restore the bands only when the previous preset was Custom, because a non-custom preset does not expose the stored custom curve. The app reads the edition information command to label Pikachu and Kuromi units separately, and resolves the first Chroma region before brightness reads or writes. The commands come from the current `1354` middleware's 91-byte feature report (report ID `0x07`) with a cycling `0..31` transaction. This is a source-backed candidate implementation; read/write and restore behavior still needs validation on a physical 054A device. Protocol details are in [LEVIATHAN_V2X_054A.md](LEVIATHAN_V2X_054A.md).

## Kraken lighting

| Model | USB PID | Controls |
|---|---|---|
| Kraken 7.1 | `0501`, `0506` | Lighting |
| Kraken 7.1 Chroma | `0504` | Lighting |
| Kraken 7.1 V2 | `0510` | Lighting |
| Kraken Tournament Edition | `0520` | Lighting |
| Kraken Ultimate | `0527` | Lighting |
| Kraken Kitty V2 | `0560` | Lighting |

Kraken support uses a separate 37-byte Output Report path. Audio, microphone, EQ, and THX controls are not included.

## BlackShark V3 Pro audio

| Model | USB PID | Controls |
|---|---|---|
| BlackShark V3 Pro (USB) | `101C` | Battery, charging, sidetone, THX, ANC, and game/chat balance |
| BlackShark V3 Pro (2.4 GHz) | `101B` | Battery, charging, sidetone, THX, ANC, and game/chat balance |

OpenRazer ships no BlackShark driver, so there is no upstream device list to follow. The control collection has the shape Razer vendor `1532`, collection page `FF13`/`FF14`, usage `1`, and an output report of at least 64 bytes. That shape is not exclusive to BlackShark: Kraken V4 (`056B`/`056C`) enumerates with the same shape. Selection therefore also requires the HID product name to contain `BlackShark`, or the product id to be one of the verified `101B` (2.4 GHz) / `101C` (USB) values. The transport byte does not distinguish wired from wireless on these devices, and every observed variant answers on `0x80`.

The headset uses its own 64-byte Report `0x02` channel. Queries run only when the device page is opened or refreshed; OpenSynapse does not poll battery at a fixed interval. EQ, microphone gain, firmware updates, and indicator-LED modes are outside this first release.

## OpenRazer catalog

259 devices are present in the pinned catalog:

- Mice: 112
- Keyboards: 73
- Laptops: 48
- Accessories: 26

### Mice (112)

| Device | USB VID:PID | Type |
|---|---|---|
| Razer Abyssus | `1532:0042` | Mouse |
| Razer Abyssus 1800 | `1532:0020` | Mouse |
| Razer Abyssus 2000 | `1532:005E` | Mouse |
| Razer Abyssus Elite (D.Va Edition) | `1532:006A` | Mouse |
| Razer Abyssus Essential | `1532:006B` | Mouse |
| Razer Abyssus V2 | `1532:005B` | Mouse |
| Razer Atheris (Receiver) | `1532:0062` | Mouse |
| Razer Basilisk | `1532:0064` | Mouse |
| Razer Basilisk Essential | `1532:0065` | Mouse |
| Razer Basilisk Mobile (Receiver) | `1532:00D4` | Mouse |
| Razer Basilisk Mobile (Wired) | `1532:00D3` | Mouse |
| Razer Basilisk Ultimate | `1532:0086` | Mouse |
| Razer Basilisk V2 | `1532:0085` | Mouse |
| Razer Basilisk V3 | `1532:0099` | Mouse |
| Razer Basilisk V3 35K | `1532:00CB` | Mouse |
| Razer Basilisk V3 Pro (Wired) | `1532:00AA` | Mouse |
| Razer Basilisk V3 Pro (Wireless) | `1532:00AB` | Mouse |
| Razer Basilisk V3 Pro 35K (Wired) | `1532:00CC` | Mouse |
| Razer Basilisk V3 Pro 35K (Wireless) | `1532:00CD` | Mouse |
| Razer Basilisk V3 Pro 35K Phantom Green Edition (Wired) | `1532:00D6` | Mouse |
| Razer Basilisk V3 Pro 35K Phantom Green Edition (Wireless) | `1532:00D7` | Mouse |
| Razer Basilisk V3 X HyperSpeed | `1532:00B9` | Mouse |
| Razer Basilisk X HyperSpeed | `1532:0083` | Mouse |
| Razer Cobra | `1532:00A3` | Mouse |
| Razer Cobra HyperSpeed (Wired) | `1532:00DA` | Mouse |
| Razer Cobra HyperSpeed (Wireless) | `1532:00DB` | Mouse |
| Razer Cobra Pro (Wired) | `1532:00AF` | Mouse |
| Razer Cobra Pro (Wireless) | `1532:00B0` | Mouse |
| Razer DeathAdder 1800 | `1532:0038` | Mouse |
| Razer DeathAdder 2000 | `1532:004F` | Mouse |
| Razer DeathAdder 2013 | `1532:0037` | Mouse |
| Razer DeathAdder 3500 | `1532:0054` | Mouse |
| Razer DeathAdder Chroma | `1532:0043` | Mouse |
| Razer DeathAdder Elite | `1532:005C` | Mouse |
| Razer DeathAdder Essential | `1532:006E` | Mouse |
| Razer DeathAdder Essential (2021) | `1532:0098` | Mouse |
| Razer DeathAdder Essential (White Edition) | `1532:0071` | Mouse |
| Razer DeathAdder V2 | `1532:0084` | Mouse |
| Razer DeathAdder V2 Lite | `1532:00A1` | Mouse |
| Razer DeathAdder V2 Mini | `1532:008C` | Mouse |
| Razer DeathAdder V2 Pro (Wired) | `1532:007C` | Mouse |
| Razer DeathAdder V2 Pro (Wireless) | `1532:007D` | Mouse |
| Razer DeathAdder V2 X HyperSpeed | `1532:009C` | Mouse |
| Razer DeathAdder V3 | `1532:00B2` | Mouse |
| Razer DeathAdder V3 HyperSpeed (Wired) | `1532:00C4` | Mouse |
| Razer DeathAdder V3 HyperSpeed (Wireless) | `1532:00C5` | Mouse |
| Razer DeathAdder V3 Pro (Wired) | `1532:00B6` | Mouse |
| Razer DeathAdder V3 Pro (Wired) | `1532:00C2` | Mouse |
| Razer DeathAdder V3 Pro (Wireless) | `1532:00B7` | Mouse |
| Razer DeathAdder V3 Pro (Wireless) | `1532:00C3` | Mouse |
| Razer DeathAdder V4 Pro (Wired) | `1532:00BE` | Mouse |
| Razer DeathAdder V4 Pro (Wireless) | `1532:00BF` | Mouse |
| Razer Diamondback Chroma | `1532:004C` | Mouse |
| Razer HyperPolling Wireless Dongle | `1532:00B3` | Mouse |
| Razer Imperator 2012 | `1532:002F` | Mouse |
| Razer Lancehead (Wired) | `1532:0059` | Mouse |
| Razer Lancehead (Wireless) | `1532:005A` | Mouse |
| Razer Lancehead Tournament Edition | `1532:0060` | Mouse |
| Razer Lancehead Wireless (Receiver) | `1532:006F` | Mouse |
| Razer Lancehead Wireless (Wired) | `1532:0070` | Mouse |
| Razer Mamba 2012 (Wired) | `1532:0024` | Mouse |
| Razer Mamba 2012 (Wireless) | `1532:0025` | Mouse |
| Razer Mamba Chroma (Wired) | `1532:0044` | Mouse |
| Razer Mamba Chroma (Wireless) | `1532:0045` | Mouse |
| Razer Mamba Elite | `1532:006C` | Mouse |
| Razer Mamba Tournament Edition | `1532:0046` | Mouse |
| Razer Mamba Wireless (Receiver) | `1532:0072` | Mouse |
| Razer Mamba Wireless (Wired) | `1532:0073` | Mouse |
| Razer Naga | `1532:0015` | Mouse |
| Razer Naga 2012 | `1532:002E` | Mouse |
| Razer Naga 2014 | `1532:0040` | Mouse |
| Razer Naga Chroma | `1532:0053` | Mouse |
| Razer Naga Epic | `1532:001F` | Mouse |
| Razer Naga Epic Chroma (Wired) | `1532:003E` | Mouse |
| Razer Naga Epic Chroma (Wireless) | `1532:003F` | Mouse |
| Razer Naga Hex | `1532:0041` | Mouse |
| Razer Naga Hex (Red) | `1532:0036` | Mouse |
| Razer Naga Hex V2 | `1532:0050` | Mouse |
| Razer Naga Left Handed Edition 2020 | `1532:008D` | Mouse |
| Razer Naga Pro (Wired) | `1532:008F` | Mouse |
| Razer Naga Pro (Wireless) | `1532:0090` | Mouse |
| Razer Naga Trinity | `1532:0067` | Mouse |
| Razer Naga V2 HyperSpeed (Receiver) | `1532:00B4` | Mouse |
| Razer Naga V2 Pro (Wired) | `1532:00A7` | Mouse |
| Razer Naga V2 Pro (Wireless) | `1532:00A8` | Mouse |
| Razer Naga X | `1532:0096` | Mouse |
| Razer Orochi (Wired) | `1532:0048` | Mouse |
| Razer Orochi 2011 | `1532:0013` | Mouse |
| Razer Orochi 2013 | `1532:0039` | Mouse |
| Razer Orochi V2 (Bluetooth) | `1532:0095` | Mouse |
| Razer Orochi V2 (Receiver) | `1532:0094` | Mouse |
| Razer Ouroboros | `1532:0032` | Mouse |
| Razer Pro Click (Receiver) | `1532:0077` | Mouse |
| Razer Pro Click (Wired) | `1532:0080` | Mouse |
| Razer Pro Click Mini (Receiver) | `1532:009A` | Mouse |
| Razer Pro Click V2 (Wired) | `1532:00D0` | Mouse |
| Razer Pro Click V2 (Wireless) | `1532:00D1` | Mouse |
| Razer Pro Click V2 Vertical Edition (Wired) | `1532:00C7` | Mouse |
| Razer Pro Click V2 Vertical Edition (Wireless) | `1532:00C8` | Mouse |
| Razer Taipan | `1532:0034` | Mouse |
| Razer Viper | `1532:0078` | Mouse |
| Razer Viper 8KHz | `1532:0091` | Mouse |
| Razer Viper Mini | `1532:008A` | Mouse |
| Razer Viper Mini SE (Wired) | `1532:009E` | Mouse |
| Razer Viper Mini SE (Wireless) | `1532:009F` | Mouse |
| Razer Viper Ultimate (Wired) | `1532:007A` | Mouse |
| Razer Viper Ultimate (Wireless) | `1532:007B` | Mouse |
| Razer Viper V2 Pro (Wired) | `1532:00A5` | Mouse |
| Razer Viper V2 Pro (Wireless) | `1532:00A6` | Mouse |
| Razer Viper V3 Pro (Wired) | `1532:00C0` | Mouse |
| Razer Viper V3 Pro (Wireless) | `1532:00C1` | Mouse |
| RazerBasiliskUltimateReceiver | `1532:0088` | Mouse |

### Keyboards (73)

| Device | USB VID:PID | Type |
|---|---|---|
| BlackWidow Chroma V2 | `1532:0221` | Keyboard |
| Razer Anansi | `1532:010F` | Keyboard |
| Razer BlackWidow (Classic) | `1532:011B` | Keyboard |
| Razer BlackWidow 2019 | `1532:0241` | Keyboard |
| Razer BlackWidow Chroma | `1532:0203` | Keyboard |
| Razer BlackWidow Chroma (Overwatch) | `1532:0211` | Keyboard |
| Razer BlackWidow Elite | `1532:0228` | Keyboard |
| Razer BlackWidow Essential | `1532:0237` | Keyboard |
| Razer BlackWidow Lite | `1532:0235` | Keyboard |
| Razer BlackWidow Stealth Edition | `1532:010E` | Keyboard |
| Razer BlackWidow Tournament Edition 2014 | `1532:011C` | Keyboard |
| Razer BlackWidow Tournament Edition Chroma | `1532:0209` | Keyboard |
| Razer BlackWidow Ultimate 2012 | `1532:010D` | Keyboard |
| Razer BlackWidow Ultimate 2013 | `1532:011A` | Keyboard |
| Razer BlackWidow Ultimate 2016 | `1532:0214` | Keyboard |
| Razer BlackWidow V3 | `1532:024E` | Keyboard |
| Razer BlackWidow V3 Mini HyperSpeed (Wired) | `1532:0258` | Keyboard |
| Razer BlackWidow V3 Mini HyperSpeed (Wireless) | `1532:0271` | Keyboard |
| Razer BlackWidow V3 Pro 2.4 Ghz Wireless | `1532:025C` | Keyboard |
| Razer BlackWidow V3 Pro Wired | `1532:025A` | Keyboard |
| Razer BlackWidow V3 TK | `1532:0A24` | Keyboard |
| Razer BlackWidow V4 | `1532:0287` | Keyboard |
| Razer BlackWidow V4 75% | `1532:02A5` | Keyboard |
| Razer BlackWidow V4 Mini HyperSpeed (Wired) | `1532:02B9` | Keyboard |
| Razer BlackWidow V4 Mini HyperSpeed (Wireless) | `1532:02BA` | Keyboard |
| Razer BlackWidow V4 Pro | `1532:028D` | Keyboard |
| Razer BlackWidow V4 Tenkeyless HyperSpeed (Wired) | `1532:02D7` | Keyboard |
| Razer BlackWidow V4 Tenkeyless HyperSpeed (Wireless) | `1532:02D5` | Keyboard |
| Razer BlackWidow V4 X | `1532:0293` | Keyboard |
| Razer BlackWidow X Chroma | `1532:0216` | Keyboard |
| Razer BlackWidow X Tournament Edition Chroma | `1532:021A` | Keyboard |
| Razer BlackWidow X Ultimate | `1532:0217` | Keyboard |
| Razer Cynosa Chroma | `1532:022A` | Keyboard |
| Razer Cynosa Chroma Pro | `1532:022C` | Keyboard |
| Razer Cynosa Lite | `1532:023F` | Keyboard |
| Razer Cynosa V2 | `1532:025E` | Keyboard |
| Razer DeathStalker Chroma | `1532:0204` | Keyboard |
| Razer DeathStalker Expert | `1532:0202` | Keyboard |
| Razer DeathStalker V2 | `1532:0295` | Keyboard |
| Razer DeathStalker V2 Pro (Wired) | `1532:0292` | Keyboard |
| Razer DeathStalker V2 Pro (Wireless) | `1532:0290` | Keyboard |
| Razer DeathStalker V2 Pro TKL (Wired) | `1532:0298` | Keyboard |
| Razer DeathStalker V2 Pro TKL (Wireless) | `1532:0296` | Keyboard |
| Razer DeathStalker/DeathStalker Essential | `1532:0118` | Keyboard |
| Razer Huntsman | `1532:0227` | Keyboard |
| Razer Huntsman Elite | `1532:0226` | Keyboard |
| Razer Huntsman Mini | `1532:0257` | Keyboard |
| Razer Huntsman Mini Analog | `1532:0282` | Keyboard |
| Razer Huntsman Mini JP | `1532:0269` | Keyboard |
| Razer Huntsman Tournament Edition | `1532:0243` | Keyboard |
| Razer Huntsman V2 | `1532:026C` | Keyboard |
| Razer Huntsman V2 Analog | `1532:0266` | Keyboard |
| Razer Huntsman V2 Tenkeyless | `1532:026B` | Keyboard |
| Razer Huntsman V3 Pro | `1532:02A6` | Keyboard |
| Razer Huntsman V3 Pro 8KHz | `1532:02CF` | Keyboard |
| Razer Huntsman V3 Pro Mini | `1532:02B0` | Keyboard |
| Razer Huntsman V3 Pro TKL | `1532:02A7` | Keyboard |
| Razer Joro | `1532:02CD` | Keyboard |
| Razer Nostromo | `1532:0111` | Keyboard |
| Razer Orbweaver | `1532:0113` | Keyboard |
| Razer Orbweaver Chroma | `1532:0207` | Keyboard |
| Razer Ornata | `1532:021F` | Keyboard |
| Razer Ornata Chroma | `1532:021E` | Keyboard |
| Razer Ornata V2 | `1532:025D` | Keyboard |
| Razer Ornata V3 | `1532:02A1` | Keyboard |
| Razer Ornata V3 (Alternate) | `1532:028F` | Keyboard |
| Razer Ornata V3 Tenkeyless | `1532:02A3` | Keyboard |
| Razer Ornata V3 X | `1532:0294` | Keyboard |
| Razer Ornata V3 X (Alternate) | `1532:02A2` | Keyboard |
| Razer Tartarus | `1532:0201` | Keyboard |
| Razer Tartarus Chroma | `1532:0208` | Keyboard |
| Razer Tartarus Pro | `1532:0244` | Keyboard |
| Razer Tartarus V2 | `1532:022B` | Keyboard |

### Laptops (48)

| Device | USB VID:PID | Type |
|---|---|---|
| Razer Blade (Late 2016) | `1532:0224` | Laptop |
| Razer Blade (QHD) | `1532:020F` | Laptop |
| Razer Blade 14 (2021) | `1532:0270` | Laptop |
| Razer Blade 14 (2022) | `1532:028C` | Laptop |
| Razer Blade 14 (2023) | `1532:029D` | Laptop |
| Razer Blade 14 (2024) | `1532:02B6` | Laptop |
| Razer Blade 14 (2025) | `1532:02C5` | Laptop |
| Razer Blade 15 (2018) | `1532:0233` | Laptop |
| Razer Blade 15 (2018) Base Model | `1532:023B` | Laptop |
| Razer Blade 15 (2018) Mercury | `1532:0240` | Laptop |
| Razer Blade 15 (2019) Advanced | `1532:023A` | Laptop |
| Razer Blade 15 (2023) | `1532:029E` | Laptop |
| Razer Blade 15 (Mid 2019) Base Model | `1532:0246` | Laptop |
| Razer Blade 15 (Mid 2019) Mercury | `1532:0245` | Laptop |
| Razer Blade 15 Advanced (2020) | `1532:0253` | Laptop |
| Razer Blade 15 Advanced (Early 2021) | `1532:026D` | Laptop |
| Razer Blade 15 Advanced (Early 2022) | `1532:028A` | Laptop |
| Razer Blade 15 Advanced (Mid 2021) | `1532:0276` | Laptop |
| Razer Blade 15 Studio Edition (2019) | `1532:024D` | Laptop |
| Razer Blade 16 (2023) | `1532:029F` | Laptop |
| Razer Blade 16 (2026) | `1532:02E0` | Laptop |
| Razer Blade 17 (2022) | `1532:028B` | Laptop |
| Razer Blade 17 Pro (Early 2021) | `1532:026E` | Laptop |
| Razer Blade 17 Pro (Mid 2021) | `1532:0279` | Laptop |
| Razer Blade 18 (2023) | `1532:02A0` | Laptop |
| Razer Blade 18 (2024) | `1532:02B8` | Laptop |
| Razer Blade 18 (2025) | `1532:02C7` | Laptop |
| Razer Blade 18 (2026) | `1532:02E1` | Laptop |
| Razer Blade Advanced (Late 2019) | `1532:024B` | Laptop |
| Razer Blade Base (Early 2020) | `1532:0255` | Laptop |
| Razer Blade Base (Early 2021) | `1532:026F` | Laptop |
| Razer Blade Base (Early 2022) | `1532:027A` | Laptop |
| Razer Blade Late 2020 Base | `1532:0268` | Laptop |
| Razer Blade Pro (2017) | `1532:0225` | Laptop |
| Razer Blade Pro (Early 2020) | `1532:0256` | Laptop |
| Razer Blade Pro (Late 2016) | `1532:0210` | Laptop |
| Razer Blade Pro (Late 2019) | `1532:024C` | Laptop |
| Razer Blade Pro 17 (2019) | `1532:0234` | Laptop |
| Razer Blade Pro FullHD (2017) | `1532:022F` | Laptop |
| Razer Blade Stealth | `1532:0205` | Laptop |
| Razer Blade Stealth (2019) | `1532:0239` | Laptop |
| Razer Blade Stealth (Early 2020) | `1532:0252` | Laptop |
| Razer Blade Stealth (Late 2016) | `1532:0220` | Laptop |
| Razer Blade Stealth (Late 2017) | `1532:0232` | Laptop |
| Razer Blade Stealth (Late 2019) | `1532:024A` | Laptop |
| Razer Blade Stealth (Late 2020) | `1532:0259` | Laptop |
| Razer Blade Stealth (Mid 2017) | `1532:022D` | Laptop |
| Razer Book (2020) | `1532:026A` | Laptop |

### Accessories (26)

| Device | USB VID:PID | Type |
|---|---|---|
| Lian Li O11 Dynamic - Razer Edition | `1532:0F13` | Accessory |
| Razer Base Station Chroma (Headphone Stand) | `1532:0F08` | Accessory |
| Razer Base Station V2 Chroma | `1532:0F20` | Accessory |
| Razer Charging Pad Chroma | `1532:0F26` | Accessory |
| Razer Chroma Hardware Development Kit (HDK) | `1532:0F09` | Accessory |
| Razer Chroma Mug Holder | `1532:0F07` | Accessory |
| Razer Core | `1532:0215` | Accessory |
| Razer Core X Chroma | `1532:0F1A` | Accessory |
| Razer Firefly (2013) | `1532:0C00` | Mouse pad |
| Razer Firefly Hyperflux (2018) | `1532:0068` | Mouse pad |
| Razer Firefly V2 | `1532:0C04` | Mouse pad |
| Razer Firefly V2 Pro | `1532:0C08` | Mouse pad |
| Razer Goliathus (2018) | `1532:0C01` | Mouse pad |
| Razer Goliathus Chroma 3XL | `1532:0C06` | Mouse pad |
| Razer Goliathus Extended (2018) | `1532:0C02` | Mouse pad |
| Razer Laptop Stand Chroma | `1532:0F0D` | Accessory |
| Razer Laptop Stand Chroma V2 | `1532:0F2B` | Accessory |
| Razer Mouse Bungee V3 Chroma | `1532:0F1D` | Accessory |
| Razer Mouse Dock | `1532:007E` | Accessory |
| Razer Mouse Dock Pro | `1532:00A4` | Accessory |
| Razer Nommo Chroma (Speakers) | `1532:0517` | Accessory |
| Razer Nommo Pro (Speakers) | `1532:0518` | Accessory |
| Razer Raptor 27 | `1532:0F12` | Monitor |
| Razer Strider Chroma | `1532:0C05` | Mouse pad |
| Razer Thunderbolt 4 Dock Chroma | `1532:0F21` | Accessory |
| Razer Tomahawk ATX | `1532:0F17` | Accessory |

## Capability and verification boundaries

Catalog presence means the model is recognized by the pinned backend. The actual controls shown depend on the resolved HID endpoint and supported transactions. Depending on the model, the app may expose device information, battery state, polling rate, DPI and DPI stages, power saving, low-battery warning, lighting brightness and effects, per-zone LEDs, matrix lighting, scroll-wheel settings, keyswitch optimization, Fn-primary behavior, or HyperPolling receiver controls.

Catalog presence does not mean every control has been physically tested on every model. An unresolved or busy endpoint stays read only until the next scan. OpenSynapse does not guess support from a product name or send writes through another HID collection.

## Devices not yet supported

Every device whose own handler exposes commands now has a page, with one exception: Philips Hue (0x0301) is a network bridge rather than a HID device, so it needs its own network integration and a physical bridge.

Wireless mice such as the Viper V4 Pro use protocol 30 over Bluetooth LE instead of the 91-byte feature report. The channel is documented in the [BLE protocol 30 reference](PROTOCOL30_BLE.zh-CN.md): GATT UUIDs, the 8-byte request and 20-byte reply headers, polling rates, DPI payloads, and the connection and retry rules. The protocol and a service with read-back verification live in `RazerBleProtocol30`, `RazerBleSession` and `RazerBleDeviceService`; the page that surfaces them still needs a Bluetooth-connected mouse for acceptance.



The protocol evidence comes from static analysis of the Synapse middleware and only covers commands that appear in a product's own handler. A write still needs capability reads, a read-back, and a recovery path.
The Blade 16 (2026) and Blade 18 (2026) now have standard OpenRazer catalogue lighting entries, but their telemetry, fan, performance, and MUX controls still need a dedicated Blade manifest.


For external lighting integrations, see [Chroma-supported devices](CHROMA_SUPPORTED_DEVICES.md).

Back to [README](../README.md).
