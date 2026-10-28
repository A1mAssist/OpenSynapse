# Razer 耳机音频对齐：12 个新 PID

扫描时间：2026-10-28。包根目录：`artifacts/razer-mw-full-cdn/bundles/<PID>/`。

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
| `0A4E` | Razer BlackShark V3 Pro For Xbox | `AudioValkyrie.ae788284bca26f67c622.js`（module 90254）<br>命令表 `6555.49fa8c750b03dc1d4b27.js`（module 56555） | EQ preset `0x13:00/0x93:00`；EQ bands `0x15:00/0x95:00`；mic preset `0x16:00/0x96:00`；mic bands `0x17:00/0x97:00`；sidetone status `0x18:00/0x98:00`；sidetone volume `0x19:00/0x99:00`；ANC `0x12:00/0x92:00`；mic mute `0x55:00/0xE9:00`；battery `0x21:00`；charging `0x2A:00`；DND `0x27:00/0xA7:00` | 64-byte report id 2；class 0x13/0x93；现有协议是 91-byte class 0x08（`08:82/08:02` 等） | Blocked |
| `0A51` | Razer BlackShark V3 For Xbox | `AudioValkyrie.589a975d6e8397fb08c1.js`（module 90254）<br>命令表 `6555.9a4f40feab0bc8f0f579.js` | 同 0A4E | 同 0A4E | Blocked |
| `0A54` | Razer BlackShark V3 X Hyperspeed For Xbox | `rzDevice25ValkyrieT3.b0468303787a6b47ecb2.js`（module 7025） | EQ preset `0x18:80/0x18:00`；EQ bands `0x18:81/0x18:01`；mic mute `0x18:84/0x18:04`；sidetone `0x18:83/0x18:03`；DND `0x18:85/0x18:05`；silence detect `0x18:8F/0x18:0F`；battery `0x07:80`；reset `0x00:0B` | 64-byte report id 7；EQ/mute/sidetone 走 class 0x18；只有 DND `18:85/18:05` 和 battery `07:80` 与现协议重合 | Blocked |
| `0A61` | Razer Hammerhead V3 HyperSpeed | `rzDevice40.5e322395ea5fe2b013f4.js`（module 84892） | EQ preset `0x18:80/0x18:00`；EQ bands `0x18:81/0x18:01`；EQ status `0x18:82/0x18:02`；mute `0x18:84/0x18:04`；DND `0x18:85/0x18:05`；bass `0x08:87/0x08:07`；mic volume `0x08:97/0x08:17`；audio source `0x08:8A/0x08:0A`；Dolby `0x08:8C/0x08:0C`；master volume `0x08:86/0x08:06` | 64-byte report id 2；只有 bass/mic volume/source/Dolby 的 0x08 子集重合；EQ/mute 走 0x18 | Blocked |
| `0A64` | Razer Hammerhead V3 X Hyperspeed | `rzDevice40.fcbfcb8041004338a920.js`（module 84892） | 同 0A61 | 同 0A61 | Blocked |
| `0A68` | Razer Hammerhead V3 X HyperSpeed for Xbox | `rzDevice40.0e84cb4c36efa7056054.js` | 同 0A61（`He/Be/Fe/Ve/Ye/Xe` 常量相同） | 同 0A61 | Blocked |
| `0A6C` | Razer Hammerhead V3 X HyperSpeed For PlayStation | `rzDevice40.8c12e02a23ccb7b6a9b6.js` | 同 0A61（`He/Be/Fe/Ve/Ye/Xe` 常量相同） | 同 0A61 | Blocked |
| `0A81` | Razer Hammerhead V3 X Hyperspeed Kuromi Edition | `rzDevice40.fcbfcb8041004338a920.js`（module 84892） | 同 0A61 | 同 0A61 | Blocked |
| `101C` | Razer BlackShark V3 Pro For Playstation | `AudioValkyrie.fa0f38e58477ff55f355.js`（module 90254）<br>命令表 `6555.ec89cb99ea82ae4ff8fd.js` | 同 0A4E | 同 0A4E | Blocked |
| `101E` | Razer BlackShark V3 For Playstation | `AudioValkyrie.9a50b92c456273c62681.js`（module 90254）<br>命令表 `6555.ef40c53b5cf27792e4ef.js` | 同 0A4E | 同 0A4E | Blocked |
| `101F` | Razer BlackShark V3 For Playstation | `AudioValkyrie.f96ce3a5d92ceda5a451.js`（module 90254）<br>命令表 `6555.f7b7fa8e755dc0407835.js` | 同 0A4E | 同 0A4E；该 bundle 是 2025 旧构建，没有 `rzDevice25Audio*.js` | Blocked |
| `1022` | Razer BlackShark V3 X Hyperspeed For Playstation | `rzDevice25ValkyrieT3.735dd11f4a7bacc421de.js`（module 7025） | 同 0A54 | 同 0A54 | Blocked |

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

## 结论

上面的“差异”一栏是拿 91-byte 的 `RazerHeadsetAudioProtocol` 比的，那张表之外还有
一套已经存在的实现：`RazerMxicHeadsetProtocol`（64-byte、report id 2、50 41/50 49
同步字、XOR 在 byte 62）。

| PID | 结论 | 依据 |
|---|---|---|
| `0A4E` `0A51` `101E` `101F` | **Added** | AudioValkyrie 的 `[19,0,0]/[147,0,1]`（EQ preset）、`[21,0,0]/[149,0,10]`（EQ bands 10 段）、`[85,0,0]/[233,0,1]`（mic mute）、`[33,0,0]`（battery）、`[42,0,0]`（charging）与 `RazerMxicHeadsetProtocol` 现有常量逐字节相同；已加入 `Products` 和 `IsSupported` |
| `101C` | Skipped | 命令与上面四款相同，但 `101C` 是 `BlackSharkV3ProProtocol.UsbProductId`（仓库已按实机观测接入 THX/ANC/sidetone 面板）。同一台设备再进 MXIC 会多出一张音频卡，所以保留专用面板，不加进 `Products` |
| `0A54` `1022` | Blocked | rzDevice25ValkyrieT3：report id **7**，EQ/mute/sidetone 走 `18:80/18:00`、`18:81/18:01`、`18:84/18:04`、`18:83/18:03`。帧 id 和三元组顺序都和 MXIC 不同，需要独立的帧构造器 |
| `0A61` `0A64` `0A68` `0A6C` `0A81` | **Added** | rzDevice40：同一套 64-byte `50 41` 信封，但三元组是 `[dataSize, class, id]`，EQ/mute 在 class `0x18`。已实现 EQ preset、EQ 10 段、麦克风静音、电量、充电、固件；细节见下 |

### rzDevice40（Hammerhead V3，report id 2）

请求三元组和 payload 逐字节取自 bundle，响应是 request payload 的回显（`Rn`/`On`/`Bn` 三个解析器）：

| 用途 | 三元组 | 请求 payload | 响应 |
|---|---|---|---|
| 读 EQ preset | `02 18 80` | `[profile=0, 0]` | `[profile, source, group, preset]` |
| 写 EQ preset | `04 18 00` | `[0, source=1, 0, preset]` | 同上 |
| 读 EQ 段 | `0C 18 81` | 12 字节，`[0]`=profile | `[profile, source, band0..band9]` |
| 写 EQ 段 | `0C 18 01` | `[0, 1, band0..band9]` | 同上 |
| 读麦克风静音 | `02 18 84` | `[0, source=2]` | `[profile, source, muted]` |
| 写麦克风静音 | `03 18 04` | `[0, 2, muted]` | 同上 |

`TD={Speaker:1,Mic:2}`；bundle 读写都用当前 profile `0`，所以回读能对上。EQ 段电平是
符号-幅值（`level<0 ? 128|abs(level) : level`），与 0555 的编码一致。

**没做**：sidetone（`08:83`/`08:03`）、DND（`18:85/18:05`）、bass / 麦克风音量 / 音源 / Dolby
（`08:86/08:87/08:97/08:8A/08:8C` 一族）。这组的 class 是 `0x08`，但三元组顺序同样是
`[dataSize, class, id]`，和 0555 的 `[class, id, dataSize]` 相反——不能直接复用旧常量，
payload 语义也没有从 JS 里确认，所以先不写。

未实现的 AudioValkyrie 子命令：sidetone `18:00/98:00`、ANC `12:00/92:00`、
DND `27:00/A7:00`。三条都是 get/set 对称的 1 字节 payload，但本仓库还没有对应的
UI 入口和取值枚举，所以先不进代码。

现有 `0x0576`/`0x0579` 条目未改动。它们的 bundle 同样选中
`AudioValkyrie`，因此这次扫描不能证明它们的 91-byte 准入；如果要继续确认，
需要单独做硬件 HID descriptor / 91-byte feature report 回读。

