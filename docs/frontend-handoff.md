# OpenSynapse WinUI 3 前端开发交接文档

更新时间：2026-08-14

本文档是当前 OpenSynapse 代码的前端实施契约。若本文档与旧版设计 Brief 冲突，以本文档和下方引用的 C# 公共契约为准。

## 1. 项目本质

OpenSynapse 是一个本地运行的 WinUI 3 桌面应用，不是 Web 前端，也没有 HTTP 后端。

```text
WinUI 3 XAML / code-behind
        -> MainViewModel
        -> OpenSynapse.Core 公共契约
        -> OpenSynapse.Windows 实现
        -> SetupAPI / hid.dll / Windows API
```

前端禁止：

- 直接打开 HID 句柄；
- 构造或发送 Feature Report；
- 直接调用 `OpenSynapse.Windows.Protocols` 下的协议 builder；
- 仅因为识别到某个 VID/PID 就启用写入控件；
- 把 SET 返回成功当成最终状态已经写入。

Windows 后端负责报文验证、当前设备路径写入门禁、GET 读回、重试、取消和失败恢复。前端只提交强类型参数、等待结果，并展示后端返回的实际值。

## 2. 构建与运行

环境要求：

- Windows 11 x64；
- .NET 10 SDK；
- WinUI 3 所需的 Windows App SDK workload；
- Visual Studio 的 .NET 桌面开发与 Windows 应用开发 workload，或等价命令行工具链。

```powershell
dotnet restore 'OpenSynapse.slnx'
dotnet build 'OpenSynapse.slnx' --no-restore
& '.\src\OpenSynapse.App\bin\x64\Debug\net10.0-windows10.0.19041.0\OpenSynapse.App.exe'
```

应用当前为 unpackaged、自包含发布：

```text
TargetFramework: net10.0-windows10.0.19041.0
Windows App SDK: 1.8.260710003
Platform: x64
WindowsPackageType: None
WindowsAppSDKSelfContained: true
```

普通前端开发不要运行硬件测试。设置 `OPENSYNAPSE_HARDWARE_TEST=1` 后，测试会真实修改设备状态。

## 3. 主要文件

| 文件 | 职责 |
|---|---|
| `src/OpenSynapse.App/App.xaml` | 主题资源和共享 WinUI 样式 |
| `src/OpenSynapse.App/MainWindow.xaml` | 当前窗口、导航和页面布局 |
| `src/OpenSynapse.App/MainWindow.xaml.cs` | 窗口生命周期、导航和点击事件路由 |
| `src/OpenSynapse.App/ViewModels/MainViewModel.cs` | UI 状态、设备刷新和用户操作 |
| `src/OpenSynapse.Core/Devices/RazerDeviceTelemetry.cs` | 强类型遥测和后端写接口 |
| `src/OpenSynapse.Core/Devices/DeviceDescriptor.cs` | 设备身份和通道状态 |
| `src/OpenSynapse.Core/Profiles` | 命名 Profile、设备覆盖、电源覆盖和应用绑定 |
| `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs` | 生产硬件实现，前端不得复制其中逻辑 |
| `docs/device-capability-matrix.md` | 协议证据和生产状态 |

`App.OnLaunched` 目前直接构造具体实现，没有 DI 容器。前端重构不需要为了形式引入 DI 框架。

## 4. 运行时生命周期

`MainWindow` 当前行为：

1. 把 `MainViewModel` 设置为 `RootNavigationView.DataContext`。
2. 第一次激活调用 `InitializeAsync`。
3. 系统性能遥测每 2 秒刷新一次。
4. HID 设备状态和前台应用 Profile 每 3 秒检查一次。
5. 每 30 秒至少完整刷新一次设备；设备、电源、Profile、系统恢复变化时会提前刷新。
6. 普通关闭窗口会隐藏到托盘；托盘“退出”才终止进程。
7. 启动第二个实例时会激活已有窗口。

不要在 Page 或 UserControl 内新增第二套轮询。新控件应绑定 `MainViewModel`，需要操作时在 ViewModel 增加一个明确入口。

## 5. 设备识别与可用状态

设备名称来自内置 manifest，并通过 `DeviceDescriptor.Name`、`BladeDeviceName` 和 `ViperDeviceName` 暴露。新增界面不要硬编码当前两个型号名。

按协议族选择设备页面：

```text
blade-710 -> Blade 控制
viper-184 -> Viper 控制
```

`VID:PID` 用于具体设备身份和设备级 Profile key，不再用于选择 Blade/Viper handler。

发现状态：

| 状态 | UI 含义 |
|---|---|
| `Access=Available` 且 `Capability=PendingValidation` | 控制 collection 可打开；每个写控件仍须等待对应遥测成功 |
| `Access=BusyOrUnavailable` | Synapse 占用、访问拒绝、设备断开或打开失败 |
| `Capability=Unsupported` | 已发现但不是获准控制 collection 的接口 |
| `Capability=Blocked` | 已识别设备，但安全控制通道不可用 |

最终写入门禁始终是具体功能的 `CanSet...`，不能只看设备列表状态。

## 6. 当前 MainViewModel 契约

### 通用状态

| 属性 | 前端用途 |
|---|---|
| `IsBusy` | 稳定尺寸的加载状态；禁止重复提交 |
| `CanRefresh` | 重新探测按钮 |
| `Devices` | 已发现设备列表 |
| `Diagnostics` | 诊断记录列表 |
| `DeviceErrorText` / `HasDeviceError` | 设备页 `InfoBar` |
| `ErrorText` / `HasError` | 汇总诊断 `InfoBar` |
| `LastDeviceRefreshText` | 最近发现时间 |
| `DeviceTelemetryTimeText` | 最近硬件读取/写入时间 |
| `TelemetryTimeText` | 最近系统性能采样时间 |
| `ProfileStatusText` | Profile 加载/保存状态 |

刷新开始时，硬件数值会回到 `--`，写控件全部禁用。遥测字段为 `null` 表示没有获得可信值，禁止用猜测的默认值填充。

### 已经端到端接好的控件

这些功能已有 ViewModel 选中值、`CanSet...` 和应用方法，可以直接重画 XAML：

| 功能 | 编辑值 | 启用条件 | 操作入口 |
|---|---|---|---|
| Blade 键盘亮度 | `BladeBrightnessPercent`，UI 为 0..100% | `CanSetBladeBrightness` | `ApplyBladeBrightnessAsync` |
| Blade 性能模式 | `BladePerformanceModeIndex` | `CanSetBladePerformanceMode` | `ApplyBladePerformanceModeAsync` |
| Blade 充电上限 | `BladeChargeLimitIndex` | `CanSetBladeChargeLimit` | `ApplyBladeChargeLimitAsync` |
| Blade CPU Boost | `BladeCpuBoostIndex` | `CanSetBladeCpuBoost`，仅 Custom | `ApplyBladeCpuBoostAsync` |
| Blade GPU Boost | `BladeGpuBoostIndex` | `CanSetBladeGpuBoost`，仅 Custom | `ApplyBladeGpuBoostAsync` |
| Blade Max Fan | `BladeMaxFanEnabled` | `CanSetBladeMaxFan`，仅 Custom | `ApplyBladeMaxFanAsync` |
| Blade A 面 Logo | `BladeLogoIndex`，仅 Off/Static | `CanSetBladeLogo` | `ApplyBladeLogoAsync` |
| Blade 键盘快速灯效 | `BladeLightingModeIndex`、颜色和 Wave 方向 | `CanSetBladeLighting` | `ApplySelectedBladeLightingEffectAsync` |
| Blade 内置屏刷新率 | `InternalDisplayRefreshRateHertz` | `CanSetInternalDisplayRefreshRate` | `ApplyInternalDisplayRefreshRateAsync` |
| Viper 轮询率 | `ViperPollingRateIndex`，125/500/1000 Hz | `CanSetViperPollingRate` | `ApplyViperPollingRateAsync` |
| Viper 当前 DPI | `ViperDpiXValue`、`ViperDpiYValue` | `CanSetViperDpi` | `ApplyViperDpiAsync` |
| Viper 休眠 | `ViperIdleMinutesValue`，整数 1..15 | `CanSetViperIdle` | `ApplyViperIdleAsync` |
| Viper DPI 档位表 | 档位数、活动档和每档 X/Y | `CanSetViperDpiStages` | `ApplyViperDpiStagesAsync` |

`MainWindow.xaml.cs` 的点击事件使用窗口生命周期 token 调用以上方法。若继续使用 code-behind，保留这一取消模式。

内置屏刷新率选择器和应用按钮已经接入当前 XAML；外屏、多内屏和克隆拓扑仍由 Windows 后端 fail closed。

### 已暴露的状态值

Blade：

- `BladePerformanceModeText`；
- `BladeFanText`；
- `BladeChargeLimitText`；
- 键盘亮度文本和编辑值；
- 设备状态；
- 内置屏分辨率和刷新率。

Viper：

- `ViperBatteryText`；
- `ViperPollingRateText`；
- `ViperDpiText`；
- `ViperIdleText`；
- `ViperDpiStagesText`；
- `ViperLowBatteryThresholdText`；
- 设备状态。

Blade CPU/GPU Boost、Max Fan、Logo、性能模式和充电上限均已映射到 ViewModel 和设备页控制。风扇状态仍只读；高级风扇模式、有线电池、充电状态、自动休眠和休眠倒计时仍只存在于后端遥测，新增展示时不要在 XAML 内解析原始协议值。Viper 电量和低电量阈值保持只读，DPI 档位表已经提供整表编辑器。

## 7. 已接出的生产控制

以下生产方法均已通过 `MainViewModel` 和设备页控件接出，并保留写入门禁、读回和失败恢复。前端重画时必须复用现有属性和操作入口。

| 功能 | 后端方法 | 合法输入 | 推荐控件 |
|---|---|---|---|
| Blade 性能模式 | `SetBladePerformanceModeAsync` | `Balanced`、`Performance`、`Custom`、`Silent`、`Battery`、`Hyperboost` | `ComboBox` 或分段选择 |
| Blade CPU Boost | `SetBladeCpuBoostModeAsync` | `Low`、`Medium`、`High`、`Boost`、`Undervolt` | `ComboBox`，仅 Custom 模式可用 |
| Blade GPU Boost | `SetBladeGpuBoostModeAsync` | `Low`、`Medium`、`High` | `ComboBox`，仅 Custom 模式可用 |
| Blade Max Fan | `SetBladeMaxFanModeAsync` | `Disabled`、`Enabled` | `ToggleSwitch`，仅 Custom 模式可用 |
| Blade 充电上限 | `SetBladeChargeLimitAsync` | 50、55、60、65、70、75、80、100；100 表示关闭限制 | `ComboBox`，不能用自由滑块 |
| Blade A 面 Logo | `SetBladeLogoModeAsync` | 仅 `Off`、`Static` | 两项选择；不得出现 Breathing |
| Viper DPI 档位 | `SetViperDpiStagesAsync` | 1..5 个连续档位，活动档位在表内，每轴 100..30000、步进 50 | 可编辑列表和活动档位选择器 |

Blade 键盘快速灯效走独立的长生命周期后端 `IBladeLightingController`，不要塞进一次性 telemetry setter：

```csharp
await viewModel.ApplyBladeLightingEffectAsync(
    new BladeLightingEffect(
        BladeLightingMode.Wave,
        Direction: BladeWaveDirection.Right),
    cancellationToken);
```

当前生产 UI 模式为 `Off`、`Static`、`Breathing`、`Spectrum`、`Wave`、`Fire`。`Static` 和 `Breathing` 使用 `Color`；`Wave` 使用 `Direction`。控制器在返回前已完成亮度读回门禁及首个 `6 x 17` 完整矩阵帧 ACK；切换、退出和 transport 故障会停止旧任务并恢复 `#99DD72` 持久帧。后端另外提供 `Reactive` 和 `Ripple` 的低级键盘输入适配器与有界事件队列，但它们仍是 `SourceBacked`，尚未进入 `BladeLightingModeOptions`。

`Wave` 和 `Fire` 是基于本地 Lighting Engine 证据实现的近似软件渲染器，不是 Synapse 1:1 复刻。当前仍缺少 Wave 的 exact speed、pause 和 angle scaling，Fire 原生 `7 x 23` 工作网格到 Blade `6 x 17` 输出的确切映射，以及两种效果的实际刷新率证据。前端可以保留现有模式和方向选择，但不得标注“与雷云完全一致”或暴露尚无证据的速度/角度参数。响应、涟漪、音频计和星光不得出现在生产 UI，直到对应输入/WASAPI/Lighting Engine 证据完成。

设备页现已通过 `BladeLightingModeOptions`、`BladeLightingColor`、`BladeWaveDirectionOptions` 和 `ApplySelectedBladeLightingEffectAsync` 接入以上六种模式。颜色使用 WinUI `ColorPicker`，应用按钮只在 Blade 亮度成功读回且 App 持有控制器时启用；前端不应再增加第二套灯光状态或直接调用矩阵报文。Reactive/Ripple 仍需真实事件过滤、布局映射和 side-by-side 视觉验证，完成前不应自行加入下拉选项。

新增 ViewModel 操作应复用当前模式：

```csharp
if (IsBusy || !CanSetFeature)
{
    return;
}

await RunDeviceOperationAsync(
    "用户可理解的操作名称",
    async () =>
    {
        var actual = await _deviceTelemetryReader.SetFeatureAsync(
            _deviceDescriptors, requested, cancellationToken);
        // 展示和持久化 actual，不能直接使用 requested。
    },
    cancellationToken,
    restoreSelection: () => Selection = ConfirmedSelection);
```

如果某项设置尚未出现在 `ProfileModels.cs`，必须先决定它是仅当前会话生效，还是需要持久化。不能只向 JSON 模型塞字段而不处理 clone、default、resolver、applier 和测试。

## 8. Profile 能力矩阵

“模型中有字段”“会自动应用”“有安全生产 setter”是三件不同的事：

| 功能 | Profile 可持久化 | `VerifiedProfileApplier` 自动应用 | 生产 setter |
|---|---|---|---|
| Blade 键盘亮度 | 是 | 是 | 是 |
| Blade 性能模式 | 是 | 是 | 是 |
| Blade 充电上限 | 是 | 是 | 是 |
| Blade Max Fan | 是 | 是 | 是，仅 Custom |
| Blade FanMode / FanTargetRpm | 字段存在 | 否 | 否，禁止接 UI 写入 |
| Blade CPU/GPU Boost | 否 | 否 | 是，仅 Custom |
| Blade Logo | 否 | 否 | 是，仅 Off/Static |
| Blade 内置屏刷新率 | 是 | 由显示控制流程应用 | Windows 显示 setter 已完成 |
| Viper 当前 DPI | 是 | 是 | 是 |
| Viper 轮询率 | 是 | 是 | 是 |
| Viper 休眠 | 是 | 是 | 是 |
| Viper DPI 档位 | 否 | 否 | 是，必须整表提交 |

不要因为 `BladeProfileSettings` 中存在 `FanMode` 或 `FanTargetRpm` 就画出可应用控件。当前自动应用器明确忽略它们，生产后端也没有开放固定/曲线风扇 setter。

## 9. 各功能交互规则

### Blade 性能与 Boost

- 性能模式写入会保留当前风扇模式，并校验两个固件分区。
- CPU/GPU Boost 仅在当前性能模式为 `Custom` 时可写。
- Max Fan 同样仅允许在 `Custom` 模式写入。
- 非 Custom 状态下禁用 Boost/Max Fan，并说明依赖条件。
- 不要在 XAML 中串联多个后端调用。依赖操作放进一个 ViewModel 方法。

### Blade 充电上限

只允许：

```text
50, 55, 60, 65, 70, 75, 80, 100
```

`100` 显示为“关闭限制（100%）”。自由滑块会产生无效中间值，不得使用。

### Blade Logo

生产目标只有 `Off` 和 `Static`。`BladeLogoMode.Breathing` 存在的原因是后端需要解析并恢复设备原始状态；把它作为新目标传入会被拒绝。普通控制中不要放一个“即将支持”的 Breathing 选项，这会制造错误预期。

### Viper 当前 DPI

- X/Y 可分别编辑；
- 范围 `100..30000`；
- 步进必须为 `50`；
- 保存最后确认值，失败后恢复编辑器。

### Viper DPI 档位

- 1 到 5 档；
- UI 档位号连续且从 1 开始；
- 活动档位为 `1..档位数`；
- 每档 X/Y 均为 `100..30000`，步进 50；
- 用一个 `ViperDpiStagesTelemetry` 一次提交完整表；
- 禁止按行调用当前 DPI SET；
- 写入失败时后端会尝试恢复完整原表，异常消息包含恢复结果，前端必须展示。

### Viper 休眠与轮询率

- 休眠为 1 到 15 的整数分钟；
- 轮询率只有 125、500、1000 Hz；
- 不要提供自由文本的额外值。

### 键盘亮度

后端参数是 `byte 0..255`，当前 ViewModel 已完成 UI 百分比转换：

```text
UI 0..100% -> 四舍五入到 byte 0..255
byte 0..255 -> UI 百分比
```

重画 XAML 时继续绑定 `BladeBrightnessPercent`，不要把百分比直接传给后端 setter。

## 10. 错误和忙碌状态

ViewModel 当前区分：

- 发现/查询错误；
- 用户写入错误；
- 显示控制器错误；
- 性能采样错误。

使用 `InfoBar` 展示错误。写入失败后不得把界面值保留为用户请求值，必须恢复到最后确认值。后端错误可能包含：

```text
原值已恢复
原值恢复失败，请立即检查 Synapse/设备
写入后读回不一致
当前设备路径未完成验证
控制通道不可用
```

恢复失败不能只用短暂 Toast，必须留在持久 `InfoBar` 和诊断信息中。

`IsBusy` 目前是全局状态。为避免并发破坏硬件操作，在它为 true 时禁用所有提交，保持控件尺寸稳定，只显示一个克制的进度状态。后端和 ViewModel 尚未支持逐控件并发，不要自行增加。

## 11. Profile 和设置页面

Profile 页面已接入活动配置选择、新建、克隆、重命名、删除、JSON 导入导出、应用绑定和当前用户开机启动。删除最后一个 Profile 仍由 Core 拒绝；开机启动写入 HKCU，不需要管理员权限。

可用 Core API：

- `ProfileCatalog.GetNames`、`Select`、`Create`、`Clone`、`Rename`、`Delete`；
- `ApplicationProfileBinding.Bind`、`Unbind`、`Resolve`；
- `ProfileStore.ImportAsync`、`ExportAsync` 和原子 `SaveAsync`；
- 全局值、`VID:PID` 设备覆盖、插电覆盖和电池覆盖；
- 前台应用自动切换 Profile；
- `WindowsStartupManager.IsEnabled`、`SetEnabled`。

以上管理能力均已通过 `MainViewModel` 接入 App；文件选择器使用当前 WinUI 窗口句柄初始化，XAML 不反射访问 `_profile`，也不直接读取或写入 JSON。修改操作先保留完整内存快照，只有原子保存成功后才刷新设备；失败或取消会同时恢复 Profile 文档和自动切换 fallback 状态。

尚未提供的是全局/当前设备/插电/电池作用域编辑器。当前各设备控件继续写入活动 Profile 的全局层；不要仅依据 JSON 模型自动生成未验证硬件控制。

Core 禁止删除最后一个 Profile。Profile 名最多 64 个字符，且禁止控制字符和 `\ / : * ? " < > |`。

默认 Profile 文件：

```text
%LocalAppData%\OpenSynapse\profiles.json
```

本地诊断日志已经启用：

```text
%LocalAppData%\OpenSynapse\logs\opensynapse.log
```

日志达到 1 MiB 后保留一份 `opensynapse.log.previous`。当前 UI 尚无“打开日志目录”“复制错误”“导出日志”入口；增加这些命令时只操作日志文件，不要把 HID 路径、设备序列号或其它个人信息自动放进导出内容。

## 12. WinUI 3 实施规则

- 保留 `NavigationView`、Mica 和 `AppWindow` 生命周期行为。
- 使用原生 `ComboBox`、`NumberBox`、`Slider`、`ToggleSwitch`、`InfoBar`、`ContentDialog`、`ProgressRing`、`ItemsRepeater` 和文件选择器。
- 保留托盘关闭、退出、第二实例激活和系统恢复刷新。
- 设备标题绑定 `BladeDeviceName` 和 `ViperDeviceName`。
- 页面颜色使用 `ThemeResource`/应用资源，不要散落硬编码颜色。
- 保留 OpenSynapse 品牌绿色：主色 `#99DD72`、悬停 `#A9E889`、按下 `#82C95D`。
- 支持浅色、深色和 High Contrast。当前已有浅/深按钮；可增加跟随系统，但不能破坏现有生命周期。
- 图标按钮设置 `AutomationProperties.Name`、键盘焦点和 Tooltip。
- 不要卡片套卡片；卡片只用于离散的设置单元或工具。
- 不要引入 React、WebView、Tailwind 或第三方 UI 框架。

当前 `MainWindow.xaml` 过度压缩，可以拆成职责明确的 WinUI `Page` 或 `UserControl`，但仍通过现有 ViewModel 交互。不要为了拆文件引入一套导航框架。

## 13. 明确禁止开放的功能

不得创建可用写入控件或 ViewModel 写入口：

- Blade 固定转速或风扇曲线；
- Chroma Studio、高级灯效编辑器和任意自定义矩阵编辑；现有六种快速灯效除外；
- Blade Logo Breathing；
- GPU MUX；
- Viper 低电量阈值写入；
- Viper 电池类型写入；
- Viper 按键映射、HyperShift 和板载 Profile；
- Viper 表面校准；
- 宏系统。

部分功能可能已有 parser、builder 或只读遥测，但这不代表生产写入已经完成。若展示，只能做只读信息并准确标注。

## 14. 前端验收清单

- 解决方案构建为 0 warning / 0 error。
- 普通测试通过，且没有启用硬件测试。
- UI 项目没有调用协议 builder 或 HID transport。
- 每个写控件只有在当前设备路径对应 GET 成功后才启用。
- 写入完成后显示后端返回值，不显示未经确认的请求值。
- 写入失败后编辑器恢复到最后确认值。
- 原状态恢复失败持续可见。
- 设备断开后清空旧值并禁用 setter。
- 重连、Resume、电源变化、活动 Profile 变化后正常刷新。
- 设备名称来自 manifest；协议族路由不比较硬编码 PID。
- DPI、休眠、轮询率和充电上限控件无法生成非法值。
- Logo 只出现 Off 和 Static。
- 非 Custom 模式下禁用 Boost 和 Max Fan。
- 验证浅色、深色、键盘导航、High Contrast，以及 100/150/200% 缩放。
- 验证托盘隐藏、退出和第二实例激活未被破坏。

验证命令：

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore
dotnet build 'OpenSynapse.slnx' --no-restore
```

除非设备所有者明确授权真实硬件验证，否则不要设置 `OPENSYNAPSE_HARDWARE_TEST`。

## 15. 当前基线

2026-08-14 最新完整验证结果：

```text
360 个非硬件测试通过
9 个硬件测试被 opt-in 门禁跳过
0 个测试失败
0 个构建警告
0 个构建错误
```

后续增加 Profile 作用域编辑器时，不得削弱本文定义的门禁、读回、事务回滚和失败状态。
