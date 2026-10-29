# 架构边界与设备能力模型

本文是 OpenSynapse 第 0 步重构的架构基线。它约束三件事：程序集之间能依赖什么、设备能力由哪几层共同决定、OpenRazer 与 middleware 两套后端在哪里汇合。后续改动应先满足这里的边界，再讨论具体型号和命令。

结构与预算的后续治理（棘轮检查、`MainViewModel` 与 XAML 的拆分顺序、仓库行为规范）见 [`docs/MAINTAINABILITY-PLAN.md`](MAINTAINABILITY-PLAN.md)。

## 基线

| 项目 | 值 |
|---|---|
| 基线日期 | 2026-10-29 |
| 分支 | `main` |
| 构建 | `dotnet build src\OpenSynapse.App\OpenSynapse.App.csproj -c Release -r win-x64 --no-restore` → 0 警告，0 错误 |
| 测试 | `dotnet test tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj -c Release --no-restore` → 1643 个测试，1643 通过，0 失败，0 跳过 |
| Blade manifest | `BladeModelCapabilities` 的 39 个 PID 都有内建 manifest |

测试前需要清空 `HTTP_PROXY` / `HTTPS_PROXY` / `ALL_PROXY` 及其小写形式，并设置 `NO_PROXY=no_proxy=127.0.0.1,localhost`；否则 Chroma 测试会尝试走本机代理 `127.0.0.1:7890`。

## 1. 三条程序集边界

### 1.1 OpenSynapse.Core：模型与纯逻辑

`src/OpenSynapse.Core` 只放与平台无关的模型、策略和纯逻辑，例如设备分类、性能模式定义、配置模型、宏解析和 Blade 型号准入表。

允许：

- .NET 基础类库和纯内存计算；
- 不依赖设备句柄、窗口、COM 或 P/Invoke 的模型与算法；
- 可以在没有设备、没有 UI 的测试进程中直接执行的代码。

禁止：

- 引用 `OpenSynapse.Windows` 或 `OpenSynapse.App`；
- 引用 WinUI、Windows App SDK、`System.Windows` 或其他 UI 框架；
- HID 读写、设备发现、WMI、注册表、服务控制、音频设备控制等系统调用；
- 持有设备路径、端点生命周期或 UI 状态。

判断标准：把这段代码放进控制台测试，不需要连接任何硬件就能运行。

### 1.2 OpenSynapse.Windows：设备、协议与系统调用

`src/OpenSynapse.Windows` 负责把 Windows 设备变成可查询的能力，并把协议命令编码成 HID 报文。设备发现、端点状态、feature report 传输、响应校验、固件/遥测读取都在这里。

允许：

- 引用 `OpenSynapse.Core`；
- HID 发现与读写、PnP、WMI、服务控制、音频和显示系统调用；
- `RazerFeatureReport`、`RazerDeviceManifest`、`OpenRazerDeviceDefinition` 等设备模型；
- 协议族命令号、报文参数、超时和校验逻辑；
- 只读探测、写入前校验、读回确认和恢复路径。

禁止：

- 引用 `OpenSynapse.App`、ViewModel、XAML 或本地化资源；
- 根据页面状态决定能力是否显示；
- 让 UI 直接持有 `IRazerFeatureTransport` 或自行拼装 report。

判断标准：删掉 App，Windows 项目仍然能编译，并且设备读取/写入路径仍然完整。

### 1.3 OpenSynapse.App：UI、ViewModel 与装配

`src/OpenSynapse.App` 只做 WinUI 页面、ViewModel、导航、窗口生命周期、依赖装配和面向用户的文案。

允许：

- 引用 `OpenSynapse.Core` 和 `OpenSynapse.Windows`；
- 调用 Windows 层公开的服务、resolved device 和结果模型；
- 把能力结果映射成控件可见性、命令可用性和提示文本；
- 处理 UI 线程、窗口和本地化。

禁止：

- 直接构造 HID 报文、调用 `RazerFeatureReport.CreateRequest` 或复制协议常量；
- 直接 P/Invoke 设备接口、WMI 或服务控制；
- 用产品名、PID 或“读回成功”猜测设备支持；
- 绕过 Windows 层的校验、恢复和错误处理。

依赖方向固定为：

```text
OpenSynapse.App ────────► OpenSynapse.Windows ────────► OpenSynapse.Core
       │                                                     ▲
       └─────────────────────────────────────────────────────┘
```

没有反向依赖：Core 不引用 Windows/App，Windows 不引用 App。测试工程为了复用纯逻辑会链接少量 App 源文件，这不改变产品程序集的依赖方向。

## 2. 设备能力三层

一个控制项是否出现，不由单一文件决定。它依次经过 manifest、family contract 和型号准入三层；任何一层没有证据，能力就保持关闭。

### 2.1 manifest：设备声明 capability

manifest 位于 `src/OpenSynapse.Windows/Devices/Manifests/*.json`，以内嵌资源加载，描述“这个 VID/PID 实际使用哪组 HID 报文”。

一个 manifest 声明：

- `vendorId`、`productIds`、`displayName`、`category`；
- `collection`：usage page、usage、feature report 长度；
- `protocolFamily`：该设备属于哪个协议家族；
- `capabilities`：每个 capability 的 `transactionId`、`dataSize`、`commandClass`、`commandId`、`arguments`、`waitMilliseconds`；
- 可选的矩阵行列数。

manifest 只描述报文和设备身份，不决定 UI 是否显示某个按钮，也不负责性能模式、风扇曲线等型号策略。

外部 manifest 放在 `%LOCALAPPDATA%\A1mAssist\OpenSynapse\devices`，加载时会与内建协议家族契约逐项比对：命令号、参数、data size、等待时间和分类必须一致，不能通过外部文件发明新命令。`openrazer-standard` 只允许审查过的内建 manifest，不接受外部注册。

### 2.2 family contract：协议家族的命令号与 required 集合

family contract 位于 `RazerDeviceRegistry` 的 `FamilyCapabilities` 和 `FamilyRequiredCapabilities`。它定义同一协议家族共享的命令语义，以及每个 manifest 必须提供的最小能力集合。

当前家族：

| 家族 | 用途 |
|---|---|
| `blade-710` | 早期 Blade 710 报文 |
| `blade-710-chroma` | 710 家族中键盘亮度改走 Chroma 区域的型号 |
| `blade-671` | Blade 16 (2023) 等使用独立灯效矩阵的型号 |
| `blade-597` | Blade 15 Base (Early 2020) 的最小命令集 |
| `viper-184` | Viper 有线/无线鼠标 |
| `openrazer-standard` | 仅内建、逐型号审查的 OpenRazer 标准报文 |

required 集合是“这个家族的所有设备都应该能回答”的命令；可选能力可以只出现在部分 manifest 中。加载时只要缺少 required capability，manifest 就会被拒绝。新增能力时的顺序是：先补 family contract，再补 manifest，最后才允许协议读取和 UI 暴露。

`max-fan.get` / `max-fan.set` 是旧名称，加载时会迁移为 `power-mode.get` / `power-mode.set`；新代码只使用新名称。

### 2.3 BladeModelCapabilities：具体型号准入与 UI

`src/OpenSynapse.Core/Devices/BladeModelCapabilities.cs` 按精确 USB PID 决定 Blade 型号能显示和写入哪些控制项，包括：

- 性能模式列表和 HyperBoost；
- Max Fan、固定转速和风扇曲线；
- CPU/GPU Boost 档位；
- 每个模式的 RPM 上下限和默认值；
- 是否允许控制写入；
- 2026 代的热选择器差异。

它不定义任何报文、命令号或 HID 细节。它回答的是“这个确切型号是否已经有过证据，可以向用户开放这个控制项”。未知 PID 默认关闭写入，使用保守范围；共享 Power Mode 位掩码读回成功也不等于该型号支持某个位，因此读回不能替代型号准入。

### 2.4 三层如何合成

对某个设备的一个控制项，运行时必须同时满足：

```text
manifest 声明了该 capability
AND family contract 允许并校验了该 capability
AND 如果这是 Blade 型号策略，BladeModelCapabilities 已准入该 PID
AND 设备端点已发现、可访问并完成必要的读取验证
```

任何一项不成立，控制项保持关闭。这样 manifest 负责“报文存在”，family contract 负责“报文语义正确”，BladeModelCapabilities 负责“这个型号可以显示/写入”，运行时负责“当前设备真的能连上”。

## 3. OpenRazer 与 middleware 两套后端

OpenSynapse 同时使用两套设备后端。它们的数据来源、加载方式和适用设备不同，但运行时必须收敛成同一种设备视图。

### 3.1 OpenRazer catalog

- 数据：`src/OpenSynapse.Windows/Devices/OpenRazer/*.json`，来源固定到 OpenRazer 上游提交 `477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d`；
- 加载：`OpenRazerDeviceCatalog` → `OpenRazerDeviceDefinition`；
- 协议：`OpenRazerDeviceService`、`OpenRazerMouseProtocol` 和 `OpenRazerStandardProtocol`；
- 适用：上游已收录的鼠标、键盘、耳机、鼠标垫、配件等；
- 本地扩展：Viper V3 Pro SE 别名、middleware 鼠标、Leviathan V2 X 等会在这里补充；这些条目仍然必须走同一套能力校验，不能因为“名字像”就开放写入。

OpenRazer catalog 的优点是覆盖面大、命令来源明确；缺点是同一型号的 Windows 端点、feature report 长度和固件差异仍要在运行时验证。

### 3.2 middleware manifest

- 数据：`src/OpenSynapse.Windows/Devices/Manifests/*.json`；
- 加载：`RazerDeviceRegistry` → `RazerDeviceManifest`；
- 使用：Blade 遥测、风扇/性能/充电、Viper 184 等型号的 manifest 后端；
- 适用：已经从官方 middleware 或设备证据中提取出完整报文、并且能由 family contract 校验的设备。

middleware manifest 的优点是能把设备声明、协议契约和型号准入分开；缺点是只覆盖已经审查过的型号，不能靠猜测扩展。

### 3.3 运行时统一到 ResolvedRazerDevice

发现层只产出 `DeviceDescriptor`。解析层按 VID/PID 选择后端定义，并把 descriptor 与 manifest 合成 `ResolvedRazerDevice`：

```text
WindowsHidDiscovery
        │
        ▼
DeviceDescriptor
        │  RazerDeviceResolver.FindReadyBlade / FindReadyDevice
        ▼
ResolvedRazerDevice(Descriptor, Manifest)
        │
        ├─ Owns(capability)
        ├─ GetCapability(capability)
        └─ BladeModelCapabilities 派生属性
```

`RazerDeviceResolver` 只返回 `Access = Available` 且 `Capability = PendingValidation` 的设备；未解析、忙或不稳定的端点保持只读或不显示控制项。`ResolvedRazerDevice` 是运行时能力的统一出口：ViewModel 和读取器问它“这个设备是否拥有某能力”，而不是重新查 catalog、manifest 或产品名。

当前 manifest 后端已经直接使用 `ResolvedRazerDevice`。OpenRazer 后端仍以 `OpenRazerDeviceDefinition` + `OpenRazerDeviceConnection` 承载等价的 resolved 语义；第 0 步把这个差异记录下来，后续重构应把 OpenRazer 的只读视图也收口到 `ResolvedRazerDevice`，而不是让 App 直接依赖 catalog 细节。

### 3.4 遥测读取器的组合关系

`RazerDeviceTelemetryReader` 只负责解析设备、调用子读取器、合并遥测字段和错误。Blade 逻辑按职责组合为 `BladeTelemetryReader`、`BladeFanTelemetryReader`、`BladePowerTelemetryReader`、`BladeDisplayTelemetryReader`；鼠标通用读写由 `OpenRazerTelemetryReader` 承担，Viper OBM 由 `ViperTelemetryReader` 承担。所有子读取器通过 `RazerCapabilityClient` 访问 manifest 能力，不自行拼 HID 报文或裸查 capability。这是组合关系，不是用 partial 文件把同一个类切碎。

解析顺序和 PID 预留是后端优先级的一部分：`RazerDeviceRegistry.ReservedProductFamilies` 把 manifest 的 Blade 和 Viper 184 身份固定到对应协议家族，避免 manifest 之间互相抢占；`OpenRazerDeviceCatalog.ReservedProductIds` 则把 `00B8` 和 `02C6` 排除在 catalog 之外。两套目录可以同时收录同一台机器的不同 PID 或不同功能集，但运行时只选择一条已解析路径，UI 不直接把两边的能力结果合并。

## 4. 结构测试

`tests/OpenSynapse.Core.Tests/DeviceCapabilityStructureTests.cs` 固定以下结构：

1. BuiltIn registry 中每个 `(VID, PID)` 只能出现一次；
2. 每个 family 的 required capability 都能在同一个 family contract 中找到同名项；
3. `BladeModelCapabilities` 的每个 PID 都能在 BuiltIn registry 找到 Blade family manifest；
4. OpenRazer catalog（含本地别名和 middleware 派生条目）声明的每个轮询率都能由 `OpenRazerMouseProtocol.SetPollingRate` 编码，不启动真实 transport。

`BladeModelCapabilities` 的 39 个 PID 现在都有内建 Blade family manifest。旧款 Blade 按 `rzDeviceType` 和同组已有 manifest 分为 `blade-597`（最小命令集）与 `blade-671`（完整风扇/电源/键盘控制）两组；manifest 只声明该组已共享的报文，未凭产品名开启额外能力。
