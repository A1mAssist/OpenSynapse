# Handoff：补齐雷云 JS 已实现但尚无 UI 的 9 个 OpenRazer 能力

## 目标

雷云 JS 驱动了 11 个 OpenRazer 侧此前没有的能力。协议层与设备目录已经全部落地并与 JS
对齐，但只有 **Auto Sleep** 和 **Battery Charge Limiter** 有界面。剩下 9 个能力
在设备上能被读到、写到，但用户在应用里看不到也改不了。

本任务就是给这 9 个能力补上界面，让用户在设备页能读出当前值并修改。

## 起点状态

- 分支 `main`，HEAD `095a2ba`
- 协议对齐校验 `python tools\check_razer_js_alignment.py --protocols artifacts\razer-mw-full-cdn\out7\protocols.json`
  → `checked 2627 device/builder pairs, mismatches: 0`
- `dotnet test OpenSynapse.slnx` → 952/952 通过

**不要破坏上面两条。** 每次改完都要重跑。

## 9 个待补 UI 的能力

协议层的 builder 已经全部写好，在
`src/OpenSynapse.Windows/Protocols/OpenRazerMouseProtocol.cs`。
下表是 JS 原文核对的命令、目录覆盖与 UI 语义。

| 能力 | builder（读/写） | class:id | JS 机型数 | 目录覆盖 | UI 形态建议 |
|---|---|---|---:|---:|---|
| Time To Dim | `razer_chroma_misc_get/set_time_to_dim` | `07:8B` / `07:0B` | 166 | 151 | 开关 + 秒数 + 变暗百分比 |
| Indicator LED Configuration | `razer_chroma_misc_get/set_indicator_led_configuration` | `07:90` / `07:10` | 166 | 102 | 状态下拉（见下） |
| GPU Card Detection | `razer_chroma_misc_get/set_gpu_card_detection` | `03:A0` / `03:20` | 139 | 102 | 开关（LED class id 固定 0） |
| Force Chroma Ctrl | `razer_chroma_misc_get/set_force_chroma_ctrl` | `0F:90` / `0F:10` | 101 | 102 | 状态下拉 |
| Profile Debounce Time | `razer_chroma_misc_get/set_profile_debounce_time` | `02:95` / `02:15` | 95 | 62 | 4 个毫秒数输入 |
| Chroma Tap | `razer_chroma_misc_get/set_chroma_tap` | `0F:93` / `0F:13` | 85 | 100 | 状态下拉 + 模式 + 功能 id |
| Battery Discharge Curve | `razer_chroma_misc_get/set_battery_discharge_curve` | `07:94` / `07:14` | 56 | 102 | 电池化学类型下拉 |
| Macro Storage Info | `razer_chroma_misc_get_macro_storage_info` | `06:8E` | 206 | 151 | **只读**显示容量摘要 |
| LK Scroll Wheel Mode | **未实现** | `15:8B` / `15:0B` | 85 | **0** | 见「LK 特殊」 |

各能力的 payload 布局（**已按 JS 原文核对**）：

```text
Time To Dim
  GET  07:8B size=5  请求 [batteryId]
  SET  07:0B size=5  请求 [profileId, enabled, High(seconds), Low(seconds), percentage]
  响应 [profileId, enabled, timeToSleep(U1 大端), percentage]

Profile Debounce Time
  GET  02:95 size=5  请求 [profileId]
  SET  02:15 size=5  请求 [profileId, makeDebounce, makeMask, breakDebounce, breakMask]
  响应 [profileId, makeDebounce, makeMask, breakDebounce, breakMask]

Chroma Tap
  GET  0F:93 size=4  请求 [profileId, 0, 0, 0]
  SET  0F:13 size=4  请求 [profileId, status, tapMode, functionId]
  响应 [profileId, status, statusEnum, tapMode, tapModeEnum, functionId, functionIdEnum]

Macro Storage Info
  GET  06:8E size=14 请求 []
  响应 [maxId(U1), maxMemory(U1,U1), availableMemory(U1,U1), recyclableMemory(U1,U1)]  共 7 个 u16

Battery Discharge Curve
  GET  07:94 size=1  请求 []
  SET  07:14 size=1  请求 [chemistry]

Indicator LED Configuration
  GET  07:90 size=1  请求 []
  SET  07:10 size=1  请求 [configuration]

GPU Card Detection
  GET  03:A0 size=3  请求 [ledClassId]
  SET  03:20 size=3  请求 [ledClassId, enabled]
  响应 [ledClassId, enabled, status]

Force Chroma Ctrl
  GET  0F:90 size=1  请求 []
  SET  0F:10 size=1  请求 [configuration]
```

## 必须先解决的三个问题

这三个不解决，UI 做出来也是错的。

### 1. 目录里的机型集合与 JS 不一致

上一轮按「同设备 class 7 电池族已有 transaction id」批量添加，结果和 JS 的实际集合对不上：

| 能力 | JS 机型 | 目录覆盖 | 目录里有但 JS 没有 |
|---|---:|---:|---:|
| Time To Dim | 166 | 151 | 29 |
| Battery Discharge Curve | 56 | 102 | 67 |
| Indicator LED Configuration | 166 | 102 | 3 |
| GPU Card Detection | 139 | 102 | 3 |
| Profile Debounce Time | 95 | 62 | 0 |
| Chroma Tap | 85 | 100 | 65 |
| Force Chroma Ctrl | 101 | 102 | 67 |
| Macro Storage Info | 206 | 151 | 3 |

「多出来」的机型在 JS 里是存在的，只是**没有这条命令**——雷云的 bundle 按设备类型分化，
`chroma_tap` / `force_chroma_ctrl` 是鼠标专属，键盘（如 0221 BlackWidow Chroma V2、
0226 Huntsman Elite）从来没有。上一轮把它们也加进了键盘目录。

**正确做法**：从 `artifacts/razer-mw-full-cdn/out7/protocols.json` 里逐条按 JS 的
`usedCommands` 名字取出机型集合，重写 catalog 的 `transactions` 与 `capabilitySets`。
JS 里的命令名（注意 GPU 那条名字很怪，是雷云自己拼出来的）：

```text
Get Time To Dim                              Get Indicator Led Configuration
Get Battery Discharge Curve                  Get LED Get GPU Card Detection
Get Profile Debounce Time                    Get Chroma Tap
Get Force Chroma Ctrl                        Get Macro Storage Info
```

修改 catalog 的注意事项：

- JSON 写入必须 byte-identical round-trip：
  `json.dumps(doc, indent=2, ensure_ascii=False) + "\n"` 再把 `\n` 换成 `\r\n`，
  编码 UTF-8。改完用这个方法验证仍是 identical。
- `commandSizes` 的 builder 必须同时存在于 `transactions`，否则
  `OpenRazerDeviceCatalog` 抛 `contains invalid command sizes`。
- `capabilitySets` 的名字要对应 service 层 `Add()` 里的 source capability，
  命名规则是 `get_<x>` / `set_<x>`。

### 2. LK Scroll Wheel Mode 协议层根本没写

JS 里 85 个机型用它，class `15`（21）id `139`（读）/ `11`（写），size 2：

```text
GET  15:8B size=2  请求 [classId]
SET  15:0B size=2  请求 [classId, mode]
响应 [classId, level]   （JS 解析函数 pn）
```

需要先写 builder，再加目录条目和 UI。注意 class 是 **21 = 0x15**，
不是之前文档里写的别的值。

### 3. 能力枚举与 capability 激活

`OpenRazerBackendCapability`（`src/OpenSynapse.Windows/Devices/OpenRazerDeviceState.cs`）
目前只有 `AutoSleepRead/Write` 和 `BatteryChargeLimiterRead/Write`。
9 个新能力要各自加 Read/Write 枚举项。

`OpenRazerDeviceService.ResolveCapabilities` 里的 `Add(capability, sourceCapability, builder)`
要求 **source capability 存在且 builder 存在**，两个条件缺一能力就永不激活。
上一轮 auto_sleep / charge_limiter 就是因为 capabilitySet 里没有对应名字而成了死代码。

每个新能力要同时补三处，缺一个都不会出现在界面上：

1. `OpenRazerBackendCapability` 枚举项
2. `ResolveCapabilities` 里的 `Add(...)` 调用
3. 目录 `capabilitySets` 里的 source capability 名字

`Macro Storage Info` 只有读，没有写，对应只加 Read。

## UI 实现方式

照抄已落地的 Auto Sleep / Battery Charge Limiter，不要另起一套。

### 参照实现

`Auto Sleep` 与 `Battery Charge Limiter` 已经完整走通一遍，涉及：

| 位置 | 内容 |
|---|---|
| `src/OpenSynapse.Windows/Devices/OpenRazerDeviceState.cs` | `OpenRazerBasicState` record 加字段 |
| `src/OpenSynapse.Windows/Devices/OpenRazerDeviceService.cs` | 读取链路 `ReadAsync(...)` + 公开的 Get/Set 方法 |
| `src/OpenSynapse.App/ViewModels/OpenRazerDeviceViewModel.cs` | 属性、`Visibility`、`CanWrite*`、`CanEdit*` |
| `src/OpenSynapse.App/ViewModels/OpenRazerDeviceViewModel.Basic.cs` | `ApplyBasic` 里把 state 灌进属性 |
| `src/OpenSynapse.App/ViewModels/OpenRazerDeviceViewModel.Helpers.cs` | `OnPropertyChanged(nameof(XxxText))` |
| `src/OpenSynapse.App/ViewModels/OpenRazerDeviceViewModel.Pointing.cs` | `ApplyXxxAsync` 写入方法 |
| `src/OpenSynapse.App/MainWindow.OpenRazer.cs` | `OpenRazerApplyXxxClick` 事件处理 |
| `src/OpenSynapse.App/MainWindow.xaml` | 「电源管理」卡片（约 721 行）里的控件 |
| `src/OpenSynapse.App/Strings/{zh-CN,en-US}/Resources.resw` | 标签文案 |

### 命名

ViewModel 属性用 `XxxVisibility` / `XxxWriteVisibility` / `XxxText` / `CanWriteXxx` /
`CanEditXxx` / `IsXxxBusy`，与既有的 `IdleVisibility` / `LowBatteryVisibility` 对齐。

### 文案要走资源

ViewModel 里的中文要用 `AppStrings.Text("资源名")`，不要硬编码。
资源名规则：既有的是 `Text_4366F0BC` 这种哈希名，新增的可以用可读名（本轮已经这么做了，
见 `Text_OpenRazerAutoSleepOn`）。`AppStrings.Text` 在
`src/OpenSynapse.App/Strings/{zh-CN,en-US}/Resources.resw` 两份里都要加。

### 控件摆放

Time To Dim / Battery Discharge Curve / Indicator LED / Battery Charge Limiter
都属于电源与电池，放进现有的「电源管理」卡片。

GPU Card Detection 属于灯光，放进「灯光」卡片。

Force Chroma Ctrl / Chroma Tap 属于灯光效果，放进「灯光」卡片。

Profile Debounce Time 属于按键响应，可以放进滚轮设置那张卡片，或新建一张
「按键响应」卡片——9 个能力一次性铺开会让页面很长，倾向新建卡片。

Macro Storage Info 是只读摘要，可以并进「板载宏」卡片（那卡片已经在显示板载宏状态）。

LK Scroll Wheel Mode 放进「滚轮」卡片。

### 值的约束

`CanEdit*` 要限制取值范围，否则用户能填出设备不认的值。参考既有做法：

```csharp
public bool CanEditIdle => CanUse(OpenRazerBackendCapability.IdleTimeoutWrite) && !IsIdleBusy &&
    IdleTimeoutSeconds is >= 60 and <= 900;
```

`Macro Storage Info` 是只读，没有 `CanEdit*` 和保存按钮。

## 验收

```powershell
dotnet build OpenSynapse.slnx -v q --nologo          # 0 警告 0 错误
dotnet test OpenSynapse.slnx --nologo -v q          # 全部通过
python tools\check_razer_js_alignment.py --protocols artifacts\razer-mw-full-cdn\out7\protocols.json
```

第三条必须仍是 `mismatches: 0`。改了 catalog 的机型集合之后，这条校验的
`checked` 数字会变（会涨，因为补了 JS 有而目录没有的机型），mismatch 必须还是 0。

`tests/OpenSynapse.Core.Tests/OpenRazerBackendTests.cs` 里的
`EveryPublishedDeviceCapabilityCanConstructItsDefaultRequest` 会遍历所有设备的
所有 capability 并构造默认请求。**每加一个 capability 枚举项，都必须在这个测试的
`Construct` switch 里补一条**，否则测试会报
`Capability Xxx has no construction assertion`。

`EveryPublishedDeviceCapabilityCanConstructItsDefaultRequest` 和
`OpenRazerBackendTests.BasiliskV3XHyperSpeedDoesNotInheritUnlistedGenericControls`
对 capability 数量有断言（后者当前是 18）。**动了 00B9 的 capability 集合就要同步改数字。**

## 实机说明

本任务的全部结论来自 JS 源码静态对照，**没有实机验证**。这些能力在真机上生效需要一台
对应机型。提交时在 commit message 里说清楚这一点，不要写成已验证。

## 相关文件速查

```text
协议层     src/OpenSynapse.Windows/Protocols/OpenRazerMouseProtocol.cs
目录       src/OpenSynapse.Windows/Devices/OpenRazer/{mice,keyboards,laptops,accessories}.json
目录解析   src/OpenSynapse.Windows/Devices/OpenRazerDeviceCatalog.cs
能力枚举   src/OpenSynapse.Windows/Devices/OpenRazerDeviceState.cs
能力激活   src/OpenSynapse.Windows/Devices/OpenRazerDeviceService.cs
视图模型   src/OpenSynapse.App/ViewModels/OpenRazerDeviceViewModel*.cs
界面       src/OpenSynapse.App/MainWindow.xaml （电源管理卡片约 721 行）
事件       src/OpenSynapse.App/MainWindow.OpenRazer.cs
文案       src/OpenSynapse.App/Strings/{zh-CN,en-US}/Resources.resw
测试       tests/OpenSynapse.Core.Tests/OpenRazerBackendTests.cs
           tests/OpenSynapse.Core.Tests/OpenRazerMiddlewareAlignmentTests.cs
基准       artifacts/razer-mw-full-cdn/out7/protocols.json  （由 tools/ 下的提取器生成）
提取器     tools/extract_razer_middleware_protocols.py
校验器     tools/check_razer_js_alignment.py
审计文档   docs/JS_PROTOCOL_ALIGNMENT_AUDIT.md  （gitignored，本地文件）
```

`artifacts/` 和 `tools/` 都在 `.gitignore` 里，本机可用但不进 commit。
`docs/` 目录被 `docs/*` 规则忽略，只有白名单里的文件会被跟踪。
