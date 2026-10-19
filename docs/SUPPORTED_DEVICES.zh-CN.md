# 支持设备

OpenSynapse 的支持范围不只有少数产品专用型号。当前支持范围包括：

- **254 个 OpenRazer 目录设备**：覆盖鼠标、键盘、笔记本和配件。
- **2 套产品专用控制**：Blade 和 Viper 中需要使用专用协议的功能。
- **6 款 Kraken 耳机**：支持灯光控制。
- **Chroma REST**：供兼容游戏和外部程序接入灯光。

OpenRazer 部分固定于提交 `6820f9da169d354bc7e6e93a0aa8683a6bb75792`。OpenSynapse 只为已连接且被识别的具体设备创建页面，只有成功解析 Windows HID 端点和事务后才会显示对应控件。

## 产品专用控制

| 设备 | USB VID:PID | 可用功能 |
|---|---|---|
| Razer Blade 16 (2025) | `1532:02C6` | 遥测、键盘灯光、性能、风扇、显示、电池、触控板、充电上限，以及经过验证的 Fn/M3/M4/M5 |
| Razer Blade 16 (2023) | `1532:029F` | 遥测、性能模式、自定义性能档位（含 Boost）、风扇全速、固定风扇转速（2000–5000 RPM）、充电上限、一次性充满、Gaming 模式、键盘亮度与固件原生灯效（关闭、呼吸、光谱、波浪） |
| Razer Viper V3 HyperSpeed | `1532:00B8` | 电量、DPI、轮询率、休眠、电池类型和板载映射 |

Blade 的波浪和光谱循环在未保存颜色调色板时由固件渲染，保存调色板后改用软件逐帧渲染。Blade 16 (2023) 没有逐键矩阵通道，只开放键盘亮度，以及固件能自绘的 4 个灯效（关闭、呼吸、光谱、波浪）；该机型的灯效颜色无法回读，所以自定义颜色、音频律动、逐帧渲染、Chroma 接管、波浪方向选择和导入 .chroma 按钮都不展示。Viper 支持 `125 / 500 / 1000 Hz` 轮询率、`100` 至 `30000` 的 X/Y DPI、最多 5 档 DPI，以及固定 Profile 1 的 Normal/HyperShift 板载映射。

## Kraken 灯光

| 型号 | USB PID | 可用功能 |
|---|---|---|
| Kraken 7.1 | `0501`、`0506` | 灯光 |
| Kraken 7.1 Chroma | `0504` | 灯光 |
| Kraken 7.1 V2 | `0510` | 灯光 |
| Kraken Tournament Edition | `0520` | 灯光 |
| Kraken Ultimate | `0527` | 灯光 |
| Kraken Kitty V2 | `0560` | 灯光 |

Kraken 使用独立的 37-byte Output Report 通道。这里不包含音频、麦克风、EQ 或 THX 控制。

## OpenRazer 目录

固定目录中共有 254 个设备：

- 鼠标：110
- 键盘：72
- 笔记本：46
- 配件：26

### 鼠标（110）

| 设备 | USB VID:PID | 类型 |
|---|---|---|
| Razer Abyssus | `1532:0042` | 鼠标 |
| Razer Abyssus 1800 | `1532:0020` | 鼠标 |
| Razer Abyssus 2000 | `1532:005E` | 鼠标 |
| Razer Abyssus Elite (D.Va Edition) | `1532:006A` | 鼠标 |
| Razer Abyssus Essential | `1532:006B` | 鼠标 |
| Razer Abyssus V2 | `1532:005B` | 鼠标 |
| Razer Atheris (Receiver) | `1532:0062` | 鼠标 |
| Razer Basilisk | `1532:0064` | 鼠标 |
| Razer Basilisk Essential | `1532:0065` | 鼠标 |
| Razer Basilisk Mobile (Receiver) | `1532:00D4` | 鼠标 |
| Razer Basilisk Mobile (Wired) | `1532:00D3` | 鼠标 |
| Razer Basilisk Ultimate | `1532:0086` | 鼠标 |
| Razer Basilisk V2 | `1532:0085` | 鼠标 |
| Razer Basilisk V3 | `1532:0099` | 鼠标 |
| Razer Basilisk V3 35K | `1532:00CB` | 鼠标 |
| Razer Basilisk V3 Pro (Wired) | `1532:00AA` | 鼠标 |
| Razer Basilisk V3 Pro (Wireless) | `1532:00AB` | 鼠标 |
| Razer Basilisk V3 Pro 35K (Wired) | `1532:00CC` | 鼠标 |
| Razer Basilisk V3 Pro 35K (Wireless) | `1532:00CD` | 鼠标 |
| Razer Basilisk V3 Pro 35K Phantom Green Edition (Wired) | `1532:00D6` | 鼠标 |
| Razer Basilisk V3 Pro 35K Phantom Green Edition (Wireless) | `1532:00D7` | 鼠标 |
| Razer Basilisk V3 X HyperSpeed | `1532:00B9` | 鼠标 |
| Razer Basilisk X HyperSpeed | `1532:0083` | 鼠标 |
| Razer Cobra | `1532:00A3` | 鼠标 |
| Razer Cobra Pro (Wired) | `1532:00AF` | 鼠标 |
| Razer Cobra Pro (Wireless) | `1532:00B0` | 鼠标 |
| Razer DeathAdder 1800 | `1532:0038` | 鼠标 |
| Razer DeathAdder 2000 | `1532:004F` | 鼠标 |
| Razer DeathAdder 2013 | `1532:0037` | 鼠标 |
| Razer DeathAdder 3500 | `1532:0054` | 鼠标 |
| Razer DeathAdder Chroma | `1532:0043` | 鼠标 |
| Razer DeathAdder Elite | `1532:005C` | 鼠标 |
| Razer DeathAdder Essential | `1532:006E` | 鼠标 |
| Razer DeathAdder Essential (2021) | `1532:0098` | 鼠标 |
| Razer DeathAdder Essential (White Edition) | `1532:0071` | 鼠标 |
| Razer DeathAdder V2 | `1532:0084` | 鼠标 |
| Razer DeathAdder V2 Lite | `1532:00A1` | 鼠标 |
| Razer DeathAdder V2 Mini | `1532:008C` | 鼠标 |
| Razer DeathAdder V2 Pro (Wired) | `1532:007C` | 鼠标 |
| Razer DeathAdder V2 Pro (Wireless) | `1532:007D` | 鼠标 |
| Razer DeathAdder V2 X HyperSpeed | `1532:009C` | 鼠标 |
| Razer DeathAdder V3 | `1532:00B2` | 鼠标 |
| Razer DeathAdder V3 HyperSpeed (Wired) | `1532:00C4` | 鼠标 |
| Razer DeathAdder V3 HyperSpeed (Wireless) | `1532:00C5` | 鼠标 |
| Razer DeathAdder V3 Pro (Wired) | `1532:00B6` | 鼠标 |
| Razer DeathAdder V3 Pro (Wired) | `1532:00C2` | 鼠标 |
| Razer DeathAdder V3 Pro (Wireless) | `1532:00B7` | 鼠标 |
| Razer DeathAdder V3 Pro (Wireless) | `1532:00C3` | 鼠标 |
| Razer DeathAdder V4 Pro (Wired) | `1532:00BE` | 鼠标 |
| Razer DeathAdder V4 Pro (Wireless) | `1532:00BF` | 鼠标 |
| Razer Diamondback Chroma | `1532:004C` | 鼠标 |
| Razer HyperPolling Wireless Dongle | `1532:00B3` | 鼠标 |
| Razer Imperator 2012 | `1532:002F` | 鼠标 |
| Razer Lancehead (Wired) | `1532:0059` | 鼠标 |
| Razer Lancehead (Wireless) | `1532:005A` | 鼠标 |
| Razer Lancehead Tournament Edition | `1532:0060` | 鼠标 |
| Razer Lancehead Wireless (Receiver) | `1532:006F` | 鼠标 |
| Razer Lancehead Wireless (Wired) | `1532:0070` | 鼠标 |
| Razer Mamba 2012 (Wired) | `1532:0024` | 鼠标 |
| Razer Mamba 2012 (Wireless) | `1532:0025` | 鼠标 |
| Razer Mamba Chroma (Wired) | `1532:0044` | 鼠标 |
| Razer Mamba Chroma (Wireless) | `1532:0045` | 鼠标 |
| Razer Mamba Elite | `1532:006C` | 鼠标 |
| Razer Mamba Tournament Edition | `1532:0046` | 鼠标 |
| Razer Mamba Wireless (Receiver) | `1532:0072` | 鼠标 |
| Razer Mamba Wireless (Wired) | `1532:0073` | 鼠标 |
| Razer Naga | `1532:0015` | 鼠标 |
| Razer Naga 2012 | `1532:002E` | 鼠标 |
| Razer Naga 2014 | `1532:0040` | 鼠标 |
| Razer Naga Chroma | `1532:0053` | 鼠标 |
| Razer Naga Epic | `1532:001F` | 鼠标 |
| Razer Naga Epic Chroma (Wired) | `1532:003E` | 鼠标 |
| Razer Naga Epic Chroma (Wireless) | `1532:003F` | 鼠标 |
| Razer Naga Hex | `1532:0041` | 鼠标 |
| Razer Naga Hex (Red) | `1532:0036` | 鼠标 |
| Razer Naga Hex V2 | `1532:0050` | 鼠标 |
| Razer Naga Left Handed Edition 2020 | `1532:008D` | 鼠标 |
| Razer Naga Pro (Wired) | `1532:008F` | 鼠标 |
| Razer Naga Pro (Wireless) | `1532:0090` | 鼠标 |
| Razer Naga Trinity | `1532:0067` | 鼠标 |
| Razer Naga V2 HyperSpeed (Receiver) | `1532:00B4` | 鼠标 |
| Razer Naga V2 Pro (Wired) | `1532:00A7` | 鼠标 |
| Razer Naga V2 Pro (Wireless) | `1532:00A8` | 鼠标 |
| Razer Naga X | `1532:0096` | 鼠标 |
| Razer Orochi (Wired) | `1532:0048` | 鼠标 |
| Razer Orochi 2011 | `1532:0013` | 鼠标 |
| Razer Orochi 2013 | `1532:0039` | 鼠标 |
| Razer Orochi V2 (Bluetooth) | `1532:0095` | 鼠标 |
| Razer Orochi V2 (Receiver) | `1532:0094` | 鼠标 |
| Razer Ouroboros | `1532:0032` | 鼠标 |
| Razer Pro Click (Receiver) | `1532:0077` | 鼠标 |
| Razer Pro Click (Wired) | `1532:0080` | 鼠标 |
| Razer Pro Click Mini (Receiver) | `1532:009A` | 鼠标 |
| Razer Pro Click V2 (Wired) | `1532:00D0` | 鼠标 |
| Razer Pro Click V2 (Wireless) | `1532:00D1` | 鼠标 |
| Razer Pro Click V2 Vertical Edition (Wired) | `1532:00C7` | 鼠标 |
| Razer Pro Click V2 Vertical Edition (Wireless) | `1532:00C8` | 鼠标 |
| Razer Taipan | `1532:0034` | 鼠标 |
| Razer Viper | `1532:0078` | 鼠标 |
| Razer Viper 8KHz | `1532:0091` | 鼠标 |
| Razer Viper Mini | `1532:008A` | 鼠标 |
| Razer Viper Mini SE (Wired) | `1532:009E` | 鼠标 |
| Razer Viper Mini SE (Wireless) | `1532:009F` | 鼠标 |
| Razer Viper Ultimate (Wired) | `1532:007A` | 鼠标 |
| Razer Viper Ultimate (Wireless) | `1532:007B` | 鼠标 |
| Razer Viper V2 Pro (Wired) | `1532:00A5` | 鼠标 |
| Razer Viper V2 Pro (Wireless) | `1532:00A6` | 鼠标 |
| Razer Viper V3 Pro (Wired) | `1532:00C0` | 鼠标 |
| Razer Viper V3 Pro (Wireless) | `1532:00C1` | 鼠标 |
| RazerBasiliskUltimateReceiver | `1532:0088` | 鼠标 |

### 键盘（72）

| 设备 | USB VID:PID | 类型 |
|---|---|---|
| BlackWidow Chroma V2 | `1532:0221` | 键盘 |
| Razer Anansi | `1532:010F` | 键盘 |
| Razer BlackWidow (Classic) | `1532:011B` | 键盘 |
| Razer BlackWidow 2019 | `1532:0241` | 键盘 |
| Razer BlackWidow Chroma | `1532:0203` | 键盘 |
| Razer BlackWidow Chroma (Overwatch) | `1532:0211` | 键盘 |
| Razer BlackWidow Elite | `1532:0228` | 键盘 |
| Razer BlackWidow Essential | `1532:0237` | 键盘 |
| Razer BlackWidow Lite | `1532:0235` | 键盘 |
| Razer BlackWidow Stealth Edition | `1532:010E` | 键盘 |
| Razer BlackWidow Tournament Edition 2014 | `1532:011C` | 键盘 |
| Razer BlackWidow Tournament Edition Chroma | `1532:0209` | 键盘 |
| Razer BlackWidow Ultimate 2012 | `1532:010D` | 键盘 |
| Razer BlackWidow Ultimate 2013 | `1532:011A` | 键盘 |
| Razer BlackWidow Ultimate 2016 | `1532:0214` | 键盘 |
| Razer BlackWidow V3 | `1532:024E` | 键盘 |
| Razer BlackWidow V3 Mini HyperSpeed (Wired) | `1532:0258` | 键盘 |
| Razer BlackWidow V3 Mini HyperSpeed (Wireless) | `1532:0271` | 键盘 |
| Razer BlackWidow V3 Pro 2.4 Ghz Wireless | `1532:025C` | 键盘 |
| Razer BlackWidow V3 Pro Wired | `1532:025A` | 键盘 |
| Razer BlackWidow V3 TK | `1532:0A24` | 键盘 |
| Razer BlackWidow V4 | `1532:0287` | 键盘 |
| Razer BlackWidow V4 75% | `1532:02A5` | 键盘 |
| Razer BlackWidow V4 Mini HyperSpeed (Wired) | `1532:02B9` | 键盘 |
| Razer BlackWidow V4 Mini HyperSpeed (Wireless) | `1532:02BA` | 键盘 |
| Razer BlackWidow V4 Pro | `1532:028D` | 键盘 |
| Razer BlackWidow V4 Tenkeyless HyperSpeed (Wired) | `1532:02D7` | 键盘 |
| Razer BlackWidow V4 Tenkeyless HyperSpeed (Wireless) | `1532:02D5` | 键盘 |
| Razer BlackWidow V4 X | `1532:0293` | 键盘 |
| Razer BlackWidow X Chroma | `1532:0216` | 键盘 |
| Razer BlackWidow X Tournament Edition Chroma | `1532:021A` | 键盘 |
| Razer BlackWidow X Ultimate | `1532:0217` | 键盘 |
| Razer Cynosa Chroma | `1532:022A` | 键盘 |
| Razer Cynosa Chroma Pro | `1532:022C` | 键盘 |
| Razer Cynosa Lite | `1532:023F` | 键盘 |
| Razer Cynosa V2 | `1532:025E` | 键盘 |
| Razer DeathStalker Chroma | `1532:0204` | 键盘 |
| Razer DeathStalker Expert | `1532:0202` | 键盘 |
| Razer DeathStalker V2 | `1532:0295` | 键盘 |
| Razer DeathStalker V2 Pro (Wired) | `1532:0292` | 键盘 |
| Razer DeathStalker V2 Pro (Wireless) | `1532:0290` | 键盘 |
| Razer DeathStalker V2 Pro TKL (Wired) | `1532:0298` | 键盘 |
| Razer DeathStalker V2 Pro TKL (Wireless) | `1532:0296` | 键盘 |
| Razer DeathStalker/DeathStalker Essential | `1532:0118` | 键盘 |
| Razer Huntsman | `1532:0227` | 键盘 |
| Razer Huntsman Elite | `1532:0226` | 键盘 |
| Razer Huntsman Mini | `1532:0257` | 键盘 |
| Razer Huntsman Mini Analog | `1532:0282` | 键盘 |
| Razer Huntsman Mini JP | `1532:0269` | 键盘 |
| Razer Huntsman Tournament Edition | `1532:0243` | 键盘 |
| Razer Huntsman V2 | `1532:026C` | 键盘 |
| Razer Huntsman V2 Analog | `1532:0266` | 键盘 |
| Razer Huntsman V2 Tenkeyless | `1532:026B` | 键盘 |
| Razer Huntsman V3 Pro | `1532:02A6` | 键盘 |
| Razer Huntsman V3 Pro 8KHz | `1532:02CF` | 键盘 |
| Razer Huntsman V3 Pro Mini | `1532:02B0` | 键盘 |
| Razer Huntsman V3 Pro TKL | `1532:02A7` | 键盘 |
| Razer Nostromo | `1532:0111` | 键盘 |
| Razer Orbweaver | `1532:0113` | 键盘 |
| Razer Orbweaver Chroma | `1532:0207` | 键盘 |
| Razer Ornata | `1532:021F` | 键盘 |
| Razer Ornata Chroma | `1532:021E` | 键盘 |
| Razer Ornata V2 | `1532:025D` | 键盘 |
| Razer Ornata V3 | `1532:02A1` | 键盘 |
| Razer Ornata V3 (Alternate) | `1532:028F` | 键盘 |
| Razer Ornata V3 Tenkeyless | `1532:02A3` | 键盘 |
| Razer Ornata V3 X | `1532:0294` | 键盘 |
| Razer Ornata V3 X (Alternate) | `1532:02A2` | 键盘 |
| Razer Tartarus | `1532:0201` | 键盘 |
| Razer Tartarus Chroma | `1532:0208` | 键盘 |
| Razer Tartarus Pro | `1532:0244` | 键盘 |
| Razer Tartarus V2 | `1532:022B` | 键盘 |

### 笔记本（46）

| 设备 | USB VID:PID | 类型 |
|---|---|---|
| Razer Blade (Late 2016) | `1532:0224` | 笔记本 |
| Razer Blade (QHD) | `1532:020F` | 笔记本 |
| Razer Blade 14 (2021) | `1532:0270` | 笔记本 |
| Razer Blade 14 (2022) | `1532:028C` | 笔记本 |
| Razer Blade 14 (2023) | `1532:029D` | 笔记本 |
| Razer Blade 14 (2024) | `1532:02B6` | 笔记本 |
| Razer Blade 14 (2025) | `1532:02C5` | 笔记本 |
| Razer Blade 15 (2018) | `1532:0233` | 笔记本 |
| Razer Blade 15 (2018) Base Model | `1532:023B` | 笔记本 |
| Razer Blade 15 (2018) Mercury | `1532:0240` | 笔记本 |
| Razer Blade 15 (2019) Advanced | `1532:023A` | 笔记本 |
| Razer Blade 15 (2023) | `1532:029E` | 笔记本 |
| Razer Blade 15 (Mid 2019) Base Model | `1532:0246` | 笔记本 |
| Razer Blade 15 (Mid 2019) Mercury | `1532:0245` | 笔记本 |
| Razer Blade 15 Advanced (2020) | `1532:0253` | 笔记本 |
| Razer Blade 15 Advanced (Early 2021) | `1532:026D` | 笔记本 |
| Razer Blade 15 Advanced (Early 2022) | `1532:028A` | 笔记本 |
| Razer Blade 15 Advanced (Mid 2021) | `1532:0276` | 笔记本 |
| Razer Blade 15 Studio Edition (2019) | `1532:024D` | 笔记本 |
| Razer Blade 16 (2023) | `1532:029F` | 笔记本 |
| Razer Blade 17 (2022) | `1532:028B` | 笔记本 |
| Razer Blade 17 Pro (Early 2021) | `1532:026E` | 笔记本 |
| Razer Blade 17 Pro (Mid 2021) | `1532:0279` | 笔记本 |
| Razer Blade 18 (2023) | `1532:02A0` | 笔记本 |
| Razer Blade 18 (2024) | `1532:02B8` | 笔记本 |
| Razer Blade 18 (2025) | `1532:02C7` | 笔记本 |
| Razer Blade Advanced (Late 2019) | `1532:024B` | 笔记本 |
| Razer Blade Base (Early 2020) | `1532:0255` | 笔记本 |
| Razer Blade Base (Early 2021) | `1532:026F` | 笔记本 |
| Razer Blade Base (Early 2022) | `1532:027A` | 笔记本 |
| Razer Blade Late 2020 Base | `1532:0268` | 笔记本 |
| Razer Blade Pro (2017) | `1532:0225` | 笔记本 |
| Razer Blade Pro (Early 2020) | `1532:0256` | 笔记本 |
| Razer Blade Pro (Late 2016) | `1532:0210` | 笔记本 |
| Razer Blade Pro (Late 2019) | `1532:024C` | 笔记本 |
| Razer Blade Pro 17 (2019) | `1532:0234` | 笔记本 |
| Razer Blade Pro FullHD (2017) | `1532:022F` | 笔记本 |
| Razer Blade Stealth | `1532:0205` | 笔记本 |
| Razer Blade Stealth (2019) | `1532:0239` | 笔记本 |
| Razer Blade Stealth (Early 2020) | `1532:0252` | 笔记本 |
| Razer Blade Stealth (Late 2016) | `1532:0220` | 笔记本 |
| Razer Blade Stealth (Late 2017) | `1532:0232` | 笔记本 |
| Razer Blade Stealth (Late 2019) | `1532:024A` | 笔记本 |
| Razer Blade Stealth (Late 2020) | `1532:0259` | 笔记本 |
| Razer Blade Stealth (Mid 2017) | `1532:022D` | 笔记本 |
| Razer Book (2020) | `1532:026A` | 笔记本 |

### 配件（26）

| 设备 | USB VID:PID | 类型 |
|---|---|---|
| Lian Li O11 Dynamic - Razer Edition | `1532:0F13` | 配件 |
| Razer Base Station Chroma (Headphone Stand) | `1532:0F08` | 配件 |
| Razer Base Station V2 Chroma | `1532:0F20` | 配件 |
| Razer Charging Pad Chroma | `1532:0F26` | 配件 |
| Razer Chroma Hardware Development Kit (HDK) | `1532:0F09` | 配件 |
| Razer Chroma Mug Holder | `1532:0F07` | 配件 |
| Razer Core | `1532:0215` | 配件 |
| Razer Core X Chroma | `1532:0F1A` | 配件 |
| Razer Firefly (2013) | `1532:0C00` | 鼠标垫 |
| Razer Firefly Hyperflux (2018) | `1532:0068` | 鼠标垫 |
| Razer Firefly V2 | `1532:0C04` | 鼠标垫 |
| Razer Firefly V2 Pro | `1532:0C08` | 鼠标垫 |
| Razer Goliathus (2018) | `1532:0C01` | 鼠标垫 |
| Razer Goliathus Chroma 3XL | `1532:0C06` | 鼠标垫 |
| Razer Goliathus Extended (2018) | `1532:0C02` | 鼠标垫 |
| Razer Laptop Stand Chroma | `1532:0F0D` | 配件 |
| Razer Laptop Stand Chroma V2 | `1532:0F2B` | 配件 |
| Razer Mouse Bungee V3 Chroma | `1532:0F1D` | 配件 |
| Razer Mouse Dock | `1532:007E` | 配件 |
| Razer Mouse Dock Pro | `1532:00A4` | 配件 |
| Razer Nommo Chroma (Speakers) | `1532:0517` | 配件 |
| Razer Nommo Pro (Speakers) | `1532:0518` | 配件 |
| Razer Raptor 27 | `1532:0F12` | 显示器 |
| Razer Strider Chroma | `1532:0C05` | 鼠标垫 |
| Razer Thunderbolt 4 Dock Chroma | `1532:0F21` | 配件 |
| Razer Tomahawk ATX | `1532:0F17` | 配件 |

## 能力与验证边界

出现在目录中，表示固定后端能够识别该型号。最终显示的控件取决于能否解析实际 HID 端点和对应事务。不同型号可能提供设备信息、电池状态、轮询率、DPI 与 DPI 档位、省电设置、低电量提醒、灯光亮度与效果、分区 LED、矩阵灯光、滚轮设置、键轴优化、Fn 优先行为或 HyperPolling 接收器控制。

设备出现在目录中，不代表每个控制项都已在该型号上完成真机验证。端点无法解析或正被占用时，页面会保持只读，直到重新扫描。OpenSynapse 不会按产品名称猜测能力，也不会改用另一个 HID 接口强行写入。

外部灯光接入见 [Chroma 支持设备](CHROMA_SUPPORTED_DEVICES.zh-CN.md)。

返回 [README](../README.zh-CN.md)。
