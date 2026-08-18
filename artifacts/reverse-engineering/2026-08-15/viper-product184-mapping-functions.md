# Viper V3 HyperSpeed（Product 184）映射协议逆向记录

## 结论

Product 184 的板载按键映射由 Synapse 的 `obmEngineMouse` 处理，存储在鼠标板载 Profile 中。UI 的普通层和 HyperShift 层分别读取、编码并写入单键 assignment；这不是只存在于 AppEngine JSON 的“假配置”。本记录只整理本地雷云缓存 JS、产品 UI/MW 日志和仓库内已完成的协议实现，**没有执行硬件写入**。

已被源代码直接证明的协议边界：

- Product 184 使用 `NormalMode=0` 与 `HypershiftMode=1` 两个映射层。
- 单键读取走 `rzDevice.getSingleButtonAssignment(profileId, buttonId, mode, 10)`；单键写入走 `setSingleButtonAssignment(profileId, buttonId, mode, fnId, dataSize, dataArray, 10)`。
- UI 的 HyperShift 激活器是 `ModeButtonkey`（function ID `12`，payload `[1]`）。不要把它和通用 `RazerKey` 的 `HyperShiftButton`（function ID `17`，payload `[89]`）混用。
- 当前线上 Product 184 的 `OBMSpecs.supportedMappings` 已把产品级白名单钉死：Off、Mouse、Keyboard、Multimedia、Sensitivity 和 HyperShift；Turbo 由产品谓词对普通 Mouse/Keyboard 动作放行。Macro/Profile/Lighting/Power/Controller/RazerKey/WindowsShortcuts 不属于 Product 184。

## 逆向来源

实际加载的 Product 184 mapping 模块：

```text
%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Default\Service Worker\CacheStorage\3842aea8f625086ac73d0f8e1c00277da03b0e65\3737c1e1-e37b-4535-9986-0a70bf4b302d\90df47dfb3ccc2a3_0
URL: https://apps.razer.com/synapse/products/184/mw/7303.7e80bbc75500e344896a.js
SHA-256: F33B63B1EA0267EE829C707D6A44354A691E9784D454BD5A3B5B870668DAC90D
```

该 webpack chunk 的类名为 `obmEngineMouse`。它接受 UI 的 `mappingListIn`，从
`inputIDToMapping.mouse.NormalMode/HypershiftMode` 建立 `fwIDToMapping`，再按固件 button ID 去重、排序。`getAllButtonAssignmentInfo()` 分别读取两个 mode；`setSingleButtonMapping()` 调用 `setSingleButtonAssignment()`。

通用 mapping UI/encoder 所在 Product 184 主 MW bundle：

```text
%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Default\Service Worker\CacheStorage\3842aea8f625086ac73d0f8e1c00277da03b0e65\3737c1e1-e37b-4535-9986-0a70bf4b302d\ae48bf012e072658_0
URL: https://apps.razer.com/synapse/products/184/mw/6259.c7cbeba24cbeff930baa.js
SHA-256: B6EFC53585C00B8AA2804DD8C08A5BA8C4A3FB957FF4F9DC53263D1ECF20AE53
```

其中模块 `4442` 是 generic mapping encoder/decoder，模块 `7215` 提供 enum。另一个本地
`rzDevice30` bundle（`d0a968352063798d_0`，SHA-256
`FFBB77D9211E31E85EF7380CFAE574390014663FAAF182FB8F14EBB7F1DFAB61`）只作为通用对照，不能替换 Product 184 已验证的 Protocol 2.5 命令。

产品 UI 日志位于：

```text
%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Logs\products_184_ui {9E502CF7-160A-51EA-8250-14BD19EB4A4A}.log
```

日志中出现 `key mappings value []`、`key sidePanelMappings value {}`、
`appEngine {"mappings":[],...}` 以及 `isHyperShift : false, filteredMappings : []`；这些是 UI 初始状态证据，不应被解释为设备没有 HyperShift 存储层。

## Product 184 的板载对象

### 固件 ID 与层

```text
NormalMode     = 0
HypershiftMode = 1
```

当前设备的只读枚举结果已经记录在
`artifacts/reverse-engineering/2026-08-15/viper-product184-obm-mapping.md`：

```text
MaximumProfiles = 1
ProfileIds      = [1]
ButtonIds       = [1, 2, 3, 4, 5, 9, 10, 96]
```

因此当前 Viper 是单个板载 Profile，UI 不应凭通用依赖渲染多 Profile 管理器。每个 button
有 Normal 和 HyperShift 两条 assignment，共 16 条记录。

### 单键报文（已和仓库实现对齐）

所有报文使用 Product 184 的 transaction `0x1F` 与 91-byte Razer feature-report envelope。

```text
读取 assignment：
  dataSize = 0x50
  class/id = 02/8C
  args     = [profileId, buttonId, mode]

写入 assignment：
  dataSize = 0x50
  class/id = 02/0C
  args     = [profileId, buttonId, mode,
              functionId, functionDataSize, data[0..4], 00...padding]
```

`functionDataSize` 的合法范围是 0..5；响应始终暴露 5 个物理 data byte，但只有声明长度的前缀有效。解析时必须校验 transaction、command、CRC 以及回显的 profile/button，不能只按固定偏移取值。Product 184 对 Normal 请求也会在响应 mode byte 回显 `1`，因此层归属必须采用经过校验的请求上下文，不能相信该回显字节。

Profile/button 元数据读取：

| 操作 | class/id | 结果 |
| --- | --- | --- |
| 最大 Profile 数 | `05/8A` | `maxProfilesSupported` |
| Profile 数量 | `05/80` | `numOfProfiles` |
| Profile ID 列表 | `05/81` | `[count,id0,...]` |
| 当前 Profile | `05/84` | `profileId` |
| 固件 button ID 列表 | `02/84` | `[count,buttonId0,...]` |

仓库已有的 `ViperObmProtocol.cs`、`ViperObmReadValidation.cs` 和
`ViperObmWriteValidation.cs` 实现了严格的 GET/SET 构造、回显校验及恢复型验证；生产调用仍保持关闭。

## Function ID 与 payload 编码

以下是从 Product 184 实际调用的 generic encoder `4442` 中反编译得到的静态契约。LE16 表示小端 16 位整数。

| UI mapping group | functionId | dataSize / data |
| --- | ---: | --- |
| `DisableGroup` | 0 (`Off`) | `0`, `[]` |
| `MouseGroup` 普通 | 1 (`ButtonCode`) | `1`, `[buttonId]` |
| `MouseGroup` turbo | 14 (`TurboModeButton`) | `3`, `[buttonId, delayLE16]` |
| `MouseGroup` double click | 11 (`DoubleClick`) | `1`, `[1]` |
| `KeyboardGroup` 普通 | 2 (`KeyCode`) | `2`, `[modifierMask, HID]` |
| `KeyboardGroup` turbo | 13 (`TurboModeKey`) | `4`, `[modifierMask, HID, delayLE16]` |
| `SensitivityGroup` DPI | 6 (`DPI`) | 第一字节 `1/2/5/6/7`；clutch 为 `5 + XLE16 + YLE16`（size 5） |
| `ProfileNavigationGroup` | 7 (`Profile`) | 第一字节 `1=up,2=down,3=specific(+profileId),4=cycleUp,5=cycleDown` |
| `PowerKeyGroup` | 9 (`PowerKeys`) | `[enum]` |
| `MultimediaGroup` | 10 (`MediaKeys`) | `[USB media key LE16]` |
| `HyperShiftGroup` | 12 (`ModeButtonkey`) | `1`, `[1]` |
| `RazerKeyGroup` | 17 (`RazerKey`) | `1`, `[RazerKey enum]` |
| Macro Type I | 3 (`MacroTypeI`) | Macro ID LE16 + repeat count |
| Macro Type II（held） | 4 (`MacroTypeII`) | generic macro payload |
| Macro Type III（toggle） | 5 (`MacroTypeIII`) | generic macro payload |
| Macro Type IV（sequence） | 15 (`MacroTypeIV`) | generic macro payload |

依赖还声明 `Lighting=8`、`Controller=16` 以及 `Win8ShortcutsKey=18`，但本次 Product 184 UI/设备证据没有证明这些是当前 Viper 的可用 button mapping 路径；不要因 enum 存在就把它们当作已完成能力。

## HyperShift：两个容易混淆的表示

### 产品 UI 实际路径（已证实）

模块 `4442` 的 `HyperShiftGroup` encoder 直接生成：

```text
functionId = 12  // ModeButtonkey
dataSize   = 1
data       = [1]
```

对应 decoder 将 function 12 映射回 `HyperShiftGroup`。产品 capability predicate 也对
`hyperShiftGroup` 和 `disableGroup` 特殊放行。因此后端的 HyperShift activator 应使用
`12/[1]`，并按 Normal/HyperShift mode 分层读写。

### 通用 RazerKey enum（未证明 Product 184 UI 使用）

同一 generic encoder 支持 `RazerKeyGroup`。其 enum 包含：

```text
FnKey=1, RazerKey=2, GameModeKey=3, MacroRecordKey=4,
DisplayInternalExternal=5, DisplayBrightnessUp=6,
DisplayBrightnessDown=7, KeyboardBacklightUp=8,
KeyboardBacklightDown=9, ToggleLowPowerMode=11,
MouseDPIUpButton=32, MouseDPIDownButton=33,
HyperShiftButton=89, ... , SnapTapToggle=117, MicMute=216
```

如果 UI 明确选择 `razerKeyGroup.razerKeyAssignment = HyperShiftButton`，静态 encoder 会生成：

```text
functionId = 17
dataSize   = 1
data       = [89]
```

但当前 Product 184 的加载映射没有发现该字段；这只是“generic encoder 支持的另一表示”，不能自动替代产品实际的 `ModeButtonkey 12/[1]`。

## 证据分级与剩余工作

### Source-backed（可以进入适配层，但仍需产品级回归）

1. Product 184 的 profile/button 枚举与两层 assignment 读取。
2. `02/8C` 读取和 `02/0C` 写入的字段布局、长度和回显校验。
3. Product 184 的 Off、ButtonCode、KeyCode、DPI、MediaKeys、DoubleClick、ModeButtonkey、TurboModeKey 和 TurboModeButton 编码规则。
4. HyperShift UI activator 的 `12/[1]` 契约。
5. Product 184 的 Profile 固定为 `1`，button ID 固定为 `1,2,3,4,5,9,10,96`。
6. DPI 与普通 polling 开启；brightness、scroll wheel、debounce、macro、reset 和 kill-switch 关闭。`highSpeedPollingRate` 是单独启用的共享特性，不等于标准 collection 已证明支持 1000 Hz 以上值。

### 仍需物理验证补证（不是静态逆向缺口）

1. Keyboard、Media、DoubleClick 和 Turbo 的逐函数写入、断开 dongle、睡眠/唤醒及退出 Synapse 后持久化等级。
2. Product 184 明确排除的 Macro/Profile/Lighting/Power/Controller/RazerKey/WindowsShortcuts 不再列为待逆功能。
3. 产品 UI 的 polling 列表只有 125/500/1000 Hz；超过 1000 Hz 的 shared high-speed dongle 路径要按实际配对硬件另行验证，不能在标准 00B8 collection 上猜写。

完整的当前线上 bundle 证据见
`artifacts/reverse-engineering/2026-08-16/viper-product184-product-scope-and-obm-capabilities.md`。

## 验证边界

本文件是静态逆向记录，不代表生产功能已经开放。任何新 function 的第一次 SET 都必须：

1. 先保存完整的 16 条 assignment JSON；
2. 只改一个按钮、一个 mode，写入前要求 baseline 与预期完全相等；
3. 立即读回并校验 profile/button/mode/function/dataSize/data；
4. 在 `finally` 中恢复原始 bytes，再用新进程全量读取确认；
5. 恢复失败时保持生产 SET 禁用，不覆盖原始 baseline。
