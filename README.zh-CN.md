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

OpenSynapse 会先识别已经连接的设备，再按具体 USB 设备和 HID 端点显示能够确认的控制项。当前可识别 389 个设备 ID，覆盖鼠标、键盘、笔记本、耳机、音箱、麦克风、鼠标垫、显示器、配件和网络灯光。

> 当前版本 `v1.6.0` · Windows 11 x64 · 未签名

完整版本记录见 [CHANGELOG.zh-CN.md](CHANGELOG.zh-CN.md)，英文版见 [CHANGELOG.md](CHANGELOG.md)。

## 支持范围

OpenSynapse 不只有少数型号。当前支持范围包含 **389 个可识别设备 ID**，按设备类型分为鼠标 136、键盘 96、笔记本 50、耳机 40、音箱 8、麦克风 4、鼠标垫 8、显示器 2、配件 44 和网络灯光桥 1。

**[查看完整设备与能力列表](docs/SUPPORTED_DEVICES.zh-CN.md)**，其中包含型号名称、USB PID、控制项以及明确的验证边界。

### 按能力显示控制项

OpenSynapse 会先识别已经连接的设备，再按具体 USB 设备和 HID 端点显示能够确认的控制项。不同型号可能提供设备信息、电池状态、轮询率、DPI 与 DPI 档位、省电设置、灯光、矩阵输出、滚轮设置、键轴优化、Fn 行为或 HyperPolling 接收器控制。

列表按设备类型分组，并逐个列出可识别的 PID，可直接对照设备管理器。设备出现在列表中，不代表每个控制项都已在该型号上完成真机验证。端点无法解析或正被占用时，页面会保持只读，直到重新扫描；OpenSynapse 不会按产品名称猜测能力，也不会通过未经验证的 HID 接口强行写入。

### 主要控制项

- **Blade：** 遥测、键盘灯光、性能、风扇、显示、面板模式、支持机型上的局部调光、电池、触控板、充电上限，以及经过验证的 Fn/M3/M4/M5。
- **显示：** 可选的“仅核显”模式通过 Windows PnP 禁用 NVIDIA 独显。它不是 MUX 切换；重新启用独显前，外接屏可能没有画面。机器级 MSI 安装会使用 LocalSystem 服务，切换时不再请求 UAC；当前用户安装和 Portable 仍会逐次请求 UAC。
- **Blade THX 音频：** 受支持的 Blade 16 和 Blade 14 机型提供空间音频、均衡器预设与频段、人声清晰度和扬声器音量标准化。Blade 设备页会打开可重复使用的 THX 控制窗口。带签名的雷蛇原生组件不随安装包分发；检测到缺失时，会提供经过校验的官方下载。
- **Viper：** 电量、DPI、轮询率、休眠、电池类型和板载映射。
- **耳机：** 音频页按设备和已安装组件提供 EQ、麦克风音量、侧音、ANC、THX 与游戏/语音平衡。Kraken 7.1 仅支持灯光；受支持机型缺少原生组件时提供 THX V3/V4 安装入口。
- **BlackShark V3 Pro：** 通过独立 HID 通道提供电量、充电、侧音、THX、主动降噪和游戏/语音平衡。暂不包含 EQ、麦克风增益、固件更新和指示灯模式。

### Chroma REST

参见 [Chroma 支持设备](docs/CHROMA_SUPPORTED_DEVICES.zh-CN.md) 查看当前设备与传输方式矩阵。

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

- `OpenSynapse-win.msi` 安装到 `Program Files`，并为所有用户注册 GPU 模式服务。安装时只请求一次管理员权限；之后切换“仅核显”不再弹 UAC。检测到旧的当前用户 Setup 安装时，会先保存配置和设置，卸载旧版后恢复数据，并把开机启动项迁移到新程序。
- `OpenSynapse-win-Setup.exe` 安装到当前用户目录并支持自动更新。它不安装机器级服务，切换“仅核显”仍会逐次请求 UAC。
- `OpenSynapse-win-Portable.zip` 解压后即可运行；使用“仅核显”时会请求 UAC。

MSI 服务以 `LocalSystem` 运行，使用 `Program Files` 内受保护的应用程序，只接受状态查询和固定的启用/禁用请求。控制管道只允许安装该应用的 Windows 账户访问。

配置、日志和设备清单保存在 `%LOCALAPPDATA%\A1mAssist\OpenSynapse`。用户数据与 Velopack 管理的目录分离，避免 MSI 升级或卸载时被清理。

扫描设备前请先退出 Razer Synapse，避免两个程序争用同一个 HID 端点。OpenSynapse 会报告访问失败，不会自行结束雷云进程。

发布文件没有代码签名，第一次运行时 Windows SmartScreen 可能显示警告。

### 驱动要求

核心设备控制不依赖 Razer Synapse、AppEngine 或 `mapping_engine.dll`。Blade 的 Fn、M3、M4 和 M5 功能仍依赖 Product 710 Razer 设备驱动。THX 音频需要匹配的雷蛇 THX 音频栈，以及 Blade 音频窗口提供的带签名原生组件。

## 当前不包含

- 固件更新、Razer 账号、云服务和 Chroma Studio。
- 除 Hybrid/Discrete 之外的 GPU MUX 模式，以及没有协议证据的硬件写入。
- 缺少 Windows 传输实现的旧式固定报告设备。

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

如果需要为新设备补齐支持，请阅读 [设备适配协作指南](docs/DEVICE_ADAPTATION.zh-CN.md)，并使用[设备支持 issue 模板](https://github.com/A1mAssist/OpenSynapse/issues/new?template=device-support.yml)。不要把 Razer 专有文件或用户设备隐私数据上传到公开 issue。

## 许可

OpenSynapse 采用 [MIT License](LICENSE)。第三方组件和随附资源继续遵循各自的许可证与分发条款。

协议实现参考了 [OpenRazer](https://github.com/openrazer/openrazer)、[OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) 及其他公开实现。OpenSynapse 与 Razer Inc. 没有隶属或认可关系，Razer 及相关产品名称是其各自所有者的商标。

Made with ❤ in C# by [A1mAssist](https://github.com/A1mAssist).
