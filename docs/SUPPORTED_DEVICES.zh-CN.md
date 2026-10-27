# 支持设备

OpenSynapse 只为已连接且成功识别 Windows HID 端点和协议的设备创建页面。下表按设备类型分组，不按协议或目录来源分组。

当前表格共收录 389 个可识别设备 ID：鼠标 136、键盘 96、笔记本 50、耳机 40、音箱 8、麦克风 4、鼠标垫 8、显示器 2、配件 44、网络灯光桥 1。

每一行是一个可识别的设备 ID。没有说明的行使用该型号的标准控制集；有说明的行标注专用页面、额外能力、仅灯光、只读探针或备用接口。部分实体设备会暴露多个 PID，因此每个可识别 PID 都单独列出，方便直接对照设备管理器。

Chroma REST 是软件集成，不是设备，因此不放进下表。

## 鼠标

标准鼠标控制包括 DPI、轮询率、电量、灯光和板载映射；设备不支持的项目会自动隐藏。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Razer Abyssus | `1532:0042` | — |
| Razer Abyssus 1800 | `1532:0020` | — |
| Razer Abyssus 2000 | `1532:005E` | — |
| Razer Abyssus Elite (D.Va Edition) | `1532:006A` | — |
| Razer Abyssus Essential | `1532:006B` | — |
| Razer Abyssus Lite | `1532:0074` | DPI、轮询率、电量、灯光和板载映射 |
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
| Razer Basilisk V3 Pro (Wired) | `1532:00AA` | DPI、轮询率、电量、灯光和板载映射；蓝牙连接时提供 BLE 页面 |
| Razer Basilisk V3 Pro (Wireless) | `1532:00AB` | DPI、轮询率、电量、灯光和板载映射；蓝牙连接时提供 BLE 页面 |
| Razer Basilisk V3 Pro 35K (Wired) | `1532:00CC` | — |
| Razer Basilisk V3 Pro 35K (Wireless) | `1532:00CD` | — |
| Razer Basilisk V3 Pro 35K Phantom Green Edition (Wired) | `1532:00D6` | — |
| Razer Basilisk V3 Pro 35K Phantom Green Edition (Wireless) | `1532:00D7` | — |
| Razer Basilisk V3 X HyperSpeed | `1532:00B9` | — |
| Razer Basilisk V4 HyperSpeed (Wired) | `1532:00EB` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Basilisk V4 HyperSpeed (Wireless) | `1532:00EA` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Basilisk V4 Pro (Wired) | `1532:00E2` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Basilisk V4 Pro (Wireless) | `1532:00E3` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Basilisk X HyperSpeed | `1532:0083` | — |
| Razer Cobra | `1532:00A2` | 通过中间件协议提供 DPI、轮询率和板载映射 |
| Razer Cobra | `1532:00A3` | — |
| Razer Cobra HyperSpeed (Wired) | `1532:00DA` | — |
| Razer Cobra HyperSpeed (Wireless) | `1532:00DB` | — |
| Razer Cobra Pro (Wired) | `1532:00AF` | — |
| Razer Cobra Pro (Wireless) | `1532:00B0` | — |
| Razer DeathAdder 1800 | `1532:0038` | — |
| Razer DeathAdder 2000 | `1532:004F` | — |
| Razer DeathAdder 2000 (read-only probe) | `1532:004D` | 只读探针：固件、序列号、DPI、轮询率和亮度 |
| Razer DeathAdder 2000 (read-only probe) | `1532:005F` | 只读探针：固件、序列号、DPI、轮询率和亮度 |
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
| Razer DeathAdder V3 Pro (Wired) | `1532:00C2` | — |
| Razer DeathAdder V3 Pro (Wireless) | `1532:00B7` | — |
| Razer DeathAdder V3 Pro (Wireless) | `1532:00C3` | — |
| Razer DeathAdder V4 Pro (Wired) | `1532:00BE` | — |
| Razer DeathAdder V4 Pro (Wireless) | `1532:00BF` | — |
| Razer DeathAdder V4 Pro Carbon Fiber Edition (Wired) | `1532:00EF` | DPI、轮询率、电量和板载映射；无灯光 |
| Razer DeathAdder V4 Pro Carbon Fiber Edition (Wireless) | `1532:00F0` | DPI、轮询率、电量和板载映射；无灯光 |
| Razer Diamondback Chroma | `1532:004C` | — |
| Razer HyperFlux V2 | `1532:00CF` | 仅灯光 |
| Razer HyperPolling Wireless Dongle | `1532:00B3` | — |
| Razer Imperator 2012 | `1532:002F` | — |
| Razer Jugan | `1532:0063` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Krakoff | `1532:00DD` | DPI、轮询率、电量、灯光和板载映射 |
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
| Razer Mamba Hyperflux | `1532:0069` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Mamba Tournament Edition | `1532:0046` | — |
| Razer Mamba Wireless (Receiver) | `1532:0072` | — |
| Razer Mamba Wireless (Wired) | `1532:0073` | — |
| Razer Naga | `1532:0015` | — |
| Razer Naga 2012 | `1532:002E` | — |
| Razer Naga 2014 | `1532:0040` | — |
| Razer Naga Chroma | `1532:0053` | — |
| Razer Naga Classic Edition | `1532:0093` | DPI、轮询率、电量、灯光和板载映射 |
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
| Razer Naga V3 Pro (Wired) | `1532:00E7` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Naga V3 Pro (Wireless) | `1532:00E8` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Naga X | `1532:0096` | — |
| Razer Orochi (Wired) | `1532:0048` | — |
| Razer Orochi 2011 | `1532:0013` | — |
| Razer Orochi 2013 | `1532:0039` | — |
| Razer Orochi V2 | `1532:00E0` | DPI、轮询率、电量、灯光和板载映射 |
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
| Razer Turret Mouse Xbox One Edition | `1532:0075` | DPI、轮询率、电量、灯光和板载映射 |
| Razer Viper | `1532:0078` | — |
| Razer Viper 8KHz | `1532:0091` | — |
| Razer Viper Mini | `1532:008A` | — |
| Razer Viper Mini SE (Wired) | `1532:009E` | — |
| Razer Viper Mini SE (Wireless) | `1532:009F` | — |
| Razer Viper Ultimate (Wired) | `1532:007A` | — |
| Razer Viper Ultimate (Wireless) | `1532:007B` | — |
| Razer Viper V2 Pro (Wired) | `1532:00A5` | — |
| Razer Viper V2 Pro (Wireless) | `1532:00A6` | — |
| Razer Viper V3 HyperSpeed | `1532:00B8` | Viper 专用页面：电量、DPI、轮询率、休眠和板载映射 |
| Razer Viper V3 Pro (Wired) | `1532:00C0` | — |
| Razer Viper V3 Pro (Wireless) | `1532:00C1` | — |
| Razer Viper V3 Pro SE (Wired) | `1532:00DE` | 00C0 的别名；Viper V3 Pro 专用控制 |
| Razer Viper V3 Pro SE (Wireless) | `1532:00DF` | 00C1 的别名；Viper V3 Pro 专用控制 |
| Razer Viper V4 Pro (Wired) | `1532:00E5` | DPI、轮询率、电量和板载映射；蓝牙连接时提供 BLE 页面；无灯光 |
| Razer Viper V4 Pro (Wireless) | `1532:00E6` | DPI、轮询率、电量和板载映射；蓝牙连接时提供 BLE 页面；无灯光 |
| RazerBasiliskUltimateReceiver | `1532:0088` | — |

Naga V3 Pro（`00E7`/`00E8`）使用 Naga V2 Pro（`00A8`）协议。Viper V4 Pro 和 DeathAdder V4 Pro Carbon Fiber Edition 没有灯光。Viper V3 HyperSpeed 使用 Viper 专用页面，不使用标准鼠标布局。

## 键盘

标准键盘控制包括固件、序列号、电量、游戏模式、轮询率和灯光；设备不支持的项目会自动隐藏。

| 设备 | USB VID:PID | 说明 |
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
| Razer BlackWidow V4 75% Xbox 25th Anniversary Edition | `1532:02F0` | 键盘页面 |
| Razer BlackWidow V4 Low-profile HyperSpeed | `1532:02CC` | 键盘页面 |
| Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed | `1532:02D4` | 键盘页面 |
| Razer BlackWidow V4 Mini HyperSpeed (Wired) | `1532:02B9` | — |
| Razer BlackWidow V4 Mini HyperSpeed (Wireless) | `1532:02BA` | — |
| Razer BlackWidow V4 Pro | `1532:028D` | — |
| Razer BlackWidow V4 Pro 75% | `1532:02B3` | 键盘页面 |
| Razer BlackWidow V4 Tenkeyless HyperSpeed (Wired) | `1532:02D7` | — |
| Razer BlackWidow V4 Tenkeyless HyperSpeed (Wireless) | `1532:02D5` | — |
| Razer BlackWidow V4 X | `1532:0293` | — |
| Razer BlackWidow X Chroma | `1532:0216` | — |
| Razer BlackWidow X Tenkeyless | `1532:024F` | 键盘页面 |
| Razer BlackWidow X Tournament Edition Chroma | `1532:021A` | — |
| Razer BlackWidow X Ultimate | `1532:0217` | — |
| Razer Cynosa Chroma | `1532:022A` | — |
| Razer Cynosa Chroma Pro | `1532:022C` | — |
| Razer Cynosa Lite | `1532:023F` | — |
| Razer Cynosa V2 | `1532:025E` | — |
| Razer Cynosapro | `1532:028E` | 键盘页面 |
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
| Razer Huntsman Essential | `1532:0250` | 键盘页面 |
| Razer Huntsman Mini | `1532:0257` | — |
| Razer Huntsman Mini Analog | `1532:0282` | — |
| Razer Huntsman Mini JP | `1532:0269` | — |
| Razer Huntsman Signature Edition | `1532:02D8` | 键盘页面 |
| Razer Huntsman Tournament Edition | `1532:0243` | — |
| Razer Huntsman V2 | `1532:026C` | — |
| Razer Huntsman V2 Analog | `1532:0266` | — |
| Razer Huntsman V2 Tenkeyless | `1532:026B` | — |
| Razer Huntsman V3 HE Magnetic Mini 65% 8KHz | `1532:02E4` | 键盘页面 |
| Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz | `1532:02EA` | 键盘页面 |
| Razer Huntsman V3 Pro | `1532:02A6` | — |
| Razer Huntsman V3 Pro 8KHz | `1532:02CF` | — |
| Razer Huntsman V3 Pro Low-Profile Tenkeyless 8KHz | `1532:02E6` | 键盘页面 |
| Razer Huntsman V3 Pro Mini | `1532:02B0` | — |
| Razer Huntsman V3 Pro Mini 8KHz | `1532:02D1` | 键盘页面 |
| Razer Huntsman V3 Pro Tenkeyless 8KHz | `1532:02D0` | 键盘页面 |
| Razer Huntsman V3 Pro TKL | `1532:02A7` | — |
| Razer Huntsman V3 Tenkeyless 8KHz | `1532:02E5` | 键盘页面 |
| Razer Huntsman V3 X Tenkeyless | `1532:02B1` | 键盘页面 |
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
| Razer Pikachu Limited Edition Backlit Keyboard | `1532:0251` | 键盘页面 |
| Razer Pro Type | `1532:0249` | 键盘页面 |
| Razer Pro Type Ergo | `1532:02C4` | 键盘页面 |
| Razer Pro Type Ultra | `1532:0277` | 键盘页面 |
| Razer Reclusa Mini 65% | `1532:02F4` | 键盘页面 |
| Razer Reclusa X Mini 65% | `1532:02E3` | 键盘页面 |
| Razer Tartarus | `1532:0201` | — |
| Razer Tartarus Chroma | `1532:0208` | — |
| Razer Tartarus Pro | `1532:0244` | — |
| Razer Tartarus V2 | `1532:022B` | — |
| Razer Tartarus V2 Pro | `1532:02EB` | 键盘页面 |
| Razer Turret Keyboard Xbox One Edition | `1532:023E` | 键盘页面 |

## 笔记本

带专用 manifest 的 Blade 机型提供遥测、性能、风扇、显示、电池和键盘灯光；普通笔记本条目提供对应目录中的标准控制。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Razer Blade (Late 2016) | `1532:0224` | — |
| Razer Blade (QHD) | `1532:020F` | — |
| Razer Blade 14 (2021) | `1532:0270` | — |
| Razer Blade 14 (2022) | `1532:028C` | Blade 专用控制；自定义模式下固定转速 3500–5000 RPM |
| Razer Blade 14 (2023) | `1532:029D` | Blade 专用控制 |
| Razer Blade 14 (2024) | `1532:02B6` | Blade 专用控制 |
| Razer Blade 14 (2025) | `1532:02C5` | Blade 专用控制 |
| Razer Blade 15 (2018) | `1532:0233` | — |
| Razer Blade 15 (2018) Base Model | `1532:023B` | — |
| Razer Blade 15 (2018) Mercury | `1532:0240` | — |
| Razer Blade 15 (2019) Advanced | `1532:023A` | — |
| Razer Blade 15 (2023) | `1532:029E` | — |
| Razer Blade 15 (Mid 2019) Base Model | `1532:0246` | — |
| Razer Blade 15 (Mid 2019) Mercury (read-only probe) | `1532:0245` | Blade 探针控制：遥测、风扇、电源、充电上限、键盘灯光和游戏模式 |
| Razer Blade 15 Advanced (2020) | `1532:0253` | — |
| Razer Blade 15 Advanced (Early 2021) | `1532:026D` | — |
| Razer Blade 15 Advanced (Early 2022) | `1532:028A` | — |
| Razer Blade 15 Advanced (Mid 2021) | `1532:0276` | — |
| Razer Blade 15 Studio Edition (2019) | `1532:024D` | — |
| Razer Blade 16 (2023) | `1532:029F` | Blade 专用控制；固件渲染键盘灯效 |
| Razer Blade 16 (2024) | `1532:02B7` | Blade 专用控制 |
| Razer Blade 16 (2025) | `1532:02C6` | Blade 专用控制 |
| Razer Blade 16 (2026) | `1532:02E0` | 仅标准灯光；遥测、风扇和 MUX 需要专用 Blade manifest |
| Razer Blade 17 (2022) | `1532:028B` | — |
| Razer Blade 17 Pro (Early 2021) | `1532:026E` | — |
| Razer Blade 17 Pro (Mid 2021) | `1532:0279` | — |
| Razer Blade 18 (2023) | `1532:02A0` | — |
| Razer Blade 18 (2024) | `1532:02B8` | Blade 专用控制 |
| Razer Blade 18 (2025) | `1532:02C7` | Blade 专用控制 |
| Razer Blade 18 (2026) | `1532:02E1` | 仅标准灯光；遥测、风扇和 MUX 需要专用 Blade manifest |
| Razer Blade Advanced (Late 2019) | `1532:024B` | — |
| Razer Blade Base (Early 2020) | `1532:0255` | Blade 专用控制；百分比风扇目标不伪装成 RPM |
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

Blade 16 (2023) 只提供固件渲染的关闭、呼吸、光谱和波浪效果，没有逐键矩阵通道。Blade 16 (2026) 和 Blade 18 (2026) 目前只有标准目录灯光；遥测、风扇和显示 MUX 仍需要专用 Blade manifest。

## 耳机

音频页按设备和已安装组件提供 EQ、麦克风音量、侧音、ANC、THX 与游戏/语音平衡。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Razer Barracuda | `1532:053C` | 音频页 |
| Razer Barracuda 2.4 | `1532:053B` | 音频页 |
| Razer Barracuda 2.4 | `1532:0584` | 音频页 |
| Razer Barracuda Pro | `1532:0539` | 音频页 |
| Razer Barracuda Pro | `1532:0583` | 音频页 |
| Razer Barracuda V2 Pro | `1532:0593` | 音频页；提供 THX V4 安装入口 |
| Razer BlackShark V2 HyperSpeed | `1532:0565` | 音频页 |
| Razer BlackShark V2 HyperSpeed | `1532:056E` | 音频页 |
| Razer BlackShark V2 Pro (2020) | `1532:0528` | 音频页；提供 THX 安装入口 |
| Razer BlackShark V2 Pro (2023) | `1532:0555` | 音频页；提供 THX 安装入口 |
| Razer BlackShark V3 | `1532:0579` | 音频页 |
| Razer BlackShark V3 Pro (0x0576 interface) | `1532:0576` | 音频页；BlackShark V3 Pro 有线控制接口 |
| Razer BlackShark V3 Pro (2.4 GHz) | `1532:101B` | 音频、ANC、THX 和游戏/语音平衡（2.4 GHz） |
| Razer BlackShark V3 Pro (USB) | `1532:101C` | 音频、ANC、THX 和游戏/语音平衡（USB） |
| Razer Kraken 7.1 | `1532:0501` | 仅灯光 |
| Razer Kraken 7.1 | `1532:0506` | 仅灯光 |
| Razer Kraken 7.1 Chroma | `1532:0504` | 仅灯光 |
| Razer Kraken 7.1 V2 | `1532:0510` | 仅灯光 |
| Razer Kraken BT Kitty Edition | `1532:0530` | 音频页 |
| Razer Kraken BT Sanrio Limited Edition | `1532:0309` | 音频页 |
| Razer Kraken BT Sanrio Limited Edition | `1532:0534` | 音频页 |
| Razer Kraken Kitty Edition | `1532:0521` | 音频页 |
| Razer Kraken Kitty V2 | `1532:0560` | 音频页与灯光 |
| Razer Kraken Kitty V2 BT Quartz Edition | `1532:0562` | 音频页 |
| Razer Kraken Kitty V2 Pro | `1532:0554` | 音频页 |
| Razer Kraken Kitty V3 Pro | `1532:0587` | 音频页；提供 THX V4 安装入口 |
| Razer Kraken Tournament Edition | `1532:0520` | 音频页与灯光；提供 Kraken TE 的 THX 安装入口 |
| Razer Kraken Ultimate | `1532:0527` | 音频页与灯光；提供 THX 安装入口 |
| Razer Kraken V3 | `1532:0549` | 音频页 |
| Razer Kraken V3 HyperSense | `1532:0533` | 音频页 |
| Razer Kraken V3 Pro | `1532:052D` | 音频页 |
| Razer Kraken V3 X | `1532:0537` | 音频页 |
| Razer Kraken V4 | `1532:056B` | 灯光页（有线） |
| Razer Kraken V4 Pro | `1532:0567` | 音频页 |
| Razer Kraken V4 X | `1532:056D` | 音频页 |
| Razer Kraken X USB | `1532:0526` | 音频页 |
| Razer Marci | `1532:0F66` | 音频页 |
| Razer Nari | `1532:051C` | 音频页 |
| Razer Nari Essential | `1532:051E` | 音频页；提供 THX 安装入口 |
| Razer Nari Ultimate | `1532:051A` | 音频页 |

Kraken 7.1 使用 37-byte output-report 通道；Kraken V4 有线版使用 64-byte report-0x02 通道。Kraken V4 的 2.4 GHz 接收器（`056C`）目前未开放。

## 音箱

音箱控制按型号提供 EQ 预设、频段、低音、输入源和灯光。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Razer Leviathan V2 | `1532:0532` | 音频页 |
| Razer Leviathan V2 Pro | `1532:0548` | 音频页 |
| Razer Leviathan V2 X | `1532:054A` | 声霸音频与灯光；仍待实机验证 |
| Razer Nommo Chroma | `1532:0517` | Chroma 灯光 |
| Razer Nommo Pro | `1532:0518` | Chroma 灯光 |
| Razer Nommo V2 | `1532:055C` | 音频页 |
| Razer Nommo V2 Pro | `1532:055A` | 音频页 |
| Razer Nommo V2 X | `1532:055E` | 音频页 |

Leviathan V2 X（`054A`）覆盖 edition ID 0（标准版）、128（Pikachu）、129（Pokémon AP）、131（Kuromi）和 132（Kuromi Edition）；应用会读取 edition 信息区分 Pikachu 和 Kuromi 版本。

## 麦克风

麦克风控制按型号提供 EQ、麦克风音量和灯光效果。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Razer Seiren Emote | `1532:0F1B` | 音频页；8×8 LED 矩阵 |
| Razer Seiren V2 Pro | `1532:0542` | 音频页 |
| Razer Seiren V2 X | `1532:0543` | 音频页 |
| Razer Seiren V3 Mini | `1532:056A` | 音频页 |

## 鼠标垫

鼠标垫提供对应型号的 Chroma 灯光控制。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Razer Firefly (2013) | `1532:0C00` | — |
| Razer Firefly Hyperflux (2018) | `1532:0068` | — |
| Razer Firefly V2 | `1532:0C04` | — |
| Razer Firefly V2 Pro | `1532:0C08` | — |
| Razer Goliathus (2018) | `1532:0C01` | — |
| Razer Goliathus Chroma 3XL | `1532:0C06` | — |
| Razer Goliathus Extended (2018) | `1532:0C02` | — |
| Razer Strider Chroma | `1532:0C05` | — |

## 显示器

Raptor 27 提供对应型号的 Chroma 灯光控制。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Razer Raptor 27 | `1532:0F12` | Chroma 灯光 |
| Razer Raptor 27 | `1532:0F28` | Chroma 灯光 |

## 配件

配件控制包括灯光、扩展坞、支架、风扇控制和在位检测。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Esther | `1532:0F45` | 仅灯光 |
| Lian Li O11 Dynamic - Razer Edition | `1532:0F13` | — |
| MarciT2H | `1532:0F6D` | 仅灯光 |
| Razer Aether Lamp | `1532:030E` | 仅灯光 |
| Razer Aether Lamp Pro | `1532:030D` | 仅灯光 |
| Razer Aether Light Bulb | `1532:030F` | 仅灯光 |
| Razer Aether Light Strip | `1532:0310` | 仅灯光 |
| Razer Aether Monitor Light Bar | `1532:0316` | 仅灯光 |
| Razer Aether Standing Light Bars | `1532:0317` | 仅灯光 |
| Razer Audio Mixer | `1532:053E` | 音频控制器页面；混音器控制 |
| Razer Base Station Chroma (Headphone Stand) | `1532:0F08` | — |
| Razer Base Station V2 Chroma | `1532:0F20` | — |
| Razer Base Station V3 Chroma | `1532:0F6A` | 仅灯光 |
| Razer Charging Pad Chroma | `1532:0F26` | — |
| Razer Chroma Addressable RGB Controller | `1532:030A` | 标准 Chroma 灯光 |
| Razer Chroma Addressable RGB Controller | `1532:0F1F` | 逐灯 ARGB 控制；6 端口 × 80 颗 |
| Razer Chroma Hardware Development Kit (HDK) | `1532:0F09` | — |
| Razer Chroma Mug Holder | `1532:0F07` | — |
| Razer Chroma Wireless ARGB Controller | `1532:0F2C` | 标准 Chroma 灯光 |
| Razer Chroma Wireless ARGB Controller | `1532:0F2E` | 标准 Chroma 灯光 |
| Razer Core | `1532:0215` | — |
| Razer Core X Chroma | `1532:0F1A` | — |
| Razer Core X V2 | `1532:0F51` | 仅灯光 |
| Razer Hanbo AIO | `1532:0F35` | 仅灯光 |
| Razer Handheld Dock Chroma | `1532:0F5C` | 仅灯光 |
| Razer Head Cushion Chroma | `1532:0F36` | 仅灯光 |
| Razer Key Light Chroma | `1532:030C` | 仅灯光 |
| Razer Laptop Cooling Pad | `1532:0F43` | 用于 HyperBoost 在位检测；没有独立控制页 |
| Razer Laptop Monitor Stand Chroma | `1532:0F5A` | 仅灯光 |
| Razer Laptop Stand Chroma | `1532:0F0D` | — |
| Razer Laptop Stand Chroma V2 | `1532:0F2B` | — |
| Razer Monitor Stand Chroma | `1532:0F59` | 仅灯光 |
| Razer Mouse Bungee V3 Chroma | `1532:0F1D` | — |
| Razer Mouse Dock | `1532:007E` | — |
| Razer Mouse Dock Pro | `1532:00A4` | — |
| Razer Mouse Dock V2 Pro | `1532:00F1` | 仅灯光 |
| Razer PWM PC Fan Controller | `1532:0F3C` | 仅灯光 |
| Razer Soma Chroma | `1532:0F64` | 仅灯光 |
| Razer Thunderbolt 4 Dock Chroma | `1532:0F21` | — |
| Razer Thunderbolt 5 Dock Chroma | `1532:0F52` | 仅灯光 |
| Razer Tomahawk ATX | `1532:0F17` | — |
| Razer Tomahawk Gaming Desktop | `1532:0F27` | 仅灯光 |
| Razer USB Sound Card | `1532:0529` | 音频控制器页面；声卡控制 |
| Razer Wireless Control Pod | `1532:0566` | 仅灯光 |

## 网络灯光

Philips Hue 配对后通过桥的 HTTPS API 控制。

| 设备 | USB VID:PID | 说明 |
|---|---|---|
| Philips Hue Bridge | `Network` | 桥配对；开关、亮度和颜色 |

## 能力与验证边界

条目出现在表里表示应用已经有对应代码路径，不代表每个控制项都完成了真机验证。端点无法解析或正被占用时，页面会保持只读，直到下一次扫描。协议允许时写入会读回校验；设备提供旧值时，失败会恢复原值。

THX V3/V4 耳机路径和部分中间件推导设备来自原生客户端与协议证据，标注未实机验证的仍需硬件验收。BLE 设备必须暴露 Razer protocol 30 服务 `52401523`；BLE 页面面向 Basilisk V3 Pro、Viper V4 Pro 这类 protocol 30 鼠标，按设备实际响应提供电量、轮询率、DPI、灯光和按键映射。

外部灯光集成见 [Chroma 支持设备](CHROMA_SUPPORTED_DEVICES.zh-CN.md)。

返回 [README](../README.zh-CN.md)。
