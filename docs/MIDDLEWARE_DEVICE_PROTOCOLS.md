# 中间件设备协议参考

<!-- English summary: this reference lists the HID commands each Razer product
     actually calls from its own middleware handler chunk, grouped into 33 command
     profiles covering 119 products. Use it to decide whether a feature is
     implementable for a device the OpenRazer catalogue does not cover, and which
     commandClass:commandId pairs it needs. The tables are in Chinese. -->

这张表来自对 Razer Synapse 中间件包的静态分析，列出每个产品在**自己的 handler chunk** 里实际调用的 HID 命令。

用途：OpenRazer 目录覆盖不到的设备，可以从这里判断某个功能能不能实现、用哪组 `commandClass:commandId`。

## 读法

- 命令写作 `class:commandId`，十进制。`dataSize` 是负载长度上限。
- **profile** 表示一组命令集完全相同的产品，可以共用一套实现。
- **上游** 列 `Y` 表示 OpenRazer 上游已收录该设备，只是 OpenSynapse 的目录还没同步到那个提交。

## 限制

- 纯静态文本分析，不执行 JavaScript。
- 只收录出现在产品**自身 handler** 里的命令。共享设备类库（`rzDevice25`、`rzDevice30` 等）里定义的命令不算——库里定义了不代表这台设备支持。
- 命令存在不等于硬件支持。写入前仍需读能力位、写后读回、准备恢复路径。
- `dataSize` 是从命令常量推出的上限，实际响应可能更短。
- 不是所有功能都走 HID。中间件把这些交给原生 DLL：Philips Hue 的桥发现与配对（`DiscoverBridges` / `PairBridge` / `GetConnectedBridgeInfo`）、THX 音效里的侧音（sidetone）、麦克风压缩器、齿音消除、混响、限制器、HRTF。这类条目即使出现在 profile 表里，也没有可解析的报文，不能按 HID 命令实现。

## 设备与 profile

| VID:PID | 设备 | 命令数 | 上游 | profile |
|---|---|---:|:---:|:---:|

| `1532:051A` | Razer Nari Ultimate | 135 |  | P1 |
| `1532:051C` | Razer Nari | 135 |  | P1 |
| `1532:051E` | Razer Nari | 135 |  | P1 |
| `1532:0520` | RAZER USB AUDIO CONTROLLER | 135 | Y | P1 |
| `1532:0521` | Razer Kraken Kitty Edition | 135 |  | P1 |
| `1532:0527` | Razer Kraken Ultimate | 135 | Y | P1 |
| `1532:0528` | Razer BlackShark V2 Pro | 135 |  | P1 |
| `1532:0529` | Razer USB Sound Card | 135 |  | P1 |
| `1532:052D` | Razer Kraken V3 Pro | 135 |  | P1 |
| `1532:0533` | Razer Kraken V3 HyperSense | 135 |  | P1 |
| `1532:0539` | Razer Barracuda Pro | 135 |  | P1 |
| `1532:053B` | Razer Barracuda 2.4 | 135 |  | P1 |
| `1532:053E` | Razer Audio Mixer | 135 |  | P1 |
| `1532:0549` | Razer Kraken V3 | 135 |  | P1 |
| `1532:0554` | Razer Kraken Kitty V2 Pro | 135 |  | P1 |
| `1532:0555` | Razer BlackShark V2 Pro | 135 |  | P1 |
| `1532:0567` | Razer Kraken V4 Pro | 135 |  | P1 |
| `1532:056B` | Razer Kraken V4 | 135 |  | P1 |
| `1532:0075` | Razer Turret Mouse Xbox One Edition | 50 |  | P2 |
| `1532:00B8` | Razer Viper V3 HyperSpeed | 50 | Y | P2 |
| `1532:00CF` | Razer HyperFlux V2 | 50 |  | P2 |
| `1532:00DD` | Razer Krakoff | 50 |  | P2 |
| `1532:0316` | RAZER AETHER MONITOR LIGHT BAR | 50 |  | P2 |
| `1532:0542` | Razer Seiren V2 Pro | 50 |  | P2 |
| `1532:0548` | Razer Leviathan V2 Pro | 50 |  | P2 |
| `1532:054A` | Razer \| Pokémon Pikachu Edition Soundbar | 50 |  | P2 |
| `1532:056A` | Razer Seiren V3 Mini | 50 |  | P2 |
| `1532:056D` | Razer Kraken V4 X | 50 |  | P2 |
| `1532:0309` | RAZER KRAKEN BT SANRIO LIMITED EDITION | 133 |  | P3 |
| `1532:0526` | Razer Kraken X USB | 133 |  | P3 |
| `1532:0530` | Razer Kraken BT Kitty Edition | 133 |  | P3 |
| `1532:0532` | Razer Leviathan V2 | 133 |  | P3 |
| `1532:0534` | RAZER KRAKEN BT SANRIO LIMITED EDITION | 133 |  | P3 |
| `1532:0537` | Razer Kraken V3 X | 133 |  | P3 |
| `1532:0560` | Razer Kraken Kitty V2 | 133 | Y | P3 |
| `1532:0562` | Razer Kraken Kitty V2 BT Quartz Edition | 133 |  | P3 |
| `1532:0063` | Razer Jugan | 47 |  | P4 |
| `1532:0074` | Razer Abyssus Lite | 47 |  | P4 |
| `1532:030C` | Razer Key Light Chroma | 47 |  | P4 |
| `1532:030D` | Razer Aether Lamp Pro | 47 |  | P4 |
| `1532:030E` | Razer Aether Lamp | 47 |  | P4 |
| `1532:030F` | Razer Aether Light Bulb | 47 |  | P4 |
| `1532:0310` | Razer Aether Light Strip | 47 |  | P4 |
| `1532:0317` | Razer Aether Standing Light Bars | 47 |  | P4 |
| `1532:0069` | Razer Mamba Hyperflux | 171 |  | P5 |
| `1532:0093` | Razer Naga Classic Edition | 171 |  | P5 |
| `1532:00DE` | Razer Viper V3 Pro SE | 171 | Y | P5 |
| `1532:00E0` | Razer Orochi V2 | 171 |  | P5 |
| `1532:00EF` | Razer DeathAdder V4 Pro Carbon Fiber Edition | 171 |  | P5 |
| `1532:030A` | RAZER CHROMA ADDRESSABLE RGB CONTROLLER | 171 |  | P5 |
| `1532:0566` | Razer WIRELESS CONTROL POD | 171 |  | P5 |
| `1532:02D0` | Razer Huntsman V3 Pro Tenkeyless 8KHZ | 159 |  | P6 |
| `1532:02D1` | Razer Huntsman V3 Pro Mini 8KHz | 159 |  | P6 |
| `1532:02D8` | Razer Huntsman Signature Edition | 159 |  | P6 |
| `1532:02E4` | Razer Huntsman V3 HE Magnetic Mini 65% 8KHz | 159 |  | P6 |
| `1532:02E5` | Razer Huntsman V3 Tenkeyless 8KHZ | 159 |  | P6 |
| `1532:02EA` | Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz | 159 |  | P6 |
| `1532:0249` | Razer Pro Type | 80 |  | P7 |
| `1532:0251` | Razer Pokémon Pikachu Limited Edition Backlit Keyboard | 80 |  | P7 |
| `1532:0277` | Razer Pro Type Ultra | 80 |  | P7 |
| `1532:028E` |  | 80 |  | P7 |
| `1532:02CD` | Razer Joro | 80 | Y | P7 |
| `1532:02F0` | Razer BlackWidow V4 75% XBOX 25 Anniversary Edition | 80 |  | P7 |
| `1532:0F1F` | RAZER CHROMA ADDRESSABLE RGB CONTROLLER | 22 | Y | P8 |
| `1532:0F28` | Razer Raptor 27 (165Hz) | 22 |  | P8 |
| `1532:0F2C` | Razer Chroma Wireless ARGB Controller | 22 |  | P8 |
| `1532:0F35` | RAZER HANBO AIO | 22 |  | P8 |
| `1532:0F3C` | RAZER PWM PC FAN CONTROLLER | 22 |  | P8 |
| `1532:0F64` | Razer Soma Chroma | 22 |  | P8 |
| `1532:0F45` | Esther | 142 |  | P9 |
| `1532:0F52` | Razer Thunderbolt 5 Dock Chroma | 142 |  | P9 |
| `1532:0F59` | Razer Monitor Stand Chroma | 142 |  | P9 |
| `1532:0F5C` | Razer Handheld Dock Chroma | 142 |  | P9 |
| `1532:0F6D` | MarciT2H | 142 |  | P9 |
| `1532:0F1B` |  | 20 |  | P10 |
| `1532:0F27` | Tomahawk Gaming Desktop | 20 |  | P10 |
| `1532:0F36` | Razer Head Cushion Chroma | 20 |  | P10 |
| `1532:0F51` | Razer Core X V2 | 20 |  | P10 |
| `1532:0F6A` | Razer Base Station V3 Chroma | 20 |  | P10 |
| `1532:02B1` | Razer Huntsman V3 X Tenkeyless | 159 |  | P11 |
| `1532:02B3` | Blackwidow V4 Pro 75% | 159 |  | P11 |
| `1532:02CC` | Razer BlackWidow V4 Low-profile HyperSpeed | 159 |  | P11 |
| `1532:02D4` | Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed | 159 |  | P11 |
| `1532:023E` | Turret Keyboard Xbox One Edition | 157 |  | P12 |
| `1532:024F` | Razer BlackWidow X: Tenkeyless | 157 |  | P12 |
| `1532:0250` | Razer Huntsman Essential | 157 |  | P12 |
| `1532:00C7` | Razer Pro Click V2 Vertical Edition | 108 | Y | P13 |
| `1532:00CB` | Razer Basilisk V3 35K | 108 | Y | P13 |
| `1532:00D0` | Razer Pro Click V2 | 108 | Y | P13 |
| `1532:02B6` | Razer Blade 14 | 105 | Y | P14 |
| `1532:02B7` | Razer Blade 16 | 105 |  | P14 |
| `1532:02B8` | Razer Blade 18 | 105 | Y | P14 |
| `1532:0255` | Razer Blade 15 Base Model | 102 | Y | P15 |
| `1532:029D` | Razer Blade 14 | 102 | Y | P15 |
| `1532:029F` | Razer Blade 16 | 102 | Y | P15 |
| `1532:02C5` | Razer Blade 14 | 99 | Y | P16 |
| `1532:02C6` | Razer Blade 16 | 99 | Y | P16 |
| `1532:02C7` | Razer Blade 18 | 99 | Y | P16 |
| `1532:055A` | Razer Nommo V2 Pro | 129 |  | P17 |
| `1532:055C` | Razer Nommo V2 | 129 |  | P17 |
| `1532:02C4` | Razer Pro Type Ergo | 81 |  | P18 |
| `1532:02E6` | RAZER HUNTSMAN V3 PRO LOW-PROFILE TENKEYLESS 8KHZ | 81 |  | P18 |
| `1532:00E2` | Razer Basilisk V4 Pro | 61 |  | P19 |
| `1532:00EB` | Razer Basilisk V4 HyperSpeed | 61 |  | P19 |
| `1532:00DA` | Razer Cobra HyperSpeed | 48 | Y | P20 |
| `1532:0543` | Razer Seiren V2 X | 48 |  | P20 |
| `1532:00F1` | Razer Mouse Dock V2 Pro | 169 |  | P21 |
| `1532:00E5` | Razer Viper V4 Pro | 167 |  | P22 |
| `1532:02E1` | Razer Blade 18 | 158 | Y | P23 |
| `1532:02E0` | Razer Blade 16 | 156 | Y | P24 |
| `1532:02E3` | Razer Reclusa X Mini 65% | 155 |  | P25 |
| `1532:0F5A` | Razer Laptop Monitor Stand Chroma | 140 |  | P26 |
| `1532:02EB` | Tartarus V2 Pro | 135 |  | P27 |
| `1532:055E` | Razer Nommo V2 X | 128 |  | P28 |
| `1532:02F4` | Razer Reclusa Mini 65% | 123 |  | P29 |
| `1532:00E7` | RAZER NAGA V3 PRO | 45 |  | P30 |
| `1532:0301` | RAZER PHILIPS HUE | 42 |  | P31 |
| `1532:0F66` | Razer Marci | 26 |  | P32 |
| `1532:0F43` | RAZER LAPTOP COOLING PAD | 16 |  | P33 |

## 各 profile 的命令

### P1 — 18 设备 / 133 命令

设备：`051A` Razer Nari Ultimate、`051C` Razer Nari、`051E` Razer Nari、`0520` RAZER USB AUDIO CONTROLLER、`0521` Razer Kraken Kitty Edition、`0527` Razer Kraken Ultimate、`0528` Razer BlackShark V2 Pro、`0529` Razer USB Sound Card、`052D` Razer Kraken V3 Pro、`0533` Razer Kraken V3 HyperSense、`0539` Razer Barracuda Pro、`053B` Razer Barracuda 2.4、`053E` Razer Audio Mixer、`0549` Razer Kraken V3、`0554` Razer Kraken Kitty V2 Pro、`0555` Razer BlackShark V2 Pro、`0567` Razer Kraken V4 Pro、`056B` Razer Kraken V4

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:00` | 25 | Get Sidetone Volume |
| `00:01` | 150 | Set Mic Preset EQ Index |
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:10` | 149 | Set Customer EQ Band |
| `00:11` | 1 | Set Device Reset |
| `00:129` | 2 | Get Firmware Version |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:145` | 2 | Get Dock Firmware Version |
| `00:146` | 22 | Get Dock Serial Number |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `04:00` | 253 | Get Link Status |
| `04:02` | 253 | Get Haptics Control |
| `04:07` | 253 | Get Extended Firmware Version |
| `04:18` | 255 | Set Silence Detection Control |
| `07:08` | 3 | Set Auto Sleep Mode |
| `07:128` | 2 | Get Battery Level |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 1 | Get Auto Sleep Mode |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:08` | 2 | Set mic Mute State |
| `08:09` | 2 | Set Mute State |
| `08:10` | 2 | Set Audio Source |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:22` | 3 | Set Audio input control State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:25` | 2 | Set Combo Mode |
| `08:26` | 9 | Set Parametric EQ Setting |
| `08:28` | 2 | Set AI Noise Suppression |
| `08:29` | 9 | Set Mic Compressor |
| `08:30` | 2 | Set Mic De-Esser |
| `08:31` | 3 | Set Mic Reverb |
| `08:32` | 2 | Set Mic Limiter |
| `08:34` | 2 | Set Return Switch Status |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:136` | 2 | Get Mic Mute State |
| `08:137` | 2 | Get Mute State |
| `08:138` | 2 | Get Audio Source |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:150` | 3 | Get Audio input control State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `08:154` | 9 | Get Parametric EQ Setting |
| `08:156` | 2 | Get AI Noise Suppression |
| `08:157` | 9 | Get Mic Compressor |
| `08:158` | 2 | Get Mic De-Esser |
| `08:159` | 3 | Get Mic Reverb |
| `08:160` | 2 | Get Mic Limiter |
| `08:162` | 2 | Get Return Switch Status |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:18` | 3 | Set Audio Chroma display Switch |
| `15:19` | 4 | Set Chroma Tap |
| `15:130` | 80 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:146` | 3 | Get Audio Chroma display Switch |
| `15:147` | 4 | Get Chroma Tap |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 11 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:128` | 4 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:141` | 2 | Get OLED Home Screen Active Item |
| `23:143` | 22 | Get OLED Image GUID |
| `23:144` | 7 | Get OLED Image Check Sum |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `24:05` | 2 | Set Audio DND Status |
| `24:06` | 3 | Set ANC Status and Level |
| `24:15` | 2 | Set Capture Noise Reduction |
| `24:16` | 3 | Set Audio Voice Prompts Status |
| `24:21` | 2 | Set Audio DND Status |
| `24:30` | 2 | Set Audio Auto Pause Status |
| `24:34` | 2 | Set Audio Gesture Control |
| `24:128` | 2 | Get EQ Preset |
| `24:133` | 1 | Get Audio DND Status |
| `24:134` | 1 | Get ANC Status and Level |
| `24:143` | 2 | Get Capture Noise Reduction |
| `24:144` | 2 | Get Audio Voice Prompts Status |
| `24:154` | 1 | Get Audio Connection Status |
| `24:158` | 1 | Get Audio Auto Pause Status |
| `24:161` | 0 | Get THX Activation Code |
| `24:162` | 2 | Get Audio Gesture Control |

### P2 — 10 设备 / 48 命令

设备：`0075` Razer Turret Mouse Xbox One Edition、`00B8` Razer Viper V3 HyperSpeed、`00CF` Razer HyperFlux V2、`00DD` Razer Krakoff、`0316` RAZER AETHER MONITOR LIGHT BAR、`0542` Razer Seiren V2 Pro、`0548` Razer Leviathan V2 Pro、`054A` Razer \| Pokémon Pikachu Edition Soundbar、`056A` Razer Seiren V3 Mini、`056D` Razer Kraken V4 X

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:130` | 22 | Get Serial Number |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:187` | 1 | Get Device On Dock Charging Status |
| `01:131` | 0 | Get Serial Number |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `02:12` | 80 | Set Single Button Assignment |
| `02:20` | 2 | Set Scroll Free-Notched Mode |
| `02:21` | 5 | Set Profile Debounce Time |
| `02:140` | 80 | Get Single Button Assignment |
| `02:148` | 2 | Get Scroll Free-Notched Mode |
| `02:149` | 5 | Get Profile Debounce Time |
| `03:06` | 0 | Delete Profile |
| `05:128` | 1 | Get Number of Profiles |
| `05:138` | 1 | Get Max Profiles Supported |
| `06:142` | 14 | Get Macro Storage Info |
| `07:162` | 0 | Exit Dim Status |
| `08:04` | 11 | Set EQ Setting |
| `08:132` | 11 | Get EQ Setting |
| `11:02` | 8 | Set Proximity Sensor Threshold |
| `11:03` | 3 | Set Proximity Sensor State |
| `11:05` | 10 | Set Proximity Sensor Configuration |
| `11:08` | 50 | Set Proximity Sensor Calibration Data |
| `11:09` | 4 | Set Proximity Sensor Control |
| `11:11` | 4 | Set Proximity Sensor Lift Setting |
| `11:12` | 18 | Set Proximity Sensor Lift To Track Calibration Data |
| `11:13` | 18 | Set Proximity Sensor Track To Lift Calibration Data |
| `11:130` | 8 | Get Proximity Sensor Threshold |
| `11:131` | 3 | Get Proximity Sensor State |
| `11:133` | 10 | Get Proximity Sensor Configuration |
| `11:136` | 50 | Get Proximity Sensor Calibration Data |
| `11:137` | 4 | Get Proximity Sensor Control |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `15:132` | 3 | Get Chroma Brightness |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `21:11` | 2 | Set LK Scroll Wheel Mode Selected Information |
| `21:139` | 2 | Get LK Scroll Wheel Mode Selected Information |
| `131:00` | 130 | Get Serial Number |

### P3 — 8 设备 / 131 命令

设备：`0309` RAZER KRAKEN BT SANRIO LIMITED EDITION、`0526` Razer Kraken X USB、`0530` Razer Kraken BT Kitty Edition、`0532` Razer Leviathan V2、`0534` RAZER KRAKEN BT SANRIO LIMITED EDITION、`0537` Razer Kraken V3 X、`0560` Razer Kraken Kitty V2、`0562` Razer Kraken Kitty V2 BT Quartz Edition

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:00` | 25 | Get Sidetone Volume |
| `00:01` | 150 | Set Mic Preset EQ Index |
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:10` | 149 | Set Customer EQ Band |
| `00:11` | 1 | Set Device Reset |
| `00:129` | 2 | Get Firmware Version |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:145` | 2 | Get Dock Firmware Version |
| `00:146` | 22 | Get Dock Serial Number |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `04:00` | 253 | Get Link Status |
| `04:02` | 253 | Get Haptics Control |
| `04:07` | 253 | Get Extended Firmware Version |
| `04:18` | 255 | Set Silence Detection Control |
| `07:08` | 3 | Set Auto Sleep Mode |
| `07:128` | 2 | Get Battery Level |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 1 | Get Auto Sleep Mode |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:08` | 2 | Set mic Mute State |
| `08:09` | 2 | Set Mute State |
| `08:10` | 2 | Set Audio Source |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:22` | 3 | Set Audio input control State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:25` | 2 | Set Combo Mode |
| `08:26` | 9 | Set Parametric EQ Setting |
| `08:28` | 2 | Set AI Noise Suppression |
| `08:29` | 9 | Set Mic Compressor |
| `08:30` | 2 | Set Mic De-Esser |
| `08:31` | 3 | Set Mic Reverb |
| `08:32` | 2 | Set Mic Limiter |
| `08:34` | 2 | Set Return Switch Status |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:136` | 2 | Get Mic Mute State |
| `08:137` | 2 | Get Mute State |
| `08:138` | 2 | Get Audio Source |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:150` | 3 | Get Audio input control State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `08:154` | 9 | Get Parametric EQ Setting |
| `08:156` | 2 | Get AI Noise Suppression |
| `08:157` | 9 | Get Mic Compressor |
| `08:158` | 2 | Get Mic De-Esser |
| `08:159` | 3 | Get Mic Reverb |
| `08:160` | 2 | Get Mic Limiter |
| `08:162` | 2 | Get Return Switch Status |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:18` | 3 | Set Audio Chroma display Switch |
| `15:130` | 80 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:146` | 3 | Get Audio Chroma display Switch |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 11 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:128` | 4 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:141` | 2 | Get OLED Home Screen Active Item |
| `23:143` | 22 | Get OLED Image GUID |
| `23:144` | 7 | Get OLED Image Check Sum |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `24:05` | 2 | Set Audio DND Status |
| `24:06` | 3 | Set ANC Status and Level |
| `24:15` | 2 | Set Capture Noise Reduction |
| `24:16` | 3 | Set Audio Voice Prompts Status |
| `24:21` | 2 | Set Audio DND Status |
| `24:30` | 2 | Set Audio Auto Pause Status |
| `24:34` | 2 | Set Audio Gesture Control |
| `24:128` | 2 | Get EQ Preset |
| `24:133` | 1 | Get Audio DND Status |
| `24:134` | 1 | Get ANC Status and Level |
| `24:143` | 2 | Get Capture Noise Reduction |
| `24:144` | 2 | Get Audio Voice Prompts Status |
| `24:154` | 1 | Get Audio Connection Status |
| `24:158` | 1 | Get Audio Auto Pause Status |
| `24:161` | 0 | Get THX Activation Code |
| `24:162` | 2 | Get Audio Gesture Control |

### P4 — 8 设备 / 45 命令

设备：`0063` Razer Jugan、`0074` Razer Abyssus Lite、`030C` Razer Key Light Chroma、`030D` Razer Aether Lamp Pro、`030E` Razer Aether Lamp、`030F` Razer Aether Light Bulb、`0310` Razer Aether Light Strip、`0317` Razer Aether Standing Light Bars

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `01:02` | 2 | Set Device Mode |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:134` | 0 | Get Edition Information |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `02:148` | 2 | Get Scroll Free-Notched Mode |
| `03:06` | 0 | Delete Profile |
| `03:137` | 0 | Get Max Profiles Supported |
| `05:128` | 1 | Get Number of Profiles |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:23` | 2 | Set Mic Volume |
| `08:132` | 11 | Get EQ Setting |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `08:151` | 2 | Get Mic Volume |
| `11:05` | 10 | Set Proximity Sensor Configuration |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `15:02` | 6 | Set Proximity Sensor Threshold |
| `15:03` | 1 | Set Proximity Sensor State |
| `15:05` | 9 | Set Proximity Sensor Configuration |
| `15:08` | 48 | Set Proximity Sensor Calibration Data |
| `15:09` | 2 | Set Proximity Sensor Control |
| `15:11` | 2 | Set Proximity Sensor Lift Setting |
| `15:12` | 16 | Set Proximity Sensor Lift To Track Calibration Data |
| `15:13` | 16 | Set Proximity Sensor Track To Lift Calibration Data |
| `15:130` | 0 | Get Proximity Sensor Threshold |
| `15:131` | 0 | Get Proximity Sensor State |
| `15:133` | 0 | Get Proximity Sensor Configuration |
| `15:136` | 0 | Get Proximity Sensor Calibration Data |
| `15:137` | 0 | Get Proximity Sensor Control |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P5 — 7 设备 / 169 命令

设备：`0069` Razer Mamba Hyperflux、`0093` Razer Naga Classic Edition、`00DE` Razer Viper V3 Pro SE、`00E0` Razer Orochi V2、`00EF` Razer DeathAdder V4 Pro Carbon Fiber Edition、`030A` RAZER CHROMA ADDRESSABLE RGB CONTROLLER、`0566` Razer WIRELESS CONTROL POD

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:05` | 1 | Set Device Polling Period |
| `00:06` | 3 | Set Edition Information |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:51` | 1 | Set Device Ambidextrous Information |
| `00:54` | 80 | Set Device HW Info Force Check |
| `00:55` | 1 | Set Device External Power Supply Status |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:68` | 1 | Set Hardware Auto Detect Ctrl |
| `00:71` | 2 | Set Active Wireless Connection State |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:133` | 1 | Get Device Polling Period |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:179` | 1 | Get Device Ambidextrous Information |
| `00:181` | 80 | Get Device HW Info |
| `00:183` | 1 | Get Device External Power Supply Status |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:187` | 1 | Get Device On Dock Charging Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:196` | 1 | Get Hardware Auto Detect Ctrl |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:199` | 2 | Get Active Wireless Connection State |
| `00:215` | 1 | Get Device Power Button Status |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:04` | 1 | Set Device Polling Period |
| `01:10` | 1 | Set Device Ambidextrous Information |
| `01:15` | 1 | Set Device External Power Supply Status |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:20` | 0 | Set Device HW Info Force Check |
| `01:22` | 1 | Set Device Hardware Auto Detect Ctrl |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:132` | 0 | Get Device Polling Period |
| `01:134` | 0 | Get Edition Information |
| `01:138` | 0 | Get Device Ambidextrous Information |
| `01:140` | 0 | Get Device Profile Polling Period |
| `01:143` | 0 | Get Device External Power Supply Status |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `01:149` | 0 | Get Device HW Info |
| `01:150` | 0 | Get Device Hardware Auto Detect Ctrl |
| `02:12` | 80 | Set Single Button Assignment |
| `02:20` | 2 | Set Scroll Free-Notched Mode |
| `02:21` | 5 | Set Profile Debounce Time |
| `02:140` | 80 | Get Single Button Assignment |
| `02:148` | 2 | Get Scroll Free-Notched Mode |
| `02:149` | 5 | Get Profile Debounce Time |
| `03:00` | 3 | Set LED State |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:130` | 3 | Get LED Effect |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `05:128` | 1 | Get Number of Profiles |
| `05:138` | 1 | Get Max Profiles Supported |
| `06:142` | 14 | Get Macro Storage Info |
| `07:01` | 1 | Set Battery Critical Level |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:19` | 3 | Set Smart illumination Control |
| `07:20` | 1 | Set Battery Discharge Curve |
| `07:21` | 3 | Set indicator LEDs Configuration |
| `07:128` | 2 | Get Battery Level |
| `07:129` | 1 | Get Battery Critical Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:142` | 3 | Get Power Security Status |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:147` | 3 | Get Smart illumination Control |
| `07:148` | 1 | Get Battery Discharge Curve |
| `07:149` | 3 | Get indicator LEDs Configuration |
| `07:150` | 1 | Get Dongle Connection Quality Status |
| `07:162` | 0 | Exit Dim Status |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:10` | 2 | Set Audio Source |
| `08:12` | 11 | Set Dolby State |
| `08:17` | 2 | Set audio output source |
| `08:18` | 2 | Set EQ night mode |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:25` | 2 | Set Combo Mode |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:138` | 2 | Get Audio Source |
| `08:139` | 5 | Get Audio I/O Status |
| `08:140` | 11 | Get Dolby State |
| `08:145` | 2 | Get audio output source |
| `08:146` | 2 | Get EQ night mode |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `11:02` | 8 | Set Proximity Sensor Threshold |
| `11:03` | 3 | Set Proximity Sensor State |
| `11:05` | 10 | Set Proximity Sensor Configuration |
| `11:08` | 50 | Set Proximity Sensor Calibration Data |
| `11:09` | 4 | Set Proximity Sensor Control |
| `11:11` | 4 | Set Proximity Sensor Lift Setting |
| `11:12` | 18 | Set Proximity Sensor Lift To Track Calibration Data |
| `11:13` | 18 | Set Proximity Sensor Track To Lift Calibration Data |
| `11:130` | 8 | Get Proximity Sensor Threshold |
| `11:131` | 3 | Get Proximity Sensor State |
| `11:133` | 10 | Get Proximity Sensor Configuration |
| `11:136` | 50 | Get Proximity Sensor Calibration Data |
| `11:137` | 4 | Get Proximity Sensor Control |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:13` | 3 | Set Thermal Fan Duty Cycle |
| `13:16` | 3 | Set Thermal Fan Cooling Mode |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:133` | 80 | Get Thermal Reading |
| `13:141` | 3 | Get Thermal Fan Duty Cycle |
| `13:144` | 3 | Get Thermal Fan Cooling Mode |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:08` | 7 | Set Chroma Stripe LED Number |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:19` | 4 | Set Chroma Tap |
| `15:128` | 80 | Get Chroma Region ID List |
| `15:130` | 80 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:136` | 0 | Get Chroma Stripe LED Number |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:147` | 4 | Get Chroma Tap |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `21:11` | 2 | Set LK Scroll Wheel Mode Selected Information |
| `21:139` | 2 | Get LK Scroll Wheel Mode Selected Information |
| `131:00` | 130 | Get Serial Number |

### P6 — 6 设备 / 159 命令

设备：`02D0` Razer Huntsman V3 Pro Tenkeyless 8KHZ、`02D1` Razer Huntsman V3 Pro Mini 8KHz、`02D8` Razer Huntsman Signature Edition、`02E4` Razer Huntsman V3 HE Magnetic Mini 65% 8KHz、`02E5` Razer Huntsman V3 Tenkeyless 8KHZ、`02EA` Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:89` | 3 | Set System Multi Function Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:10` | 5 | Set Time To Dim |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:129` | 0 | Get Battery Level |
| `05:138` | 1 | Get Max Profiles Supported |
| `05:141` | 3 | Get Low Power Mode State |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:130` | 0 | Get Debounce Time |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:23` | 80 | Set Trigger Firmware Effects |
| `15:24` | 2 | Set Startup Animation Control |
| `15:130` | 80 | Get Chroma Effect |
| `15:152` | 1 | Get Startup Animation Control |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P7 — 6 设备 / 80 命令

设备：`0249` Razer Pro Type、`0251` Razer Pokémon Pikachu Limited Edition Backlit Keyboard、`0277` Razer Pro Type Ultra、`028E` 、`02CD` Razer Joro、`02F0` Razer BlackWidow V4 75% XBOX 25 Anniversary Edition

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:06` | 0 | Delete Profile |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:138` | 1 | Get Max Profiles Supported |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 1 | Set Function Key Swap |
| `07:130` | 0 | Get Debounce Time |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 0 | Get Function Key Swap |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:131` | 0 | Get Chroma Effect |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P8 — 6 设备 / 20 命令

设备：`0F1F` RAZER CHROMA ADDRESSABLE RGB CONTROLLER、`0F28` Razer Raptor 27 (165Hz)、`0F2C` Razer Chroma Wireless ARGB Controller、`0F35` RAZER HANBO AIO、`0F3C` RAZER PWM PC FAN CONTROLLER、`0F64` Razer Soma Chroma

> ARGB 帧不走 HID feature report。OpenRazer（pinned `6820f9d`）的 `razer_send_argb_msg` 发的是 325 字节 SET_REPORT：`report_id`（通道 <5 用 `0x04`，≥5 用 `0x84`）+ `channel_1` + `channel_2` + `pad` + `last_idx`（颜色数 −1）+ 320 字节 RGB，`wValue 0x0300`、`wIndex 1`。Windows 的 `HidD_SetFeature` 送不了这个长度，需要 WinUSB 或 Razer 过滤驱动通道；这也是 `0F1F` 目前只能发现、不能写的唯一原因。

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `01:02` | 2 | Set Device Mode |
| `01:15` | 1 | Set Device External Power Supply Status |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:134` | 0 | Get Edition Information |
| `01:143` | 0 | Get Device External Power Supply Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `08:04` | 11 | Set EQ Setting |
| `08:132` | 11 | Get EQ Setting |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P9 — 5 设备 / 140 命令

设备：`0F45` Esther、`0F52` Razer Thunderbolt 5 Dock Chroma、`0F59` Razer Monitor Stand Chroma、`0F5C` Razer Handheld Dock Chroma、`0F6D` MarciT2H

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:05` | 1 | Set Device Polling Period |
| `00:06` | 3 | Set Edition Information |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:51` | 1 | Set Device Ambidextrous Information |
| `00:54` | 80 | Set Device HW Info Force Check |
| `00:55` | 1 | Set Device External Power Supply Status |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:68` | 1 | Set Hardware Auto Detect Ctrl |
| `00:71` | 2 | Set Active Wireless Connection State |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:133` | 1 | Get Device Polling Period |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:179` | 1 | Get Device Ambidextrous Information |
| `00:181` | 80 | Get Device HW Info |
| `00:183` | 1 | Get Device External Power Supply Status |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:196` | 1 | Get Hardware Auto Detect Ctrl |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:199` | 2 | Get Active Wireless Connection State |
| `00:215` | 1 | Get Device Power Button Status |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:04` | 1 | Set Device Polling Period |
| `01:10` | 1 | Set Device Ambidextrous Information |
| `01:15` | 1 | Set Device External Power Supply Status |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:20` | 0 | Set Device HW Info Force Check |
| `01:22` | 1 | Set Device Hardware Auto Detect Ctrl |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:132` | 0 | Get Device Polling Period |
| `01:134` | 0 | Get Edition Information |
| `01:138` | 0 | Get Device Ambidextrous Information |
| `01:140` | 0 | Get Device Profile Polling Period |
| `01:143` | 0 | Get Device External Power Supply Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `01:149` | 0 | Get Device HW Info |
| `01:150` | 0 | Get Device Hardware Auto Detect Ctrl |
| `03:00` | 3 | Set LED State |
| `03:02` | 3 | Set LED Effect |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:130` | 3 | Get LED Effect |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `07:01` | 1 | Set Battery Critical Level |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:19` | 3 | Set Smart illumination Control |
| `07:20` | 1 | Set Battery Discharge Curve |
| `07:21` | 3 | Set indicator LEDs Configuration |
| `07:128` | 2 | Get Battery Level |
| `07:129` | 1 | Get Battery Critical Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:142` | 3 | Get Power Security Status |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:147` | 3 | Get Smart illumination Control |
| `07:148` | 1 | Get Battery Discharge Curve |
| `07:149` | 3 | Get indicator LEDs Configuration |
| `07:150` | 1 | Get Dongle Connection Quality Status |
| `07:162` | 0 | Exit Dim Status |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:10` | 2 | Set Audio Source |
| `08:12` | 11 | Set Dolby State |
| `08:17` | 2 | Set audio output source |
| `08:18` | 2 | Set EQ night mode |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:25` | 2 | Set Combo Mode |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:138` | 2 | Get Audio Source |
| `08:139` | 5 | Get Audio I/O Status |
| `08:140` | 11 | Get Dolby State |
| `08:145` | 2 | Get audio output source |
| `08:146` | 2 | Get EQ night mode |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:13` | 3 | Set Thermal Fan Duty Cycle |
| `13:16` | 3 | Set Thermal Fan Cooling Mode |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:133` | 80 | Get Thermal Reading |
| `13:141` | 3 | Get Thermal Fan Duty Cycle |
| `13:144` | 3 | Get Thermal Fan Cooling Mode |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:08` | 7 | Set Chroma Stripe LED Number |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:19` | 4 | Set Chroma Tap |
| `15:128` | 80 | Get Chroma Region ID List |
| `15:130` | 80 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:136` | 0 | Get Chroma Stripe LED Number |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:147` | 4 | Get Chroma Tap |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P10 — 5 设备 / 18 命令

设备：`0F1B` 、`0F27` Tomahawk Gaming Desktop、`0F36` Razer Head Cushion Chroma、`0F51` Razer Core X V2、`0F6A` Razer Base Station V3 Chroma

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:131` | 0 | Get Serial Number |
| `01:134` | 0 | Get Edition Information |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `08:04` | 11 | Set EQ Setting |
| `08:23` | 2 | Set Mic Volume |
| `08:132` | 11 | Get EQ Setting |
| `08:151` | 2 | Get Mic Volume |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P11 — 4 设备 / 159 命令

设备：`02B1` Razer Huntsman V3 X Tenkeyless、`02B3` Blackwidow V4 Pro 75%、`02CC` Razer BlackWidow V4 Low-profile HyperSpeed、`02D4` Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:05` | 1 | Set Device Polling Period |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:89` | 3 | Set System Multi Function Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:133` | 1 | Get Device Polling Period |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:10` | 5 | Set Time To Dim |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:129` | 0 | Get Battery Level |
| `05:138` | 1 | Get Max Profiles Supported |
| `05:141` | 3 | Get Low Power Mode State |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:130` | 0 | Get Debounce Time |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:23` | 80 | Set Trigger Firmware Effects |
| `15:24` | 2 | Set Startup Animation Control |
| `15:130` | 80 | Get Chroma Effect |
| `15:152` | 1 | Get Startup Animation Control |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P12 — 3 设备 / 157 命令

设备：`023E` Turret Keyboard Xbox One Edition、`024F` Razer BlackWidow X: Tenkeyless、`0250` Razer Huntsman Essential

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:89` | 3 | Set System Multi Function Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:10` | 5 | Set Time To Dim |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:129` | 0 | Get Battery Level |
| `05:138` | 1 | Get Max Profiles Supported |
| `05:141` | 3 | Get Low Power Mode State |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:130` | 0 | Get Debounce Time |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:23` | 80 | Set Trigger Firmware Effects |
| `15:24` | 2 | Set Startup Animation Control |
| `15:130` | 80 | Get Chroma Effect |
| `15:152` | 1 | Get Startup Animation Control |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P13 — 3 设备 / 108 命令

设备：`00C7` Razer Pro Click V2 Vertical Edition、`00CB` Razer Basilisk V3 35K、`00D0` Razer Pro Click V2

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:05` | 1 | Set Device Polling Period |
| `00:06` | 3 | Set Edition Information |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:51` | 1 | Set Device Ambidextrous Information |
| `00:54` | 80 | Set Device HW Info Force Check |
| `00:55` | 1 | Set Device External Power Supply Status |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:68` | 1 | Set Hardware Auto Detect Ctrl |
| `00:71` | 2 | Set Active Wireless Connection State |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:133` | 1 | Get Device Polling Period |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:179` | 1 | Get Device Ambidextrous Information |
| `00:181` | 80 | Get Device HW Info |
| `00:183` | 1 | Get Device External Power Supply Status |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:196` | 1 | Get Hardware Auto Detect Ctrl |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:199` | 2 | Get Active Wireless Connection State |
| `00:215` | 1 | Get Device Power Button Status |
| `03:00` | 3 | Set LED State |
| `03:02` | 3 | Set LED Effect |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:130` | 3 | Get LED Effect |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `07:01` | 1 | Set Battery Critical Level |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:19` | 3 | Set Smart illumination Control |
| `07:20` | 1 | Set Battery Discharge Curve |
| `07:21` | 3 | Set indicator LEDs Configuration |
| `07:128` | 2 | Get Battery Level |
| `07:129` | 1 | Get Battery Critical Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:142` | 3 | Get Power Security Status |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:147` | 3 | Get Smart illumination Control |
| `07:148` | 1 | Get Battery Discharge Curve |
| `07:149` | 3 | Get indicator LEDs Configuration |
| `07:150` | 1 | Get Dongle Connection Quality Status |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 9 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:10` | 2 | Set Audio Source |
| `08:12` | 11 | Set Dolby State |
| `08:17` | 2 | Set audio output source |
| `08:18` | 2 | Set EQ night mode |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:25` | 2 | Set Combo Mode |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 9 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:138` | 2 | Get Audio Source |
| `08:139` | 5 | Get Audio I/O Status |
| `08:140` | 11 | Get Dolby State |
| `08:145` | 2 | Get audio output source |
| `08:146` | 2 | Get EQ night mode |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:13` | 3 | Set Thermal Fan Duty Cycle |
| `13:16` | 3 | Set Thermal Fan Cooling Mode |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:133` | 80 | Get Thermal Reading |
| `13:141` | 3 | Get Thermal Fan Duty Cycle |
| `13:144` | 3 | Get Thermal Fan Cooling Mode |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:08` | 7 | Set Chroma Stripe LED Number |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:19` | 4 | Set Chroma Tap |
| `15:128` | 80 | Get Chroma Region ID List |
| `15:130` | 80 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:136` | 0 | Get Chroma Stripe LED Number |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:147` | 4 | Get Chroma Tap |

### P14 — 3 设备 / 105 命令

设备：`02B6` Razer Blade 14、`02B7` Razer Blade 16、`02B8` Razer Blade 18

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:89` | 3 | Set System Multi Function Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `06:03` | 2 | Delete Macro |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:23` | 80 | Set Trigger Firmware Effects |
| `15:24` | 2 | Set Startup Animation Control |
| `15:130` | 80 | Get Chroma Effect |
| `15:152` | 1 | Get Startup Animation Control |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 11 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 4 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:141` | 2 | Get OLED Home Screen Active Item |
| `23:143` | 22 | Get OLED Image GUID |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 1 | Get OLED Low Power Mode |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P15 — 3 设备 / 102 命令

设备：`0255` Razer Blade 15 Base Model、`029D` Razer Blade 14、`029F` Razer Blade 16

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `06:03` | 2 | Delete Macro |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Chroma Effect |
| `15:23` | 80 | Set Trigger Firmware Effects |
| `15:24` | 2 | Set Startup Animation Control |
| `15:130` | 80 | Get Chroma Effect |
| `15:152` | 1 | Get Startup Animation Control |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 11 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 4 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:141` | 2 | Get OLED Home Screen Active Item |
| `23:143` | 22 | Get OLED Image GUID |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 1 | Get OLED Low Power Mode |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |

### P16 — 3 设备 / 99 命令

设备：`02C5` Razer Blade 14、`02C6` Razer Blade 16、`02C7` Razer Blade 18

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:89` | 3 | Set System Multi Function Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `06:03` | 2 | Delete Macro |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 11 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 4 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:141` | 2 | Get OLED Home Screen Active Item |
| `23:143` | 22 | Get OLED Image GUID |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 1 | Get OLED Low Power Mode |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P17 — 2 设备 / 127 命令

设备：`055A` Razer Nommo V2 Pro、`055C` Razer Nommo V2

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:00` | 25 | Get Sidetone Volume |
| `00:01` | 150 | Set Mic Preset EQ Index |
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:10` | 149 | Set Customer EQ Band |
| `00:11` | 1 | Set Device Reset |
| `00:129` | 2 | Get Firmware Version |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:145` | 2 | Get Dock Firmware Version |
| `00:146` | 22 | Get Dock Serial Number |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `04:00` | 253 | Get Link Status |
| `04:02` | 253 | Get Haptics Control |
| `04:07` | 253 | Get Extended Firmware Version |
| `04:18` | 255 | Set Silence Detection Control |
| `07:08` | 3 | Set Auto Sleep Mode |
| `07:128` | 2 | Get Battery Level |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 3 | Get Auto Sleep Mode |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:08` | 2 | Set mic Mute State |
| `08:09` | 2 | Set Mute State |
| `08:10` | 2 | Set Audio Source |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:22` | 3 | Set Audio input control State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:25` | 2 | Set Combo Mode |
| `08:26` | 9 | Set Parametric EQ Setting |
| `08:28` | 2 | Set AI Noise Suppression |
| `08:29` | 9 | Set Mic Compressor |
| `08:30` | 2 | Set Mic De-Esser |
| `08:31` | 3 | Set Mic Reverb |
| `08:32` | 2 | Set Mic Limiter |
| `08:34` | 2 | Set Return Switch Status |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:136` | 2 | Get Mic Mute State |
| `08:137` | 2 | Get Mute State |
| `08:138` | 2 | Get Audio Source |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:150` | 3 | Get Audio input control State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `08:154` | 9 | Get Parametric EQ Setting |
| `08:156` | 2 | Get AI Noise Suppression |
| `08:157` | 9 | Get Mic Compressor |
| `08:158` | 2 | Get Mic De-Esser |
| `08:159` | 3 | Get Mic Reverb |
| `08:160` | 2 | Get Mic Limiter |
| `08:162` | 2 | Get Return Switch Status |
| `11:19` | 37 | Set Proximity Sensor Acceleration curve data |
| `11:147` | 37 | Get Proximity Sensor Acceleration curve data |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:18` | 3 | Set Audio Chroma display Switch |
| `15:19` | 4 | Set Chroma Tap |
| `15:130` | 2 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:146` | 3 | Get Audio Chroma display Switch |
| `15:147` | 4 | Get Chroma Tap |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 11 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:128` | 4 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:141` | 2 | Get OLED Home Screen Active Item |
| `23:143` | 22 | Get OLED Image GUID |
| `23:144` | 7 | Get OLED Image Check Sum |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `24:05` | 2 | Set Audio DND Status |
| `24:15` | 2 | Set Capture Noise Reduction |
| `24:21` | 2 | Set Audio DND Status |
| `24:128` | 2 | Get EQ Preset |
| `24:133` | 2 | Get Audio DND Status |
| `24:143` | 2 | Get Capture Noise Reduction |

### P18 — 2 设备 / 81 命令

设备：`02C4` Razer Pro Type Ergo、`02E6` RAZER HUNTSMAN V3 PRO LOW-PROFILE TENKEYLESS 8KHZ

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:06` | 0 | Delete Profile |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:138` | 1 | Get Max Profiles Supported |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 1 | Set Function Key Swap |
| `07:130` | 0 | Get Debounce Time |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 0 | Get Function Key Swap |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:131` | 0 | Get Chroma Effect |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P19 — 2 设备 / 59 命令

设备：`00E2` Razer Basilisk V4 Pro、`00EB` Razer Basilisk V4 HyperSpeed

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:71` | 2 | Set Active Wireless Connection State |
| `00:199` | 2 | Get Active Wireless Connection State |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:04` | 1 | Set Device Polling Period |
| `01:10` | 1 | Set Device Ambidextrous Information |
| `01:15` | 1 | Set Device External Power Supply Status |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:20` | 0 | Set Device HW Info Force Check |
| `01:22` | 1 | Set Device Hardware Auto Detect Ctrl |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:132` | 0 | Get Device Polling Period |
| `01:134` | 0 | Get Edition Information |
| `01:138` | 0 | Get Device Ambidextrous Information |
| `01:140` | 0 | Get Device Profile Polling Period |
| `01:143` | 0 | Get Device External Power Supply Status |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `01:149` | 0 | Get Device HW Info |
| `01:150` | 0 | Get Device Hardware Auto Detect Ctrl |
| `02:148` | 2 | Get Scroll Free-Notched Mode |
| `03:06` | 0 | Delete Profile |
| `03:137` | 0 | Get Max Profiles Supported |
| `05:128` | 1 | Get Number of Profiles |
| `08:04` | 255 | Set Single Button Assignment |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:132` | 11 | Get EQ Setting |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `15:02` | 6 | Set Proximity Sensor Threshold |
| `15:03` | 1 | Set Proximity Sensor State |
| `15:05` | 9 | Set Proximity Sensor Configuration |
| `15:08` | 48 | Set Proximity Sensor Calibration Data |
| `15:09` | 2 | Set Proximity Sensor Control |
| `15:11` | 2 | Set Proximity Sensor Lift Setting |
| `15:12` | 16 | Set Proximity Sensor Lift To Track Calibration Data |
| `15:13` | 16 | Set Proximity Sensor Track To Lift Calibration Data |
| `15:130` | 0 | Get Proximity Sensor Threshold |
| `15:131` | 0 | Get Proximity Sensor State |
| `15:133` | 0 | Get Proximity Sensor Configuration |
| `15:136` | 0 | Get Proximity Sensor Calibration Data |
| `15:137` | 0 | Get Proximity Sensor Control |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P20 — 2 设备 / 46 命令

设备：`00DA` Razer Cobra HyperSpeed、`0543` Razer Seiren V2 X

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:130` | 22 | Get Serial Number |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:187` | 1 | Get Device On Dock Charging Status |
| `01:131` | 0 | Get Serial Number |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `02:12` | 80 | Set Single Button Assignment |
| `02:20` | 2 | Set Scroll Free-Notched Mode |
| `02:21` | 5 | Set Profile Debounce Time |
| `02:140` | 80 | Get Single Button Assignment |
| `02:148` | 2 | Get Scroll Free-Notched Mode |
| `02:149` | 5 | Get Profile Debounce Time |
| `03:06` | 0 | Delete Profile |
| `05:128` | 1 | Get Number of Profiles |
| `05:138` | 1 | Get Max Profiles Supported |
| `06:142` | 14 | Get Macro Storage Info |
| `07:162` | 0 | Exit Dim Status |
| `08:04` | 11 | Set EQ Setting |
| `08:132` | 11 | Get EQ Setting |
| `11:02` | 8 | Set Proximity Sensor Threshold |
| `11:03` | 3 | Set Proximity Sensor State |
| `11:05` | 10 | Set Proximity Sensor Configuration |
| `11:08` | 50 | Set Proximity Sensor Calibration Data |
| `11:09` | 4 | Set Proximity Sensor Control |
| `11:11` | 4 | Set Proximity Sensor Lift Setting |
| `11:12` | 18 | Set Proximity Sensor Lift To Track Calibration Data |
| `11:13` | 18 | Set Proximity Sensor Track To Lift Calibration Data |
| `11:130` | 8 | Get Proximity Sensor Threshold |
| `11:131` | 3 | Get Proximity Sensor State |
| `11:133` | 10 | Get Proximity Sensor Configuration |
| `11:136` | 50 | Get Proximity Sensor Calibration Data |
| `11:137` | 4 | Get Proximity Sensor Control |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `15:132` | 3 | Get Chroma Brightness |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P21 — 1 设备 / 167 命令

设备：`00F1` Razer Mouse Dock V2 Pro

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:05` | 1 | Set Device Polling Period |
| `00:06` | 3 | Set Edition Information |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:51` | 1 | Set Device Ambidextrous Information |
| `00:54` | 80 | Set Device HW Info Force Check |
| `00:55` | 1 | Set Device External Power Supply Status |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:68` | 1 | Set Hardware Auto Detect Ctrl |
| `00:71` | 2 | Set Active Wireless Connection State |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:133` | 1 | Get Device Polling Period |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:179` | 1 | Get Device Ambidextrous Information |
| `00:181` | 80 | Get Device HW Info |
| `00:183` | 1 | Get Device External Power Supply Status |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:187` | 1 | Get Device On Dock Charging Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:196` | 1 | Get Hardware Auto Detect Ctrl |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:199` | 2 | Get Active Wireless Connection State |
| `00:215` | 1 | Get Device Power Button Status |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:04` | 1 | Set Device Polling Period |
| `01:10` | 1 | Set Device Ambidextrous Information |
| `01:15` | 1 | Set Device External Power Supply Status |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:20` | 0 | Set Device HW Info Force Check |
| `01:22` | 1 | Set Device Hardware Auto Detect Ctrl |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:132` | 0 | Get Device Polling Period |
| `01:134` | 0 | Get Edition Information |
| `01:138` | 0 | Get Device Ambidextrous Information |
| `01:140` | 0 | Get Device Profile Polling Period |
| `01:143` | 0 | Get Device External Power Supply Status |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `01:149` | 0 | Get Device HW Info |
| `01:150` | 0 | Get Device Hardware Auto Detect Ctrl |
| `02:12` | 80 | Set Single Button Assignment |
| `02:20` | 2 | Set Scroll Free-Notched Mode |
| `02:21` | 5 | Set Profile Debounce Time |
| `02:140` | 80 | Get Single Button Assignment |
| `02:148` | 2 | Get Scroll Free-Notched Mode |
| `02:149` | 5 | Get Profile Debounce Time |
| `03:00` | 3 | Set LED State |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:130` | 3 | Get LED Effect |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `05:128` | 1 | Get Number of Profiles |
| `05:138` | 1 | Get Max Profiles Supported |
| `06:142` | 14 | Get Macro Storage Info |
| `07:01` | 1 | Set Battery Critical Level |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:19` | 3 | Set Smart illumination Control |
| `07:20` | 1 | Set Battery Discharge Curve |
| `07:21` | 3 | Set indicator LEDs Configuration |
| `07:128` | 2 | Get Battery Level |
| `07:129` | 1 | Get Battery Critical Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:142` | 3 | Get Power Security Status |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:147` | 3 | Get Smart illumination Control |
| `07:148` | 1 | Get Battery Discharge Curve |
| `07:149` | 3 | Get indicator LEDs Configuration |
| `07:150` | 1 | Get Dongle Connection Quality Status |
| `07:162` | 0 | Exit Dim Status |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:10` | 2 | Set Audio Source |
| `08:12` | 11 | Set Dolby State |
| `08:17` | 2 | Set audio output source |
| `08:18` | 2 | Set EQ night mode |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:25` | 2 | Set Combo Mode |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:138` | 2 | Get Audio Source |
| `08:139` | 5 | Get Audio I/O Status |
| `08:140` | 11 | Get Dolby State |
| `08:145` | 2 | Get audio output source |
| `08:146` | 2 | Get EQ night mode |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `11:02` | 8 | Set Proximity Sensor Threshold |
| `11:03` | 3 | Set Proximity Sensor State |
| `11:05` | 10 | Set Proximity Sensor Configuration |
| `11:08` | 50 | Set Proximity Sensor Calibration Data |
| `11:09` | 4 | Set Proximity Sensor Control |
| `11:11` | 4 | Set Proximity Sensor Lift Setting |
| `11:12` | 18 | Set Proximity Sensor Lift To Track Calibration Data |
| `11:13` | 18 | Set Proximity Sensor Track To Lift Calibration Data |
| `11:130` | 8 | Get Proximity Sensor Threshold |
| `11:131` | 3 | Get Proximity Sensor State |
| `11:133` | 10 | Get Proximity Sensor Configuration |
| `11:136` | 50 | Get Proximity Sensor Calibration Data |
| `11:137` | 4 | Get Proximity Sensor Control |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:13` | 3 | Set Thermal Fan Duty Cycle |
| `13:16` | 3 | Set Thermal Fan Cooling Mode |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:133` | 80 | Get Thermal Reading |
| `13:141` | 3 | Get Thermal Fan Duty Cycle |
| `13:144` | 3 | Get Thermal Fan Cooling Mode |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:08` | 7 | Set Chroma Stripe LED Number |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:19` | 4 | Set Chroma Tap |
| `15:128` | 80 | Get Chroma Region ID List |
| `15:130` | 80 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:136` | 0 | Get Chroma Stripe LED Number |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:147` | 4 | Get Chroma Tap |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P22 — 1 设备 / 165 命令

设备：`00E5` Razer Viper V4 Pro

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:05` | 1 | Set Device Polling Period |
| `00:06` | 3 | Set Edition Information |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:51` | 1 | Set Device Ambidextrous Information |
| `00:54` | 80 | Set Device HW Info Force Check |
| `00:55` | 1 | Set Device External Power Supply Status |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:68` | 1 | Set Hardware Auto Detect Ctrl |
| `00:71` | 2 | Set Active Wireless Connection State |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:133` | 1 | Get Device Polling Period |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:179` | 1 | Get Device Ambidextrous Information |
| `00:181` | 80 | Get Device HW Info |
| `00:183` | 1 | Get Device External Power Supply Status |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:187` | 1 | Get Device On Dock Charging Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:196` | 1 | Get Hardware Auto Detect Ctrl |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:199` | 2 | Get Active Wireless Connection State |
| `00:215` | 1 | Get Device Power Button Status |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:04` | 1 | Set Device Polling Period |
| `01:10` | 1 | Set Device Ambidextrous Information |
| `01:15` | 1 | Set Device External Power Supply Status |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:20` | 0 | Set Device HW Info Force Check |
| `01:22` | 1 | Set Device Hardware Auto Detect Ctrl |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:132` | 0 | Get Device Polling Period |
| `01:134` | 0 | Get Edition Information |
| `01:138` | 0 | Get Device Ambidextrous Information |
| `01:140` | 0 | Get Device Profile Polling Period |
| `01:143` | 0 | Get Device External Power Supply Status |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `01:149` | 0 | Get Device HW Info |
| `01:150` | 0 | Get Device Hardware Auto Detect Ctrl |
| `02:12` | 80 | Set Single Button Assignment |
| `02:20` | 2 | Set Scroll Free-Notched Mode |
| `02:21` | 5 | Set Profile Debounce Time |
| `02:140` | 80 | Get Single Button Assignment |
| `02:148` | 2 | Get Scroll Free-Notched Mode |
| `02:149` | 5 | Get Profile Debounce Time |
| `03:00` | 3 | Set LED State |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:130` | 3 | Get LED Effect |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `05:128` | 1 | Get Number of Profiles |
| `05:138` | 1 | Get Max Profiles Supported |
| `06:142` | 14 | Get Macro Storage Info |
| `07:01` | 1 | Set Battery Critical Level |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:19` | 3 | Set Smart illumination Control |
| `07:20` | 1 | Set Battery Discharge Curve |
| `07:21` | 3 | Set indicator LEDs Configuration |
| `07:128` | 2 | Get Battery Level |
| `07:129` | 1 | Get Battery Critical Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:142` | 3 | Get Power Security Status |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:147` | 3 | Get Smart illumination Control |
| `07:148` | 1 | Get Battery Discharge Curve |
| `07:149` | 3 | Get indicator LEDs Configuration |
| `07:150` | 1 | Get Dongle Connection Quality Status |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:10` | 2 | Set Audio Source |
| `08:12` | 11 | Set Dolby State |
| `08:17` | 2 | Set audio output source |
| `08:18` | 2 | Set EQ night mode |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:138` | 2 | Get Audio Source |
| `08:139` | 5 | Get Audio I/O Status |
| `08:140` | 11 | Get Dolby State |
| `08:145` | 2 | Get audio output source |
| `08:146` | 2 | Get EQ night mode |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `11:02` | 8 | Set Proximity Sensor Threshold |
| `11:03` | 3 | Set Proximity Sensor State |
| `11:05` | 10 | Set Proximity Sensor Configuration |
| `11:08` | 50 | Set Proximity Sensor Calibration Data |
| `11:09` | 4 | Set Proximity Sensor Control |
| `11:11` | 4 | Set Proximity Sensor Lift Setting |
| `11:12` | 18 | Set Proximity Sensor Lift To Track Calibration Data |
| `11:13` | 18 | Set Proximity Sensor Track To Lift Calibration Data |
| `11:130` | 8 | Get Proximity Sensor Threshold |
| `11:131` | 3 | Get Proximity Sensor State |
| `11:133` | 10 | Get Proximity Sensor Configuration |
| `11:136` | 50 | Get Proximity Sensor Calibration Data |
| `11:137` | 4 | Get Proximity Sensor Control |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:13` | 3 | Set Thermal Fan Duty Cycle |
| `13:16` | 3 | Set Thermal Fan Cooling Mode |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:133` | 80 | Get Thermal Reading |
| `13:141` | 3 | Get Thermal Fan Duty Cycle |
| `13:144` | 3 | Get Thermal Fan Cooling Mode |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:08` | 7 | Set Chroma Stripe LED Number |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:19` | 4 | Set Chroma Tap |
| `15:128` | 80 | Get Chroma Region ID List |
| `15:130` | 80 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:136` | 0 | Get Chroma Stripe LED Number |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:147` | 4 | Get Chroma Tap |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P23 — 1 设备 / 158 命令

设备：`02E1` Razer Blade 18

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:89` | 3 | Set System Multi Function Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:10` | 5 | Set Time To Dim |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:129` | 0 | Get Battery Level |
| `05:138` | 1 | Get Max Profiles Supported |
| `05:141` | 3 | Get Low Power Mode State |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:130` | 0 | Get Debounce Time |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Device Game Mode Selection |
| `15:148` | 2 | Get Thermal Fan Current Speed |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:131` | 0 | Get Chroma Effect |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P24 — 1 设备 / 156 命令

设备：`02E0` Razer Blade 16

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:89` | 3 | Set System Multi Function Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:10` | 5 | Set Time To Dim |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:129` | 0 | Get Battery Level |
| `05:138` | 1 | Get Max Profiles Supported |
| `05:141` | 3 | Get Low Power Mode State |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:130` | 0 | Get Debounce Time |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:131` | 0 | Get Chroma Effect |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P25 — 1 设备 / 155 命令

设备：`02E3` Razer Reclusa X Mini 65%

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:08` | 4 | Set Game Mode State |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:88` | 80 | Set Multiple Device Wired Connection Status |
| `00:89` | 3 | Set System Multi Function Status |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:136` | 4 | Get Game Mode State |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:216` | 80 | Get Multiple Device Wired Connection Status |
| `00:218` | 3 | Get Device Game Mode Selection |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:10` | 5 | Set Time To Dim |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:129` | 0 | Get Battery Level |
| `05:138` | 1 | Get Max Profiles Supported |
| `05:141` | 3 | Get Low Power Mode State |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:130` | 0 | Get Debounce Time |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:23` | 80 | Set Trigger Firmware Effects |
| `15:24` | 2 | Set Startup Animation Control |
| `15:130` | 80 | Get Chroma Effect |
| `15:152` | 1 | Get Startup Animation Control |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P26 — 1 设备 / 138 命令

设备：`0F5A` Razer Laptop Monitor Stand Chroma

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:05` | 1 | Set Device Polling Period |
| `00:06` | 3 | Set Edition Information |
| `00:11` | 1 | Set Device Reset |
| `00:14` | 2 | Set Device Profile Polling Period |
| `00:51` | 1 | Set Device Ambidextrous Information |
| `00:54` | 80 | Set Device HW Info Force Check |
| `00:55` | 1 | Set Device External Power Supply Status |
| `00:60` | 3 | Set Dock Edition Information |
| `00:64` | 2 | Set USB High Speed Polling Period |
| `00:65` | 3 | Set Device Pairing Mode |
| `00:66` | 2 | Set Device Unpair |
| `00:68` | 1 | Set Hardware Auto Detect Ctrl |
| `00:71` | 2 | Set Active Wireless Connection State |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:133` | 1 | Get Device Polling Period |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:142` | 2 | Get Device Profile Polling Period |
| `00:147` | 4 | Get Dock Extended Firmware Version |
| `00:179` | 1 | Get Device Ambidextrous Information |
| `00:181` | 80 | Get Device HW Info |
| `00:183` | 1 | Get Device External Power Supply Status |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `00:185` | 1 | Get Mouse Side Pad Connection Status |
| `00:188` | 3 | Get Dock Edition Information |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:192` | 2 | Get USB High Speed Polling Period |
| `00:196` | 1 | Get Hardware Auto Detect Ctrl |
| `00:197` | 3 | Get Multiple Device Primary Device |
| `00:199` | 2 | Get Active Wireless Connection State |
| `00:215` | 1 | Get Device Power Button Status |
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:04` | 1 | Set Device Polling Period |
| `01:10` | 1 | Set Device Ambidextrous Information |
| `01:15` | 1 | Set Device External Power Supply Status |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:20` | 0 | Set Device HW Info Force Check |
| `01:22` | 1 | Set Device Hardware Auto Detect Ctrl |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:132` | 0 | Get Device Polling Period |
| `01:134` | 0 | Get Edition Information |
| `01:138` | 0 | Get Device Ambidextrous Information |
| `01:140` | 0 | Get Device Profile Polling Period |
| `01:143` | 0 | Get Device External Power Supply Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `01:149` | 0 | Get Device HW Info |
| `01:150` | 0 | Get Device Hardware Auto Detect Ctrl |
| `03:00` | 3 | Set LED State |
| `03:02` | 3 | Set LED Effect |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:130` | 3 | Get LED Effect |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `07:01` | 1 | Set Battery Critical Level |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:11` | 5 | Set Time To Dim |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:19` | 3 | Set Smart illumination Control |
| `07:20` | 1 | Set Battery Discharge Curve |
| `07:21` | 3 | Set indicator LEDs Configuration |
| `07:128` | 2 | Get Battery Level |
| `07:129` | 1 | Get Battery Critical Level |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:139` | 5 | Get Time To Dim |
| `07:142` | 3 | Get Power Security Status |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:147` | 3 | Get Smart illumination Control |
| `07:148` | 1 | Get Battery Discharge Curve |
| `07:149` | 3 | Get indicator LEDs Configuration |
| `07:150` | 1 | Get Dongle Connection Quality Status |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:10` | 2 | Set Audio Source |
| `08:12` | 11 | Set Dolby State |
| `08:17` | 2 | Set audio output source |
| `08:18` | 2 | Set EQ night mode |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:138` | 2 | Get Audio Source |
| `08:139` | 5 | Get Audio I/O Status |
| `08:140` | 11 | Get Dolby State |
| `08:145` | 2 | Get audio output source |
| `08:146` | 2 | Get EQ night mode |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:13` | 3 | Set Thermal Fan Duty Cycle |
| `13:16` | 3 | Set Thermal Fan Cooling Mode |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:133` | 80 | Get Thermal Reading |
| `13:141` | 3 | Get Thermal Fan Duty Cycle |
| `13:144` | 3 | Get Thermal Fan Cooling Mode |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:08` | 7 | Set Chroma Stripe LED Number |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:19` | 4 | Set Chroma Tap |
| `15:128` | 80 | Get Chroma Region ID List |
| `15:130` | 80 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:136` | 0 | Get Chroma Stripe LED Number |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:147` | 4 | Get Chroma Tap |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P27 — 1 设备 / 135 命令

设备：`02EB` Tartarus V2 Pro

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:00` | 3 | Set LED State |
| `03:01` | 5 | Set LED RGB |
| `03:02` | 3 | Set LED Effect |
| `03:06` | 0 | Delete Profile |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:32` | 3 | Set LED Get GPU Card Detection |
| `03:128` | 3 | Get LED State |
| `03:129` | 5 | Get LED RGB |
| `03:130` | 3 | Get LED Effect |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `03:160` | 3 | Get LED Get GPU Card Detection |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:04` | 1 | Set Active Profile ID |
| `05:10` | 5 | Set Time To Dim |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:129` | 0 | Get Battery Level |
| `05:132` | 1 | Get Active Profile ID |
| `05:138` | 1 | Get Max Profiles Supported |
| `05:141` | 3 | Get Low Power Mode State |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:13` | 50 | Set Knob Configuration Information |
| `06:14` | 8 | Set Current Knob Mode |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:141` | 50 | Get Knob Configuration Information |
| `06:142` | 8 | Get Current Knob Mode |
| `07:02` | 4 | Set Debounce Time |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:130` | 0 | Get Debounce Time |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:23` | 80 | Set Trigger Firmware Effects |
| `15:24` | 2 | Set Startup Animation Control |
| `15:130` | 80 | Get Chroma Effect |
| `15:152` | 1 | Get Startup Animation Control |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P28 — 1 设备 / 126 命令

设备：`055E` Razer Nommo V2 X

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:00` | 25 | Get Sidetone Volume |
| `00:01` | 150 | Set Mic Preset EQ Index |
| `00:02` | 22 | Set Serial Number |
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:10` | 149 | Set Customer EQ Band |
| `00:11` | 1 | Set Device Reset |
| `00:129` | 2 | Get Firmware Version |
| `00:130` | 22 | Get Serial Number |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:135` | 4 | Get Extended Firmware Version |
| `00:145` | 2 | Get Dock Firmware Version |
| `00:146` | 22 | Get Dock Serial Number |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `04:00` | 253 | Get Link Status |
| `04:02` | 253 | Get Haptics Control |
| `04:07` | 253 | Get Extended Firmware Version |
| `04:18` | 255 | Set Silence Detection Control |
| `07:08` | 3 | Set Auto Sleep Mode |
| `07:128` | 2 | Get Battery Level |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 3 | Get Auto Sleep Mode |
| `08:02` | 2 | Set EQ Preset |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:08` | 2 | Set mic Mute State |
| `08:09` | 2 | Set Mute State |
| `08:10` | 2 | Set Audio Source |
| `08:20` | 2 | Set Peaking indicator |
| `08:21` | 2 | Set HPF State |
| `08:22` | 3 | Set Audio input control State |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:25` | 2 | Set Combo Mode |
| `08:26` | 9 | Set Parametric EQ Setting |
| `08:28` | 2 | Set AI Noise Suppression |
| `08:29` | 9 | Set Mic Compressor |
| `08:30` | 2 | Set Mic De-Esser |
| `08:31` | 3 | Set Mic Reverb |
| `08:32` | 2 | Set Mic Limiter |
| `08:34` | 2 | Set Return Switch Status |
| `08:130` | 2 | Get EQ Preset |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:136` | 2 | Get Mic Mute State |
| `08:137` | 2 | Get Mute State |
| `08:138` | 2 | Get Audio Source |
| `08:148` | 2 | Get Peaking indicator |
| `08:149` | 2 | Get HPF State |
| `08:150` | 3 | Get Audio input control State |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `08:154` | 9 | Get Parametric EQ Setting |
| `08:156` | 2 | Get AI Noise Suppression |
| `08:157` | 9 | Get Mic Compressor |
| `08:158` | 2 | Get Mic De-Esser |
| `08:159` | 3 | Get Mic Reverb |
| `08:160` | 2 | Get Mic Limiter |
| `08:162` | 2 | Get Return Switch Status |
| `11:19` | 37 | Set Proximity Sensor Acceleration curve data |
| `11:147` | 37 | Get Proximity Sensor Acceleration curve data |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:04` | 3 | Set Chroma Brightness |
| `15:16` | 1 | Set Force Chroma Ctrl |
| `15:18` | 3 | Set Audio Chroma display Switch |
| `15:19` | 4 | Set Chroma Tap |
| `15:130` | 2 | Get Chroma Effect |
| `15:132` | 3 | Get Chroma Brightness |
| `15:144` | 1 | Get Force Chroma Ctrl |
| `15:146` | 3 | Get Audio Chroma display Switch |
| `15:147` | 4 | Get Chroma Tap |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 11 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:128` | 4 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:141` | 2 | Get OLED Home Screen Active Item |
| `23:143` | 22 | Get OLED Image GUID |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `24:05` | 2 | Set Audio DND Status |
| `24:15` | 2 | Set Capture Noise Reduction |
| `24:21` | 2 | Set Audio DND Status |
| `24:128` | 2 | Get EQ Preset |
| `24:133` | 2 | Get Audio DND Status |
| `24:143` | 2 | Get Capture Noise Reduction |

### P29 — 1 设备 / 123 命令

设备：`02F4` Razer Reclusa Mini 65%

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `01:01` | 0 | Set Device Reset |
| `01:02` | 2 | Set Device Mode |
| `01:32` | 1 | Set Layout Switch |
| `01:128` | 255 | Get Thermal Fan Id List |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:160` | 1 | Get Layout Switch |
| `03:06` | 0 | Delete Profile |
| `03:18` | 50 | Set Knob Configuration Information |
| `03:19` | 8 | Set Current Knob Mode |
| `03:146` | 50 | Get Knob Configuration Information |
| `03:147` | 8 | Get Current Knob Mode |
| `04:03` | 255 | Set Macro Name |
| `04:131` | 0 | Get Macro Name |
| `05:02` | 1 | Create New Profile |
| `05:04` | 1 | Set Active Profile ID |
| `05:10` | 5 | Set Time To Dim |
| `05:11` | 1 | Set Profile Reset |
| `05:128` | 1 | Get Number of Profiles |
| `05:129` | 0 | Get Battery Level |
| `05:132` | 1 | Get Active Profile ID |
| `05:138` | 1 | Get Max Profiles Supported |
| `05:141` | 3 | Get Low Power Mode State |
| `06:03` | 2 | Delete Macro |
| `06:08` | 6 | Allocate Macro Data Size |
| `06:09` | 80 | Set Macro Memory Data |
| `06:128` | 2 | Get Number of Macros |
| `06:136` | 6 | Get Macro Data Size |
| `06:137` | 80 | Get Macro Memory Data |
| `06:142` | 14 | Get Macro Storage Info |
| `07:02` | 4 | Set Debounce Time |
| `07:03` | 2 | Set Time to Sleep Mode on Battery Mode |
| `07:08` | 2 | Set Auto Sleep Mode |
| `07:10` | 16 | Set Snap Tap Ctrl |
| `07:11` | 5 | Set Time To Dim |
| `07:15` | 1 | Set CPU Boost Control |
| `07:16` | 1 | Set Indicator Led Configuration |
| `07:18` | 1 | Set Battery Charge Limiter Control |
| `07:128` | 2 | Get Battery Level |
| `07:130` | 0 | Get Debounce Time |
| `07:131` | 2 | Get Time to Sleep Mode on Battery Mode |
| `07:132` | 2 | Get Device Charging Status |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:138` | 16 | Get Snap Tap Ctrl |
| `07:139` | 5 | Get Time To Dim |
| `07:140` | 2 | Get Adapter Wattage Level |
| `07:143` | 1 | Get CPU Boost Control |
| `07:144` | 1 | Get Indicator Led Configuration |
| `07:146` | 1 | Get Battery Charge Limiter Control |
| `07:149` | 1 | Get Low Power Mode State |
| `08:04` | 255 | Set Single Button Assignment |
| `08:05` | 1 | Set Scroll Free-Notched Mode |
| `08:132` | 0 | Get Single Button Assignment |
| `08:133` | 1 | Get Scroll Free-Notched Mode |
| `13:01` | 3 | Set Thermal Fan Speed |
| `13:02` | 4 | Set Thermal Fan Mode |
| `13:07` | 3 | Set Thermal Advance Fan Mode |
| `13:09` | 2 | Set VGA Configure Setting |
| `13:14` | 1 | Set Mini LED Panel Resolution |
| `13:128` | 80 | Get Thermal Fan Id List |
| `13:129` | 3 | Get Thermal Fan Speed |
| `13:130` | 4 | Get Thermal Fan Mode |
| `13:131` | 12 | Get Thermal Fan Information |
| `13:135` | 3 | Get Thermal Advance Fan Mode |
| `13:136` | 3 | Get Thermal Fan Current Speed |
| `13:137` | 2 | Get VGA Configure Setting |
| `13:138` | 2 | Get VGA Hardware Information |
| `13:139` | 2 | Get Synapse UI Setting |
| `13:142` | 1 | Get Mini LED Panel Resolution |
| `13:143` | 1 | Get SKU hardware configuration |
| `15:02` | 80 | Set Chroma Effect |
| `15:03` | 80 | Set Chroma Frame |
| `15:23` | 80 | Set Trigger Firmware Effects |
| `15:24` | 2 | Set Startup Animation Control |
| `15:130` | 80 | Get Chroma Effect |
| `15:152` | 1 | Get Startup Animation Control |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `23:00` | 4 | Set OLED Command dial active mode |
| `23:01` | 10 | Set OLED Command dial mode state |
| `23:03` | 1 | Set OLED Display brightness |
| `23:04` | 1 | Set OLED Display time to home screen |
| `23:05` | 1 | Set OLED Display language |
| `23:06` | 1 | Set OLED Display time to dim |
| `23:09` | 40 | Set OLED Update System Information |
| `23:10` | 33 | Set OLED Update Audio Meter Display |
| `23:11` | 2 | Set OLED Update Volume |
| `23:13` | 2 | Set OLED Home Screen Active Item |
| `23:15` | 2 | Set OLED Screensaver Option |
| `23:18` | 1 | Set OLED Low Battery Warning Percentage |
| `23:19` | 10 | Set OLED Home Screen Image State |
| `23:20` | 6 | Set OLED Home Screen Animation State |
| `23:21` | 10 | Set OLED Home Screen Image State |
| `23:22` | 2 | Set OLED Screensaver Option |
| `23:24` | 3 | Set Chroma Brightness Status |
| `23:128` | 3 | Get OLED Command dial active mode |
| `23:129` | 11 | Get OLED Command dial mode state |
| `23:131` | 1 | Get OLED Display brightness |
| `23:132` | 1 | Get OLED Display time to home screen |
| `23:133` | 1 | Get OLED Display language |
| `23:134` | 1 | Get OLED Display time to dim |
| `23:136` | 2 | Get OLED Home Screen Active Item |
| `23:137` | 1 | Get OLED Display language |
| `23:141` | 1 | Get OLED Low Battery Warning Percentage |
| `23:143` | 2 | Get OLED Screensaver Option |
| `23:146` | 1 | Get OLED Low Battery Warning Percentage |
| `23:147` | 10 | Get OLED Home Screen Image State |
| `23:148` | 6 | Get OLED Home Screen Animation State |
| `23:149` | 10 | Get OLED Home Screen Image State |
| `23:150` | 2 | Get OLED Screensaver Option |
| `23:152` | 3 | Get Chroma Brightness Status |
| `24:04` | 3 | Set Audio Mute Status |

### P30 — 1 设备 / 43 命令

设备：`00E7` RAZER NAGA V3 PRO

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `01:02` | 2 | Set Device Mode |
| `01:19` | 2 | Set Device Wireless Connection Status |
| `01:130` | 0 | Get Device Mode |
| `01:131` | 0 | Get Serial Number |
| `01:134` | 0 | Get Edition Information |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:147` | 0 | Get Device Wireless Connection Status |
| `03:06` | 0 | Delete Profile |
| `03:137` | 0 | Get Max Profiles Supported |
| `05:128` | 1 | Get Number of Profiles |
| `08:04` | 255 | Set Single Button Assignment |
| `08:23` | 2 | Set Mic Volume |
| `08:24` | 2 | Set Dial Control |
| `08:132` | 11 | Get EQ Setting |
| `08:151` | 2 | Get Mic Volume |
| `08:152` | 2 | Get Dial Control |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `15:02` | 6 | Set Proximity Sensor Threshold |
| `15:03` | 1 | Set Proximity Sensor State |
| `15:05` | 9 | Set Proximity Sensor Configuration |
| `15:08` | 48 | Set Proximity Sensor Calibration Data |
| `15:09` | 2 | Set Proximity Sensor Control |
| `15:11` | 2 | Set Proximity Sensor Lift Setting |
| `15:12` | 16 | Set Proximity Sensor Lift To Track Calibration Data |
| `15:13` | 16 | Set Proximity Sensor Track To Lift Calibration Data |
| `15:130` | 0 | Get Proximity Sensor Threshold |
| `15:131` | 0 | Get Proximity Sensor State |
| `15:133` | 0 | Get Proximity Sensor Configuration |
| `15:136` | 0 | Get Proximity Sensor Calibration Data |
| `15:137` | 0 | Get Proximity Sensor Control |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P31 — 1 设备 / 40 命令

设备：`0301` RAZER PHILIPS HUE

> 这一组不是 Hue 的真实报文。`0301` 的 bundle 里只有 Philips 原生 DLL 的调用壳（`philipsEngineManager` → `callDLLApi`），下表是共享鼠标库被一并打包进该 bundle 后产生的噪声，不要据此认为 Hue 有 HID 命令。

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `00:199` | 2 | Get Active Wireless Connection State |
| `01:131` | 0 | Get Serial Number |
| `01:144` | 0 | Get Device On Dock Charging Status |
| `01:145` | 0 | Get Mouse Side Pad Connection Status |
| `03:06` | 0 | Delete Profile |
| `03:137` | 0 | Get Max Profiles Supported |
| `05:128` | 1 | Get Number of Profiles |
| `07:162` | 0 | Exit Dim Status |
| `08:04` | 255 | Set Single Button Assignment |
| `08:23` | 2 | Set Mic Volume |
| `08:132` | 11 | Get EQ Setting |
| `08:151` | 2 | Get Mic Volume |
| `12:28` | 2 | Set Controller Mode Switch |
| `12:156` | 2 | Get Controller Mode Switch |
| `15:02` | 6 | Set Proximity Sensor Threshold |
| `15:03` | 1 | Set Proximity Sensor State |
| `15:05` | 9 | Set Proximity Sensor Configuration |
| `15:08` | 48 | Set Proximity Sensor Calibration Data |
| `15:09` | 2 | Set Proximity Sensor Control |
| `15:11` | 2 | Set Proximity Sensor Lift Setting |
| `15:12` | 16 | Set Proximity Sensor Lift To Track Calibration Data |
| `15:13` | 16 | Set Proximity Sensor Track To Lift Calibration Data |
| `15:130` | 0 | Get Proximity Sensor Threshold |
| `15:131` | 0 | Get Proximity Sensor State |
| `15:133` | 0 | Get Proximity Sensor Configuration |
| `15:136` | 0 | Get Proximity Sensor Calibration Data |
| `15:137` | 0 | Get Proximity Sensor Control |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

### P32 — 1 设备 / 26 命令

设备：`0F66` Razer Marci

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `00:04` | 2 | Set Device Mode |
| `00:06` | 3 | Set Edition Information |
| `00:11` | 1 | Set Device Reset |
| `00:132` | 2 | Get Device Mode |
| `00:134` | 3 | Get Edition Information |
| `00:184` | 1 | Get Device Wireless Connection Status |
| `00:191` | 80 | Get Multiple Device Wireless Connection Status V2 |
| `07:08` | 3 | Set Auto Sleep Mode |
| `07:136` | 2 | Get Auto Sleep Mode |
| `07:162` | 0 | Exit Dim Status |
| `08:04` | 11 | Set EQ Setting |
| `08:07` | 2 | Set Bass Level |
| `08:09` | 2 | Set Mute State |
| `08:23` | 2 | Set Mic Volume |
| `08:25` | 2 | Set Combo Mode |
| `08:132` | 11 | Get EQ Setting |
| `08:135` | 2 | Get Bass Level |
| `08:137` | 2 | Get Mute State |
| `08:151` | 2 | Get Mic Volume |
| `24:05` | 2 | Set Audio DND Status |
| `24:21` | 2 | Set Audio DND Status |
| `24:133` | 2 | Get Audio DND Status |

### P33 — 1 设备 / 14 命令

设备：`0F43` RAZER LAPTOP COOLING PAD

| class:cmd | dataSize | 命令 |
|---|---:|---|
| `07:162` | 0 | Exit Dim Status |
| `08:04` | 11 | Set EQ Setting |
| `08:132` | 11 | Get EQ Setting |
| `15:132` | 3 | Get Chroma Brightness |
| `16:03` | 10 | Set Chroma Effect |
| `16:04` | 255 | Set Chroma Frame |
| `16:05` | 1 | Set Chroma Brightness |
| `16:08` | 7 | Set Chroma Stripe LED Number |
| `16:131` | 0 | Get Chroma Effect |
| `16:133` | 0 | Get Chroma Brightness |
| `16:136` | 0 | Get Chroma Stripe LED Number |
| `17:01` | 6 | Set Thermal Fan Speed |
| `17:129` | 6 | Get Thermal Fan Speed |
| `131:00` | 130 | Get Serial Number |

