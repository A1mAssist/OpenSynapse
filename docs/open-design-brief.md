# OpenSynapse · Open Design 设计 Brief

- 日期：2026-08-12
- 产品：OpenSynapse
- 设计工具：Open Design
- 平台：Windows 11 桌面应用
- 前端：WinUI 3 / Windows App SDK
- 后端：.NET 10 / Windows HID / Windows 性能 API
- 目标设备：Razer Blade 16 2025、Razer Viper V3 HyperSpeed

## 1. 给 Open Design 的直接要求

请为 OpenSynapse 设计一套真正的 Windows 11 WinUI 3 桌面应用界面，不要输出 Web、React、Tailwind 或营销落地页。

OpenSynapse 是一个本地运行的 Razer 设备控制器。它的目标是在不依赖账号、云端、商城、新闻、游戏库和遥测上传的前提下，提供可验证的设备控制能力。它不是 Razer Synapse 的视觉复制品，也不是一个电竞风格的网页仪表盘。

设计必须能直接翻译为 WinUI 3 XAML。输出应优先描述信息架构、布局、控件、状态和交互，而不是输出只能在浏览器中实现的 CSS 效果。

## 2. 项目真实架构

```text
OpenSynapse
├─ src/OpenSynapse.App
│  ├─ App.xaml
│  │                     全局主题、颜色、字体和控件样式
│  ├─ MainWindow.xaml
│  │                     WinUI 3 主窗口、侧边导航和页面布局
│  ├─ MainWindow.xaml.cs
│  │                     窗口生命周期、导航和 UI 事件路由
│  └─ ViewModels/MainViewModel.cs
│                        UI 状态、刷新循环、写入操作和错误状态
│
├─ src/OpenSynapse.Core
│  ├─ Devices            设备描述、快照、遥测契约
│  └─ Sensors            性能采样契约
│
├─ src/OpenSynapse.Windows
│  ├─ Devices/WindowsHidDiscovery.cs
│  │                     SetupAPI + hid.dll 设备发现
│  ├─ Devices/RazerDeviceTelemetryReader.cs
│  │                     Blade/Viper 协议读取和写入
│  ├─ Protocols
│  │                     91 字节 HID Feature Report 传输
│  └─ Sensors
│                        Windows 性能和 NVIDIA 遥测
│
├─ tests/OpenSynapse.Core.Tests
│                        协议、解析、边界和恢复测试
└─ tools/OpenSynapse.ProtocolProbe
                         只读协议探针，不接受任意 SET
```

这是一个单进程桌面应用，不是传统的 HTTP 前后端项目。WinUI 3 前端不直接打开 HID，也不构造 Razer 报文。

### 数据流

```text
WinUI 3 XAML
    ↓ Binding
MainViewModel
    ↓
IDeviceDiscovery / IRazerDeviceTelemetryReader / IPerformanceMonitor
    ↓
OpenSynapse.Windows
    ↓
SetupAPI / hid.dll / NVIDIA / Windows 性能 API
    ↓
DeviceSnapshot / RazerDeviceTelemetry / PerformanceSnapshot
    ↓
ViewModel 更新界面
```

### 当前技术环境

- .NET 10
- Windows App SDK `1.8.260710003`
- Windows 10 `19041` 以上
- x64
- Windows App SDK self-contained
- Unpackaged 应用，`WindowsPackageType=None`
- 当前没有数据库、云服务、账号系统或第三方 UI 框架
- 当前应用在 `App.OnLaunched` 中直接创建后端实现，没有 DI 容器

## 3. 当前已经存在的页面

### 概览

- CPU、GPU、内存和存储实时遥测
- GPU 温度、功耗、时钟和显存
- Blade 性能模式、风扇状态、充电上限的只读遥测
- Blade 和 Viper 的设备状态
- 最近一次刷新时间和错误信息

### 设备

- Blade 16 2025
- Viper V3 HyperSpeed
- VID/PID
- HID Usage Page / Usage
- Feature Report 长度
- 控制通道是否可打开
- 协议可用、部分可用或查询失败

### 灯光

- Blade 键盘亮度
- 亮度已经完成真机读取、写入、读回和恢复验证
- 高级灯效当前不应伪装成已完成能力

### 鼠标

- Viper 电量，只读
- 当前 X/Y DPI，可写
- `125 / 500 / 1000 Hz` 轮询率，可写
- 休眠时间，可写

### 诊断

- HID 控制通道状态
- 单项协议查询状态
- 最近一次错误
- Synapse 占用、访问拒绝、超时、设备断开
- 只有诊断页面使用协议和证据等级术语

## 4. 能力状态和真实边界

所有控件都必须能表达以下状态：

```text
Unavailable       未发现设备或接口不可用
Blocked           已知功能，但没有足够协议证据
Ready             已读取并允许操作
Busy              正在读写，禁止重复提交
Failed            查询或写入失败
ReadOnly          只能显示遥测，不能写入
Restoring         写入失败后正在恢复原值
```

### 当前已经验证可写

- Blade 键盘亮度
- Viper 当前 DPI
- Viper `125 / 500 / 1000 Hz` 轮询率
- Viper 休眠时间

### 当前只读或阻塞

- Blade 性能模式写入
- Blade 手动风扇和风扇曲线
- Blade 充电上限写入
- Blade 高级灯效和自定义矩阵
- Viper DPI 档位
- Viper 按键映射、Hypershift 和板载配置
- Viper Smart Tracking、抬起距离和校准
- 宏系统

未验证能力不能画成看起来可以点击的成品按钮。可以提供状态样式或设计规范，但当前产品界面不能虚构完成度。

## 5. 目标信息架构

```text
概览
├─ 系统遥测
├─ 已连接设备
└─ 最近状态

Blade 16
├─ 自定义
├─ 性能
├─ 显示
└─ 电池

灯光
├─ 系统灯光
└─ 快速灯效

Viper V3 HyperSpeed
├─ DPI / 轮询率
├─ 电源
└─ 设备状态

配置
├─ 本地配置
├─ 应用绑定
└─ 插电 / 电池策略

诊断
├─ HID 通道
├─ 能力证据
└─ 最近错误
```

当前代码只完成了概览、设备、灯光亮度、鼠标已验证设置和诊断。其余页面属于后续按协议证据逐项开放的产品结构，不得在设计稿中假设后端已经完成。

## 6. WinUI 3 特性使用要求

不要为了“使用特性”而堆控件，只使用真正服务于工作流的 WinUI 3 能力。

| 场景 | 应使用的 WinUI 3 能力 |
|---|---|
| 主窗口 | `NavigationView`、`AppWindow`、`ExtendsContentIntoTitleBar`、自定义拖拽区域 |
| Windows 11 材质 | `MicaBackdrop`，弹出层使用 Acrylic 风格 |
| 页面层级 | `NavigationView` 自适应展开/收起、`TabView`、`BreadcrumbBar` |
| 设备设置 | `ToggleSwitch`、`Slider`、`NumberBox`、`ComboBox`、`ColorPicker` |
| 高级选项 | `Expander`、`MenuFlyout`、`CommandBar` |
| 危险写入 | `ContentDialog` 确认，并展示当前值、目标值和恢复说明 |
| 错误与占用 | `InfoBar`，区分查询失败、访问拒绝和设备断开 |
| 首次引导 | `TeachingTip`，只解释不明显的设备状态 |
| 查询中 | `ProgressRing`，控件尺寸必须稳定 |
| 设备和诊断列表 | `ItemsRepeater`、`ListView`、`GridView` |
| 遥测展示 | `ProgressBar`，必要时使用轻量 Composition 绘制趋势线 |
| 页面切换 | `VisualStateManager` 和克制的 Composition 动效 |
| 快捷操作 | `KeyboardAccelerator`、`AccessKey` |
| 无障碍 | `AutomationProperties`、键盘焦点、High Contrast、Reduced Motion |
| 主题 | `ThemeResource`，支持深色、浅色和跟随系统 |
| 配置导入导出 | `FileOpenPicker`、`FileSavePicker` |
| 生命周期 | `AppWindow`、`DispatcherQueue`、单实例处理 |

不要新增 React、WebView、Tailwind 或第三方控件库来模拟以上能力。

## 7. 视觉方向

关键词：

```text
精密硬件控制台
工业仪器
安静可靠
高信息密度
Windows 11 Fluent
```

视觉要求：

- 深色为主，同时支持浅色和跟随系统
- 使用 Mica 背景、透明标题栏和窄边界
- 首屏清楚显示当前设备名称、连接状态和协议可用状态
- `Segoe UI Variable` 用于正文和标题
- `Cascadia Mono` 用于数值、PID、协议值和采样时间
- 绿色代表已验证可用
- 黄色代表只读、待验证、忙或被占用
- 红色代表失败或危险操作
- 青色代表 GPU 和系统遥测
- 不要电竞霓虹、紫黑渐变、巨型英雄区和过度发光
- 不要圆角卡片套卡片
- 不要为了填空间加入无意义图表
- 不要让禁用控件看起来像已经完成的功能
- 所有滑块、数值框和写入按钮必须有读取中、写入中、成功、失败恢复状态

## 8. 交付给开发的设计物

Open Design 输出应包括：

1. 页面信息架构图。
2. `1280 x 800` 和 `1440 x 900` 两个桌面断点。
3. 每个页面的 XAML 级布局结构。
4. 主题颜色、字体、间距和控件 token。
5. 控件状态矩阵：默认、悬停、聚焦、按下、禁用、加载、失败、只读。
6. 设备未连接、Synapse 占用、协议超时、设备断开和恢复状态。
7. 键盘导航、焦点顺序和 AutomationProperties 说明。
8. 所有交互文案使用用户语言，不在普通页面暴露协议实现细节。
9. 所有设计都能映射到现有 `MainViewModel` 数据绑定，不要求前端直接访问 HID。

## 9. 现有代码参考

- [主窗口 XAML](/D:/Workspaces/OpenSynapse/src/OpenSynapse.App/MainWindow.xaml)
- [全局资源和样式](/D:/Workspaces/OpenSynapse/src/OpenSynapse.App/App.xaml)
- [主视图模型](/D:/Workspaces/OpenSynapse/src/OpenSynapse.App/ViewModels/MainViewModel.cs)
- [窗口生命周期](/D:/Workspaces/OpenSynapse/src/OpenSynapse.App/MainWindow.xaml.cs)
- [设备能力矩阵](/D:/Workspaces/OpenSynapse/docs/device-capability-matrix.md)
- [协议能力台账](/D:/Workspaces/OpenSynapse/docs/protocol/capability-ledger.md)
