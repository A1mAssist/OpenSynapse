# 灵刃 16（2026 款）四显卡模式可行性研判

> 基于《雷蛇灵刃 16（2026 款）复刻 G-Helper 四种显卡模式》方案文档、当前 OpenSynapse 代码，以及 `docs/GPU_MUX_HID_ADAPTATION.zh-CN.md` 的 HID MUX 适配记录。

- 日期：2026-10-31
- 代码基线：`main`（只读核对；本文不改代码、不改协议）
- 性质：可行性研判，不是已交付能力清单

> **证据边界：本文不是“已完成”清单；2026 款相关写入与自动化均未实机验证。**
>
> 截至本文日期，本文没有取得 2026 款灵刃 16（代码识别 `1532:02E0`）的 MUX 写入、DDS=2 写入、NVIDIA 控制面板 UIAutomation 或电源事件切换实机验证记录。本文可引用的现有实机数据只有 2025 款 `1532:02C6` 的只读 MUX 记录。下文把“代码已实现”“协议已测试”“实机已验证”“方案建议”分开标注；没有标注“实机已验证”的，均不能对外宣称可用。

## 0. 结论先行

1. **四模式在硬件概念上可以映射，但不是把 G-Helper 搬过来。** G-Helper 的显卡模式执行层依赖华硕私有的 ATKACPI / ACPI-WMI 通道；雷蛇机器没有这些设备和端点，改常量、加分支都跑不通。
2. **当前 OpenSynapse 的显示模式执行层只有两块执行器，不是四模式成品。** 一块是 `WindowsDedicatedGpuMode` 的 PnP“仅核显（禁用独显）”；另一块是 `BladeDisplayTelemetryReader` 通过 `0D:89/8A/09` 读写 MUX。没有四模式选择器，没有 NVIDIA 控制面板 UIAutomation，也没有电源事件状态机。
3. **2026 款的 `0D:8A` 能力位是分水岭。** 如果它只报告 `0x06`（Discrete + DDS，无 Hybrid），当前 HID 路径只能提供“独显直连”和“DDS”，无法直接提供“标准（Optimus/Hybrid）”；“标准”必须依赖 NVIDIA 控制面板 UIA 或 BIOS/驱动默认策略。如果它报告 `0x07`（Hybrid + Discrete + DDS），当前代码才可以直接写 `0x00` 得到“标准”。
4. **当前 HID MUX 写入成功返回 `RestartRequired`，不适合按 AC/电池事件反复切换。** 真正可用的“自动”应优先依赖 DDS（`0x02`）由固件/驱动动态切换；应用层电源状态机只能是策略层，不能在每次电源变化时反复写 MUX，否则会变成反复重启。
5. **DDS=2 写入仍需实机复核。** 代码能生成 `0D:09` 的 `0x02` 写入，协议测试也覆盖了字节和解析，但本文没有取得 2026 实机的写入、重启后持久性、恢复和黑屏时间证据。在完成这些验证前，“自动/DDS”只能标为“待验证”，不能标为“已完成”。

## 1. 术语与证据等级

| 术语 | 本文含义 |
|---|---|
| MUX | 内置屏信号走 iGPU 还是 dGPU 的硬件显示路由切换。本文的 MUX 写入指 `0D:09`。 |
| Hybrid / Optimus | iGPU 驱动内置屏，dGPU 按需渲染，即通常说的“混合模式/标准模式”。 |
| DDS | Dynamic Display Switch，动态显示切换；NVIDIA 语境下对应 Advanced Optimus。本文的 DDS 模式值为 `0x02`。 |
| 集显模式 | 本文对应当前 UI 的“仅核显（禁用独显）”：通过 Windows PnP 禁用 NVIDIA 显示设备，不是 ACPI Eco，也不是 MUX 模式。 |
| UIA | Windows UI Automation，用于模拟操作 NVIDIA 控制面板。 |
| Eco | 华硕 G-Helper 的 dGPU 断电模式，通过 ASUS ACPI 实现；雷蛇没有等价通道。 |

| 等级 | 含义 | 本文中的例子 |
|---|---|---|
| E0 | 代码存在，且有协议/策略单元测试；本次未运行测试 | `0D:89/8A/09` 报文布局、MUX 模式解析、PnP 策略 |
| E1 | 只读实机记录 | 2025 款 `1532:02C6` 的 `0D:89/8A` 只读结果 |
| E2 | 2026 款只读实机记录 | **尚未完成** |
| E3 | 2026 款写入 + 重启 + 恢复实机验证 | **尚未完成** |
| E4 | UIA / 电源状态机 / BIOS 实机验证 | **未实现，未验证** |

> `manifest` 里声明了 `display-mux.get/set`，只证明代码知道怎么发这条命令，不证明当前机器支持。最终以设备返回的 `0D:8A` 能力位和 `0D:89` 当前模式为准。

## 2. 硬件事实

方案文档给出的目标机信息如下。它们来自方案材料，本文没有逐项做独立硬件复核；涉及具体 SKU 时，以实机 BIOS、设备管理器和 `0D:8A` 读取结果为准。

| 项目 | 方案材料中的事实 | 对四模式的意义 |
|---|---|---|
| 目标机型 | Razer Blade 16（2026）/ RZ09-0581x，RTX 5080 / 5090 Laptop | 高配 SKU；不同显卡和面板可能报告不同能力位 |
| 代码识别 | Blade 16（2026）= `1532:02E0`；Blade 18（2026）= `1532:02E1` | OpenSynapse 按 USB PID 选 manifest；`RZ09-0581x` 是型号名，不是能力判断依据 |
| GPU | RTX 5080 / 5090 Laptop，最高 165 W TGP | 支持 Advanced Optimus 的 Blackwell 移动版 |
| 显示切换 | NVIDIA Advanced Optimus（DDS 动态显示切换） | 四模式的核心硬件基础 |
| MUX | BIOS 提供 `Dedicated GPU Only` / `NVIDIA Dynamic Display Switch` 二选一 | BIOS 是上游开关；应用层 HID 不能绕过 BIOS 选择 |
| 屏幕 | 16" 2560×1600 240 Hz OLED | 切换过程可能有 1~3 秒黑屏；OLED 上更明显 |
| iGPU | Intel Core Ultra 9 386H 集成显卡 | 集显模式和 Hybrid 的输出通路 |
| 厂商软件 | Synapse 在 DDS 机型不提供显卡模式切换（方案材料引 KB 4585） | 没有官方一键按钮，需要自建执行层 |
| 外接输出 | HDMI 2.1 与部分 Type-C 直连 dGPU | 外接屏可能绕过 MUX；模式语义和恢复路径要单独定义 |

对工程最重要的三点：

1. **BIOS 是上游。** 如果 BIOS 当前是 `Dedicated GPU Only`，系统可能只暴露独显直连；想让“标准/自动”可用，通常需要先把 BIOS 切到 `NVIDIA Dynamic Display Switch`，再重启。应用内写 BIOS 变量风险极高，本文不建议。
2. **能力位决定 HID 能做什么。** `0D:8A` 返回的 bit 0x01/0x02/0x04/0x08 分别对应 Hybrid、Discrete、DDS、IntegratedOnly；当前代码只使用前三项，`0x08` 没有映射到任何模式。
3. **外接屏会改变语义。** HDMI 和部分 Type-C 可能直连 dGPU；接外接屏时，MUX 切换、PnP 禁用和“集显模式”的行为都可能与内屏不同。当前 MUX 切换没有外接屏前置检查，这是需要补的护栏。

## 3. 为什么 G-Helper 不能直接移植

### 3.1 不是“私有代码”问题，是“私有硬件通道”问题

G-Helper 本身是公开的 GPL-3.0 项目，可以 clone、编译、修改；“编译 ROG 私有代码”这个说法不准确。真正不能搬的是它脚下的华硕硬件抽象层：

| 依赖 | G-Helper 的用法 | 雷蛇上的情况 | 后果 |
|---|---|---|---|
| `\\.\ATKACPI` | `CreateFile` 打开，`DeviceIoControl(0x0022240C)` 读写 DEVS/DSTS | 设备不存在 | `CreateFile` 返回无效句柄 |
| `AsusAtkWmi_WMNB` | `root\wmi` 下的 WMI 备通道 | WMI 类不存在 | 查询抛 `InvalidClass` |
| `ATKD` ACPI 设备 | BIOS 注册 `\_SB.ATKD`，提供 DEVS/DSTS | 雷蛇 BIOS 不注册 | ACPI 方法不存在 |
| `GPUEco` | `AsusACPI.SetGPUEco(1/0)`，端点 `0x00090020` | 无对应端点 | WMI/IOCTL 调用失败 |
| `GPUMux` | `AsusACPI.SetGPUMux(0/1/2)`，端点 `0x00090016` | 无对应端点 | 无法读写 MUX |
| 设备识别 | 代码大量判断 `ASUS` / `ROG` / `TUF` | 雷蛇设备 ID 和名称不同 | GPU 之外的风扇、功耗、传感器逻辑也不工作 |
| 状态回读 | 依赖 Armoury Crate 的 `AsusSystemAnalysis` / `AsusOptimization` 服务 | 雷蛇无此服务 | 部分状态无法回读，容易出现假成功 |

端点号只是“参数”，发参数的“电话线”是 ATKACPI 设备和 ACPI 方法。雷蛇上要让这条线存在，等价于自己写内核驱动、逆向 EC 寄存器并做 ACPI 注入，已经超出“改代码”的范畴，风险和主板变砖同级。

### 3.2 静默失败是移植最危险的部分

G-Helper 的 WMI 调用失败后通常只记日志、界面保持原状态；按钮仍然可点。搬到雷蛇上，用户会看到“点了没反应但界面显示已切换”的假成功。四模式是显示链路操作，假成功比报错更危险。

### 3.3 法律边界

如果 fork G-Helper 并对外分发，GPL-3.0 的传染性覆盖整个衍生作品；不能只开源华硕部分、把雷蛇部分闭源。本文的建议是**不复制 G-Helper 源码，只借鉴“四模式”的产品概念**，在 OpenSynapse 里独立实现 Razer 执行层。这样既避开 GPL 传染，也避开一个注定跑不起来的硬件层。

## 4. 四模式映射

### 4.1 四模式不是四个并列开关

方案文档的核心判断是对的：G-Helper 的四种模式是三个控制面的组合——

1. **dGPU 供电**：通电 / 断电；
2. **MUX 显示通路**：iGPU / dGPU 直连 / 动态；
3. **驱动调度**：Optimus / DDS / 不使用 iGPU。

所以正确形态是“状态机 + 执行器”，不是“四个独立命令”。但落到雷蛇 2026，必须把“硬件能做什么”和“产品想叫什么”分开，否则会把“标准”和“自动”混成一个东西。

### 4.2 模式语义

| 产品模式 | 屏幕输出 | dGPU 供电 | 驱动调度 | 目标执行器 |
|---|---|---|---|---|
| 集显 | iGPU 独占 | 禁用（PnP） | 不参与 | `WindowsDedicatedGpuMode` |
| 标准 | iGPU 输出，dGPU 按需渲染 | 通电 | Optimus / Hybrid | MUX `0x00`，或 NVIDIA 控制面板 `Optimus` |
| 独显直连 | dGPU 直连内置屏 | 通电 | 不使用 iGPU | MUX `0x01` |
| 自动 | 按负载/电源动态 | 通电 | DDS / Advanced Optimus | MUX `0x02`；可选电源策略层 |

### 4.3 当前代码的映射与证据

| 模式 | 首选实现 | 当前 OpenSynapse 支撑 | 证据等级 | 关键条件 |
|---|---|---|---|---|
| 集显 | PnP 禁用 NVIDIA 显示设备 | `WindowsDedicatedGpuMode` + `DedicatedGpuModePolicy` + UI 开关 | E0；无 2026 实机 | 内屏不能由 dGPU 驱动；不能有走 dGPU 的外接屏；可能需要重启 |
| 标准 | `0D:09` 写 `0x00`（Hybrid） | `BladeDisplayTelemetryReader` + `BladeDisplayMuxMode.Hybrid` | E0；无 2026 实机 | `0D:8A` 必须报告 bit 0x01；若只报 0x06 则 HID 不可用 |
| 独显直连 | `0D:09` 写 `0x01`（Discrete） | `BladeDisplayTelemetryReader` + `BladeDisplayMuxMode.Discrete` | E0；无 2026 实机 | `0D:8A` 必须报告 bit 0x02；写入后需重启 |
| 自动 | `0D:09` 写 `0x02`（DDS） | 代码路径存在；`AllowsDds` 当前恒为 true | E0（仅字节/解析）；**无 2026 实机写入** | `0D:8A` 必须报告 bit 0x04；BIOS 需为 DDS；重启后仍生效才算通过 |

### 4.4 2026 款 `0D:8A` 的两种情形

2025 款 `1532:02C6` 的只读记录是 `0x06`：Discrete + DDS，没有 Hybrid 位。2026 款的目标机没有同样的公开记录，因此必须分情形讨论：

| `0D:8A` 返回 | 可用 HID 模式 | “标准”怎么实现 | 四模式是否完整 |
|---|---|---|---|
| `0x06`（Discrete + DDS） | DDS、独显直连 | HID 没有 Hybrid；只能走 NVIDIA 控制面板 `Optimus`（UIA）或 BIOS/驱动默认策略 | 否；当前代码只能覆盖“集显 + 独显直连 + DDS” |
| `0x07`（Hybrid + Discrete + DDS） | Hybrid、DDS、独显直连 | 可直接写 `0x00` | 接近完整；仍缺 UIA 和电源状态机 |
| `0x02`（仅 Discrete） | 独显直连 | 无动态路径，`CanSwitch` 为 false | 否；只能只读或走其他层 |
| `0x00` / 读取失败 | 无 | 无 | 否；应显示不支持，不能猜 |

> 这就是当前最关键的未知数：**2026 款到底报 `0x06` 还是 `0x07`。** 在读到真实 `0D:8A` 之前，不能把“标准模式”写成已支持，也不能把“四模式”写成已闭环。

### 4.5 “自动”的两种含义

方案文档把“自动”描述为“电源事件驱动的状态机”，但当前 HID MUX 写入成功返回 `RestartRequired`。这两件事放在一起会产生一个不成立的实现：

- 如果每次 AC/电池变化都调用 `0D:09` 切 MUX，用户会反复重启，自动切换不可用；
- 真正无感的“自动”应依赖 **DDS（`0x02`）由固件/驱动动态切换**，应用只在用户选择“自动”时写一次，之后不按电源事件反复写 MUX；
- 应用层电源状态机只能是可选的策略层，例如“插电用标准、拔电用集显”，而且必须处理重启、黑屏、冷却时间和恢复。它不是 DDS 的替代品。

因此本文建议把“自动”定义为：**首选 DDS 固件动态切换；DDS 不可用时，不提供自动模式，或明确标注为需要重启的实验性策略。** 不要把“自动”写成已经能用电源事件无缝切换。

## 5. 当前 OpenSynapse 已实现部分

### 5.1 `WindowsDedicatedGpuMode`

位置：`src/OpenSynapse.Windows/Lifecycle/WindowsDedicatedGpuMode.cs`，策略在 `src/OpenSynapse.Core/Displays/DedicatedGpuModePolicy.cs`。

它做的是：

- 通过 Windows PnP 禁用/启用 NVIDIA 显示适配器，和“设备管理器里禁用设备”同类；
- 通过机器级服务（`GpuModeServiceHost`，LocalSystem）或 UAC 助手执行；当前 PnP 操作实际调用 `pnputil.exe`，不是 SetupAPI 直调；
- 检查显示拓扑：内屏不能由 dGPU 驱动、内屏必须存在、路由必须已知、不能有活动外接屏；
- 独显故障（如 code 43）时拒绝禁用，提供“重启独显设备”恢复；
- 返回 `Success` / `RestartRequired` / `Blocked` / `NoDevice` / `DeviceFaulted` / `Failed` 等结果；
- UI 是“仅核显（禁用独显）”开关，不是四模式选择器。

它不是：

- 不是 MUX 切换；
- 不是 ACPI Eco 的物理断电，功耗下降幅度需要实机测量；
- 不是四模式状态机，不会在开机/恢复后自动纠正到某个目标模式。

当前证据：代码和 `DedicatedGpuModePolicyTests` 存在；本次未运行测试；本文没有取得 2026 实机切换记录。

### 5.2 `BladeDisplayTelemetryReader` 的 `0D:89/8A`

位置：`src/OpenSynapse.Windows/Devices/BladeDisplayTelemetryReader.cs`。

读路径：

1. 找到可用的 Blade HID 设备；
2. 检查 manifest 是否同时声明 `display-mux.get`、`display-mux.set`、`display-mux-capabilities.get`；
3. 发 `0D:8A` 读能力位；
4. 发 `0D:89` 读当前模式；
5. 用 `BladeDisplayMuxModes` 判断是否可切换、目标是否受支持。

写路径：

1. 检查 manifest、能力位、目标是否受支持；
2. 读当前模式作为 `original`；
3. 目标等于当前，直接返回 `Success`，不写入；
4. 发 `0D:09`，参数 `[0x00, mode]`；
5. 回读 `0D:89`；
6. 回读不一致，尝试恢复 `original`；
7. 一致则返回 `RestartRequired`，由 UI 提示重启，不自动重启。

当前代码的两个关键事实：

- `AllowsDds(blade)` 当前**无条件返回 true**，注释说 2026 bundle 通过同一个 `0D:09` 写 `0x02`，由 `0D:8A` 的 DDS 位决定 SKU 是否暴露；
- 因此 `BladeDisplayMuxStatus.CanChange` 在 `CanSwitch` 通过后实际上恒为 true；当前没有额外的 DDS 写入策略门禁，真正的门禁只剩 `0D:8A` 的 bit 0x04。

当前证据：协议层有 `BladeDisplayMuxTests` 覆盖 manifest、get/set 字节、模式解析、能力位和未知模式拒绝；**没有 reader 层的“同模式不写、写入后回读、回读不一致恢复”单元测试**；本文没有取得 2026 实机读写记录。

### 5.3 `BladeDisplayMuxMode`

位置：`src/OpenSynapse.Core/Displays/BladeDisplayMuxMode.cs`。

- 模式值：`Hybrid = 0x00`、`Discrete = 0x01`、`Dds = 0x02`；
- 能力位：`Hybrid = 0x01`、`Discrete = 0x02`、`DynamicDisplaySwitch = 0x04`、`IntegratedOnly = 0x08`；
- `CanSwitch` 要求 Discrete +（Hybrid 或 DDS）；
- `GetSupportedModes` 的顺序是 Hybrid、DDS、Discrete；
- `IntegratedOnly = 0x08` 已定义，但**没有映射到任何 `BladeDisplayMuxMode`，也没有被 `Supports` / `GetSupportedModes` 使用**；当前“集显”走 PnP，不走这个位；
- UI 标签：Hybrid = “混合模式（Optimus）”、DDS = “DDS 动态切换”、Discrete = “独显直连”。

### 5.4 当前 UI 行为

- “显示模式（MUX）”卡片单独存在，选项由 `0D:8A` 动态生成，当前模式选中；没有硬编码三选一；
- 切换中禁用控件，成功后提示“重启后生效”，并提供“立即重启/稍后”对话框；
- 当“仅核显（禁用独显）”生效时，MUX 下拉框拒绝切换并提示先关闭它；
- “仅核显（禁用独显）”是独立开关，不是四模式选择器；
- MUX 切换当前**没有外接屏前置检查**，也没有读取 BIOS 当前是 DDS 还是 Dedicated GPU Only；
- 这些互斥检查在 UI 层；如果以后增加服务、命令行或自动化入口，必须复用同样的检查，不能只靠界面按钮。

### 5.5 已实现/未实现总表

| 能力 | 代码 | 测试 | 2026 实机 |
|---|---|---|---|
| PnP 仅核显（禁用/启用独显） | 已实现 | 策略测试存在 | 未验证 |
| `0D:89` 读当前 MUX | 已实现 | 协议测试存在 | 未验证 |
| `0D:8A` 读能力 | 已实现 | 协议测试存在 | 未验证 |
| `0D:09` 写 Hybrid / Discrete | 已实现 | 协议测试存在 | 未验证 |
| `0D:09` 写 DDS=2 | 代码路径存在 | 仅字节/解析测试 | **未验证** |
| 写入后回读与恢复 | 已实现 | reader 层无测试 | 未验证 |
| NVIDIA 控制面板 UIA | 未实现 | 无 | 未验证 |
| 电源事件四模式状态机 | 未实现 | 无 | 未验证 |
| BIOS 变量/设置写入 | 未实现 | 无 | 不建议实现 |

## 6. 未实现部分

### 6.1 NVIDIA 控制面板 UIAutomation

为什么需要它：

- 如果 2026 的 `0D:8A` 不报告 Hybrid，HID 路径无法提供“标准（Optimus）”；
- 方案材料称 NVIDIA 没有通过 NVAPI 开放 Advanced Optimus 模式切换，NVIDIA 控制面板的“管理显示模式”是官方入口；
- 该入口通常有三档：自动选择 / 仅 Optimus / 仅 NVIDIA GPU，对应“自动/标准/独显直连”。

当前状态：**没有实现。** `src/` 中没有 `UIAutomationClient` 的显示模式自动化代码，也没有 `nvcplui` 启动、窗口查找、控件选择或失败回滚。

建议的最小实现（仅作为设计，不是现状）：

- 只在用户会话中运行，不能在 Session 0 的服务里直接操作控制面板；
- 通过 UIA 找到“管理显示模式”下拉框/列表，按本地化名称和控件类型双匹配；
- 选择目标项后，用独立状态源回读：控制面板当前选项、`nvidia-smi` 是否能枚举 dGPU、当前内屏输出适配器；
- 任何一步失败都保持/恢复原模式，不能把“点击成功”当成“模式切换成功”；
- 记录控制面板版本、语言、DPI、窗口状态，便于定位选择器失效；
- 默认隐藏或加实验开关，不作为主路径。

### 6.2 电源事件四模式状态机

当前状态：**没有实现。** `src/` 中没有 `SystemEvents.PowerModeChanged`、`WM_POWERBROADCAST`、`PBT_APMRESUMEAUTOMATIC` 或等效的电源事件订阅；没有持久化“用户想要哪个模式”；没有开机/恢复后的状态纠正。

一个可用的状态机至少需要：

- 输入：AC/电池、睡眠/恢复、当前 MUX 模式、PnP 状态、显示拓扑、外接屏、用户目标模式；
- 状态：集显 / 标准 / 独显直连 / 自动 / 切换中 / 阻塞或故障；
- 迁移约束：
  - 内屏由 dGPU 驱动时，绝不能直接禁用 dGPU；
  - PnP 操作进行中，不能写 MUX；
  - MUX 写入后按 `RestartRequired` 处理，不能假装已经生效；
  - 外接屏走 dGPU 时，禁用 dGPU 或切 MUX 都要先提示/阻止；
  - 失败回滚到标准或原模式，不能停在黑屏状态；
- 运行约束：单实例、防抖（例如电源变化后延迟 5 秒）、冷却时间、切换中不接受第二次请求、日志记录前后状态；
- 恢复约束：开机、睡眠恢复、服务重启后读实际状态，与目标比对；不一致时按安全策略纠正，不能盲目重放写入。

方案材料建议的“拔电进集显、插电进独显直连”是产品策略，不是硬件默认行为。首版不建议这么激进；更稳妥的默认是“插电标准、拔电集显”，把“插电独显直连”作为可选性能策略。等 DDS/UIA 实机验证后，再决定是否开放更激进的自动策略。

### 6.3 其他缺口

- 没有开机/恢复后的 MUX/PnP 状态纠正；
- 没有持久化目标模式和切换历史；
- 没有跨入口的互斥锁（当前只有 UI 的 `_displayMuxBusy` / `_dedicatedGpuModeBusy`）；
- MUX 切换没有外接屏检查；
- 没有读取 BIOS 当前显示设置；
- 没有 reader 层的写入/回读/恢复测试；
- `IntegratedOnly = 0x08` 能力位未使用，若 2026 报告该位，当前代码会忽略。

## 7. 分阶段建议

| 阶段 | 目标 | 范围 | 退出条件 | 不建议 |
|---|---|---|---|---|
| P0 只读盘点 | 确认 2026 真实能力 | 在 `1532:02E0` 上读 `0D:89`、`0D:8A`，记录 BIOS 设置、NVIDIA 控制面板状态、`nvidia-smi`、显示拓扑 | 有原始响应、能力位、当前模式、BIOS 状态记录 | 不做任何写入 |
| P1 两态可用 | 先交付低风险价值 | 集显（PnP）+ 一个受支持 MUX 模式（有 Hybrid 位则标准，否则独显直连） | 实机切换 + 重启 + 恢复；外接屏/内屏路由护栏通过 | 不做 UIA、不做自动、不写 BIOS |
| P2 三态 MUX | 补齐 MUX 模式 | 在 P1 基础上加入另一个受支持 MUX 模式；验证 DDS=2 | 每个模式写入、重启、回读、恢复通过；DDS 有独立结论 | 不把 DDS 未验证就标为可用 |
| P3 UIA 兜底 | 补 HID 拿不到的模式 | 仅在 `0D:8A` 缺 Hybrid/目标模式时，实现 NVIDIA 控制面板 UIA | 控制面板版本/语言矩阵、选择器、独立回读、失败回滚通过 | 不把 UIA 当主路径 |
| P4 电源自动 | 做“自动”策略 | DDS 优先；否则不做，或做保守的 AC/电池策略 | 睡眠/恢复、开机、服务重启、失败回滚全部通过 | 不在每次电源事件写 MUX |
| P5 BIOS | 只做人工指引 | 文档说明如何在 BIOS 切换 DDS / Dedicated GPU Only | 用户能在 BIOS 恢复显示 | 不在应用内写 BIOS 变量 |

推荐的第一批交付：**P0 + P1**。先把 2026 的真实能力读清楚，再用现有代码交付“集显 + 一个受支持 MUX 模式”。这一步不需要 UIA、不需要电源状态机，风险最低，也能覆盖大部分续航/性能诉求。P2 的 DDS=2 是“自动”的前置条件；P3/P4 只在 P0/P1/P2 稳定后再做。

## 8. UIA / BIOS / PnP 风险

| 路径 | 能做什么 | 主要风险 | 结论 |
|---|---|---|---|
| UIA 操作 NVIDIA 控制面板 | 选“自动选择 / 仅 Optimus / 仅 NVIDIA GPU” | 面板改版、语言/DPI 差异、Session 0 看不到窗口、控件树不可读、点击成功但模式未变、启动面板抢焦点 | 只做兜底；必须独立回读；失败回滚 |
| BIOS 设置写入 | 切 `Dedicated GPU Only` / `NVIDIA Dynamic Display Switch` | 机型变量不公开、写错可能无法启动、Secure Boot/固件保护、恢复成本高 | 不在应用内实现；只提供人工步骤 |
| PnP 禁用/启用 dGPU | 让 dGPU 从枚举中移除/恢复 | 内屏走 dGPU 时禁用会黑屏；外接 HDMI/Type-C 可能失信号；驱动重载失败/code 43；Windows 更新可能重新启用 | 当前已有拓扑护栏；仍需开机恢复和实机验证 |
| HID MUX `0D:09` | 切 Hybrid/Discrete/DDS | 固件语义差异；回读可能先于实际显示路由；写入后需重启；DDS=2 未验证 | 读写必须带能力检查和回读/恢复；DDS 单独验收 |

### 8.1 UIA 的具体风险

- NVIDIA 控制面板可能是经典 `nvcplui`，也可能是新版 NVIDIA App；窗口类名、控件树、文案都会变；
- 中文/英文/其他语言的“管理显示模式”“自动选择”“仅 Optimus”“仅 NVIDIA GPU”名称不同；
- 高 DPI、最小化、隐藏窗口、远程桌面都可能让 UIA 找不到控件；
- 服务运行在 Session 0，看不到交互桌面；UIA 必须由用户会话进程执行；
- 没有稳定的 NVAPI 接口，UIA 是“模拟人点击”，不是契约；面板升级就可能失效；
- 点击成功不等于模式生效，必须用控制面板当前值 + `nvidia-smi` + 显示拓扑交叉回读。

### 8.2 BIOS 的具体风险

- `Dedicated GPU Only` / `NVIDIA Dynamic Display Switch` 是机型相关设置，没有公开的 Windows API；
- 直接写 UEFI 变量可能被固件保护拒绝，也可能让机器无法点亮；
- 不同 BIOS 版本的变量地址/选项可能不同，不能把一台机器的结果套给另一台；
- 如果用户在 `Dedicated GPU Only` 下把 MUX 写成 Hybrid，可能黑屏；恢复通常需要进 BIOS 或清 CMOS；
- 因此 OpenSynapse 只做“检测 + 提示 + 人工操作指引”，不做自动写入。

### 8.3 PnP 的具体风险

- 当前 `WindowsDedicatedGpuMode` 已检查内屏是否由 dGPU 驱动、是否有活动外接屏、拓扑是否已知；这些护栏必须保留；
- 禁用 dGPU 后，依赖 NVIDIA 的应用、外接屏、部分游戏启动器可能异常；启用后驱动重新枚举需要时间；
- `pnputil` 返回 3010 才提示重启；返回 0 时设备已禁用，但是否“彻底生效”仍需实机确认；
- Windows 更新、驱动安装、设备故障可能改变状态；需要有开机/恢复后的状态核对；
- PnP 禁用不是 ACPI Eco，节电幅度不能按 G-Helper 的 Eco 数据宣传，必须实测。

### 8.4 必须保留的安全护栏

1. 内屏由 dGPU 驱动时，禁止禁用 dGPU；
2. PnP 操作进行中，禁止写 MUX；
3. 写 MUX 前必须读 `0D:8A` 能力位，不能只信 manifest；
4. 写 MUX 后必须回读；不一致必须恢复原模式；
5. 不自动重启；重启由用户确认；
6. 外接屏走 dGPU 时，禁用 dGPU / 切 MUX 前要提示或阻止；
7. 单实例、防抖、冷却时间；切换中拒绝第二次请求；
8. 记录切换前后状态，失败要可见；
9. 不在应用内写 BIOS；
10. DDS=2 未实机验证前，不对外宣称“自动可用”。

## 9. DDS=2 写入仍需实机复核

### 9.1 代码现在能做什么

- `BladeDisplayMuxMode.Dds = 0x02`；
- `BladeDisplayMuxProtocol.CreateSetModeRequest` 会生成 `0D:09`，参数 `[0x00, 0x02]`；
- `BladeDisplayTelemetryReader.SetBladeDisplayMuxModeAsync` 会检查能力位、读原模式、写入、回读、失败恢复；
- `AllowsDds` 现在恒为 false，DDS 从可选模式中隐藏；等 9.3 的实机矩阵通过后再打开；
- 单元测试覆盖了 `0x02` 的 set 字节和 `0x02 -> Dds` 的解析。

### 9.2 还没有验证什么

- 2026 款 `1532:02E0` 的 `0D:8A` 是否真的报告 bit 0x04；
- 写入 `0x02` 是否被固件接受；
- 写入后 `0D:89` 是否立即回读为 `0x02`；
- 重启后是否仍为 `0x02`；
- NVIDIA 控制面板是否显示“自动选择”，以及实际显示路由是否符合 DDS 语义；
- 从 DDS 切回 Hybrid/Discrete 是否成功、是否需要重启、是否黑屏；
- 失败恢复路径是否真的能把机器救回来；
- 1~3 秒黑屏的时间是否准确，OLED 上是否更明显。

### 9.3 最小实机测试矩阵

| 步骤 | 操作 | 预期 | 记录 |
|---|---|---|---|
| 1 | 读 `0D:8A` | 能解析；确认 bit 0x04 | 原始响应、能力位 |
| 2 | 读 `0D:89` | 当前模式属于能力位 | 原始响应、当前模式 |
| 3 | 写 `0D:09` = `0x02` | 返回成功；回读 `0x02`；结果为 `RestartRequired` | 写入/回读原始字节 |
| 4 | 手动重启 | 系统正常启动，内屏有画面 | 启动日志、截图 |
| 5 | 重启后读 `0D:89` | 仍为 `0x02`；NVIDIA 控制面板为“自动选择” | 原始响应、面板截图 |
| 6 | 切回原模式并重启 | 回到原模式，显示正常 | 原始响应、截图 |
| 7 | 失败路径（如可构造） | 回读不一致时恢复原模式；恢复失败时提示 BIOS | 结果码、日志 |

### 9.4 通过标准与失败处理

只有完成“写入 `0x02` + 重启 + 回读仍为 `0x02` + 切回原模式并重启恢复”，才能把 DDS 标为“实机验证通过”。如果任一步失败：

- 立即把 DDS 从可选模式中隐藏，或标为“实验性/不支持”；
- 不把“自动”作为卖点；
- 保留只读读取能力，避免用户误操作；
- 记录失败机型、BIOS 版本、驱动版本、原始响应，供后续适配。

## 10. 结论

**能做，但不是现在就能交付的四模式成品。**

- 硬件层：2026 款具备 Advanced Optimus / DDS 和 BIOS MUX 的基础，四模式在概念上可映射；
- 软件层：G-Helper 不能直接移植，必须独立实现 Razer 执行层；
- 当前代码：已有 PnP 集显和 HID MUX 读写两块能力，但没有四模式状态机、没有 NVIDIA 控制面板 UIA、没有电源事件自动切换；
- 关键未知：2026 的 `0D:8A` 是否包含 Hybrid 位，以及 DDS=2 是否真的可写、可持久、可恢复；
- 推荐路径：先做 2026 只读盘点，再交付“集显 + 一个受支持 MUX 模式”的两态版本；DDS=2 和“自动”必须单独实机验收；UIA 只做兜底；BIOS 只做人工指引。

在完成 2026 实机的 MUX 写入、DDS=2、重启持久性和恢复测试之前，本文所有涉及写入/自动/UIA 的内容都应视为**方案与风险分析，不是已完成能力**。

## 附录：代码与证据索引

| 文件 | 作用 |
|---|---|
| `src/OpenSynapse.Windows/Lifecycle/WindowsDedicatedGpuMode.cs` | PnP 禁用/启用独显、UAC/服务入口、故障重启 |
| `src/OpenSynapse.Core/Displays/DedicatedGpuModePolicy.cs` | 集显模式的前置条件与阻止原因 |
| `src/OpenSynapse.Windows/Devices/BladeDisplayTelemetryReader.cs` | MUX 状态读取、写入、回读、恢复 |
| `src/OpenSynapse.Windows/Protocols/BladeDisplayMuxProtocol.cs` | `0D:89/8A/09` 报文与解析 |
| `src/OpenSynapse.Core/Displays/BladeDisplayMuxMode.cs` | 模式/能力位/可切换策略 |
| `src/OpenSynapse.App/Views/DevicesPage.Blade.cs` | MUX 与仅核显 UI 行为、互斥、重启提示 |
| `src/OpenSynapse.App/Views/DevicesPage.xaml` | MUX 卡片、仅核显卡片的界面 |
| `src/OpenSynapse.Windows/Devices/Manifests/blade-02e0.json` | Blade 16（2026）manifest，声明 `0D:89/8A/09` |
| `src/OpenSynapse.Windows/Devices/Manifests/blade-02e1.json` | Blade 18（2026）manifest，声明 `0D:89/8A/09` |
| `tests/OpenSynapse.Core.Tests/BladeDisplayMuxTests.cs` | MUX 协议字节、模式解析、能力位测试 |
| `tests/OpenSynapse.Core.Tests/Blade2026Tests.cs` | 2026 manifest 与 `0D:89/8E/0E` 等命令声明测试 |
| `tests/OpenSynapse.Core.Tests/DedicatedGpuModePolicyTests.cs` | PnP 集显模式策略测试 |
| `docs/GPU_MUX_HID_ADAPTATION.zh-CN.md` | MUX HID 适配流程与 2025 款只读记录 |

> 本文只写文档，没有修改代码或协议，也没有提交。文中“已实现”只代表当前 `main` 代码存在对应路径；除 2025 款 `1532:02C6` 的只读记录外，2026 款的读写、UIA、电源状态机和 BIOS 行为均未实机验证。
