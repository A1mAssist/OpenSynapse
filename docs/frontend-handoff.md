# OpenSynapse WinUI 3 前端交接（新版）

更新时间：2026-08-17

这份文档是当前后端的前端实施契约。代码和强类型契约优先于旧设计稿；“已存在字段”不等于“允许用户写入”。

## 1. 架构边界

OpenSynapse 是本地 WinUI 3 桌面应用，没有 HTTP 后端：

```text
WinUI 3 XAML
  -> MainViewModel
  -> OpenSynapse.Core 契约 / Profile
  -> OpenSynapse.Windows 实现
  -> Windows API / HID Feature Report
```

前端只绑定 `MainViewModel`，提交强类型值，展示后端读回的实际值。禁止在 XAML、Page 或 ViewModel 之外：

- 打开 HID 句柄或发送 Feature Report；
- 调用 `OpenSynapse.Windows.Protocols` 的 builder；
- 看到 VID/PID 就启用写入；
- 把 SET 成功当成最终状态，跳过 GET 读回；
- 为同一设备另起轮询、设备探测或灯效 runtime。

后端统一负责设备门禁、报文、读回、重试、取消、恢复和设备断开错误。

## 2. 构建和运行

- Windows 11 x64
- .NET 10 SDK
- Visual Studio 的 Windows 应用开发 workload（含 Windows App SDK）
- 当前目标：`net10.0-windows10.0.26100.0`，最低运行版本 `10.0.19041.0`，x64、unpackaged、自包含；开发机需安装 Windows SDK 10.0.26100

```powershell
dotnet restore 'OpenSynapse.slnx'
dotnet build 'OpenSynapse.slnx' --no-restore
& '.\src\OpenSynapse.App\bin\x64\Debug\net10.0-windows10.0.19041.0\OpenSynapse.App.exe'
```

不要在普通 UI 调试中运行硬件验证工具。硬件验证会真实写设备状态，并且要求 `OPENSYNAPSE_HARDWARE_TEST=1`。

## 3. 前端应依赖的文件

| 文件 | 用途 |
|---|---|
| `src/OpenSynapse.App/MainWindow.xaml` | 当前窗口、导航和页面布局 |
| `src/OpenSynapse.App/MainWindow.xaml.cs` | 窗口生命周期、点击事件和文件选择器 |
| `src/OpenSynapse.App/ViewModels/MainViewModel.cs` | 全部 UI 状态、刷新和操作入口 |
| `src/OpenSynapse.Core/Devices/RazerDeviceTelemetry.cs` | 设备遥测和写入接口 |
| `src/OpenSynapse.Core/Profiles/ProfileModels.cs` | Profile、设备覆盖和电源覆盖 |
| `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs` | 生产 HID 实现，前端不要复制 |
| `src/OpenSynapse.Windows/Lighting/BladeLightingController.cs` | 长生命周期键盘灯效控制器 |
| `docs/device-capability-matrix.md` | 协议证据、读回和生产状态 |
| `docs/device-manifest-guide.md` | 外部 manifest 适配规则 |

## 4. 生命周期和通用状态

`App.OnLaunched` 直接构造现有 ViewModel，不要为了前端重画引入 DI 框架或第二套导航框架。

- 第一次窗口激活调用 `InitializeAsync`。
- 系统性能约每 2 秒采样一次。
- 设备、Profile 和前台应用状态约每 3 秒检查一次，至少每 30 秒完整刷新一次。
- 普通关闭隐藏到托盘；托盘退出才结束进程；第二实例激活已有窗口。
- `IsBusy` 为全局硬件互斥。为 `true` 时禁用所有提交按钮，不要自行实现并发写入。

通用绑定：

| 属性 | 用法 |
|---|---|
| `IsBusy` / `CanRefresh` | 加载、刷新和提交状态 |
| `Devices` | 设备列表 |
| `Diagnostics` | 能力查询记录 |
| `DeviceErrorText` / `HasDeviceError` | 设备错误 `InfoBar` |
| `ErrorText` / `HasError` | 全局错误 `InfoBar` |
| `DeviceTelemetryTimeText` | 最近硬件读写时间 |
| `TelemetryTimeText` | 最近系统性能采样时间 |
| `ProfileStatusText` | Profile 保存、应用和恢复状态 |

刷新期间数值清为 `--`，写控件禁用；`null` 必须展示为未知，不得填入猜测的默认值。

## 5. 设备识别

按 `DeviceDescriptor.ProtocolFamily` 选择页面，不要硬编码型号名：

```text
blade-710 -> Blade 页面
viper-184 -> Viper 页面
```

设备状态组合如下：

| 条件 | UI 含义 |
|---|---|
| `Access=Available`、`Capability=PendingValidation` | 控制 collection 可打开，但每项仍须通过自己的读回门禁 |
| `BusyOrUnavailable` | Synapse 占用、拒绝、断开或打开失败 |
| `Unsupported` | 已发现但不是获准控制 collection |
| `Blocked` | 已识别，但安全控制通道不可用 |

新增设备只可通过 manifest 复用已审核协议族。外部 manifest 位于 `%LocalAppData%\OpenSynapse\devices\`，只允许修改身份字段；报文、命令、长度和 capability 名称不是扩展点。新报文必须新增审核过的内置协议族。

## 6. MainViewModel 可用操作

所有操作均为异步方法；按钮点击沿用 `MainWindow` 的窗口取消 token。

### Blade

| 属性/方法 | 输入或范围 | 控件 |
|---|---|---|
| `BladeBrightnessPercent` / `ApplyBladeBrightnessAsync` | UI `0..100%`，后端 `0..255` | `Slider` + 应用按钮 |
| `BladePerformanceModeIndex` / `ApplyBladePerformanceModeAsync` | 当前可写选项为平衡、性能、自定义、静音、HyperBoost；固件读回 `3` 显示“电池节能”，`6` 显示“平衡（电池）”，两者在固件门禁和针对性实机验证完成前都不是手动选项 | `ComboBox` |
| `BladeCpuBoostIndex` / `ApplyBladeCpuBoostAsync` | 低、中、高、Boost、降压预设；仅 Custom | `ComboBox` |
| `BladeCurveOptimizerValue` / `ReadBladeCurveOptimizerAsync` / `ApplyBladeCurveOptimizerAsync` | 研究代码保留，但 2026-08-16 同步 ABI 实机 GET 触发 `SEHException` | 生产界面不接控件；现有区域已隐藏，`CanReadBladeCurveOptimizer` / `CanSetBladeCurveOptimizer` 固定为 `false`，等待官方 async service 路径 |
| `BladeGpuBoostIndex` / `ApplyBladeGpuBoostAsync` | 低、中、高；仅 Custom | `ComboBox` |
| `BladeMaxFanEnabled` / `ApplyBladeMaxFanAsync` | 开/关；仅 Custom | `ToggleSwitch` |
| `BladeChargeLimitIndex` / `ApplyBladeChargeLimitAsync` | 50、55、60、65、70、75、80、100 | `ComboBox`，不可用自由滑块 |
| `BladeLogoIndex` / `ApplyBladeLogoAsync` | 关闭、常亮 | `ComboBox` |
| `InternalDisplayRefreshRateHertz` / `ApplyInternalDisplayRefreshRateAsync` | 后端枚举出的内部屏刷新率 | `ComboBox` |

所有 `CanSet...` 都必须绑定。CPU/GPU Boost 和 Max Fan 在非 Custom 模式自动禁用。

### Viper V3 HyperSpeed

| 属性/方法 | 输入或范围 | 控件 |
|---|---|---|
| `ViperPollingRateIndex` / `ApplyViperPollingRateAsync` | 125、500、1000 Hz | `ComboBox` |
| `ViperDpiXValue`、`ViperDpiYValue` / `ApplyViperDpiAsync` | 每轴 100..30000，步进 50 | 两个 `NumberBox` |
| `ViperIdleMinutesValue` / `ApplyViperIdleAsync` | 1..15 分钟整数 | `NumberBox` |
| `ViperDpiStageCount`、`ViperActiveDpiStage`、`ViperDpiStages` / `ApplyViperDpiStagesAsync` | 1..5 档；每档 X/Y 100..30000，步进 50；一次提交完整表 | `NumberBox` + `ItemsRepeater` |

`ViperBatteryText`、`ViperDpiText`、`ViperDpiStagesText`、`ViperLowBatteryThresholdText` 是展示值。低电量阈值当前只读，按产品要求不增加编辑控件。

板载映射后端现提供 `ReadViperButtonAssignmentsAsync` 与
`SetViperButtonAssignmentAsync`。前者在返回前校验固定 Profile `1`、八个 firmware
button ID 和全部 Normal/HyperShift 共 16 条记录；后者只改一条记录，并执行目标 GET、
另一层隔离 GET 以及失败时不可取消恢复。WinUI 已接入完整 16 条记录的读取和受限编辑。生产 SET 允许 `Off`、
已物理确认的鼠标 `ButtonCode`（`1,2,3,4,5,9,10`）、`KeyboardKey` 和 `DoubleClick`；当前 WinUI 选择器仍只生成 Off/鼠标键。
Dpi、MediaKey、HyperShift、KeyboardTurbo、MouseTurbo 的协议解析和校验虽已完成，写入仍在
transport 前被拒绝；每个函数族完成独立可逆真机验证后才能逐项放行。不得从共享 enum
渲染 Macro/Profile/Lighting/Power/Controller/RazerKey/WindowsShortcuts。

### Profile 和系统

已接入：`SelectProfileAsync`、`CreateProfileAsync`、`CloneActiveProfileAsync`、`RenameActiveProfileAsync`、`DeleteActiveProfileAsync`、`ImportProfilesAsync`、`ExportProfilesAsync`、`BindApplicationAsync`、`UnbindApplicationAsync`、`SetStartupEnabledAsync`。

Profile 支持全局、设备、插电、电池和前台应用绑定。保存采用原子替换；保存或设备应用失败时同时恢复文档和 UI 选中值。禁止直接编辑 JSON 或绕过 `ProfileStore`。

## 7. Blade 键盘灯效

长生命周期入口只有：

```csharp
await viewModel.ApplySelectedBladeLightingEffectAsync(cancellationToken);
// 或
await viewModel.ApplyBladeLightingEffectAsync(effect, cancellationToken);
```

当前生产页面可显示：

```text
Off / Static / Breathing / Spectrum / Wave / Fire / Reactive / Ripple / Audio Meter / Ambient / Wheel / Starlight / Tidal
```

- `Static`、`Breathing` 使用 `BladeLightingColor`，颜色格式为 `RRGGBB`。
- `Wave` / `Wheel` 使用 `BladeWaveDirectionOptions`（向右/顺时针、向左/逆时针）。
- `Tidal` 使用 `BladeLightingColor` 和 `BladeLightingSecondColor`。
- 运行时按约 25 FPS 生成完整 `6 x 17` 设备矩阵，首帧和每行 ACK 成功后才算应用完成。
- 切换、退出、取消或 transport 故障会停止旧 runtime，并恢复默认持久帧 `#99DD72`。
- Wave 已移植 Product 710 的 `Width=100`、`Speed=25`、`Pause=0`、`Angle=90|270`、25 FPS 投影。Fire 已移植 DLL 的真实 100 基础场、`Rate=160` 五相插值/500 帧循环、固定 mask/color 表和 `7 x 23 -> 6 x 17` 反向裁剪。两者在完成当前设备视觉/恢复对比前仍标记为 `Approximate`，不得宣称雷云 1:1。

以下模式的实现和生产接入状态：

| 模式 | 当前状态 | 前端处理 |
|---|---|---|
| Reactive / Ripple | Raw Input + Raw HID 适配器已通过普通键、Blade 右侧 M3/M4/M5、外接键盘隔离和实机视觉验证 | 已加入生产选项；继续复用后端输入适配器，不在 UI 解析按键报文 |
| Audio Meter | 默认 WASAPI loopback、RMS/Peak、端点失效重建已完成；2026-08-15 播放/暂停响应和 Normal 恢复通过实机目视验证 | 已加入生产选项 |
| Ambient | 唯一内置显示器 Graphics Capture、边缘采样、权限/拓扑 fail-closed 已完成；2026-08-15 四区颜色映射、Normal 恢复和 Windows 11 Borderless 捕获通过实机验证 | 已加入生产选项；无边框权限被拒绝时允许系统边框降级，不得让灯效失败 |
| Wheel | Product 710 色轮中心、方向、周期和逐键投影已实现并覆盖自动测试 | 已加入生产模式列表；仍标记为 `Approximate`，等待与雷云视觉对比 |
| Starlight | 确定性渲染器已实现 Product 710 容量、再生周期、节点寿命/亮度、随机数发生器和输出生命周期；视觉与 Normal 恢复已验证 | 已加入生产模式列表 |
| Tidal | 渲染器已实现 Product 710 默认请求和原生三波道投影算法，视觉与 Normal 恢复已验证，Profile 支持第二颜色 | 已加入生产模式列表和双颜色编辑器 |

不要把一次性 `KeyboardLightingValidation` 工具当作 App UI API。键盘事件适配器保持内部实现，前端不处理 HID 报文。

## 8. Blade 新增只读遥测

`RazerDeviceTelemetry` 已增加以下字段，前端可以做展示，但不能因此增加写入口：

| 字段 | 说明 | 状态 |
|---|---|---|
| `BladeCurrentFanCpuRpm` / `BladeCurrentFanGpuRpm` | 当前 CPU/GPU 转速 | 只读；查询链路已接入 |
| `BladeAdvancedFanCpuModeRaw` / `BladeAdvancedFanGpuModeRaw` | 高级风扇模式原始值 | 只读原始诊断，UI 不要自行翻译 |
| `BladeFanMode` / `BladeFanTargetRpm` | 当前风扇模式和存储目标 | 可展示；固定转速写入仍禁止 |
| `BladeStartupAnimationEnabled` | 启动动画当前状态 | 后端已有 `SetBladeStartupAnimationAsync` 的读取门禁、写入、读回和失败恢复；真机可逆验证前不要显示写控件 |

当前 ViewModel 只把部分值汇总为 `BladeFanText`。若前端要拆成独立卡片，应先在 ViewModel 增加命名展示属性，继续绑定 `RazerDeviceTelemetry`，不要在 XAML 解析 Raw 字段。

### 风扇曲线边界

后端已有 `BladeFanCurve`、温度插值和 `BladeFanCurveRuntime`，曲线在软件侧按 CPU/GPU 目标分别计算并写入，不是固件保存的一张“曲线报文”。并发 Stop/Dispose、调用方取消时不可取消恢复、短暂传感器丢失和有界失败已由测试覆盖。它仍是 `SourceBacked`：断连、睡眠/唤醒和进程硬退出的物理恢复验证未完成。当前没有 `MainViewModel` 生产写入口，前端不得自行画可应用曲线编辑器；只读风扇目标/转速可以展示。

## 9. 系统性能卡片

当前 `MainViewModel` 已暴露：

```text
CpuName, CpuValue, CpuPercent, CpuTemperatureText, CpuPowerText, CpuClockText
GpuName, GpuValue, GpuPercent, GpuTemperatureText, GpuPowerText, GpuClockText, GpuMemoryText
MemoryValue, MemoryDetail, MemoryPercent
StorageValue, StorageDetail, StoragePercent
```

数据来源：CPU 使用率/内存/磁盘为 Windows API，CPU 温度/功耗/时钟为 PDH + `CallNtPowerInformation`，GPU 为 `nvidia-smi.exe`。GPU 读取失败时只显示未知并保留诊断提示。

注意：当前 CPU 时钟是 Windows 返回的各逻辑处理器当前频率的平均值，不是“最快核心瞬时频率”；不要在 UI 文案中写成最快核心。CPU/GPU 电压已移除，GPU MUX 不在本设备范围内。

## 10. 错误、恢复和禁用规则

每个提交都必须遵循：

1. 检查 `IsBusy` 和对应 `CanSet...`。
2. 调用单个 ViewModel 操作，不在 XAML 串联多个 setter。
3. 成功后展示后端实际读回值。
4. 失败后恢复最后确认值，并在持久 `InfoBar` 和 `Diagnostics` 显示恢复结果。

常见后端错误包括：设备占用、控制路径未验证、写入后读回不一致、原值恢复失败、设备断开、睡眠/唤醒期间 transport 失效。恢复失败不能只用 Toast。

## 11. WinUI 3 交付规则

- 保留 `NavigationView`、Mica、托盘、第二实例激活和系统恢复刷新。
- 使用 `ComboBox`、`NumberBox`、`Slider`、`ToggleSwitch`、`InfoBar`、`ContentDialog`、`ProgressRing`、`ItemsRepeater`、原生文件选择器。
- 所有图标按钮设置 `AutomationProperties.Name`、Tooltip、键盘焦点和稳定尺寸。
- 颜色使用 `ThemeResource`；品牌色为 `#99DD72`，悬停 `#A9E889`，按下 `#82C95D`。
- 支持浅色、深色和 High Contrast；主题切换放在标题栏/应用设置区，不要占用设备控制卡片。
- 不引入 React、WebView、Tailwind 或第三方 UI 框架；不要卡片套卡片。
- 数值、按钮和错误文本在窄窗口下不得重叠；硬件操作期间控件尺寸不得抖动。

## 12. 前端验收清单

- [ ] 页面只绑定 `MainViewModel`，没有 HID 或协议代码。
- [ ] 每个提交按钮绑定对应 `CanSet...` 和 `IsBusy`。
- [ ] 提交成功显示读回值；失败恢复编辑值并留下 `InfoBar`。
- [ ] Blade 十三个生产灯效、亮度、Logo、性能/Boost/Max Fan/充电上限、内部屏刷新率均可操作。
- [ ] Viper 125/500/1000 Hz 轮询率、当前 DPI、休眠、完整 DPI 档位表和受限板载映射可操作。
- [ ] Viper 电量和低电量阈值只读。
- [ ] CPU/GPU 电压、GPU MUX、固定风扇/风扇曲线、宏、Blade HyperShift/键位映射、Snap Tap、Chroma Studio 和尚未接入 UI 的灯效不显示可操作入口；Blade 映射/Snap Tap 的编译器、会话、Raw Input 和 SendInput 安全收尾已在后端完成，但仍是显式 opt-in，必须通过真实按键、焦点/断连/崩溃恢复验证后才能显示。Viper 映射只读链路和 `Off`/鼠标按键恢复型写入后端已存在，其他 function 仍被生产拒绝，必须通过各自的可逆真机验证后才能逐项显示。
- [ ] Profile 创建/克隆/重命名/删除、导入/导出、应用绑定和当前用户开机启动可用。
- [ ] 窗口关闭、托盘退出、第二实例、设备断开、主题切换和 High Contrast 已手测。

## 13. 交接后优先级

1. 先按本契约重画 WinUI 页面，不改后端协议。
2. 保持 Blade 新增遥测为只读独立字段；原始值不得变成未经验证的写入口。
3. Viper 当前选择器只开放 `Off` 和已验证鼠标键码；后端也允许 `KeyboardKey`/`DoubleClick`，增加对应编辑器时直接复用现有 setter。其他函数族继续由后端拒绝。
4. 风扇曲线只有在断连、睡眠/唤醒、取消和进程退出恢复验证完成后，才讨论 UI 入口。

不要把“已研究”“有解析器”“能发出报文”写成“已完成”。完成标准是：当前设备可运行、读回或视觉结果可验证、异常可恢复。
