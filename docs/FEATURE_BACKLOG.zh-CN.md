# 功能待办与证据

记录 2026-11-12 对 6737 与 protocol 37–42 bundle 的调查结论。全部是本地 bundle 静态阅读和源码对照，没有连接设备，不声称实机验证。`artifacts/` 是本地证据目录，不随仓库发布；文件名里的 hash 会随 middleware 版本变化。

## 结论速览

| 项目 | 结论 | 本轮动作 |
|---|---|---|
| `CYCLE_SCROLL_MODE` | 同一个 `ScrollModeSwitch` 动作的两种显示标签，不新增 wire 命令 | 不改代码 |
| 耳机 4.0 解析器 | 真正选择它的 PID 已被 Mxic64 / Class18 / ValkyrieT3 覆盖 | 不重复实现 |
| Protocol25 keypad 解析器 / `obmEngineKeypadCalMaxMem` | 有 keypad PID（`02EB`），缺模拟键与校准实现 | 仅记录 |
| `ControlPodInput` / `CoolingPadInput` | 有 PID（`0F45` / `0F43`），缺配件映射入口 | 仅记录 |
| 模拟键盘 `actuationPoint` / 校准 | 协议入口在 Huntsman V3 Pro bundle；缺实机 | 仅记录 |
| 键盘 Kill Switch Ctrl | 命令头已知；本地没有 M16 键盘 PID | 仅记录 |
| Chroma 游戏列表 | 账号/本地游戏库功能，不是 HID 能力 | 明确不做 |
| Mouse Dock 配对 | IoT 配对链路，不是 HID 映射 | 明确不做 |

## 1. `CYCLE_SCROLL_MODE`（P2，不改 wire）

证据：`artifacts/razer-mw-full-cdn/bundles/022B/6737.dea211e46904fb2956f3.js`（022B 是 Razer Tartarus V2 的构建，见同目录 `catalog.json`）。

- 动作表只有一条 `ScrollModeSwitch`：`DeviceInfo.useCycleScrollModeLabel` 为真时显示 `CYCLE_SCROLL_MODE`，否则显示 `SCROLL_MODE_TOGGLE`。
- 同一个 bundle 的赋值表把 `DKM_M_106` 写成 `mouseAssignment:"ScrollModeSwitch"`，编码结果是 `{downOutput:{type:"disabled"}, upOutput:{type:"scrollModeSwitch",flag:1}}`。
- 事件分发是 `case"scrollModeSwitch"`；`ScrollModeV2Feature` 只决定走哪条发送函数，标签不参与动作 id 或 flag。

源码对照：

- `src/OpenSynapse.Windows/Protocols/RazerHidMappingActions.cs` 已有 `ScrollWheelModes ("Toggle scroll mode", 0x01)` 和 `RazerKeys ("Scroll mode switch", 84)`。
- `src/OpenSynapse.Windows/Input/RazerPrivateKeyCatalog.cs` 已把 84 映射为 `ScrollModeSwitch`。

结论：同一动作，两种标签。`CYCLE_SCROLL_MODE` 只是 UI 文案，不新增命令、码值、枚举或 per-device flag。本地快照里找不到 `useCycleScrollModeLabel` 的定义，无法把标签归到具体 PID；不猜。

下一步：只有拿到设备配置、能明确指出哪些 PID 打开该标签时，才在文案层加显示分支；协议层不动。

## 2. 耳机 protocol 4.0 解析器（P2，不重复实现）

证据：`artifacts/razer-mw-refresh-20261112/bundles/05B3/rzHardwareEventsProtocol40ParserHeadset.686b89eeb976e4908277.js`。

- 报告 id 2，validation byte 10，protocol header offset 6。
- class 24（0x18）：无线连接、EQ preset、ANC、麦克风/音频静音；class 7（0x07）：电量、充电。
- 输出 `typeId: PROTOCOL40_HEADSET`。

真正引用它的 bundle 与 PID（不是“构建里有这个 chunk”）：

- `artifacts/upstream-reference/razer-mw-full-cdn/bundles/0593/main.b9f9c975f785a39bf5f3.js`：`useFeature("rzDeviceType","rzDevice40",{hardwareEventParserType:PROTOCOL40_HEADSET})` 加 `useFeature("hardwareEvent","config",{hardwareEventParserType:PROTOCOL40_HEADSET})`。manifest 是 Razer Barracuda V2 Pro，PID `0x0593`。
- `artifacts/upstream-reference/razer-mw-full-cdn/bundles/05B9/main.6fbee7c10a0b42d68fa7.js`：同样的两组 feature。manifest 是 Razer Hammerhead V3 Chroma，PID `0x05B9`。

反证：

- `05B3`（Razer Barracuda V2 X）的 main 声明 `useFeature("rzDeviceType","rzDevice25ValkyrieT3")`，没有 `hardwareEvent` 配置。它的构建里有这个解析器 chunk，但 main 没有选择它；chunk 存在不等于该 PID 使用。
- `058A`（Razer Kraken Kitty V2 White）只有 56 个 JS 文件，没有 `rzHardwareEventsProtocol40ParserHeadset`，也没有 `PROTOCOL40_HEADSET` 引用。

覆盖情况：

- `0x0593` / `0x0592`：`RazerProtocol40HeadsetProtocol.IsSupported` 返回 true；`RazerHeadsetChannelCatalog` 走 `rzDevice40HeadsetAiroha` / `Mxic64`。
- `0x05B9`：`RazerHeadsetChannelCatalog` 走 `rzDevice40` / `Mxic64`；`RazerMxicHeadsetProtocol.UsesClass18Commands` 包含 `0x05B9`。
- `0x05B3`：`RazerHeadsetChannelCatalog` 走 `rzDevice25ValkyrieT3` / `ValkyrieT3Feature64`；`RazerMxicHeadsetProtocol.UsesValkyrieT3Commands` 包含 `0x05B3`。

结论：无需重复实现。三个 family 都已有通道和命令表；现有代码用 `ReadStateAsync` 按需读状态，没有实现 4.0 的主动通知解析。那是“实时刷新”增强，不是 PID 或通道缺口。

## 3. 只记录、不实现的能力

### 3.1 Protocol25 keypad 解析器 / `obmEngineKeypadCalMaxMem`

- 证据：`artifacts/razer-mw-refresh-20261112/bundles/05B3/rzHardwareEventsProtocol25KeypadParser.83a6f86620652109edbf.js` 解析 15 字节 `ANALOG_DATA`、按键/通知事件、校准 Z/Q 事件，输出 `PROTOCOL25_KEYPAD`；`artifacts/razer-mw-full-cdn/bundles/022B/obmEngineKeypadCalMaxMem.dc3205df44096a0cf943.js` 是 OBM profile 引擎，含 `loadProfilesFromDevice_killSwitch` 与 `analogKeyboardV3`。
- PID：只有 `0x02EB` Razer Tartarus V2 Pro 的 main 显式设置 `hardwareEventParserType:"PROTOCOL25_KEYPAD"`（`artifacts/upstream-reference/razer-mw-full-cdn/bundles/02EB/main.8ae72880bd6c9af82381.js`）。022B（Tartarus V2）和 0244（Tartarus Pro）构建里也有 chunk，但 main 没有选择它。
- 缺什么：不是 PID。OpenSynapse 已把 `02EB` 当普通键盘页，缺的是模拟键数据、校准 UI 和 OBM 写入验证。
- 下一步：要做 Tartarus V2 Pro 模拟键时，以 `02EB` 为目标，从上面的 ANALOG_DATA/校准事件和 OBM profile 命令入手；需要实机。计划里“缺 keypad PID”与本地证据不符，先更正。

### 3.2 `ControlPodInput` / `CoolingPadInput`

- 证据：`artifacts/reverse-engineering/razer-mw-peripherals/bundles/0F45/main.0b03a7026bf37874e8d5.js` 的输入分发表含 `ControlPodInput` / `CoolingPadInput`，并生成 `{type:"controlPod"}` / `{type:"coolingPad"}`；`artifacts/reverse-engineering/razer-mw-peripherals/bundles/0F43/main.395688fdc15e90725a3c.js` 用 `CoolingPadInput` 列出 CycleChromaLighting、ChromaEnabled/Disabled、CycleFanSpeedMode、TurnOn/OffFan、PowerOn/OffFan、ExternalPowerOn/Off。
- PID：`0F45` 是 Razer Freyja（本仓库只做灯光），`0F43` 是 Razer Laptop Cooling Pad（本仓库有风扇页）。所以不是缺 PID，是缺配件映射页和输入事件消费者。
- 下一步：不实现。只有将来为 Freyja / Cooling Pad 增加映射页时才需要；现有灯光/风扇页不依赖这两个 inputType。

### 3.3 模拟键盘 `actuationPoint` 与校准

- 证据：`artifacts/razer-mw-full-cdn/bundles/02A6/6737.b4bf391c075aa0879a1d.js`（Huntsman V3 Pro handler）含 `changeActuationPoint`、`setActuationPoint`、`startBottomMagneticAnalogCalibration`、`startTopMagneticAnalogCalibration`、`verifyBottom/TopMagneticAnalogCalibration` 和 `MW_UPDATE_ANALOG_CALIBRATED_KEY_LIST_TO_UI`。
- 设备命令：`artifacts/razer-mw-full-cdn/bundles/02A6/rzDevice25.bac662b0c922922e40fb.js` 的 `get/setMultiAnalogKeyActuationPoint`；`artifacts/reverse-engineering/synapse-dashboard-full/chunks/5342.915f77f3.chunk.js` 定义多键 actuation 命令头 get `[80,2,153]` / set `[80,2,25]`，rapid trigger get `[80,2,154]` / set `[80,2,26]`，模拟键顶部校准 get `[1,2,65]` / set `[2,254,173]`，校准模式 `[0,2,194]`，校准状态 `[0,2,67]`，重置 `[1,2,64]` / `[1,2,193]`。
- PID：`0x02A6` Razer Huntsman V3 Pro。`docs/SUPPORTED_DEVICES.zh-CN.md` 里 `02A6` / `02A7` / `02CF` 还没有键盘页，`02D0` / `02D1` / `02E6` 有。
- 缺什么：实机。校准写入会改变按键响应，静态 bundle 不能验证。
- 下一步：等 Huntsman V3 Pro 实机；先只读 actuation / rapid trigger，确认回读，再做写入和校准 UI。

### 3.4 键盘 Kill Switch Ctrl

- 证据：`artifacts/reverse-engineering/synapse-dashboard-full/chunks/5342.915f77f3.chunk.js` 的 `COMMAND_GET_KEYBOARD_KILL_SWITCH_CTRL_HEADER` / `COMMAND_SET_KEYBOARD_KILL_SWITCH_CTRL_HEADER` 分别为 `[4,2,161]` / `[4,2,33]`；`artifacts/razer-mw-full-cdn/bundles/022B/6737.dea211e46904fb2956f3.js` 从 `DeviceInfo.OBMSpecs.supportedFeature.killSwitch` 读能力，并把 OBM 的 `killSwitch` 映射到 snapTap 数据。
- 缺什么：本地 artifact 没有 M16 键盘标识/PID，也没有设备实测；不实现。
- 下一步：等具体键盘 PID/bundle 出现，先确认该设备的 class 0x02 支持 `4:161` / `4:33`，再决定是否实现。

### 3.5 Chroma 游戏列表（明确不做）

- 证据：`artifacts/razer-mw-full-cdn/bundles/022B/6737.dea211e46904fb2956f3.js` 的 `runtimeData.gameList`、`localGames`、`LINKED_GAMES_LOCALSTORAGE_KEY`。
- 缺什么：Synapse 账号/本地游戏库和进程匹配，不是 HID 命令；没有可复用的设备协议。
- 下一步：不做。

### 3.6 Mouse Dock 配对（明确不做）

- 证据：`artifacts/razer-mw-full-cdn/bundles/00AA/main.825b80863ba2ac985e91.js` 的 `MOUSE_DOCK_COMPATIBLE_DEVICE_MEMORY_STORAGE_KEY:"mouseDockMouse"`；同目录的 `rzDeviceIoT.db616763d33ab7b54e5b.js`（00AA / 00CC 构建都有）承载 IoT 配对链路。
- 缺什么：docking / IoT 传输和配对 UI，不是现有 HID 映射能力。
- 下一步：不做。

## 验证边界

以上结论来自 bundle 静态阅读和源码对照；没有连接任何 Razer 设备，也没有执行写命令。`artifacts/` 不入库；需要复现时从产品的 `mw/index.html` → `main.<hash>.js` → chunk map 重新解析，hash 会变。
