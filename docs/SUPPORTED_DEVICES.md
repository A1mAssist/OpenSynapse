# Supported devices

OpenSynapse creates a page only for a connected device whose Windows HID endpoint and protocol are recognized. The Razer Kiyo camera (`1532:0E03`) is the exception: its page uses Razer's camera DLL instead of HID. The list is grouped by device type, not by protocol or catalogue source.

The current tables contain 389 recognized device IDs: 136 mice, 96 keyboards, 50 laptops, 40 headsets, 8 speakers, 4 microphones, 8 mouse pads, 2 monitors, 44 accessories and 1 network lighting bridge.

Each row is one recognized device ID. A row without a note uses the model's standard control set. Notes mark dedicated pages, extra controls, lighting-only support, read-only probes, or an alternate interface. Some physical devices expose more than one PID; each recognized PID is listed so a Device Manager ID can be matched directly.

Chroma REST is a software integration rather than a device, so it is not listed here.

## Mice

Standard mouse controls include DPI, polling rate, battery, lighting and onboard mapping when the model answers the runtime button-list probe.

| Device | USB VID:PID | Notes |
|---|---|---|
| Razer Abyssus | `1532:0042` | — |
| Razer Abyssus 1800 | `1532:0020` | — |
| Razer Abyssus 2000 | `1532:005E` | — |
| Razer Abyssus Elite (D.Va Edition) | `1532:006A` | — |
| Razer Abyssus Essential | `1532:006B` | — |
| Razer Abyssus Lite | `1532:0074` | DPI, polling, battery, lighting and onboard mapping |
| Razer Abyssus V2 | `1532:005B` | — |
| Razer Atheris (Receiver) | `1532:0062` | — |
| Razer Basilisk | `1532:0064` | — |
| Razer Basilisk Essential | `1532:0065` | — |
| Razer Basilisk Mobile (Receiver) | `1532:00D4` | — |
| Razer Basilisk Mobile (Wired) | `1532:00D3` | — |
| Razer Basilisk Ultimate | `1532:0086` | — |
| Razer Basilisk V2 | `1532:0085` | — |
| Razer Basilisk V3 | `1532:0099` | — |
| Razer Basilisk V3 35K | `1532:00CB` | — |
| Razer Basilisk V3 Pro (Wired) | `1532:00AA` | DPI, polling, battery, lighting and onboard mapping; BLE page over Bluetooth |
| Razer Basilisk V3 Pro (Wireless) | `1532:00AB` | DPI, polling, battery, lighting and onboard mapping; BLE page over Bluetooth |
| Razer Basilisk V3 Pro 35K (Wired) | `1532:00CC` | — |
| Razer Basilisk V3 Pro 35K (Wireless) | `1532:00CD` | — |
| Razer Basilisk V3 Pro 35K Phantom Green Edition (Wired) | `1532:00D6` | — |
| Razer Basilisk V3 Pro 35K Phantom Green Edition (Wireless) | `1532:00D7` | — |
| Razer Basilisk V3 X HyperSpeed | `1532:00B9` | — |
| Razer Basilisk V4 HyperSpeed (Wired) | `1532:00EB` | DPI, polling, battery, lighting and onboard mapping |
| Razer Basilisk V4 HyperSpeed (Wireless) | `1532:00EA` | DPI, polling, battery, lighting and onboard mapping |
| Razer Basilisk V4 Pro (Wired) | `1532:00E2` | DPI, polling, battery, lighting and onboard mapping |
| Razer Basilisk V4 Pro (Wireless) | `1532:00E3` | DPI, polling, battery, lighting and onboard mapping |
| Razer Basilisk X HyperSpeed | `1532:0083` | — |
| Razer Cobra | `1532:00A2` | DPI, polling and onboard mapping via the middleware protocol |
| Razer Cobra | `1532:00A3` | — |
| Razer Cobra HyperSpeed (Wired) | `1532:00DA` | — |
| Razer Cobra HyperSpeed (Wireless) | `1532:00DB` | — |
| Razer Cobra Pro (Wired) | `1532:00AF` | — |
| Razer Cobra Pro (Wireless) | `1532:00B0` | — |
| Razer DeathAdder 1800 | `1532:0038` | — |
| Razer DeathAdder 2000 | `1532:004F` | — |
| Razer DeathAdder 2000 (read-only probe) | `1532:004D` | Read-only probe: firmware, serial, DPI, polling and brightness |
| Razer DeathAdder 2000 (read-only probe) | `1532:005F` | Read-only probe: firmware, serial, DPI, polling and brightness |
| Razer DeathAdder 2013 | `1532:0037` | — |
| Razer DeathAdder 3500 | `1532:0054` | — |
| Razer DeathAdder Chroma | `1532:0043` | — |
| Razer DeathAdder Elite | `1532:005C` | — |
| Razer DeathAdder Essential | `1532:006E` | — |
| Razer DeathAdder Essential (2021) | `1532:0098` | — |
| Razer DeathAdder Essential (White Edition) | `1532:0071` | — |
| Razer DeathAdder V2 | `1532:0084` | — |
| Razer DeathAdder V2 Lite | `1532:00A1` | — |
| Razer DeathAdder V2 Mini | `1532:008C` | — |
| Razer DeathAdder V2 Pro (Wired) | `1532:007C` | — |
| Razer DeathAdder V2 Pro (Wireless) | `1532:007D` | — |
| Razer DeathAdder V2 X HyperSpeed | `1532:009C` | — |
| Razer DeathAdder V3 | `1532:00B2` | — |
| Razer DeathAdder V3 HyperSpeed (Wired) | `1532:00C4` | — |
| Razer DeathAdder V3 HyperSpeed (Wireless) | `1532:00C5` | — |
| Razer DeathAdder V3 Pro (Wired) | `1532:00B6` | — |
| Razer DeathAdder V3 Pro HyperPolling (Wired) | `1532:00C2` | — |
| Razer DeathAdder V3 Pro (Wireless) | `1532:00B7` | — |
| Razer DeathAdder V3 Pro HyperPolling (Wireless) | `1532:00C3` | — |
| Razer DeathAdder V4 Pro (Wired) | `1532:00BE` | — |
| Razer DeathAdder V4 Pro (Wireless) | `1532:00BF` | — |
| Razer DeathAdder V4 Pro Carbon Fiber Edition (Wired) | `1532:00EF` | DPI, polling, battery and onboard mapping; no lighting |
| Razer DeathAdder V4 Pro Carbon Fiber Edition (Wireless) | `1532:00F0` | DPI, polling, battery and onboard mapping; no lighting |
| Razer Diamondback Chroma | `1532:004C` | — |
| Razer HyperFlux V2 | `1532:00CF` | Lighting only |
| Razer HyperPolling Wireless Dongle | `1532:00B3` | — |
| Razer Imperator 2012 | `1532:002F` | — |
| Razer Jugan | `1532:0063` | DPI, polling, battery, lighting and onboard mapping |
| Razer Krakoff | `1532:00DD` | DPI, polling, battery, lighting and onboard mapping |
| Razer Lancehead (Wired) | `1532:0059` | — |
| Razer Lancehead (Wireless) | `1532:005A` | — |
| Razer Lancehead Tournament Edition | `1532:0060` | — |
| Razer Lancehead Wireless (Receiver) | `1532:006F` | — |
| Razer Lancehead Wireless (Wired) | `1532:0070` | — |
| Razer Mamba 2012 (Wired) | `1532:0024` | — |
| Razer Mamba 2012 (Wireless) | `1532:0025` | — |
| Razer Mamba Chroma (Wired) | `1532:0044` | — |
| Razer Mamba Chroma (Wireless) | `1532:0045` | — |
| Razer Mamba Elite | `1532:006C` | — |
| Razer Mamba Hyperflux | `1532:0069` | DPI, polling, battery, lighting and onboard mapping |
| Razer Mamba Tournament Edition | `1532:0046` | — |
| Razer Mamba Wireless (Receiver) | `1532:0072` | — |
| Razer Mamba Wireless (Wired) | `1532:0073` | — |
| Razer Naga | `1532:0015` | — |
| Razer Naga 2012 | `1532:002E` | — |
| Razer Naga 2014 | `1532:0040` | — |
| Razer Naga Chroma | `1532:0053` | — |
| Razer Naga Classic Edition | `1532:0093` | DPI, polling, battery, lighting and onboard mapping |
| Razer Naga Epic | `1532:001F` | — |
| Razer Naga Epic Chroma (Wired) | `1532:003E` | — |
| Razer Naga Epic Chroma (Wireless) | `1532:003F` | — |
| Razer Naga Hex | `1532:0041` | — |
| Razer Naga Hex (Red) | `1532:0036` | — |
| Razer Naga Hex V2 | `1532:0050` | — |
| Razer Naga Left Handed Edition 2020 | `1532:008D` | — |
| Razer Naga Pro (Wired) | `1532:008F` | — |
| Razer Naga Pro (Wireless) | `1532:0090` | — |
| Razer Naga Trinity | `1532:0067` | — |
| Razer Naga V2 HyperSpeed (Receiver) | `1532:00B4` | — |
| Razer Naga V2 Pro (Wired) | `1532:00A7` | — |
| Razer Naga V2 Pro (Wireless) | `1532:00A8` | — |
| Razer Naga V3 Pro (Wired) | `1532:00E7` | DPI, polling, battery, lighting and onboard mapping |
| Razer Naga V3 Pro (Wireless) | `1532:00E8` | DPI, polling, battery, lighting and onboard mapping |
| Razer Naga X | `1532:0096` | — |
| Razer Orochi (Wired) | `1532:0048` | — |
| Razer Orochi 2011 | `1532:0013` | — |
| Razer Orochi 2013 | `1532:0039` | — |
| Razer Orochi V2 | `1532:00E0` | DPI, polling, battery, lighting and onboard mapping |
| Razer Orochi V2 (Bluetooth) | `1532:0095` | — |
| Razer Orochi V2 (Receiver) | `1532:0094` | — |
| Razer Ouroboros | `1532:0032` | — |
| Razer Pro Click (Receiver) | `1532:0077` | — |
| Razer Pro Click (Wired) | `1532:0080` | — |
| Razer Pro Click Mini (Receiver) | `1532:009A` | — |
| Razer Pro Click V2 (Wired) | `1532:00D0` | — |
| Razer Pro Click V2 (Wireless) | `1532:00D1` | — |
| Razer Pro Click V2 Vertical Edition (Wired) | `1532:00C7` | — |
| Razer Pro Click V2 Vertical Edition (Wireless) | `1532:00C8` | — |
| Razer Taipan | `1532:0034` | — |
| Razer Turret Mouse Xbox One Edition | `1532:0075` | DPI, polling, battery, lighting and onboard mapping |
| Razer Viper | `1532:0078` | — |
| Razer Viper 8KHz | `1532:0091` | — |
| Razer Viper Mini | `1532:008A` | — |
| Razer Viper Mini SE (Wired) | `1532:009E` | — |
| Razer Viper Mini SE (Wireless) | `1532:009F` | — |
| Razer Viper Ultimate (Wired) | `1532:007A` | — |
| Razer Viper Ultimate (Wireless) | `1532:007B` | — |
| Razer Viper V2 Pro (Wired) | `1532:00A5` | — |
| Razer Viper V2 Pro (Wireless) | `1532:00A6` | — |
| Razer Viper V3 HyperSpeed | `1532:00B8` | Dedicated Viper page: battery, DPI, polling, sleep timeout and mappings |
| Razer Viper V3 Pro (Wired) | `1532:00C0` | — |
| Razer Viper V3 Pro (Wireless) | `1532:00C1` | — |
| Razer Viper V3 Pro SE (Wired) | `1532:00DE` | Alias of 00C0; dedicated Viper V3 Pro controls |
| Razer Viper V3 Pro SE (Wireless) | `1532:00DF` | Alias of 00C1; dedicated Viper V3 Pro controls |
| Razer Viper V4 Pro (Wired) | `1532:00E5` | DPI, polling, battery and onboard mapping; BLE page over Bluetooth; no lighting |
| Razer Viper V4 Pro (Wireless) | `1532:00E6` | DPI, polling, battery and onboard mapping; BLE page over Bluetooth; no lighting |
| RazerBasiliskUltimateReceiver | `1532:0088` | — |

The Naga V3 Pro (`00E7`/`00E8`) uses the Naga V2 Pro (`00A8`) protocol. The Viper V4 Pro and DeathAdder V4 Pro Carbon Fiber Edition have no lighting. The Viper V3 HyperSpeed uses a dedicated Viper page instead of the standard mouse layout.

## Keyboards

Standard keyboard controls include firmware, serial number, battery, game mode, polling rate and lighting when the model answers them.

| Device | USB VID:PID | Notes |
|---|---|---|
| BlackWidow Chroma V2 | `1532:0221` | — |
| Razer Anansi | `1532:010F` | — |
| Razer BlackWidow (Classic) | `1532:011B` | — |
| Razer BlackWidow 2019 | `1532:0241` | — |
| Razer BlackWidow Chroma | `1532:0203` | — |
| Razer BlackWidow Chroma (Overwatch) | `1532:0211` | — |
| Razer BlackWidow Elite | `1532:0228` | — |
| Razer BlackWidow Essential | `1532:0237` | — |
| Razer BlackWidow Lite | `1532:0235` | — |
| Razer BlackWidow Stealth Edition | `1532:010E` | — |
| Razer BlackWidow Tournament Edition 2014 | `1532:011C` | — |
| Razer BlackWidow Tournament Edition Chroma | `1532:0209` | — |
| Razer BlackWidow Ultimate 2012 | `1532:010D` | — |
| Razer BlackWidow Ultimate 2013 | `1532:011A` | — |
| Razer BlackWidow Ultimate 2016 | `1532:0214` | — |
| Razer BlackWidow V3 | `1532:024E` | — |
| Razer BlackWidow V3 Mini HyperSpeed (Wired) | `1532:0258` | — |
| Razer BlackWidow V3 Mini HyperSpeed (Wireless) | `1532:0271` | — |
| Razer BlackWidow V3 Pro 2.4 Ghz Wireless | `1532:025C` | — |
| Razer BlackWidow V3 Pro Wired | `1532:025A` | — |
| Razer BlackWidow V3 TK | `1532:0A24` | — |
| Razer BlackWidow V4 | `1532:0287` | — |
| Razer BlackWidow V4 75% | `1532:02A5` | — |
| Razer BlackWidow V4 75% Xbox 25th Anniversary Edition | `1532:02F0` | Keyboard page |
| Razer BlackWidow V4 Low-profile HyperSpeed | `1532:02CC` | Keyboard page |
| Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed | `1532:02D4` | Keyboard page |
| Razer BlackWidow V4 Mini HyperSpeed (Wired) | `1532:02B9` | — |
| Razer BlackWidow V4 Mini HyperSpeed (Wireless) | `1532:02BA` | — |
| Razer BlackWidow V4 Pro | `1532:028D` | — |
| Razer BlackWidow V4 Pro 75% | `1532:02B3` | Keyboard page |
| Razer BlackWidow V4 Tenkeyless HyperSpeed (Wired) | `1532:02D7` | — |
| Razer BlackWidow V4 Tenkeyless HyperSpeed (Wireless) | `1532:02D5` | — |
| Razer BlackWidow V4 X | `1532:0293` | — |
| Razer BlackWidow X Chroma | `1532:0216` | — |
| Razer BlackWidow X Tenkeyless | `1532:024F` | Keyboard page |
| Razer BlackWidow X Tournament Edition Chroma | `1532:021A` | — |
| Razer BlackWidow X Ultimate | `1532:0217` | — |
| Razer Cynosa Chroma | `1532:022A` | — |
| Razer Cynosa Chroma Pro | `1532:022C` | — |
| Razer Cynosa Lite | `1532:023F` | — |
| Razer Cynosa V2 | `1532:025E` | — |
| Razer Cynosapro | `1532:028E` | Keyboard page |
| Razer DeathStalker Chroma | `1532:0204` | — |
| Razer DeathStalker Expert | `1532:0202` | — |
| Razer DeathStalker V2 | `1532:0295` | — |
| Razer DeathStalker V2 Pro (Wired) | `1532:0292` | — |
| Razer DeathStalker V2 Pro (Wireless) | `1532:0290` | — |
| Razer DeathStalker V2 Pro TKL (Wired) | `1532:0298` | — |
| Razer DeathStalker V2 Pro TKL (Wireless) | `1532:0296` | — |
| Razer DeathStalker/DeathStalker Essential | `1532:0118` | — |
| Razer Huntsman | `1532:0227` | — |
| Razer Huntsman Elite | `1532:0226` | — |
| Razer Huntsman Essential | `1532:0250` | Keyboard page |
| Razer Huntsman Mini | `1532:0257` | — |
| Razer Huntsman Mini Analog | `1532:0282` | — |
| Razer Huntsman Mini JP | `1532:0269` | — |
| Razer Huntsman Signature Edition | `1532:02D8` | Keyboard page |
| Razer Huntsman Tournament Edition | `1532:0243` | — |
| Razer Huntsman V2 | `1532:026C` | — |
| Razer Huntsman V2 Analog | `1532:0266` | — |
| Razer Huntsman V2 Tenkeyless | `1532:026B` | — |
| Razer Huntsman V3 HE Magnetic Mini 65% 8KHz | `1532:02E4` | Keyboard page |
| Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz | `1532:02EA` | Keyboard page |
| Razer Huntsman V3 Pro | `1532:02A6` | — |
| Razer Huntsman V3 Pro 8KHz | `1532:02CF` | — |
| Razer Huntsman V3 Pro Low-Profile Tenkeyless 8KHz | `1532:02E6` | Keyboard page |
| Razer Huntsman V3 Pro Mini | `1532:02B0` | — |
| Razer Huntsman V3 Pro Mini 8KHz | `1532:02D1` | Keyboard page |
| Razer Huntsman V3 Pro Tenkeyless 8KHz | `1532:02D0` | Keyboard page |
| Razer Huntsman V3 Pro TKL | `1532:02A7` | — |
| Razer Huntsman V3 Tenkeyless 8KHz | `1532:02E5` | Keyboard page |
| Razer Huntsman V3 X Tenkeyless | `1532:02B1` | Keyboard page |
| Razer Joro | `1532:02CD` | — |
| Razer Nostromo | `1532:0111` | — |
| Razer Orbweaver | `1532:0113` | — |
| Razer Orbweaver Chroma | `1532:0207` | — |
| Razer Ornata | `1532:021F` | — |
| Razer Ornata Chroma | `1532:021E` | — |
| Razer Ornata V2 | `1532:025D` | — |
| Razer Ornata V3 | `1532:02A1` | — |
| Razer Ornata V3 (Alternate) | `1532:028F` | — |
| Razer Ornata V3 Tenkeyless | `1532:02A3` | — |
| Razer Ornata V3 X | `1532:0294` | — |
| Razer Ornata V3 X (Alternate) | `1532:02A2` | — |
| Razer Pikachu Limited Edition Backlit Keyboard | `1532:0251` | Keyboard page |
| Razer Pro Type | `1532:0249` | Keyboard page |
| Razer Pro Type Ergo | `1532:02C4` | Keyboard page |
| Razer Pro Type Ultra | `1532:0277` | Keyboard page |
| Razer Reclusa Mini 65% | `1532:02F4` | Keyboard page |
| Razer Reclusa X Mini 65% | `1532:02E3` | Keyboard page |
| Razer Tartarus | `1532:0201` | — |
| Razer Tartarus Chroma | `1532:0208` | — |
| Razer Tartarus Pro | `1532:0244` | — |
| Razer Tartarus V2 | `1532:022B` | — |
| Razer Tartarus V2 Pro | `1532:02EB` | Keyboard page |
| Razer Turret Keyboard Xbox One Edition | `1532:023E` | Keyboard page |

## Laptops

Blade models with a dedicated manifest expose telemetry, performance, fans, display, battery and keyboard lighting. Standard laptop entries expose the model-dependent catalogue controls.

| Device | USB VID:PID | Notes |
|---|---|---|
| Razer Blade (Late 2016) | `1532:0224` | — |
| Razer Blade (QHD) | `1532:020F` | — |
| Razer Blade 14 (2021) | `1532:0270` | — |
| Razer Blade 14 (2022) | `1532:028C` | Dedicated Blade controls; fixed fan 3500-5400 RPM in Custom mode |
| Razer Blade 14 (2023) | `1532:029D` | Dedicated Blade controls |
| Razer Blade 14 (2024) | `1532:02B6` | Dedicated Blade controls |
| Razer Blade 14 (2025) | `1532:02C5` | Dedicated Blade controls |
| Razer Blade 15 (2018) | `1532:0233` | — |
| Razer Blade 15 (2018) Base Model | `1532:023B` | — |
| Razer Blade 15 (2018) Mercury | `1532:0240` | — |
| Razer Blade 15 (2019) Advanced | `1532:023A` | — |
| Razer Blade 15 (2023) | `1532:029E` | — |
| Razer Blade 15 (Mid 2019) Base Model | `1532:0246` | — |
| Razer Blade 15 (Mid 2019) Mercury (read-only probe) | `1532:0245` | Blade probe controls: telemetry, fan, power, charge limit, keyboard lighting and gaming mode |
| Razer Blade 15 Advanced (2020) | `1532:0253` | — |
| Razer Blade 15 Advanced (Early 2021) | `1532:026D` | — |
| Razer Blade 15 Advanced (Early 2022) | `1532:028A` | — |
| Razer Blade 15 Advanced (Mid 2021) | `1532:0276` | — |
| Razer Blade 15 Studio Edition (2019) | `1532:024D` | — |
| Razer Blade 16 (2023) | `1532:029F` | Dedicated Blade controls; firmware-rendered keyboard effects |
| Razer Blade 16 (2024) | `1532:02B7` | Dedicated Blade controls |
| Razer Blade 16 (2025) | `1532:02C6` | Dedicated Blade controls |
| Razer Blade 16 (2026) | `1532:02E0` | Dedicated Blade controls; native keyboard effects and software frame pump; MUX follows the 0D:8A capability bits (DDS only when reported) |
| Razer Blade 17 (2022) | `1532:028B` | — |
| Razer Blade 17 Pro (Early 2021) | `1532:026E` | — |
| Razer Blade 17 Pro (Mid 2021) | `1532:0279` | — |
| Razer Blade 18 (2023) | `1532:02A0` | — |
| Razer Blade 18 (2024) | `1532:02B8` | Dedicated Blade controls |
| Razer Blade 18 (2025) | `1532:02C7` | Dedicated Blade controls |
| Razer Blade 18 (2026) | `1532:02E1` | Dedicated Blade controls; native keyboard effects and software frame pump; MUX follows the 0D:8A capability bits (DDS only when reported) |
| Razer Blade Advanced (Late 2019) | `1532:024B` | — |
| Razer Blade Base (Early 2020) | `1532:0255` | Dedicated Blade controls; percentage fan target is not exposed as RPM |
| Razer Blade Base (Early 2021) | `1532:026F` | — |
| Razer Blade Base (Early 2022) | `1532:027A` | — |
| Razer Blade Late 2020 Base | `1532:0268` | — |
| Razer Blade Pro (2017) | `1532:0225` | — |
| Razer Blade Pro (Early 2020) | `1532:0256` | — |
| Razer Blade Pro (Late 2016) | `1532:0210` | — |
| Razer Blade Pro (Late 2019) | `1532:024C` | — |
| Razer Blade Pro 17 (2019) | `1532:0234` | — |
| Razer Blade Pro FullHD (2017) | `1532:022F` | — |
| Razer Blade Stealth | `1532:0205` | — |
| Razer Blade Stealth (2019) | `1532:0239` | — |
| Razer Blade Stealth (Early 2020) | `1532:0252` | — |
| Razer Blade Stealth (Late 2016) | `1532:0220` | — |
| Razer Blade Stealth (Late 2017) | `1532:0232` | — |
| Razer Blade Stealth (Late 2019) | `1532:024A` | — |
| Razer Blade Stealth (Late 2020) | `1532:0259` | — |
| Razer Blade Stealth (Mid 2017) | `1532:022D` | — |
| Razer Book (2020) | `1532:026A` | — |

Blade 16 (2023) exposes firmware-rendered Off, Breathing, Spectrum and Wave effects and has no per-key matrix channel. The Blade 15 Advanced (2019, `023A`) uses the inherited 6x16 `03:0A` effect and `03:0B` frame path. The 2026 Blade 16/18 bundles align with P23/P24 for thermal state, fixed fan speed, the software fan curve, charge limit, power mode, adapter power and VGA MUX. Their native keyboard lighting uses the middleware's 03:0A firmware-effect report and 03:0B software-frame report. MUX writes follow the 0D:8A capability bits; DDS is exposed only when the unit reports DynamicDisplaySwitch, otherwise only Hybrid/Discrete are offered. The 2026 deviceFeatures do not list the maximum-fan switch, so Max Fan remains hidden. The Blade 18 (2026) bundle labels wire value `Gaming` (0x01) as Turbo in its UI, so OpenSynapse shows the same display alias and does not publish a second mode. The Blade 16 (2026) HyperBoost and fan reads were checked on one physical unit; the Blade 18 (2026) remains bundle-aligned.

## Headsets

The audio page covers EQ, microphone volume, sidetone, ANC, THX and game/chat balance where the model and installed components support them.

| Device | USB VID:PID | Notes |
|---|---|---|
| Razer Barracuda | `1532:053C` | Not shown: no bundle evidence for the protocol family |
| Razer Barracuda 2.4 | `1532:053B` | Audio page |
| Razer Barracuda 2.4 | `1532:0584` | Audio page |
| Razer Barracuda Pro | `1532:0539` | Audio page |
| Razer Barracuda Pro | `1532:0583` | Audio page |
| Razer Barracuda V2 Pro | `1532:0593` | Audio page (battery/charging); THX V4 installer |
| Razer BlackShark V2 HyperSpeed | `1532:0565` | Not shown: no bundle evidence for the protocol family |
| Razer BlackShark V2 HyperSpeed | `1532:056E` | Audio page |
| Razer BlackShark V2 Pro (2020) | `1532:0528` | Audio page; THX installer |
| Razer BlackShark V2 Pro (2023) | `1532:0555` | Audio page; THX installer |
| Razer BlackShark V3 | `1532:0579` | Audio page |
| Razer BlackShark V3 Pro (0x0576 interface) | `1532:0576` | Audio page; wired BlackShark V3 Pro interface |
| Razer BlackShark V3 Pro (2.4 GHz) | `1532:101B` | Audio, ANC, THX and game/chat balance (2.4 GHz) |
| Razer BlackShark V3 Pro (USB) | `1532:101C` | Audio, ANC, THX and game/chat balance (USB) |
| Razer Kraken 7.1 | `1532:0501` | Lighting only |
| Razer Kraken 7.1 | `1532:0506` | Lighting only |
| Razer Kraken 7.1 Chroma | `1532:0504` | Lighting only |
| Razer Kraken 7.1 V2 | `1532:0510` | Lighting only |
| Razer Kraken BT Kitty Edition | `1532:0530` | BLE page: battery/charging, volume, sidetone, game mode, bass, haptics, speaker/mic EQ, auto power-off and Chroma |
| Razer Kraken BT Sanrio Limited Edition | `1532:0309` | BLE page: battery/charging, volume, sidetone, game mode, bass, haptics, speaker/mic EQ, auto power-off and Chroma |
| Razer Kraken BT Sanrio Limited Edition | `1532:0534` | BLE page: battery/charging, volume, sidetone, game mode, bass, haptics, speaker/mic EQ, auto power-off and Chroma |
| Razer Kraken Kitty Edition | `1532:0521` | Audio page |
| Razer Kraken Kitty V2 | `1532:0560` | Audio page plus lighting |
| Razer Kraken Kitty V2 BT Quartz Edition | `1532:0562` | BLE page: battery/charging, volume, sidetone, game mode, bass, haptics, speaker/mic EQ, auto power-off and Chroma |
| Razer Kraken Kitty V2 Pro | `1532:0554` | Audio page |
| Razer Kraken Kitty V3 Pro | `1532:0587` | Audio page (EQ/battery/charging); THX V4 installer |
| Razer Kraken Tournament Edition | `1532:0520` | Audio page plus lighting; THX installer for Kraken TE |
| Razer Kraken Ultimate | `1532:0527` | Audio page plus lighting; THX installer |
| Razer Kraken V3 | `1532:0549` | Audio page |
| Razer Kraken V3 HyperSense | `1532:0533` | Audio page |
| Razer Kraken V3 Pro | `1532:052D` | Not shown: serial audio transport not implemented |
| Razer Kraken V3 X | `1532:0537` | Audio page |
| Razer Kraken V4 | `1532:056B` | Lighting page (wired) |
| Razer Kraken V4 | `1532:056C` | Lighting page (2.4 GHz receiver) |
| Razer Kraken V4 Pro | `1532:0567` | Audio page |
| Razer Kraken V4 X | `1532:056D` | Audio page |
| Razer Kraken X USB | `1532:0526` | Audio page |
| Razer Marci | `1532:0F66` | Audio page |
| Razer Nari | `1532:051C` | Audio page |
| Razer Nari Essential | `1532:051E` | Audio page; THX installer |
| Razer Nari Ultimate | `1532:051A` | Audio page |

Kraken 7.1 uses the 37-byte output-report path; Kraken V4 wired and its 2.4 GHz receiver (`056C`) use the 64-byte report-0x02 path. The receiver sets byte 9 to `0x80`; the wired model uses `0x00`.

## Speakers

Speaker controls cover EQ presets, bands, bass, input source and lighting where available.

| Device | USB VID:PID | Notes |
|---|---|---|
| Razer Leviathan V2 | `1532:0532` | Audio page |
| Razer Leviathan V2 Pro | `1532:0548` | Audio page |
| Razer Leviathan V2 X | `1532:054A` | Soundbar audio and lighting; physical validation still pending |
| Razer Nommo Chroma | `1532:0517` | Chroma lighting |
| Razer Nommo Pro | `1532:0518` | Chroma lighting |
| Razer Nommo V2 | `1532:055C` | Audio page |
| Razer Nommo V2 Pro | `1532:055A` | Audio page |
| Razer Nommo V2 X | `1532:055E` | Audio page |

Leviathan V2 X (`054A`) covers edition IDs 0 (base), 128 (Pikachu), 129 (Pokémon AP), 131 (Kuromi) and 132 (Kuromi Edition); the app distinguishes Pikachu and Kuromi units by reading edition information.

## Microphones

Microphone controls cover EQ, microphone volume and LED effects where available.

| Device | USB VID:PID | Notes |
|---|---|---|
| Razer Seiren Emote | `1532:0F1B` | Audio page plus 8x8 solid-color and clear frames over the four-row 0x0F:0x03 protocol; arbitrary per-pixel editing is not exposed |
| Razer Seiren V2 Pro | `1532:0542` | Audio page |
| Razer Seiren V2 X | `1532:0543` | Audio page |
| Razer Seiren V3 Mini | `1532:056A` | Audio page |

## Mouse pads

Mouse pads expose their model-dependent Chroma lighting controls.

| Device | USB VID:PID | Notes |
|---|---|---|
| Razer Firefly (2013) | `1532:0C00` | — |
| Razer Firefly Hyperflux (2018) | `1532:0068` | — |
| Razer Firefly V2 | `1532:0C04` | — |
| Razer Firefly V2 Pro | `1532:0C08` | — |
| Razer Goliathus (2018) | `1532:0C01` | — |
| Razer Goliathus Chroma 3XL | `1532:0C06` | — |
| Razer Goliathus Extended (2018) | `1532:0C02` | — |
| Razer Strider Chroma | `1532:0C05` | — |

## Monitors

The Raptor 27 exposes its model-dependent Chroma lighting controls.

| Device | USB VID:PID | Notes |
|---|---|---|
| Razer Raptor 27 | `1532:0F12` | Chroma lighting |
| Razer Raptor 27 | `1532:0F28` | Chroma lighting |

## Gamepads

Gamepad configuration uses the 91-byte, report-id-4 feature transport. Analog sticks and triggers, vibration, D-pad, SOCD and button-assignment reads are exposed; a model that does not answer a command hides that row.

| Device | USB VID:PID | Notes |
|---|---|---|
| Razer Kitsune | `1532:1013` | SOCD and button assignments; no analog sticks or triggers |
| Razer Raiju V3 Pro Signature Edition | `1532:1030` | — |
| Razer Wolverine V3 Pro | `1532:0A4C` | — |
| Razer Wolverine V3 Pro PC | `1532:0A57` | — |
| Razer Wolverine V3 Pro Playstation | `1532:1025` | — |
| Razer Wolverine V3 TE PC | `1532:0A5A` | — |
| Razer Wolverine V3 Tournament Edition | `1532:0A45` | — |
| Razer Wolverine V4 Pro | `1532:0A74` | — |
| Razer Wolverine V4 Tournament Edition | `1532:0A7C` | — |

## Accessories

Accessory controls cover lighting, docks, stands, fan control and presence signaling.

| Device | USB VID:PID | Notes |
|---|---|---|
| Esther | `1532:0F45` | Lighting only |
| Lian Li O11 Dynamic - Razer Edition | `1532:0F13` | — |
| MarciT2H | `1532:0F6D` | Lighting only |
| Razer Aether Lamp | `1532:030E` | Lighting only |
| Razer Aether Lamp Pro | `1532:030D` | Lighting only |
| Razer Aether Light Bulb | `1532:030F` | Lighting only |
| Razer Aether Light Strip | `1532:0310` | Lighting only |
| Razer Aether Monitor Light Bar | `1532:0316` | Lighting only |
| Razer Aether Standing Light Bars | `1532:0317` | Lighting only |
| Razer Audio Mixer | `1532:053E` | Dedicated HID subset: firmware/serial/device mode, fader and mute status, channel/mic mute, brightness; faders/routing/EQ stay in Razer's native mixer |
| Razer Base Station Chroma (Headphone Stand) | `1532:0F08` | — |
| Razer Base Station V2 Chroma | `1532:0F20` | — |
| Razer Base Station V3 Chroma | `1532:0F6A` | Lighting only |
| Razer Charging Pad Chroma | `1532:0F26` | — |
| Razer Chroma Addressable RGB Controller | `1532:030A` | Standard Chroma lighting |
| Razer Chroma Addressable RGB Controller | `1532:0F1F` | Per-LED ARGB control; 6 ports x 80 LEDs |
| Razer Chroma Hardware Development Kit (HDK) | `1532:0F09` | — |
| Razer Chroma Mug Holder | `1532:0F07` | — |
| Razer Chroma Wireless ARGB Controller | `1532:0F2C` | Standard Chroma lighting |
| Razer Chroma Wireless ARGB Controller | `1532:0F2E` | Standard Chroma lighting |
| Razer Core | `1532:0215` | — |
| Razer Core X Chroma | `1532:0F1A` | — |
| Razer Core X V2 | `1532:0F51` | Lighting only |
| Razer Hanbo AIO | `1532:0F35` | Lighting only |
| Razer Handheld Dock Chroma | `1532:0F5C` | Lighting only |
| Razer Head Cushion Chroma | `1532:0F36` | Lighting only |
| Razer Key Light Chroma | `1532:030C` | Lighting only |
| Razer Laptop Cooling Pad | `1532:0F43` | Standalone fan page: current/target RPM with read-back, fixed low/mid/high presets, smart quiet/balanced/performance curves, and lighting; still gates HyperBoost |
| Razer Laptop Monitor Stand Chroma | `1532:0F5A` | Lighting only |
| Razer Laptop Stand Chroma | `1532:0F0D` | — |
| Razer Laptop Stand Chroma V2 | `1532:0F2B` | — |
| Razer Monitor Stand Chroma | `1532:0F59` | Lighting only |
| Razer Mouse Bungee V3 Chroma | `1532:0F1D` | — |
| Razer Mouse Dock | `1532:007E` | — |
| Razer Mouse Dock Pro | `1532:00A4` | — |
| Razer Mouse Dock V2 Pro | `1532:00F1` | Lighting only |
| Razer PWM PC Fan Controller | `1532:0F3C` | Lighting only |
| Razer Soma Chroma | `1532:0F64` | Lighting only |
| Razer Thunderbolt 4 Dock Chroma | `1532:0F21` | — |
| Razer Thunderbolt 5 Dock Chroma | `1532:0F52` | Lighting only |
| Razer Tomahawk ATX | `1532:0F17` | — |
| Razer Tomahawk Gaming Desktop | `1532:0F27` | Lighting only |
| Razer USB Sound Card | `1532:0529` | Audio controller page; sound-card controls |
| Razer Wireless Control Pod | `1532:0566` | Lighting only |

## Network lighting

Philips Hue is controlled over the bridge HTTPS API after pairing.

| Device | USB VID:PID | Notes |
|---|---|---|
| Philips Hue Bridge | `Network` | Bridge pairing; on/off, brightness and colour |

## Capability and verification boundaries

A listed ID means a code path exists for that device. It does not mean every control has been tested on physical hardware. An unresolved or busy endpoint stays read-only until the next scan. Writes are read back where the protocol allows, and a failed write restores the previous value when the device exposes one.

The THX V3/V4 headset path and parts of the middleware-derived device set are built from native clients and protocol evidence; hardware acceptance is still required where noted. BLE devices must expose Razer protocol 30 service `52401523`; the BLE page targets protocol 30 mice such as the Basilisk V3 Pro and Viper V4 Pro, with battery, polling, DPI, lighting and button mapping where the device answers those commands.

For external lighting integrations, see [Chroma-supported devices](CHROMA_SUPPORTED_DEVICES.md).

Back to [README](../README.md).
