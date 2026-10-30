# Razer 耳机音频对齐：12 个新 PID

扫描时间：2026-10-29。包根目录：`artifacts/razer-mw-full-cdn/bundles/<PID>/`。
重跑：`pwsh -File scripts/scan-device-transport.ps1`，输出 `artifacts/device-transport-scan.csv`。

本地 bundle 目录只落了入口 chunk；缺失的动态 chunk 按 `main.*.js` 里的
chunk hash 从 `https://apps.razer.com/synapse/products/<decimal>/mw/<name>.<hash>.js`
补取后分析，下表文件名就是实际参与判断的 chunk。

判断依据不是“包里有没有 `rzDevice25Audio*.js`”，而是每个 bundle 的
`main.*.js` 里 `useFeature("rzDeviceType", ...)` 选中的类。0A4E/0A51/0A54/
101C/101E 等包里确实还带着 `rzDevice25AudioCamy`、
`rzDevice25AudioCamyT2V3R`、`rzDevice25AudioMarieT1`，但这些 PID 的
`rzDeviceType` 分别指向 `AudioValkyrie`、`rzDevice25ValkyrieT3` 或
`rzDevice40`，Camy/Marie 类不会被实例化。

现有 `RazerHeadsetAudioProtocol` 的帧是 91 字节 OpenRazer envelope：
data size 在 byte 5，class 在 byte 6，command 在 byte 7，XOR 在 byte 88
（覆盖 2..87）。下面的“差异”按这个帧和 `RazerHeadsetAudioProtocol.cs`
里的 class:id 常量比较。

| PID | 设备名（manifest en） | 音频 chunk（选中类） | class:id 集合 | 与现有协议的差异 | 结论 |
|---|---|---|---|---|---|
| `0A4E` | Razer BlackShark V3 Pro For Xbox | `AudioValkyrie.ae788284bca26f67c622.js`（module 90254）<br>命令表 `6555.49fa8c750b03dc1d4b27.js`（module 56555） | EQ preset `0x13:00/0x93:00`；EQ bands `0x15:00/0x95:00`；mic preset `0x16:00/0x96:00`；mic bands `0x17:00/0x97:00`；sidetone status `0x18:00/0x98:00`；sidetone volume `0x19:00/0x99:00`；ANC `0x12:00/0x92:00`；mic mute `0x55:00/0xE9:00`；battery `0x21:00`；charging `0x2A:00`；DND `0x27:00/0xA7:00` | 64-byte report id 2；class 0x13/0x93；现有协议是 91-byte class 0x08（`08:82/08:02` 等） | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `0A51` | Razer BlackShark V3 For Xbox | `AudioValkyrie.589a975d6e8397fb08c1.js`（module 90254）<br>命令表 `6555.9a4f40feab0bc8f0f579.js` | 同 0A4E | 同 0A4E | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `0A54` | Razer BlackShark V3 X Hyperspeed For Xbox | `rzDevice25ValkyrieT3.b0468303787a6b47ecb2.js`（module 7025） | EQ preset `0x18:80/0x18:00`；EQ bands `0x18:81/0x18:01`；mic mute `0x18:84/0x18:04`；sidetone `0x18:83/0x18:03`；DND `0x18:85/0x18:05`；silence detect `0x18:8F/0x18:0F`；battery `0x07:80`；reset `0x00:0B` | 64-byte report id 7；EQ/mute/sidetone 走 class 0x18；只有 DND `18:85/18:05` 和 battery `07:80` 与现协议重合 | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `0A61` | Razer Hammerhead V3 HyperSpeed | `rzDevice40.5e322395ea5fe2b013f4.js`（module 84892） | EQ preset `0x18:80/0x18:00`；EQ bands `0x18:81/0x18:01`；EQ status `0x18:82/0x18:02`；mute `0x18:84/0x18:04`；DND `0x18:85/0x18:05`；bass `0x08:87/0x08:07`；mic volume `0x08:97/0x08:17`；audio source `0x08:8A/0x08:0A`；Dolby `0x08:8C/0x08:0C`；master volume `0x08:86/0x08:06` | 64-byte report id 2；只有 bass/mic volume/source/Dolby 的 0x08 子集重合；EQ/mute 走 0x18 | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `0A64` | Razer Hammerhead V3 X Hyperspeed | `rzDevice40.fcbfcb8041004338a920.js`（module 84892） | 同 0A61 | 同 0A61 | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `0A68` | Razer Hammerhead V3 X HyperSpeed for Xbox | `rzDevice40.0e84cb4c36efa7056054.js` | 同 0A61（`He/Be/Fe/Ve/Ye/Xe` 常量相同） | 同 0A61 | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `0A6C` | Razer Hammerhead V3 X HyperSpeed For PlayStation | `rzDevice40.8c12e02a23ccb7b6a9b6.js` | 同 0A61（`He/Be/Fe/Ve/Ye/Xe` 常量相同） | 同 0A61 | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `0A81` | Razer Hammerhead V3 X Hyperspeed Kuromi Edition | `rzDevice40.fcbfcb8041004338a920.js`（module 84892） | 同 0A61 | 同 0A61 | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `101C` | Razer BlackShark V3 Pro For Playstation | `AudioValkyrie.fa0f38e58477ff55f355.js`（module 90254）<br>命令表 `6555.ec89cb99ea82ae4ff8fd.js` | 同 0A4E | 同 0A4E | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `101E` | Razer BlackShark V3 For Playstation | `AudioValkyrie.9a50b92c456273c62681.js`（module 90254）<br>命令表 `6555.ef40c53b5cf27792e4ef.js` | 同 0A4E | 同 0A4E | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `101F` | Razer BlackShark V3 For Playstation | `AudioValkyrie.f96ce3a5d92ceda5a451.js`（module 90254）<br>命令表 `6555.f7b7fa8e755dc0407835.js` | 同 0A4E | 同 0A4E；该 bundle 是 2025 旧构建，没有 `rzDevice25Audio*.js` | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |
| `1022` | Razer BlackShark V3 X Hyperspeed For Playstation | `rzDevice25ValkyrieT3.735dd11f4a7bacc421de.js`（module 7025） | 同 0A54 | 同 0A54 | Superseded: see conclusion; mapped to Valkyrie64/Mxic64, live check pending |

## 关键 JS 片段

`AudioValkyrie` 的类定义在 `AudioValkyrie.*.js` module 90254，继承
`AudioMxicDevice`（module 59263）→ `AudioOdessaT2WirelessUSB`（module 70673），
后者固定 `REPORT_LENGTH=64`、`REPORT_ID=2`，命令和数据从 byte 10 开始，XOR
在 byte 62。命令表 `6555.*.js` module 56555 里的核心常量：

```js
const p=new Uint8Array([19,0,0]), C=new Uint8Array([19,1,1]),
      b=new Uint8Array([147,0,1]), g=new Uint8Array([147,1,1]),
      I=new Uint8Array([21,0,0]), P=new Uint8Array([149,0,10]),
      J=new Uint8Array([24,0,0]), ee=new Uint8Array([152,0,1]),
      Ue=new Uint8Array([29,0,0]), Se=new Uint8Array([157,0,1]);
```

`rzDevice25ValkyrieT3.*.js` module 7025 的常量（0A54/1022）：

```js
const f=new Uint8Array([3,24,132]), g=new Uint8Array([3,24,4]),
      S=new Uint8Array([2,24,128]), A=new Uint8Array([4,24,0]),
      U=new Uint8Array([2,24,129]), E=new Uint8Array([12,24,1]);
```

`rzDevice40.*.js` module 84892 的常量（0A61/0A64/0A68/0A6C/0A81）：

```js
const He=new Uint8Array([2,24,128]), Be=new Uint8Array([4,24,0]),
      Fe=(new Uint8Array([3,8,131]), new Uint8Array([3,8,3]), new Uint8Array([12,24,129])),
      Ve=new Uint8Array([12,24,1]), Ye=new Uint8Array([2,24,132]),
      Xe=new Uint8Array([3,24,4]), qe=new Uint8Array([2,8,135]),
      Ze=new Uint8Array([2,8,7]), An=(new Uint8Array([3,8,150]), new Uint8Array([3,8,22]), new Uint8Array([2,8,151])),
      wn=new Uint8Array([2,8,23]);
```

## 结论（2026-10-29 复核）

通道由每个 bundle 的 `useFeature("rzDeviceType", ...)` 决定，不再按 HID 接口特征猜。
`scripts/scan-device-transport.ps1` 会重扫 338 个 bundle，输出
`artifacts/device-transport-scan.csv`；代码里的表是
`src/OpenSynapse.Windows/Devices/RazerHeadsetChannelCatalog.cs`。

| 结论 | PID | 依据 |
|---|---|---|
| Billie64 | 0567 056B | `AudioBillieT1` / `AudioBillieT2`，64 字节 report id 2，`0x60` 事务号 |
| Valkyrie64 | 0579 0A4E 0A51 101E 101F | `AudioValkyrie`，同一 64 字节信封；0576/101C 保留专用面板 |
| Mxic64 | 0539 053B 0555 0583 0584 | `AudioMxicDevice`，`50 41`/`50 49` 信封（原有实现） |
| Mxic64（report id 7） | 0A54 1022 | `rzDevice25ValkyrieT3` |
| Mxic64（class 0x18） | 0A61 0A64 0A68 0A6C 0A81 | `rzDevice40` |
| Feature91 | 051A 051C 051E 0528 0529 0520 0527 0526 0521 0560 0533 0549 0537 056D 0542 0543 0F1B 0532 0548 055A 055C 055E 056A 0F66 056E | `AudioAvnera*` / `AudioKraken*` / `AudioKrystal*` / `rzDevice25*` 的 91 字节族 |
| Feature91（音频） | 0554 | `AudioKrakenKittyV2Pro` 只在 91 字节音频基类上追加 37 字节 Chroma 帧，没有独立音频通道 |
| 专用面板 | 0576 0577 101B 101C | BlackShark V3 Pro 已有 ANC/侧音/THX 面板，不再进通用耳机页 |
| Billie64 | 0587 | `AudioBastetT1`，与 `AudioBillieT2` 共用 64 字节 report id 2 和 56555 命令表 |
| Mxic64（protocol40） | 0593 | `rzDevice40HeadsetAiroha`，64 字节 report id 2；class/id/size 在 5..8，只开放电量/充电 |
| BLE 电量/控制页 | 0309 0530 0534 0562 | `AudioAWKittyBLE`：service `416d656c-...`、write `416d0000-...`、notify `416d0001-...`；电量、主音量、侧音、游戏模式、扬声器/麦克风 EQ、低音、触觉、自动关机、Chroma 亮度、固件/序列号/连接/充电读取；音频输出仍由 Windows 负责 |
| HID mixer 子集 | 053E | `AudioMixer`：64 字节 report-7 feature、report-9 状态；固件/序列号/设备模式、推子原始值、通道/麦克风静音、亮度；推子/路由/EQ/peak 需要 `CmMixerLib` |
| Feature91（音频） | 0F1B | Seiren Emote 的音频/EQ 走 91 字节通道；逐像素 8×8 帧缺几何/命令/latch 证据，不开放 |
| 不显示（串口） | 052D | `AudioNXPDevice` 走 `rzSerial`（28 字节包头），没有串口 transport |
| 不显示（无 bundle） | 053C 0565 | CDN 快照里没有 main.js，无法核实 `rzDeviceType` |

### 0593 protocol40 帧

`rzDevice40HeadsetAiroha` 继承 `rzDevice40Headset`，帧是 64 字节 report id 2：

```
0      02
1      00
2      60（设备固定事务号；DockDongle 为 E0）
5..6   class（高字节 + 低字节）
7      command id
8      size
9..    payload
62     XOR(3..61)
63     00
```

只从该 chunk 取到电量 `[00,07,80]` 和充电 `[02,07,84]`（payload 为电池索引 0）。
响应 byte 1 状态 2 成功，byte 6 为数据长度，byte 7/8 回显 id/size，数据从 byte 9 开始。
`rzDevice40` 的 EQ/mute 命令没有被这个 `rzDeviceType` 选中，因此不开放。

### AudioValkyrie / AudioBillie 帧

64 字节、report id 2，事务号固定 `0x60`：

```
0      02
2      60
6      command.Length + data.Length + 1
9      00（三个 profile 的音频命令都留 0）
10..12 command 三元组 [class, id, size]
13..   payload
62     XOR(0..61)
63     0
```

响应：byte1 状态（2 成功，1/3 重发），byte10/11 回显 class/id+1，数据从 13 开始，
长度取期望响应头的第 3 字节。

命令：电量 `21 00 00` → `21 01 01`；充电 `2A 00 00`；EQ 预设 `13 00 00`/`93 00 01`；
EQ 频段 `15 00 01`（1 字节 selector）→ `15 01 0B`；写 `95 00 0B`（selector + 10 段）。
0A4E/0A51/101E/101F 的共享 helper `bo`/`fU` 也用 `Ph7=[21,0,1]`、`jX9=[149,0,11]`，
所以全部按 11 字节形式。裸 10 段分支保留在协议层，供后续 bundle 复核。

EQ 电平是符号-幅值：`-12 → 140`（128+12），`+12 → 12`。0555 的 MXIC 走另一套
0..10 步进（UI -12..+12 → 固件 0..10），由 `RazerHeadsetEqScale` 转换；91 字节族
保持原来的 0..100。

状态：帧、命令和通道都按 bundle 对齐；没有实机验证。
