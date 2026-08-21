# OpenSynapse

[English](README.en.md) | 简体中文

OpenSynapse 是一款面向 Windows 11 的轻量级 Razer 设备控制工具。它可以在不让雷云常驻的情况下，管理已验证设备的灯光、性能和常用设置。

当前版本为 `1.0.2`，只支持经过实机验证的具体设备。OpenSynapse 不会根据相近型号猜测协议，也不会向未知设备发送控制命令。

## 支持设备

| 设备 | USB VID:PID | 状态 |
|---|---|---|
| Razer Blade 16 (2025) | `1532:02C6` | 已支持 |
| Razer Viper V3 HyperSpeed | `1532:00B8` | 已支持 |

## 主要功能

### Blade 16 (2025)

- 读取 CPU、GPU、内存、磁盘、风扇和设备状态。
- 调整键盘亮度和 13 种键盘灯效：关闭、静态、呼吸、光谱循环、波浪、火焰、响应、涟漪、音频律动、环境感知、色轮、星光和双色潮汐。
- 切换性能模式，并在 Custom 模式下调整 CPU Boost、GPU Boost 和 Max Fan。
- 设置 `50%` 至 `80%` 的充电上限，或关闭限制。
- 调整 Windows 内置屏支持的刷新率，切换触控板。
- 后台处理已验证的 Fn 组合键、M3 游戏模式、M4 性能模式和 M5 麦克风静音指示灯。
- 只读显示面板模式、SKU、Local Dimming 等平台状态。

### Viper V3 HyperSpeed

- 读取电量、低电量阈值、轮询率、当前 DPI、休眠时间和 DPI 档位。
- 调整 `125 / 500 / 1000 Hz` 轮询率。
- 调整 `100..30000`、步进 `50` 的 X/Y DPI，配置最多 5 档 DPI。
- 读取并编辑固定 Profile 1 的 Normal / HyperShift 板载映射。
- 支持关闭、鼠标键、键盘按键和双击等已验证映射动作。

低电量阈值仅展示，不提供写入。当前设备不支持 `2000 / 4000 / 8000 Hz` HyperPolling。

## 界面预览

![OpenSynapse 中文 Viper 配置页](screenshots/viper-mappings-zh.png)

## 安装与运行

1. 从 [Releases](https://github.com/A1mAssist/OpenSynapse/releases) 下载最新的 `OpenSynapse-win-Setup.exe`。
2. 运行安装包；应用安装到当前用户目录，不需要管理员权限。
3. 便携版仍可使用，但旧压缩包不支持应用内自动更新。

首次探测设备时建议退出 Razer Synapse。两者可能争用同一个 HID 控制通道；OpenSynapse 会报告访问失败，但不会结束 Synapse 进程。

OpenSynapse 不需要 Razer Synapse、AppEngine 或 `mapping_engine.dll`。Blade 的 Fn、M3、M4 和 M5 功能仍依赖 Product 710 的 Razer 设备驱动；卸载 Synapse 时请保留这些驱动。驱动不属于 OpenSynapse，仓库和发布包均不分发。

## 明确不在当前范围内

- 固件更新、Razer 账号和云服务。
- THX Spatial Audio、EQ、音量均衡和语音清晰度。
- Chroma Studio、高级宏编辑器、GPU MUX 和 AMD Curve Optimizer。
- 未经验证的电池策略写入，以及 Viper 低电量阈值、电池类型和 `2K / 4K / 8K` 轮询率写入。

## 从源码构建

需要 Windows 11 x64、.NET 10 SDK 和 Windows SDK `10.0.26100`。

```powershell
dotnet restore OpenSynapse.slnx
dotnet build OpenSynapse.slnx -c Release
dotnet build src/OpenSynapse.App/OpenSynapse.App.csproj -c Release -p:Platform=x64
```

运行本地构建：

```powershell
& '.\src\OpenSynapse.App\bin\x64\Release\net10.0-windows10.0.26100.0\OpenSynapse.App.exe'
```

生成安装包、便携包和 Velopack 更新资产：

```powershell
.\scripts\Publish-VelopackRelease.ps1 -Version 1.0.3
```

设置 `GITHUB_TOKEN` 后追加 `-Upload`，即可创建公开 GitHub Release 并上传更新源资产。

## 许可与致谢

项目代码采用 [MIT License](LICENSE)。第三方组件和随附资源遵循各自的许可证与分发条款。

协议实现参考并交叉验证了 [OpenRazer](https://github.com/openrazer/openrazer)、[OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) 及其他公开实现。OpenSynapse 与 Razer Inc. 无隶属或认可关系，Razer 及相关产品名称是其各自所有者的商标。

Made with ❤ in C# by [A1mAssist](https://github.com/A1mAssist)。
