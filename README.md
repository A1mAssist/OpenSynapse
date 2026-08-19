# OpenSynapse

[English](README.en.md) | 简体中文

OpenSynapse 是一款面向 Windows 11 的轻量级 Razer 设备控制工具。它不依赖雷云常驻，即可管理已验证设备的灯光、性能和常用设置。

当前版本为 `0.1.0`，仅支持经过实机验证的具体设备。OpenSynapse 不会根据相近型号猜测协议，也不会向未知设备发送控制命令。

## 支持设备

| 设备 | USB VID:PID | 支持状态 |
|---|---|---|
| Razer Blade 16 (2025) | `1532:02C6` | 已支持 |
| Razer Viper V3 HyperSpeed | `1532:00B8` | 已支持 |

其他 Razer 设备即使外观或型号接近，也不会自动套用以上协议。新增设备需要单独的设备清单、协议证据和读回验证。

## 主要功能

### Blade 16 (2025)

- 读取 CPU、GPU、内存、磁盘、风扇与设备状态。
- 调整键盘亮度。
- 使用 13 种键盘灯效：关闭、静态、呼吸、光谱循环、波浪、火焰、响应、涟漪、音频律动、环境感知、色轮、星光和双色潮汐。
- 调整机身 Logo 的关闭、常亮和呼吸模式。
- 切换性能模式，并在 Custom 模式下调整 CPU Boost、GPU Boost 和 Max Fan。
- 设置 `50%` 至 `80%` 的充电上限，或关闭限制。
- 调整 Windows 内置屏支持的刷新率。
- 使用 Windows 系统接口切换触控板。
- 后台处理已验证的 Fn 媒体键、屏幕亮度键、M3 游戏模式和 M5 麦克风静音指示灯。
- 调整已验证的游戏模式、启动动画和一次性充满开关；每项操作都受设备能力门禁保护并在失败时恢复。
- 只读显示面板模式、SKU、Local Dimming 等平台状态。

波浪、火焰和色轮采用已恢复的算法，但尚未完成与雷云的逐帧视觉一致性验证，因此不宣称 1:1 还原。

### Viper V3 HyperSpeed

- 读取电量、低电量阈值、轮询率、当前 DPI、休眠时间和完整 DPI 档位表。
- 调整 `125 / 500 / 1000 Hz` 轮询率。
- 调整 `100..30000`、步进 `50` 的 X/Y DPI。
- 调整休眠时间和最多 5 档 DPI 配置。
- 读取并编辑固定 Profile 1 的 Normal / HyperShift 板载映射。
- 支持关闭、鼠标键、键盘按键、双击、DPI 循环、播放/暂停、HyperShift、键盘 Turbo 和鼠标 Turbo 映射。

低电量阈值仅展示，不提供写入。当前设备不支持 `2000 / 4000 / 8000 Hz` HyperPolling。

### 应用功能

- 本地配置的新建、克隆、重命名、删除、导入和导出。
- 按前台应用或电源状态自动切换配置。
- 托盘驻留和当前用户开机启动。
- CPU、NVIDIA GPU、内存与磁盘实时遥测。
- 中文和英文界面。
- 本地滚动诊断日志：`%LocalAppData%\OpenSynapse\logs\opensynapse.log`。

## 截图

![平台状态](docs/screenshots/platform-status-zh.png)

![Viper 设备页](docs/screenshots/devices-en.png)

![板载映射](docs/screenshots/viper-mappings-zh.png)

![托盘菜单](docs/screenshots/tray-menu.png)

## 安装与运行

1. 从 [Releases](https://github.com/A1mAssist/OpenSynapse/releases) 下载最新的 x64 压缩包。
2. 完整解压压缩包，不要单独移动可执行文件，也不要删除旁边的 `resources` 目录。
3. 运行根目录中的 `OpenSynapse.exe`。

首次探测设备时建议退出 Razer Synapse。两者可能争用同一个 HID 控制通道；OpenSynapse 会报告访问失败，但不会结束 Synapse 进程。

Fn 媒体键、M3/M5 指示灯同步依赖本机已安装的 Razer AppEngine。仓库和发布包不再分发 Razer 的 `mapping_engine.dll`；缺少该组件时，应用仍可运行，但对应后台同步会保持禁用。

## 与 Razer Synapse 的关系

对上表中的日常功能，OpenSynapse 可以在不运行 Synapse 的情况下工作，但它不是雷云的完整替代品。以下功能明确不在当前范围内：

- 固件更新和 Razer 账号/云服务。
- THX Spatial Audio、EQ、音量均衡和语音清晰度。
- Chroma Studio 高级编辑器和宏编辑器。
- GPU MUX、AMD Curve Optimizer 和未经验证的电池策略写入。
- Blade 手动固定风扇和智能风扇曲线的生产写入。
- Viper 低电量阈值、电池类型和 `2K / 4K / 8K` 轮询率写入。

## 安全原则

硬件写入只有在当前设备路径读取成功后才会启用。写入流程会进行读回，并在失败或取消时恢复已确认状态。设备断开、访问被拒绝或读回不完整时，对应控件保持禁用。

协议状态和能力边界见 [设备能力矩阵](docs/device-capability-matrix.md)。硬件抓包和本机验证产物不随仓库分发；逆向资料也不会自动变成生产写入口。

## 从源码构建

需要 Windows 11 x64、.NET 10 SDK 和 Windows SDK `10.0.26100`。

```powershell
dotnet restore OpenSynapse.slnx
dotnet build OpenSynapse.slnx -c Release
dotnet build src/OpenSynapse.App/OpenSynapse.App.csproj -c Release -p:Platform=x64
dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj -c Release -p:Platform=x64
```

运行本地构建：

```powershell
& '.\src\OpenSynapse.App\bin\x64\Release\net10.0-windows10.0.26100.0\OpenSynapse.App.exe'
```

默认测试不会写入硬件。实机测试必须显式启用，并会在测试结束前读回和恢复原值：

```powershell
$env:OPENSYNAPSE_HARDWARE_TEST = '1'
dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj -c Release -p:Platform=x64 --filter 'Category=Hardware'
```

## 项目结构

- `src/OpenSynapse.App`：WinUI 3 界面、托盘、配置与用户交互。
- `src/OpenSynapse.Core`：设备、配置、显示和遥测契约。
- `src/OpenSynapse.Windows`：Windows HID、设备协议、灯光、音频和系统集成。
- `tests/OpenSynapse.Core.Tests`：协议字节、边界、读回、恢复与生命周期测试。
- `docs`：能力矩阵、前端契约和协议证据。

## 许可与致谢

项目代码采用 [MIT License](LICENSE)。第三方组件和随附资源遵循各自的许可证与分发条款。

协议实现参考并交叉验证了 [OpenRazer](https://github.com/openrazer/openrazer)、[OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) 及其他公开实现。OpenSynapse 与 Razer Inc. 无隶属或认可关系，Razer 及相关产品名称是其各自所有者的商标。

Made with ❤ in C# by [A1mAssist](https://github.com/A1mAssist).
