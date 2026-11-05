# Blade 15 Advanced 2019 (023A) 灯光协议证据

调查日期：2026-10-30。只读调研，未修改业务代码、manifest 或 bundle。

证据等级：

- **[bundle]**：`artifacts/razer-mw-full-cdn/bundles/023A/` 内的官方雷云 bundle。
- **[OpenRazer]**：`src/OpenSynapse.Windows/Devices/OpenRazer/laptops.json` 的既有设备表。
- **[code]**：仓库现有实现，只用于对比，不作为 023A 的官方证据。
- **[inference]**：由 bundle 代码直接拼出的结论，没有单独命名字段。
- **[unverified]**：需要 023A 实机确认。

## 结论摘要

| 问题 | 结论 |
|---|---|
| 023A 的灯光类 | `rzDevice25LedMatrixBlade` 继承 `rzDevice25LedMatrix`；Blade 文件本身只加屏幕、刷新率、GPU、ICC、序列号等 DLL 接口，不定义灯光命令。[bundle] |
| 固件效果命令 | `0x03:0x0A`，官方名 `Set LED Matrix RGB Effect`；payload 首字节是固件效果 id，没有 store/region 前缀。[bundle] |
| 帧命令 | `0x03:0x0B`，官方名 `Set LED Matrix RGB Define Pattern`；payload 是 `[frameId, row, start, end, RGB...]`，也没有 store/region 前缀。[bundle] |
| 矩阵行列数 | bundle 没有硬编码 6x16；`setChromaFrameLightingEngine` 只使用调用方传入的 `numRow/numCol`。`6x16` 来自 OpenRazer 表。[OpenRazer] |
| 03:0A 静态/呼吸/光谱/波浪 | 命令、效果 id、颜色位置和 dataSize 都能从 bundle 确定；波浪方向的数值枚举没有在 bundle 里命名，需要实机确认。[bundle][unverified] |
| 03:0B 自定义帧 | 命令和 payload 能确定，但 6x16 的物理行序、方向、列数没有 bundle 证据；这不影响 03:0A 固件效果。[unverified] |

## 1. 官方 bundle 能证明什么

### 1.1 设备身份和类选择

| 证据 | 内容 |
|---|---|
| `manifest.json` | `deviceName` 为 `Razer Blade 15 Advanced Model`；`editionId` 为 `[0,129]`；`productTranslations` 的 key 是 `570_0`、`570_129`；`buildVersion` 为 `2608200924`；commit 为 `b4af61bb76b3f739f7b48ae19a385247ce03f90d`。[bundle] |
| `manifest.json` 的 `resourceManifest` | 只列 `PID023aDrv_v0.1.2.0.exe` 和 `BladeNative_v1.0.10.1.dll` 等资源；没有矩阵尺寸或灯光命令。[bundle] |
| `main.4a1e14046f8fed0320c7.js` 模块 26061 | `DeviceInfo`：`productId:570`、`vendorId:5426`、`category:"SYSTEM"`、`claimInterface:2`、`WAVE_DIRECTION:"LeftRight"`。[bundle] |
| 同上，模块 35924 | `runtimeData`：`productId:570`、`isChromaDevice:true`。[bundle] |
| 同上，模块 26061 的 `CHROMA_OBJECT` | `{ category:"system", vendorId:5426, family:7 }`；family 7 只是枚举值，没有尺寸信息。[bundle] |
| 同上，注入代码 | `useFeature("rzDeviceType","rzDevice25LedMatrixBlade")`，即 023A 选择的是 Blade 的 LED Matrix 类，而不是 `Blade2` 或 `Wattage`。[bundle] |
| 同上，Blade 类型的 claimInterface 分支 | `rzDevice25LedMatrixBlade`、`Blade2`、`Wattage` 都返回 `2`。[bundle] |
| `rzDevice25LedMatrixBlade.73deb9a606f938973519.js` 模块 77127 | 类名 `rzDevice25LedMatrixBlade`，`channelName="bladeNativeAction"`，DLL 为 `bladeNative.dll`；方法只有 `init`、`setBrightness`、`setOverClockingMode`、`getBladeScreenResolution`、`getBladeMonitorWindowsHDRMode`、`getSerialNumber`、`getBladeBiosAndECVersionInfo` 等。[bundle] |
| 同上 | `class N extends o.default`，其中 `o` 是模块 71513 `rzDevice25LedMatrix`；灯光能力来自父类，不在 Blade 文件里。[bundle] |
| 同上，`setBrightness` | `0 == (regionId = +regionId)` 时把 region 改成 `63301.k9.Backlight_LED`（值为 5），再调用 `37744.dD`。[bundle] |

### 1.2 命令常量

`rzDevice25.4e5b1930728d0086f431.js` 的模块 44193 是 LED 命令表，模块 37744 是实际发送函数。

| 常量 | 值 | 官方名称/用途 |
|---|---|---|
| `44193.Fw` | `new Uint8Array([80,3,10])` | 命令 class `0x03`、id `0x0A`；官方描述 `Set LED Matrix RGB Effect`。[bundle] |
| `44193.Tt` | `"Set LED Matrix RGB Effect"` | 上面 `0x03:0x0A` 的描述。[bundle] |
| `44193.aS` | `new Uint8Array([70,3,11])` | 命令 class `0x03`、id `0x0B`；官方描述 `Set LED Matrix RGB Define Pattern`。[bundle] |
| `44193.Ko` | `"Set LED Matrix RGB Define Pattern"` | 上面 `0x03:0x0B` 的描述。[bundle] |
| `44193.vA` / `Bv` | `[80,3,12]` / `Set LED Lighting Bar Define Pattern` | 灯条/legacy 路径，023A 的 LedMatrix 类没有使用它。[bundle] |
| `44193.Xr` / `zB` | `[3,3,3]` / `Set LED Brightness` | 亮度命令；Blade 的 `setBrightness` 走这里，region 0 会被改成 `Backlight_LED=5`。[bundle] |
| `56208.sk` / `Qe` | `[80,15,3]` / `Set Chroma Frame` | 通用 0x0F:0x03 Chroma 帧命令；023A 的 LedMatrix 覆盖为 0x03:0x0B。[bundle] |
| `56208.aA` / `z` | `[80,15,2]` / `Set Chroma Effect` | 通用 0x0F:0x02 Chroma 效果命令；base `rzDevice25` 使用，LedMatrix 覆盖为 0x03:0x0A。[bundle] |

注意：`[80,3,10]` 和 `[70,3,11]` 的第一个字节是命令缓冲上限，不是实际 dataSize。实际 dataSize 在逻辑报文 `[5]`。

### 1.3 效果 id 和颜色参数

`rzDevice25LedMatrix` 的 `setChromaEffectCommon` 先把 Chroma 侧效果 id（模块 90582 的 `Kw`）映射成固件效果 id（模块 63301 的 `UE`），再调用 `37744.xO`（即 `k`）。`37744.k` 使用 `44193.Fw`，也就是 `0x03:0x0A`。[bundle]

说明：在 023A 的本地 chunk 中没有找到 Chroma 效果页对这些方法的调用点。这里证明的是 023A 选中的设备类实现了 03:0A/03:0B，而不是 023A 本地页面实际调用了它们；Chroma UI 可能来自共享仓库。[bundle][inference]

下表的 payload 从 `37744.k` 的 switch 分支直接读出；`dataSize` 是该分支设置的 `s`/`u` 值。[bundle]

| Chroma 侧 `Kw` | 线上 `UE` | `0x03:0x0A` payload | dataSize | 备注 |
|---|---:|---|---:|---|
| Off 0 | 0 | `00` | 1 | 关闭 |
| Static 1 | 6 | `06 R G B` | 4 | 颜色在 payload[1..3] |
| Breathing 2 | 3 | `03 01 R G B` | 5 | 单色呼吸 |
| 同上 | 3 | `03 02 R G B R G B` | 8 | 双色呼吸 |
| 同上 | 3 | `03 03` | 2 | 随机呼吸；不是 8 字节 |
| Spectrum 3 | 4 | `04` | 1 | 光谱循环 |
| Wave 4 | 1 | `01 dir` | 2 | `dir` 是 payload[1]，数值未在 bundle 中命名 |
| Reactive 5 | 2 | `02 flags R G B` | 5 | `flags` 常被现有代码当作 speed |
| CustomFrame 8 | 5 | `05 flags` | 2 | 帧发送完后的 latch 使用 `05 00` |
| Starlite 7 | 25 | `19 flags rate R G B ...` | 9 | 最多 6 个颜色，payload[3..8] |
| BasicRipple 6 | 19 | `13 flags rate R G B ...` | 9 | 与 Starlight 同属 9 字节族 |

结论：bundle 能证明静态、呼吸、光谱、波浪的**效果 id、参数位置和 dataSize**；不能证明 `flags`/`rate` 的语义名称。bundle 里另有一个方向枚举 `RIGHT_LEFT:0, LEFT_RIGHT:1, BOTTOM_TOP:2, TOP_BOTTOM:3`（模块 2186），但当前 023A 的 Wave 发送路径只把 `flags` 原样写入 payload[1]，没有在这个路径里引用该枚举，所以 `dir` 的数值仍需实机确认。

`rzDevice25LedMatrix` 里还定义了 `createSetChromaEffectCommonData`，映射和 payload 形状与 `37744.k` 一致；但 `setChromaEffectCommon` 实际调用的是 `37744.k`，所以本文以 `k` 的字节为准。[bundle]

### 1.4 帧 payload

`rzDevice25LedMatrix` 的 `generateChromaFrameData` 和 `37744.M`（`JZ`）构造 0x03:0x0B 的 payload：

```text
payload = [0xFF, row, startColumn, endColumn, R0, G0, B0, R1, G1, B1, ...]
dataSize = 4 + 3 * (endColumn - startColumn + 1)
buffer = 70 bytes
```

- `0xFF` 是 frameId，不是 transaction id；`row` 是行号，`startColumn`/`endColumn` 是包含端点的列范围。[bundle]
- `generateChromaFrameData` 先把数组补零到 70 字节，再返回精确的 `packetSizeSet`；`_createDataSend` 只复制 `dataSize` 字节。[bundle]
- 70 字节缓冲最多容纳 `(70-4)/3 = 22` 列；16 列时 payload 为 `4 + 48 = 52` 字节。这个上限不能反推 023A 一定是 16 列。[bundle][inference]
- `setChromaFrameLightingEngine` 按 `eventData.numRow` 逐行调用 `setChromaFrame`：`row=0..numRow-1`、`start=0`、`end=numCol-1`；每行之后等待 1ms（批量关闭时）。[bundle]
- 行循环本身只在 `isCustomFrame` 为 true 时调用 `setChromaFrame`；同样在这个条件下，所有行发完后再发一条 0x03:0x0A 的 `CustomFrame` 效果 `[05,00]`（dataSize 2）作为 latch。[bundle]
- `profileId` 和 `regionId` 出现在 JS 方法签名里，但没有进入 0x03:0B 的 payload；`37744.M` 只使用 frameId、row、start、end、colors。[bundle]

### 1.5 transaction 和 report

`rzDevice25.4e5b1930728d0086f431.js` 的 base class 模块 74503 负责组包：[bundle]

```text
logical[0]    = 0
logical[1]    = transaction id，0..31 循环
logical[2..4] = 0
logical[5]    = dataSize
logical[6]    = command class
logical[7]    = command id
logical[8..]  = payload
logical[88]   = XOR(logical[2..87])
logical[89]   = 0
```

- `_getTransactionId()` 在 31 之后回到 0，再自增；所以是 0..31，不是固定 0xFF。[bundle]
- `reportLength = 91`，`reportId` 默认 `0`；023A 的 main 没有覆盖 reportId。[bundle]
- Windows 侧的 91 字节 feature report 是 `reportId(0) + logical[0..89]`；OpenSynapse 的 `RazerFeatureReport` 也使用这个布局。[bundle][code]
- 非 Electron 路径调用 `device.controlTransferOut({requestType:"class", recipient:"interface", request:9, value:768, index:claimInterface}, logical)`；`claimInterface` 为 2，`768` 即 `0x0300`。[bundle]
- Electron 路径调用 `hid.sendFeatureReport`，参数带 `reportId:0`、`reportLength:91`、`protocol:"25"`。[bundle]
- 效果命令默认 `checkUSBTransferIn=true`，会读回响应；帧命令传 `checkUSBTransferIn=false`，不读回。[bundle]
- 批量灯光类 `rzDevice25BatchLighting` 的 `isBatchLightingSupported` 硬编码为 `false`，所以 023A 的实际路径是逐行 feature report，不是批量缓冲。[bundle]

### 1.6 是否需要 store/region 前缀

| 命令 | 前缀 | 证据 |
|---|---|---|
| `0x03:0x0A` 效果 | 无；payload[0] 是效果 id | `37744.k` 直接 `m[0]=effectId`，没有 profile/region 参数。[bundle] |
| `0x03:0x0B` 帧 | 无；payload[0] 是 frameId `0xFF` | `37744.M` 直接 `[frameId,row,start,end,...]`。[bundle] |
| `0x03:0x03` 亮度 | `[ledClassId, ledId, brightness]`；这是 LED class/zone，不是效果 store | `37744.I`；Blade 把 region 0 改成 `Backlight_LED=5`。[bundle] |
| `0x0F:0x02` 通用 Chroma 效果 | `[profileId, regionId, effect, ...]` | base `rzDevice25` 使用；LedMatrix 的 `setChromaEffectCommon` 覆盖成 03:0A，没有转发 profile/region。[bundle] |
| `0x03:0x02` 另一类 Blade 方言 | `[store=01, region=05, effect, ...]` | 仓库 `BladeLightingLedMatrixProtocol` 使用；不是 023A bundle 的 LedMatrix 路径。[code] |

因此，给 023A 写 03:0A/03:0B 时不要照搬 `01 05` 前缀。OpenRazer 的 `defaultStorage:"01"`、`defaultLedZone:"05"` 是它自己的 builder 默认值，不等于 bundle 的 03:0A/03:0B payload。[OpenRazer][bundle]

## 2. OpenRazer 能证明什么

`src/OpenSynapse.Windows/Devices/OpenRazer/laptops.json` 第 988 行起是 023A 条目：[OpenRazer]

| 项 | 值 |
|---|---|
| `productId` | `023A` |
| `displayName` | `Razer Blade 15 (2019) Advanced` |
| `category` | `laptop` |
| `capabilitySet` | `laptops-js-004` |
| `matrixDimensions` | `[6,16]`；按 `OpenRazerDeviceCatalog` 的解析，数组第 0 项是 Rows，第 1 项是 Columns，即 6 行 16 列。[code] |
| `waitMicroseconds` | `600` |
| `transactions` | 所有相关命令都是 `"FF"`，即 OpenRazer 的自动 transaction。[OpenRazer] |
| `defaultStorage` | `01` |
| `defaultLedZone` | `05` |
| 矩阵效果命令 | `razer_chroma_standard_matrix_effect_*`，包括 breathing、custom_frame、none、reactive、spectrum、starlight、wave。[OpenRazer] |
| 自定义帧命令 | `razer_chroma_standard_matrix_set_custom_frame`。[OpenRazer] |

OpenRazer 的 builder 也使用同一组命令 id：`OpenRazerLightingProtocol.cs` 第 162 行用 `0x03:0x0B` 发自定义帧，第 209 行用 `0x03:0x0A` 发标准效果；自定义帧的声明长度是 `0x46`，即 70 字节，和 bundle 的 70 字节缓冲一致。[code]

OpenRazer 能证明的只有：

- 023A 被登记为 6x16 矩阵。
- 023A 的 OpenRazer capability 表包含 03:0A 标准矩阵效果和 03:0B 自定义帧。
- OpenRazer 自己用 `FF` 作为自动 transaction，并用 `defaultStorage=01`、`defaultLedZone=05` 组它自己的报文。

它不能证明 023A 实机一定接受 bundle 的 0..31 transaction，也不能证明 6x16 的物理行序/方向。

## 3. 仍缺什么，以及是否会阻止 03:0A

| 缺口 | 影响 | 是否阻止 03:0A 静态/呼吸/光谱/波浪 |
|---|---|---|
| bundle 没有显式 6x16 常量；尺寸来自调用方 `numRow/numCol` | 只影响 03:0B 的帧几何；03:0A 是固件效果，不需要行列数 | 否 |
| 没有 bundle 证据证明 row 0 是顶部还是底部、列是左到右还是蛇形、16 列还是 17 列 | 只影响 03:0B 的像素映射；不影响 03:0A 的效果 id/颜色 | 否 |
| bundle 有方向枚举 `RIGHT_LEFT:0, LEFT_RIGHT:1, BOTTOM_TOP:2, TOP_BOTTOM:3`，但 Wave 发送路径没有引用它；`WAVE_DIRECTION:"LeftRight"` 只是设备 UI 字符串 | 影响 Wave 的 `dir` 字节；结构上仍是 `[01,dir]` | 否；但 `dir` 值需要实机确认，不能把 1/2 当成 bundle 事实 |
| `flags`/`rate` 的语义没有在 bundle 里命名；Reactive 的 flags 是否等于 speed、Breathing 的 1/2/3 是否就是单色/双色/随机，均是从分支推出来的 | 影响参数范围和 UI 映射，不影响 payload 位置 | 否；静态和光谱完全不受影响，呼吸/反应需要按分支值实现 |
| bundle 的 transaction 是 0..31 循环，OpenRazer 是 `FF`，现有 `BladeLightingProtocol` 用 `FF`/逐行 1..7 | 影响响应匹配和重试；不影响 payload | 否；但新实现必须选定一种并实机验证 |
| CustomFrame 的 flags：bundle 的 latch 用 `00`，OpenRazer 的 `defaultStorage` 是 `01` | 可能影响效果是否持久化/跨睡眠保留，不影响立即显示 | 否 |
| 没有 023A 的实机抓包或 HID 回读；所有结论来自官方 bundle 和 OpenRazer 表 | 影响“已接受”的结论等级，不影响字节定义 | 否；但实现后必须做一次实机验证 |
| 是否需要在 03:0A 前切 driver mode 或发 0F:02 gate，bundle 的 Blade 类没有直接体现 | 可能影响第一条效果是否被固件接受 | 否；payload 完整，接受条件需实测 |

| 目标 | 当前结论 |
|---|---|
| 03:0A Static | 可立即按 bundle 实现；`06 R G B`，dataSize 4。 |
| 03:0A Breathing（单色/双色/随机） | 可立即按 bundle 实现；`03 01 ...` / `03 02 ...` / `03 03`，dataSize 5/8/2。 |
| 03:0A Spectrum | 可立即按 bundle 实现；`04`，dataSize 1。 |
| 03:0A Wave | 可组包；`01 dir`，dataSize 2；`dir` 数值需实机确认。 |
| 03:0B 自定义帧 | 字节可组包；6x16 的物理行序/方向/列数需实机确认。 |

### 对实现的直接结论

- **静态、呼吸、光谱**：bundle 已给出完整命令和 payload；静态用 `06 R G B`，呼吸单色/双色/随机分别用 `03 01 ...`、`03 02 ...`、`03 03`，光谱用 `04`。这些不依赖 6x16，也不依赖 store/region。[bundle]
- **波浪**：结构是 `01 dir`；`dir` 的数值没有在 bundle 中命名。实现可以保留一个显式的方向参数，但不要把某个值写成“官方已证明”。[bundle][unverified]
- **03:0A 不被 6x16 缺口阻塞**。6x16 只影响 03:0B 的逐行帧；固件效果由设备自己渲染，不需要帧数据。[inference]
- **03:0B 可以按 bundle 组包**：`[FF,row,start,end,RGB...]`，dataSize `4+3*N`，最后发 `05 00` latch。6x16 的物理行序/方向必须实机确认后再宣称完整矩阵支持。[bundle][unverified]
- **不要复用 6x17**：仓库现有 `BladeLightingProtocol` 的 `Rows=6, Columns=17` 是另一条 Blade 16 路径，不是 023A 证据；023A 的显式尺寸来自 OpenRazer 的 6x16。[code][OpenRazer]
- **不要复用 03:02 的 `01 05` 前缀**：仓库 `BladeLightingLedMatrixProtocol` 是另一类 Blade 方言；023A bundle 的 03:0A/03:0B 没有这个前缀。[code][bundle]
- **现有 `BladeLightingProtocol` 与 bundle 的具体差异**：它的 BreathingRandom 发 dataSize 8，而 bundle 的 `03 03` 是 dataSize 2；它的 Starlight 声明 dataSize 1，而 bundle 的 9 字节族是 dataSize 9；它的帧 transaction 是逐行 1..7/效果 0xFF，而 bundle 是 0..31 循环。新实现应按 023A 的 bundle 重新组包，或在 builder 上参数化这些差异，不能直接复制旧常量。[code][bundle]

没有在 bundle 中找到的数值，本文不补默认值；上表中标为 `[unverified]` 的项需要 023A 实机确认。
