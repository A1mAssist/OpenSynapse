# Chroma 支持设备

本文逐台列出 OpenSynapse 当前可以通过 Chroma 灯光路径展示的具体设备。设备之所以列出，是因为仓库内置的 OpenRazer manifest 同时提供矩阵尺寸、`set_key_row` 和自定义帧事务；这不代表每一台实机都已经在 Windows 上验证。

## 运行时可用性

下面的清单是 manifest 能力库存。运行时仍必须先解析到设备端点，并且事务实际被设备接受，软件才会显示对应控件；事务被拒绝或端点不可用时，控件会隐藏。下面的 91-byte HID 路径来自 OpenRazer 协议定义，不是原生 Chroma SDK，也不会加载 `RzChromaConnectAPI.dll`。

## Blade Chroma REST

| 型号 | VID:PID | 矩阵 | 传输方式 | 范围 |
|---|---|---:|---|---|
| Razer Blade 16 (2025) | `1532:02C6` | 6x22 | Chroma REST（`127.0.0.1:54235`） | 已验证 Blade 16 实体键位的键盘帧输出；支持静态、`CUSTOM`、`CUSTOM_KEY` 和 `CUSTOM2`；外部控制结束后恢复选定灯效 |

## OpenRazer 矩阵帧设备

| 类别 | 型号 | VID:PID | 矩阵 | 传输方式 |
|---|---|---|---:|---|
| 配件 | Razer Mouse Dock | `1532:007E` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Core | `1532:0215` | 1x9 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Nommo Chroma (Speakers) | `1532:0517` | 2x24 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Nommo Pro (Speakers) | `1532:0518` | 2x8 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Goliathus (2018) | `1532:0C01` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Goliathus Extended (2018) | `1532:0C02` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Firefly V2 | `1532:0C04` | 1x19 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Strider Chroma | `1532:0C05` | 1x19 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Goliathus Chroma 3XL | `1532:0C06` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Firefly V2 Pro | `1532:0C08` | 1x17 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Base Station Chroma (Headphone Stand) | `1532:0F08` | 1x15 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Chroma Hardware Development Kit (HDK) | `1532:0F09` | 4x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Laptop Stand Chroma | `1532:0F0D` | 1x16 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Raptor 27 | `1532:0F12` | 1x12 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Lian Li O11 Dynamic - Razer Edition | `1532:0F13` | 4x16 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Tomahawk ATX | `1532:0F17` | 1x20 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Core X Chroma | `1532:0F1A` | 1x16 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Mouse Bungee V3 Chroma | `1532:0F1D` | 1x8 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Base Station V2 Chroma | `1532:0F20` | 1x8 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Thunderbolt 4 Dock Chroma | `1532:0F21` | 1x12 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 配件 | Razer Laptop Stand Chroma V2 | `1532:0F2B` | 1x15 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow Chroma | `1532:0203` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Orbweaver Chroma | `1532:0207` | 5x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow Tournament Edition Chroma | `1532:0209` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow Chroma (Overwatch) | `1532:0211` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow Ultimate 2016 | `1532:0214` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow X Chroma | `1532:0216` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow X Ultimate | `1532:0217` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow X Tournament Edition Chroma | `1532:021A` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Ornata Chroma | `1532:021E` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Ornata | `1532:021F` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | BlackWidow Chroma V2 | `1532:0221` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman Elite | `1532:0226` | 9x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman | `1532:0227` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow Elite | `1532:0228` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Cynosa Chroma | `1532:022A` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Tartarus V2 | `1532:022B` | 4x6 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Cynosa Chroma Pro | `1532:022C` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow Essential | `1532:0237` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow 2019 | `1532:0241` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman Tournament Edition | `1532:0243` | 6x18 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Tartarus Pro | `1532:0244` | 1x21 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V3 | `1532:024E` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman Mini | `1532:0257` | 5x15 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V3 Mini HyperSpeed (Wired) | `1532:0258` | 5x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V3 Pro Wired | `1532:025A` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V3 Pro 2.4 Ghz Wireless | `1532:025C` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Ornata V2 | `1532:025D` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Cynosa V2 | `1532:025E` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman V2 Analog | `1532:0266` | 8x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman Mini JP | `1532:0269` | 5x15 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman V2 Tenkeyless | `1532:026B` | 6x18 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman V2 | `1532:026C` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V3 Mini HyperSpeed (Wireless) | `1532:0271` | 5x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman Mini Analog | `1532:0282` | 5x15 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V4 | `1532:0287` | 8x23 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V4 Pro | `1532:028D` | 8x23 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Ornata V3 (Alternate) | `1532:028F` | 1x10 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer DeathStalker V2 Pro (Wireless) | `1532:0290` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer DeathStalker V2 Pro (Wired) | `1532:0292` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V4 X | `1532:0293` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Ornata V3 X | `1532:0294` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer DeathStalker V2 | `1532:0295` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer DeathStalker V2 Pro TKL (Wireless) | `1532:0296` | 6x17 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer DeathStalker V2 Pro TKL (Wired) | `1532:0298` | 6x17 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Ornata V3 | `1532:02A1` | 1x10 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Ornata V3 X (Alternate) | `1532:02A2` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Ornata V3 Tenkeyless | `1532:02A3` | 1x8 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V4 75% | `1532:02A5` | 6x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman V3 Pro | `1532:02A6` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman V3 Pro TKL | `1532:02A7` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman V3 Pro Mini | `1532:02B0` | 5x15 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V4 Mini HyperSpeed (Wired) | `1532:02B9` | 5x14 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V4 Mini HyperSpeed (Wireless) | `1532:02BA` | 5x14 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer Huntsman V3 Pro 8KHz | `1532:02CF` | 6x22 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V4 Tenkeyless HyperSpeed (Wireless) | `1532:02D5` | 6x18 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V4 Tenkeyless HyperSpeed (Wired) | `1532:02D7` | 6x18 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 键盘 | Razer BlackWidow V3 TK | `1532:0A24` | 6x18 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Stealth | `1532:0205` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade (QHD) | `1532:020F` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Pro (Late 2016) | `1532:0210` | 6x22 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Stealth (Late 2016) | `1532:0220` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade (Late 2016) | `1532:0224` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Pro (2017) | `1532:0225` | 6x25 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Stealth (Mid 2017) | `1532:022D` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Pro FullHD (2017) | `1532:022F` | 6x25 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Stealth (Late 2017) | `1532:0232` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 (2018) | `1532:0233` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Pro 17 (2019) | `1532:0234` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 (2019) Advanced | `1532:023A` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 (2018) Mercury | `1532:0240` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 (Mid 2019) Mercury | `1532:0245` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Advanced (Late 2019) | `1532:024B` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Pro (Late 2019) | `1532:024C` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 Studio Edition (2019) | `1532:024D` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 Advanced (2020) | `1532:0253` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade Pro (Early 2020) | `1532:0256` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 Advanced (Early 2021) | `1532:026D` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 17 Pro (Early 2021) | `1532:026E` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 14 (2021) | `1532:0270` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 Advanced (Mid 2021) | `1532:0276` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 17 Pro (Mid 2021) | `1532:0279` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 Advanced (Early 2022) | `1532:028A` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 17 (2022) | `1532:028B` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 14 (2022) | `1532:028C` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 14 (2023) | `1532:029D` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 15 (2023) | `1532:029E` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 16 (2023) | `1532:029F` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 18 (2023) | `1532:02A0` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 14 (2024) | `1532:02B6` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 18 (2024) | `1532:02B8` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 14 (2025) | `1532:02C5` | 6x16 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 笔记本 | Razer Blade 18 (2025) | `1532:02C7` | 6x19 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Naga Hex V2 | `1532:0050` | 1x3 | 标准 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Naga Chroma | `1532:0053` | 1x3 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Lancehead (Wired) | `1532:0059` | 1x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Lancehead (Wireless) | `1532:005A` | 1x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer DeathAdder Elite | `1532:005C` | 1x2 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Lancehead Tournament Edition | `1532:0060` | 1x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk | `1532:0064` | 1x2 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk Essential | `1532:0065` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Mamba Elite | `1532:006C` | 1x20 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Lancehead Wireless (Receiver) | `1532:006F` | 1x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Lancehead Wireless (Wired) | `1532:0070` | 1x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Mamba Wireless (Receiver) | `1532:0072` | 1x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Mamba Wireless (Wired) | `1532:0073` | 1x16 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Viper | `1532:0078` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Viper Ultimate (Wired) | `1532:007A` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Viper Ultimate (Wireless) | `1532:007B` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer DeathAdder V2 Pro (Wired) | `1532:007C` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer DeathAdder V2 Pro (Wireless) | `1532:007D` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer DeathAdder V2 | `1532:0084` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V2 | `1532:0085` | 1x2 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk Ultimate | `1532:0086` | 1x14 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | RazerBasiliskUltimateReceiver | `1532:0088` | 1x14 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Viper Mini | `1532:008A` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer DeathAdder V2 Mini | `1532:008C` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Naga Left Handed Edition 2020 | `1532:008D` | 1x3 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Naga Pro (Wired) | `1532:008F` | 1x3 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Naga Pro (Wireless) | `1532:0090` | 1x3 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Naga X | `1532:0096` | 1x2 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V3 | `1532:0099` | 1x11 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer DeathAdder V2 Lite | `1532:00A1` | 1x1 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Naga V2 Pro (Wired) | `1532:00A7` | 1x3 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Naga V2 Pro (Wireless) | `1532:00A8` | 1x3 | Extended2 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V3 Pro (Wired) | `1532:00AA` | 1x13 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V3 Pro (Wireless) | `1532:00AB` | 1x13 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V3 35K | `1532:00CB` | 1x13 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V3 Pro 35K (Wired) | `1532:00CC` | 1x13 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V3 Pro 35K (Wireless) | `1532:00CD` | 1x13 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V3 Pro 35K Phantom Green Edition (Wired) | `1532:00D6` | 1x12 | Extended 91-byte HID；仍需运行时解析设备端点 |
| 鼠标 | Razer Basilisk V3 Pro 35K Phantom Green Edition (Wireless) | `1532:00D7` | 1x12 | Extended 91-byte HID；仍需运行时解析设备端点 |

## Kraken 灯光设备

以下型号使用独立的 Kraken 37-byte Output Report。每个 USB PID 单独列出，因为它们对应独立的产品定义。

| 型号 | VID:PID | 传输方式 | 范围 |
|---|---|---|---|
| Kraken 7.1（PID 0501） | `1532:0501` | Kraken 37-byte Output Report | 仅灯光效果 |
| Kraken 7.1（PID 0506） | `1532:0506` | Kraken 37-byte Output Report | 仅灯光效果 |
| Kraken 7.1 Chroma | `1532:0504` | Kraken 37-byte Output Report | 仅灯光效果 |
| Kraken 7.1 V2 | `1532:0510` | Kraken 37-byte Output Report | 仅灯光效果 |
| Kraken Tournament Edition | `1532:0520` | Kraken 37-byte Output Report | 仅灯光效果 |
| Kraken Ultimate | `1532:0527` | Kraken 37-byte Output Report | 仅灯光效果 |
| Kraken Kitty V2 | `1532:0560` | Kraken 37-byte Output Report | 仅灯光效果 |

## 相关但非矩阵设备

| 型号 | VID:PID | 路径 |
|---|---|---|
| Razer Viper V3 HyperSpeed | `1532:00B8` | 产品专用 DPI、轮询率、电池、休眠时间、电池类型和板载映射路径；不提供 Chroma 矩阵灯光 |

## 边界说明

- OpenSynapse 不加载 `RzChromaConnectAPI.dll`、原生 Chroma SDK 或 Razer Synapse。
- Chroma REST 当前只在 Blade 16 键盘上完成验证；这不代表 REST 帧会被转译到所有 OpenRazer 矩阵设备。
- 矩阵尺寸和 manifest 事务只是能力元数据，不是硬件认证；连接端点和成功事务仍是最终依据。
- 不支持或未验证的控件会被隐藏，不会伪装成可配置控件。
- 无线接收器、固件差异和传输时序都可能导致某台实机无法使用某个已列出的操作。
