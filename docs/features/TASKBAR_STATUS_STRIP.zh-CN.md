# 任务栏状态条实现计划（Blade 电量 / 性能）

> 交接顺序：先做第 3 节 Spike 0；通过后再写 Core/Windows/App。不要先改设置页或完整绘制。

## 1. 目标

在 Windows 任务栏里显示一条紧凑的 Blade 状态条，视觉参考 Lyricify 的任务栏歌词条：

```text
[ 87%  ⚡ 性能 ]
```

- 显示笔记本电量、充电状态、当前性能模式。
- 点击打开 OpenSynapse 的 Blade 页面。
- 数据复用现有 2 秒遥测轮询，不新增硬件读取。
- 只对检测到 Blade 电池的机器显示；桌面机、无电池设备不显示。
- 设置页提供独立开关，默认关闭，验证稳定后再考虑默认开启。

不在第一版范围：

- 不做任务栏歌词、封面、动画。
- 不做副屏任务栏；第一版只支持主屏、任务栏在底部。
- 不做自动避让任务栏图标；固定锚在托盘区左侧，空间不够就缩窄或隐藏。
- 不新增独立进程、WPF helper、UIA 自动避让。

## 2. 结论：用 Win32 子窗口挂进任务栏

采用和 Lyricify / TaskbarLyrics 同类的宿主方式。只参考公开的 Win32 用法；TaskbarLyrics 是 MIT，若直接复用它的代码片段，保留 MIT 许可证和署名。Lyricify 本体未开源，不复制它的任何代码或资源。

```text
CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)
  -> SetParent(hwnd, Shell_TrayWnd)
  -> 样式从 WS_POPUP 改成 WS_CHILD
  -> SetWindowPos 到任务栏客户区
```

关键决定：

1. **不把 WinUI 3 主窗口 `SetParent` 进任务栏。**
   WinUI 3 窗口不是为被 reparent 设计的；把主窗口挂到 Explorer 会拖累整块 WinUI 内容，DPI、输入和 Explorer 重启后的生命周期都不可控。状态条用独立的纯 Win32 子窗口，问题只影响这一条。

2. **状态条本身用纯 Win32 窗口 + GDI 绘制，不引入 WPF/Win2D/新 NuGet。**
   窗口很小，只需要圆角背景、两段文字、一个充电符号；GDI 的 `FillRect` / `RoundRect` / `DrawText` 足够，且不增加发布体积。

3. **放在主进程，不做单独 helper。**
   App 已经有遥测、Blade 状态和 `Shell_TrayWnd` 交互；单独进程只会多出 IPC、安装包和崩溃恢复成本。

4. **先做 Spike，再写完整窗口。**
   OpenSynapse 经常以管理员权限运行，而 Explorer 是普通权限。跨完整性级别 `SetParent` 是否可见、是否能收到鼠标消息，必须在目标机器上实测；这是本计划唯一会改变方案的关键未知。

## 3. Spike 0：30 分钟，先验证宿主

在 `App` 里临时加一个只走调试入口的探针，不接设置、不做绘制：

1. 创建一个 160×32 的 `WS_POPUP` 窗口，背景填红色。
2. `FindWindowW("Shell_TrayWnd", null)` 拿主任务栏。
3. 用 `SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(taskbar))` 对齐 DPI 上下文。
4. `SetParent`，样式改成 `WS_CHILD`，`SetWindowPos` 到托盘区左侧空白区。
5. 加 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`，处理 `WM_LBUTTONUP` 打一条日志。

分别在以下条件跑：

- 普通权限启动。
- 管理员权限启动（和开机启动计划任务一致）。
- 100% 和 150% DPI。
- 任务栏在底部。
- Explorer 重启一次（任务管理器重启 Windows 资源管理器）。

通过标准：

- 红条在任务栏内可见，位置正确。
- 点击能收到 `WM_LBUTTONUP`。
- Explorer 重启后能重新创建并挂上。
- 管理员权限下不出现“窗口创建成功但看不见/点不到”。
- attach 后主 WinUI 窗口的布局、缩放和 DPI 不发生变化。

失败处理：

- 如果管理员权限下 `SetParent` 失败或窗口不可见，不要硬写完整子窗口。
- 记录 `GetLastError` 和窗口矩形；确认不可行后走下面的 Plan B。

### 3.1 Plan B：Spike 失败时的 topmost 贴合窗

- 用 WinUI 3 独立 `Window`，复用 `TrayMenuWindow` 的无边框、topmost、圆角 region 做法；不 reparent 主窗口。
- 位置和尺寸与子窗口方案相同，贴住任务栏。
- 用 `SHQueryUserNotificationState` 和前台窗口类判断，在开始菜单、通知中心、Alt+Tab、全屏应用出现时隐藏。
- 共用同一个 `TaskbarStatusPresenter`、Core 策略和设置，只换 host；不要把两套宿主塞进一个类。
- 验收额外加：开始菜单、通知中心、全屏时不覆盖系统 UI。

## 4. 分层与文件

遵守 `docs/ARCHITECTURE.md` 的三层边界：

| 层 | 职责 | 新增/修改 |
|---|---|---|
| `OpenSynapse.Core` | 纯数据、可见性策略、格式化 | 新增 `Sensors/TaskbarStatusSnapshot.cs`、`Sensors/TaskbarStatusPolicy.cs` |
| `OpenSynapse.Windows` | Win32 窗口、任务栏句柄、GDI 绘制 | 新增 `Lifecycle/TaskbarStatusWindow.cs`、`Lifecycle/TaskbarStatusNative.cs` |
| `OpenSynapse.App` | 订阅 ViewModel、设置、装配 | 新增 `TaskbarStatusPresenter.cs`；修改 `App.xaml.cs`、设置页和资源 |
| `OpenSynapse.Core.Tests` | 纯策略测试 | 新增 `TaskbarStatusPolicyTests.cs` |

不要把这些代码塞进 `MainViewModel` 或 `MainWindow`：

- `TaskbarStatusWindow` 不引用 App/ViewModel/本地化资源。
- `TaskbarStatusPresenter` 只做数据到快照的映射和窗口生命周期。
- 颜色、字体、尺寸属于 Windows 层；用户文案属于 App 资源。

## 5. 数据契约

Core 新增：

```csharp
public readonly record struct TaskbarStatusSnapshot(
    int? BatteryPercent,
    bool? IsCharging,
    string PerformanceMode);

public static class TaskbarStatusPolicy
{
    public static bool ShouldShow(TaskbarStatusSnapshot snapshot, bool enabled);
    public static string FormatBattery(int? percent);
    public static string FormatTooltip(
        TaskbarStatusSnapshot snapshot,
        string chargingText,
        string performanceText);
    public static bool ShowPerformanceSegment(int widthPx, int minWidthPx);
}
```

数据来源：

- `TelemetryWorkspace.BatteryChargeValue` / `BatteryChargeState` 已有，但为了不解析本地化字符串，给 `TelemetryWorkspace` 补两个只读属性：
  - `public int? BatteryPercent { get; private set; }`
  - `public bool? IsCharging { get; private set; }`
  - 在 `RefreshBatteryCharge()` 里从 `_powerSourceProvider.BatteryPercent` / `IsCharging` 赋值并触发 `PropertyChanged`。
- `BladeWorkspace.PerformanceModeText` 已有，直接使用。
- Blade 是否存在：用 `BladeWorkspace.BladeProductId != 0` 或 `viewModel.Devices.Any(d => DeviceProtocolFamilies.IsBlade(d.ProtocolFamily))`。设备休眠导致一次 `Devices` 为空时，不要立刻隐藏；用上次识别的 PID 或 30 秒宽限期，避免状态条闪烁。

更新触发：

- `TelemetryWorkspace.PropertyChanged` 的 `BatteryPercent` / `IsCharging`。
- `BladeWorkspace.PropertyChanged` 的 `PerformanceModeText`。
- `Devices.CollectionChanged` / 设备刷新完成后重新判断是否显示。
- 设置开关变化。

不要新增定时器轮询硬件。现有 2 秒遥测已经足够；状态条只做 `InvalidateRect`。

显示规则：

- 开关关闭 -> 隐藏。
- 没有 Blade 设备且 30 秒宽限期已过 -> 隐藏。
- `BatteryPercent is null` -> 隐藏。
- 有电量、没有性能模式文本 -> 只显示电量和充电符号。
- 任务栏方向不是底部 -> 隐藏（第一版）。
- 任务栏可用宽度不足 -> 先去掉性能模式文本；仍不足 -> 隐藏。

## 6. 窗口行为

### 6.1 尺寸与位置

- 高度：目标 32 DIP；物理高度 = min(任务栏客户区高度 - 8 × DPI scale, 40 × DPI scale)，最小 24 × DPI scale。
- 宽度：按内容测量（DIP），最小 96 DIP，最大 200 DIP；转成物理像素时乘 DPI scale。
- 位置：`TrayNotifyWnd` 左边缘换算到任务栏客户坐标后再往左 8px；没有托盘窗口时用任务栏客户区右边缘减 8px。
- y：在任务栏客户区内垂直居中。
- 圆角：`CreateRoundRectRgn(0, 0, w + 1, h + 1, h, h)`，用 `SetWindowRgn` 裁成胶囊形。
- 创建时先隐藏，改好 `WS_CHILD` 再 `SetParent`，避免顶层窗口在屏幕角落闪一下。

### 6.2 绘制

- 背景：深色 `#202020`，浅色 `#F3F3F3`；先做不透明，避免分层子窗口的兼容问题。
- 主题：读 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme`（任务栏跟随系统主题，不读 `AppsUseLightTheme`）。
- 主文字：电量百分比，白色/黑色，13 DIP 左右，Semibold。
- 充电符号：雷蛇绿 `#44D62C` 的小闪电；未充电时不画。
- 性能模式：次要文字，灰色；空间不足时省略。
- 字体：`Segoe UI Variable Text`，中文回退 `Microsoft YaHei UI`。
- 绘制 API：`FillRect` 背景、`DrawTextW` 文字、`Polygon` 或两段 `DrawText` 画闪电；不引入 `System.Drawing.Common`。
- 每次数据变化只 `InvalidateRect(hwnd, null, false)`，不要整窗 `SetWindowPos`。

### 6.3 点击

- `WM_LBUTTONUP` 触发 `Activated` 事件。
- App 层优先用 Blade 的 `DeviceRowViewModel` 调 `window.RequestDeviceNavigation(bladeRow)`；没有 Blade row 时回退 `window.RequestNavigation("devices")`。不要新造 `"blade"` 页面 key。
- 不抢焦点：`WS_EX_NOACTIVATE`；不进入 Alt+Tab。
- 悬停用 native tooltip control（`TOOLTIPS_CLASS` + `TTF_SUBCLASS`）显示 `87% · 充电中 · 性能`；不要自绘浮层。

### 6.4 生命周期

- 每 2 秒检查 `IsWindow(taskbarHwnd)`。
- 任务栏句柄失效（Explorer 重启）后，销毁旧窗口、重新 `FindWindow`、重新 `SetParent`、重新定位。
- `WM_DISPLAYCHANGE` / `WM_SETTINGCHANGE` / `WM_DPICHANGED_AFTERPARENT` 后重新计算尺寸、字体和位置。
- 任务栏自动隐藏、全屏应用：子窗口跟着任务栏走，不需要额外隐藏逻辑；验收时确认即可。
- 开始菜单、通知中心、Alt+Tab：子窗口属于任务栏，正常应被系统浮层盖住；验收时确认不穿透、不盖住。
- 退出时销毁窗口；不要留下孤儿 HWND。

### 6.5 失败处理

- `SetParent` 返回 NULL 既可能是成功（原父为桌面）也可能是失败；先 `SetLastError(0)`，调用后再用 `GetParent(hwnd) == taskbar` 判断。
- `SetParent` 失败、`GetParent` 不是任务栏、窗口创建失败：写一条英文诊断日志，隐藏功能，不影响主程序。
- 不要弹窗、不要阻塞启动、不要重试死循环。
- 诊断日志示例：`taskbar-status: attach failed (SetParent error 5)`。

## 7. 设置页

`AppBehaviorSettings` 增加：

```csharp
public bool TaskbarStatusEnabled { get; set; }
```

默认 `false`，避免未验证功能默认出现在所有用户任务栏上。

设置页在“托盘菜单外设电量”卡片后面加一张卡：

- 标题：`任务栏状态条`
- 描述：`在任务栏显示笔记本电量和当前性能模式；只在检测到 Blade 电池时显示。`
- 开关：`TaskbarStatusToggle`
- 切换后保存 `behavior.json`，失败按现有 `TrayBatteryToggled` 模式回滚。
- 给 `MainWindow` 加 `SetTaskbarStatusEnabled(bool)` 回调并转给 App；`SettingsPage` 通过现有 `Host` 调用，立即创建/销毁状态条，不要要求重启。
- 装配点：`App.OnLaunched` 里 `InitializeTray(window, viewModel)` 之后创建 `TaskbarStatusPresenter`；退出清理时 Dispose，和 `_trayIcon` / `_trayMenuWindow` 同生命周期。

本地化：

- `zh-CN/Resources.resw` 和 `en-US/Resources.resw` 各加三条：标题、描述、开关自动化名称。
- Core/Windows 里不要出现中文诊断字符串。

## 8. 验收

### 8.1 自动检查

```powershell
dotnet build src\OpenSynapse.App\OpenSynapse.App.csproj -c Release -r win-x64
dotnet test tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj -c Release
```

- 0 警告、0 错误。
- 新增 `TaskbarStatusPolicyTests` 覆盖：
  - 无电池 -> 不显示。
  - 开关关闭 -> 不显示。
  - 87% + 充电 + 性能 -> 电量文本 `87%`；`FormatTooltip` 传入中文/英文占位文本时能正确拼接，Core 不硬编码语言。
  - 无性能模式 -> 只显示电量。
  - 宽度不足 -> `ShowPerformanceSegment` 返回 false。

### 8.2 真机检查

至少一台 Blade 14/16（Win11）和一台 Win10 22H2：

- [ ] 任务栏出现状态条，位置在托盘区左侧，不压图标。
- [ ] 电量变化后 2 秒内更新。
- [ ] 插拔电源，充电符号和 tooltip 正确变化。
- [ ] 切换性能模式，文本立即变化。
- [ ] 点击状态条打开 Blade 页面。
- [ ] 设置开关关闭后立即消失；重新打开立即出现；重启后保持。
- [ ] 任务栏自动隐藏时，状态条跟着隐藏；任务栏滑出时恢复。
- [ ] 全屏游戏/视频时，状态条不浮在最上层。
- [ ] 打开开始菜单、通知中心、Alt+Tab，状态条不盖住系统浮层。
- [ ] 100% / 150% / 200% DPI 下文字不模糊、不裁切。
- [ ] Explorer 重启后 3 秒内恢复。
- [ ] 普通权限和管理员权限各跑一次。
- [ ] 无 Blade 设备的桌面机不显示，且不报错。

### 8.3 UI 自动化

- 设置页开关用现有 WinUI UIA 测试走一遍。
- 状态条是 Win32 窗口，不要求完整 UIA Provider；至少：
  - `SetWindowText` 写入完整状态文本，`GetWindowText` 能读到。
  - 用 `PrintWindow` 截图做视觉检查。
  - 用 `GetWindowRect` / `GetParent` 验证确实挂在 `Shell_TrayWnd` 下。

## 9. 风险

| 风险 | 影响 | 处理 |
|---|---|---|
| 管理员进程 `SetParent` 到 Explorer 被 UIPI/安全软件拦 | 状态条不可见或点不到 | Spike 0 先验证；失败走 Plan B |
| Explorer 重启销毁子窗口 | 状态条消失 | 2 秒 `IsWindow` 轮询 + 重建 |
| Win11 24H2 任务栏 XAML 改动 | 子窗口被盖住或位置错 | 真机验收；必要时重试 z-order |
| 多显示器/非底部任务栏 | 位置错误 | 第一版只支持主屏底部，其他情况隐藏 |
| DPI awareness 不一致 | `SetParent` 失败或尺寸错 | 创建前对齐 `GetWindowDpiAwarenessContext`，处理 `WM_DPICHANGED_AFTERPARENT` |
| GDI 文字观感不如 WinUI | 用户觉得丑 | 先做 1 台机器视觉验收；必要时再评估 DirectWrite/Win2D，不提前引入 |
| 任务栏图标太多，固定锚点被挤 | 压住图标 | 宽度不足先去性能文本，再隐藏；不做 UIA 自动避让 |

## 10. 执行顺序

1. Spike 0：验证管理员/普通权限下的子窗口可见性和点击。0.5 小时。
2. Core：`TaskbarStatusSnapshot` + `TaskbarStatusPolicy` + 单测。0.5 天。
3. Windows：`TaskbarStatusWindow` + 原生绘制 + 任务栏生命周期。1–1.5 天。
4. App：`TaskbarStatusPresenter` + `App.xaml.cs` 装配 + 设置页/资源。0.5 天。
5. 真机验收 + 截图 + 修复。0.5 天。
6. 只提交本功能文件；不要混入 Lighting Studio 的未提交改动。

## 11. 不要做

- 不要 `SetParent` WinUI 3 主窗口。
- 不要为这个功能新增 WPF/Win2D/独立进程。
- 不要用 UIA 枚举任务栏图标做自动避让；固定托盘锚点已经够用。
- 不要新增硬件轮询；复用 `TelemetryWorkspace`。
- 不要猜图标字体码点；电量图标用 GDI 画，充电符号用简单多边形。
- 不要为了“看起来完整”把副屏、顶部/侧边任务栏全部塞进第一版。

## 12. 当前工作树注意事项

当前 `main` 有大量 Lighting Studio 未提交改动，至少包括：

- `src/OpenSynapse.App/App.xaml.cs`
- `src/OpenSynapse.Core/Storage/AppBehaviorSettings.cs`
- `src/OpenSynapse.App/Views/SettingsPage.xaml`
- `src/OpenSynapse.App/Views/SettingsPage.xaml.cs`
- `src/OpenSynapse.App/Strings/zh-CN/Resources.resw`
- `src/OpenSynapse.App/Strings/en-US/Resources.resw`

实施时只暂存本功能相关的 hunk，禁止 `git add .`；不要回滚或覆盖 Lighting Studio 的改动。

本文所在目录已纳入版本库。提交时只暂存本文相关文件，禁止 `git add .`。