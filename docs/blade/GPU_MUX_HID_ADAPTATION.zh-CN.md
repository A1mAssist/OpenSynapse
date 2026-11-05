# GPU MUX HID 适配流程

适用于 Razer Blade 内置屏在 **Optimus / DDS / 独显直连** 之间切换的适配。目标是从 USB PID 和 Synapse 产品 JavaScript 找到固件 HID 命令，先做只读验证，再把经过验证的 MUX 能力接入 OpenSynapse。

这套实现只走设备 HID feature report。不要用 Windows `DisplayMuxDevice`、NVIDIA API、PnP 禁用独显或显卡超频接口来冒充 MUX；它们的语义和安全边界都不同。

最短路径：

```text
PID -> Synapse product id -> 产品 JS -> HID 命令 -> 只读探针
    -> capability 判定 -> manifest/registry -> reader 安全流程
    -> UI 动态选项 -> 真机切换/重启/恢复 -> Verified
```

## 0. 先分清哪些东西不是 MUX

| 名称 | 实际作用 | 能否当 MUX 用 |
|---|---|---|
| `display-mux.get` / `display-mux.set` / `display-mux.capabilities.get` | 读取或切换内置屏的显示路由 | 是，本文目标 |
| `SetGPUValue`、GPU Boost | 调整 GPU 时钟/性能参数 | 不是 |
| Windows `DisplayMuxDevice` | Windows 官方 MUX 设备接口 | 本实现不使用 |
| `WindowsDedicatedGpuMode` | 通过 PnP 禁用/启用独显 | 不是，和 MUX 是两套机制 |
| `native-display-mode` | 切换面板 UHD / FHD 原生模式 | 不是 |
| Thermal Fan Cooling Mode | 风扇/散热模式 | 不是 |
| NVIDIA 控制面板的显示设置 | 可能影响渲染或显示行为 | 不是本文的 HID 切换路径 |

MUX 的控制路径是主机通过 HID 把请求交给 EC/固件，实际显示路由由固件完成。命令存在不代表这台机器支持，必须读 capability 和当前模式。

## 1. 准备输入

至少收集这些信息：

- 完整型号和年份，例如 `Razer Blade 16 2025`；
- USB `VID:PID`，例如 `1532:02C6`；
- Synapse 版本和 Windows build；
- 内屏当前是否正常显示；
- 是否愿意在能进入 BIOS 的前提下做一次真实切换测试。

只拿到商品名不够。Blade 16 不同年份可能共用名称但 PID 不同。

`VID:PID` 转 Synapse product id 的规则见 [RAZER_PID_TO_MW_JS.md](../protocols/RAZER_PID_TO_MW_JS.md)：

```text
0x02C6 -> 710
product URL: https://apps.razer.com/synapse/products/710/
```

## 2. 从产品 JavaScript 找 MUX 命令

先按 `RAZER_PID_TO_MW_JS.md` 的流程取得产品 `mw` 目录、`main.<hash>.js`、公共 `rzDevice25` chunk 和产品 handler chunk。只做文本解析，不执行下载的 JavaScript。

Blade 16 2025 已验证的搜索字符串：

```text
Set VGA Configure Setting
Get VGA Configure Setting
Get VGA Hardware Information
Get Synapse UI Setting
taskRunnerSetGpuMode
ON_SET_GPU_MODE
```

不要只因为字符串出现在同一个文件里就认定命令。回到导出常量和 `sendCommand`，确认实际数组、参数长度和响应解析。

710 当前提取结果：

```text
Get VGA Configure:          [2, 0x0D, 0x89]    args [0, 0]
Set VGA Configure:          [2, 0x0D, 0x09]    args [0, mode]
Get VGA Hardware Info:      [2, 0x0D, 0x8A]    args [0, 0]
Get Synapse UI Setting:     [2, 0x0D, 0x8B]
```

这里的首字节 `2` 是 payload/参数长度；后面的 `0x0D`、命令号和参数才是报文语义。不同产品的 transaction id、data size 或命令布局可能不同，不能把 710 的常量直接套给另一台机器。

## 3. 只读探针

先读，不写。当前 OpenSynapse 的 feature report 长度是 91 字节：

```text
report[0]   = 0x00        report id
report[1]   = 0x00        请求方向
report[2]   = transaction id
report[6]   = data size
report[7]   = command class
report[8]   = command id
report[9..] = arguments
report[89]  = XOR CRC（覆盖 3..88）
report[90]  = 0x00
```

710 的请求参数：

| 操作 | transaction | dataSize | class | command | arguments |
|---|---:|---:|---:|---:|---|
| 读当前 MUX | `1F` | `02` | `0D` | `89` | `00 00` |
| 读 MUX 能力 | `1F` | `02` | `0D` | `8A` | `00 00` |
| 写 MUX | `1F` | `02` | `0D` | `09` | `00 <mode>` |
| 读 Synapse UI 设置 | `1F` | `02` | `0D` | `8B` | 按产品 JS 定义 |

成功响应至少满足：

- `response[1]` 是 `0x01` 或 `0x02`；
- `response[6]` 与请求的 data size 一致；
- 报文头、命令号、transaction id 和 CRC 能对应；
- 读取 `data[1]`，也就是实际报文中的 `response[10]`，不要习惯性读 `data[0]`。

只读探针顺序：

```text
枚举设备
  -> Get VGA Hardware Info
  -> Get VGA Configure
  -> 记录原始响应和能力位
  -> 停止，不写入
```

2026-10-25 在 `Razer Blade 16 2025 / 1532:02C6` 上的只读结果：

```text
Get VGA Configure:       data[1] = 0x02  -> DDS
Get VGA Hardware Info:   data[1] = 0x06  -> Dedicated dGPU + DDS
Get Synapse UI Setting:  switchInterface = 1

支持模式：DDS、独显直连
没有报告 Hybrid 能力位
```

这是一条只读记录，不代表已经做过真实模式切换。

## 4. 模式和能力位

模式值：

```text
0x00  Hybrid / Microsoft Hybrid / NVIDIA Optimus
0x01  Discrete / 独显直连
0x02  DDS / 动态显示切换
```

能力位：

```text
0x01  Hybrid
0x02  Dedicated dGPU
0x04  DDS
0x08  Dedicated iGPU only
```

当前实现按下面的规则开放切换：

- 目标模式必须有对应能力位；
- 设备必须报告直连独显能力，并且至少报告一种动态路径（Hybrid 或 DDS）；
- 不要求每台机器都报告 `0x01`。710 只报告 `0x06`，仍然允许在 DDS 和独显直连之间切换；
- 未知模式必须报错或拒绝，不能默认映射到 Hybrid。

能力位是设备/固件报告，不是产品目录的静态猜测。产品目录只能说明“这个型号可能支持”，最终以当前机器的读取结果为准。

## 5. 接入代码

### 5.1 Manifest

在目标设备的 manifest 和 registry 的 family capability 合同里同时加入三个命令。例如 `blade-710.json`：

```json
"display-mux.get": {
  "transactionId": "1F",
  "dataSize": "02",
  "commandClass": "0D",
  "commandId": "89",
  "arguments": "0000"
},
"display-mux.set": {
  "transactionId": "1F",
  "dataSize": "02",
  "commandClass": "0D",
  "commandId": "09",
  "arguments": ""
},
"display-mux-capabilities.get": {
  "transactionId": "1F",
  "dataSize": "02",
  "commandClass": "0D",
  "commandId": "8A",
  "arguments": "0000"
}
```

同时更新 `RazerDeviceRegistry.FamilyCapabilities` 中对应 family 的合同。只改 JSON、不改合同，或只改合同、不改 JSON，测试和运行时行为都会不一致。

如果是全新的 `protocolFamily`，还要：

- 在 `FamilyCapabilities` 增加该 family 的能力合同；
- 在 `FamilyRequiredCapabilities` 增加必选能力集合，否则加载时会报错；
- 在 `ReservedProductFamilies` 登记已知 VID/PID，避免被外部 manifest 注册到别的 family；
- 新增的 `Devices/Manifests/*.json` 会被 csproj 的通配规则自动嵌入，不需要单独加文件项。

不要把 710 的三条命令直接复制给 671 或其他 Blade。每个 family 都要单独核对 PID、transaction id、payload 和 capability 行为；目前 `BladeDisplayMuxProtocol` 的 `TransactionId = 0x1F` 是 710 的实现，如果新设备不同，需要先改造协议层读取 manifest 的 transaction id，而不是复制一份 710 常量。

### 5.2 协议和解析

Blade 710 的实现位置：

- `src/OpenSynapse.Windows/Protocols/BladeDisplayMuxProtocol.cs`
- `src/OpenSynapse.Core/Displays/BladeDisplayMuxMode.cs`

协议层负责：

- 生成 get/set/capability 请求；
- 校验响应状态、长度、命令对应关系、data size 和 CRC；
- 从 `response[ArgumentsOffset + 1]` 解析 mode 和 capability；
- 对未知 mode 抛错。

这部分不要做成“收到 ACK 就算成功”。读回值才是写入是否落地的依据。

### 5.3 Reader 安全流程

`RazerDeviceTelemetryReader` 中的 `ReadBladeDisplayMuxStatusAsync` 和 `SetBladeDisplayMuxModeAsync` 是唯一推荐的设备访问入口。写入顺序必须是：

```text
检查 manifest 是否声明三项能力
  -> 读 capability
  -> 确认目标模式受支持
  -> 读当前模式作为 original
  -> 目标等于当前 -> 直接返回 Success，不写入
  -> 写入目标模式
  -> 回读模式
  -> 回读不一致 -> 尝试恢复 original
  -> 一致 -> 返回 RestartRequired
```

失败路径也必须恢复原模式，并区分：

```text
Success          当前已经是目标模式
RestartRequired  写入和回读成功，等待用户重启
Unsupported      manifest 或 capability 不允许
NoDevice         找不到可用的 Blade HID 设备
Restored         写入失败，已恢复原模式
RestoreFailed    写入失败且恢复失败，需要立即检查 BIOS 显示模式
```

不要在写入成功后自动重启。重启由用户决定，应用只提示。

### 5.4 UI

UI 接入点：

- `src/OpenSynapse.App/Views/DevicesPage.GpuMode.cs`
- `src/OpenSynapse.App/Views/DevicesPage.xaml`
- `src/OpenSynapse.App/ViewModels/BladeWorkspace.GpuMode.cs`
- `src/OpenSynapse.Windows/Lifecycle/BladeGpuModeController.cs`
- `src/OpenSynapse.App/Strings/zh-CN/Resources.resw`
- `src/OpenSynapse.App/Strings/en-US/Resources.resw`

要求：

- 下拉项由 capability 动态生成，不硬编码 Hybrid/DDS/Discrete 三选一；
- 只显示当前设备报告的模式；
- 当前模式必须被选中，不能因为排序变化误触发写入；
- 不支持时隐藏选择器，并显示明确状态；
- 切换中禁用控件，防止并发写入；
- 写入成功后显示“重启后生效”，不显示“已生效”；
- 当“仅核显（禁用独显）”生效时拒绝 MUX 切换，并提示先关闭它；
- ComboBox 使用 `DisplayMemberPath="Label"`，避免显示记录类型名。

当前实现把“仅核显模式”检查和两种执行器的顺序收口在 `BladeGpuModeController`，不再依赖 UI 按钮各自判断。以后增加服务、命令行或自动化入口时，也必须通过该 controller，不能直接调用 `WindowsDedicatedGpuMode` 或 MUX 写入。

### 5.5 测试

现有单元测试：

```text
tests/OpenSynapse.Core.Tests/Blade/BladeDisplayMuxTests.cs
```

至少覆盖：

- manifest 中三项命令的 transaction、data size、class、command 和 args；
- get/set 请求的字节布局；
- mode 0/1/2 的解析；
- capability 位解析；
- `GetSupportedModes` 的结果顺序；
- 未知 mode 被拒绝。

另外由 `tests/OpenSynapse.Core.Tests/Blade/BladeGpuModeControllerTests.cs` 覆盖执行层护栏：型号未声明时拒绝禁用、禁用后内屏消失回滚、MUX 切换后外接屏消失恢复、启动对账、crash marker 恢复和启用失败回滚。reader 层仍建议补“同模式不写入”“写入后回读一致”“回读不一致触发恢复”的单元测试。

构建和测试：

```powershell
dotnet build src\OpenSynapse.App\OpenSynapse.App.csproj -c Release -p:Platform=x64 --nologo
dotnet test tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj -c Release -p:Platform=x64 --nologo
```

`-p:Platform=x64` 不能省；否则测试项目会落到默认平台并触发已有的平台相关编译问题。

## 6. 真机验收

真实切换是显示链路的高风险操作。只读通过不等于可以发版。

前置条件：

- 机器接电，重要工作已保存；
- 知道如何进入 BIOS 和恢复显示模式；
- 不依赖外接屏完成恢复；
- 先记录当前模式、能力位和原始响应；
- 不要在没有恢复准备时批量测试多个模式。

建议测试矩阵：

| 步骤 | 操作 | 预期 |
|---|---|---|
| 1 | 读取当前模式和能力 | 能解析，模式属于 capability |
| 2 | 对当前模式执行 set | 返回 `Success`，不产生写入 |
| 3 | 选择另一受支持模式 | 返回 `RestartRequired` |
| 4 | 手动重启 | 内屏路由发生变化，系统正常启动 |
| 5 | 重启后读取模式 | 与写入目标一致 |
| 6 | 切回原模式并重启 | 恢复原来显示链路 |

如果写入失败，先看结果是否为 `Restored`；如果是 `RestoreFailed`，不要继续重试写入，立即重启并进入 BIOS 检查显示模式。内屏黑屏时优先用 BIOS 的显示/显卡切换项恢复，不要靠反复执行 HID 命令猜。

只有完成一次“切换成功 + 重启验证 + 切回恢复”，才能把该设备的 MUX 标记为 `Verified`。当前记录只是 `1532:02C6` 的只读验证，真实切换还没有完成。

## 7. 适配收尾检查清单

- [ ] 设备型号、年份和 USB PID 已确认
- [ ] product id 已按 PID 十进制转换
- [ ] 产品 `mw` JavaScript 已定位，且没有执行下载脚本
- [ ] MUX 命令号、payload 和响应位置已从导出常量核对
- [ ] 只读 get 能返回当前模式和 capability
- [ ] 不确定或不支持的模式被拒绝
- [ ] manifest 和 family capability 合同同时更新
- [ ] parser 有单元测试
- [ ] reader 有原值读取、同值短路、回读和恢复
- [ ] `BladeGpuModeController` 统一处理 PnP/MUX 顺序、仅核显互斥和拓扑回滚
- [ ] UI 动态生成选项
- [ ] 构建和测试通过
- [ ] 真机完成切换、重启和恢复测试
- [ ] 只有真机验收后才更新支持列表和 changelog

## 8. 先不要做的事

- 不要用 Windows `DisplayMuxDevice` 绕过 HID 路径；
- 不要把 `SetGPUValue`、GPU Boost、风扇模式或面板分辨率切换叫作 MUX；
- 不要因为某个型号的 JS 里有命令就直接给所有 Blade 开放；
- 不要自动重启机器；
- 不要把未经真机验证的写入标成 `Verified`；
- 不要提供任意 raw HID 控制台给普通用户。
