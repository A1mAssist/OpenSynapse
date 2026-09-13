<p align="center">
  <img src="src/OpenSynapse.App/Assets/OpenSynapseLogo.svg" width="112" height="112" alt="OpenSynapse Logo">
</p>

<h1 align="center">OpenSynapse</h1>

<p align="center">在 Windows 11 上管理受支持的 Razer 硬件，不用让雷云一直留在后台。</p>

<p align="center">
  <a href="https://github.com/A1mAssist/OpenSynapse/releases/latest"><img alt="最新版本" src="https://img.shields.io/github/v/release/A1mAssist/OpenSynapse?style=flat-square"></a>
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-44D62C?style=flat-square"></a>
  <img alt="Windows 11 x64" src="https://img.shields.io/badge/Windows-11%20x64-0078D4?style=flat-square">
</p>

<p align="center">简体中文 · <a href="README.md">English</a></p>

OpenSynapse 会先识别已经连接的设备，再按具体 USB 设备和 HID 端点显示能够确认的控制项。Blade 和 Viper 继续使用各自的产品专用实现，OpenRazer 设备走按能力判断的通用页面。

> 当前版本 `v1.3.5` · Windows 11 x64 · 未签名

完整版本记录见 [CHANGELOG.md](CHANGELOG.md)。

## 支持范围

### 产品专用设备

| 设备 | USB VID:PID | 可用功能 |
|---|---|---|
| Razer Blade 16 (2025) | `1532:02C6` | 遥测、键盘灯光、性能、风扇、显示、电池、Fn/M3/M4/M5 |
| Razer Viper V3 HyperSpeed | `1532:00B8` | 电量、DPI、轮询率、休眠、电池类型、板载映射 |

Blade 灯光支持关闭、静态、呼吸、光谱循环、波浪、火焰、响应、涟漪、音频律动、环境感知、色轮、星光和双色潮汐。自定义性能模式可以调整 CPU Boost、GPU Boost 和 Max Fan。风扇控制、充电上限、内屏刷新率、触控板以及经过验证的 Fn 功能仍在 Blade 页面中管理。系统遥测会在 Windows 能够读取相应传感器时展示 CPU 和当前活动 GPU 的温度、功耗、负载与频率。

Viper 页面支持 `125 / 500 / 1000 Hz` 轮询率、`100` 至 `30000` 的 X/Y DPI、最多 5 档 DPI，以及固定 Profile 1 的 Normal/HyperShift 板载映射。鼠标无法可靠读回电池类型，因此该设置由用户选择。低电量阈值继续保持只读。

### OpenRazer 设备

1.2.1 加入了基于固定 OpenRazer 设备目录的通用支持。目录包含使用标准 91-byte Razer HID 报告的鼠标、键盘、笔记本和配件。软件只为当前连接的设备创建页面，后端确认端点和事务以后，对应控件才会出现。

不同型号可能提供设备信息、电池状态、轮询率、DPI 与 DPI 档位、省电设置、低电量警告、灯光亮度与效果、分区 LED、矩阵灯光、滚轮设置、键轴优化、Fn 优先行为或 HyperPolling 接收器控制。

设备出现在目录中，不代表每个控制项都已在该型号上完成真机验证。端点无法解析或正被占用时，页面会保持只读，直到重新扫描。OpenSynapse 不会按产品名称猜测能力，也不会改用另一个 HID 接口强行写入。

### Kraken 灯光

以下 Kraken USB 耳机使用独立的 37-byte Output Report 通道。OpenSynapse 只管理它们的灯光。

| 型号 | USB PID |
|---|---|
| Kraken 7.1 | `0501`、`0506` |
| Kraken 7.1 Chroma | `0504` |
| Kraken 7.1 V2 | `0510` |
| Kraken Tournament Edition | `0520` |
| Kraken Ultimate | `0527` |
| Kraken Kitty V2 | `0560` |

软件会按匹配到的设备定义显示灯效。不同型号可能支持关闭、静态、光谱、单色/双色/三色呼吸和自定义。这里不包含音频、麦克风、EQ 或 THX 控制。

### Chroma REST

兼容的游戏和外部程序可以向 `127.0.0.1:54235` 提交静态、`CUSTOM`、`CUSTOM_KEY` 和 `CUSTOM2` 键盘灯光帧。灯光帧使用经过验证的 Blade 16 实体键位，外部控制结束后会恢复当前选择的灯效。原生 Chroma SDK 和 `RzChromaConnectAPI` DLL 接口尚未实现。

## 界面预览

| 概览 | 设备 |
|---|---|
| ![OpenSynapse 中文概览页面](screenshots/overview-zh.png) | ![OpenSynapse 中文设备页面](screenshots/devices-zh.png) |

| Blade 控制 | 设置 |
|---|---|
| ![OpenSynapse 中文 Blade 控制页面](screenshots/blade-zh.png) | ![OpenSynapse 中文设置页面](screenshots/settings-zh.png) |

## 安装

从 [GitHub Releases](https://github.com/A1mAssist/OpenSynapse/releases/latest) 下载其中一种安装文件。

- `OpenSynapse-1.3.5-win-Setup.exe` 安装到当前用户目录并支持自动更新。
- `OpenSynapse-1.3.5-win-Portable.zip` 解压后即可运行。

扫描设备前请先退出 Razer Synapse，避免两个程序争用同一个 HID 端点。OpenSynapse 会报告访问失败，不会自行结束雷云进程。

发布文件没有代码签名，第一次运行时 Windows SmartScreen 可能显示警告。

### 驱动要求

应用本身不依赖 Razer Synapse、AppEngine 或 `mapping_engine.dll`。Blade 的 Fn、M3、M4 和 M5 功能仍依赖 Product 710 Razer 设备驱动，使用前需要安装 Razer 提供的对应驱动包。

## 当前不包含

- 固件更新、Razer 账号、云服务和 Chroma Studio。
- THX Spatial Audio、EQ、音量均衡、语音清晰度和高级宏编辑器。
- AMD Curve Optimizer、GPU MUX 和未经验证的硬件写入。
- ARGB Controller，以及缺少 Windows 传输实现的旧式固定报告设备。

## 构建

需要 Windows 11 x64、[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 Windows SDK `10.0.26100`。

```powershell
dotnet restore OpenSynapse.slnx
dotnet build OpenSynapse.slnx -c Release
dotnet test OpenSynapse.slnx -c Release --no-build
dotnet build src/OpenSynapse.App/OpenSynapse.App.csproj -c Release -p:Platform=x64
```

本地构建完成后可以这样启动。

```powershell
& '.\src\OpenSynapse.App\bin\x64\Release\net10.0-windows10.0.26100.0\OpenSynapse.App.exe'
```

## 参与贡献

提交代码或文档前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。生成的二进制文件、日志、抓包、逆向工程目录、凭据和本机配置不应进入 Git。

## 许可

OpenSynapse 采用 [MIT License](LICENSE)。第三方组件和随附资源继续遵循各自的许可证与分发条款。

协议实现参考了 [OpenRazer](https://github.com/openrazer/openrazer)、[OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) 及其他公开实现。OpenSynapse 与 Razer Inc. 没有隶属或认可关系，Razer 及相关产品名称是其各自所有者的商标。

Made with ❤ in C# by [A1mAssist](https://github.com/A1mAssist).
