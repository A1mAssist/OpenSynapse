<p align="center">
  <img src="src/OpenSynapse.App/Assets/OpenSynapseLogo.svg" width="112" height="112" alt="OpenSynapse logo">
</p>

<h1 align="center">OpenSynapse</h1>

<p align="center">Control supported Razer hardware on Windows 11 without keeping Razer Synapse open.</p>

<p align="center">
  <a href="https://github.com/A1mAssist/OpenSynapse/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/A1mAssist/OpenSynapse?style=flat-square"></a>
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-44D62C?style=flat-square"></a>
  <img alt="Windows 11 x64" src="https://img.shields.io/badge/Windows-11%20x64-0078D4?style=flat-square">
</p>

<p align="center"><a href="README.zh-CN.md">简体中文</a> · English</p>

OpenSynapse reads connected devices first, then exposes only the controls resolved for that exact USB device and HID endpoint. Product-specific Blade and Viper support remains separate from the capability-driven OpenRazer path.

> Current release `v1.3.6` · Windows 11 x64 · unsigned

See [CHANGELOG.md](CHANGELOG.md) for release history.

## What it supports

### Product-specific devices

| Device | USB VID:PID | Available controls |
|---|---|---|
| Razer Blade 16 (2025) | `1532:02C6` | Telemetry, keyboard lighting, performance, fans, display, battery, Fn/M3/M4/M5 |
| Razer Viper V3 HyperSpeed | `1532:00B8` | Battery, DPI, polling, sleep timeout, battery type, onboard mappings |

Blade lighting includes Off, Static, Breathing, Spectrum, Wave, Fire, Reactive, Ripple, Audio Meter, Ambient, Wheel, Starlight, and two-color Tidal. Custom performance mode exposes CPU Boost, GPU Boost, and Max Fan. Fan control, charge limits, internal-display refresh rates, the touchpad, and verified Fn behavior remain on the Blade page. System telemetry displays CPU and active-GPU temperature, power, load, and clock data when the corresponding Windows sensor is available.

The Viper page supports `125 / 500 / 1000 Hz` polling, X/Y DPI from `100` to `30000`, up to five DPI stages, and Normal/HyperShift mappings in fixed Profile 1. Battery type is selected by the user because the mouse does not provide a reliable readback value. The low-battery threshold remains read only.

### OpenRazer devices

Version 1.2.1 adds capability-driven support based on a pinned OpenRazer device catalog. The catalog contains mice, keyboards, laptops, and accessories that use the standard 91-byte Razer HID report. OpenSynapse creates a page only for connected devices and shows a control only when the backend resolves its endpoint and transaction.

Depending on the connected model, the page may provide device information, battery state, polling rate, DPI and DPI stages, power saving, low-battery warning, lighting brightness and effects, per-zone LEDs, matrix lighting, scroll-wheel settings, keyswitch optimization, Fn-primary behavior, or HyperPolling receiver controls.

Catalog presence does not mean every control has been tested on every model. An unresolved or busy endpoint stays read only until the next scan. OpenSynapse does not guess support from a product name or send a write through another HID collection.

### Kraken lighting

The following Kraken USB headsets use a separate 37-byte Output Report path. OpenSynapse supports their lighting only.

| Model | USB PID |
|---|---|
| Kraken 7.1 | `0501`, `0506` |
| Kraken 7.1 Chroma | `0504` |
| Kraken 7.1 V2 | `0510` |
| Kraken Tournament Edition | `0520` |
| Kraken Ultimate | `0527` |
| Kraken Kitty V2 | `0560` |

Available effects are read from the matched device definition. Models may expose Off, Static, Spectrum, one-, two-, or three-color Breathing, and Custom. Audio, microphone, EQ, and THX controls are not included.

### Chroma REST

Compatible games and integrations can send static, `CUSTOM`, `CUSTOM_KEY`, and `CUSTOM2` keyboard frames to `127.0.0.1:54235`. Frames use the verified Blade 16 key layout, and OpenSynapse restores the selected lighting effect after external control ends. The native Chroma SDK and `RzChromaConnectAPI` DLL interface are not implemented.

## Screenshots

| Overview | Devices |
|---|---|
| ![OpenSynapse English overview page](screenshots/overview-en.png) | ![OpenSynapse English devices page](screenshots/devices-en.png) |

| Blade controls | Settings |
|---|---|
| ![OpenSynapse English Blade controls](screenshots/blade-en.png) | ![OpenSynapse English settings page](screenshots/settings-en.png) |

## Install

Download one of these files from [GitHub Releases](https://github.com/A1mAssist/OpenSynapse/releases/latest).

- `OpenSynapse-1.3.6-win-Setup.exe` installs for the current user and supports automatic updates.
- `OpenSynapse-1.3.6-win-Portable.zip` runs without installation.

Exit Razer Synapse before scanning devices so both applications do not contend for the same HID endpoint. OpenSynapse reports access failures and does not terminate Synapse itself.

The release is not code signed. Windows SmartScreen may show a warning on first launch.

### Driver requirements

The application does not require Razer Synapse, AppEngine, or `mapping_engine.dll`. Blade Fn, M3, M4, and M5 support still depends on the Product 710 Razer device drivers. Install the matching driver package from Razer before using those functions.

## Not included

- Firmware updates, Razer accounts, cloud services, and Chroma Studio.
- THX Spatial Audio, EQ, volume leveling, voice clarity, and advanced macro editing.
- AMD Curve Optimizer, GPU MUX controls, and unverified hardware writes.
- ARGB Controller and legacy fixed-report devices whose Windows transport is unavailable.

## Build

Building requires Windows 11 x64, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), and Windows SDK `10.0.26100`.

```powershell
dotnet restore OpenSynapse.slnx
dotnet build OpenSynapse.slnx -c Release
dotnet test OpenSynapse.slnx -c Release --no-build
dotnet build src/OpenSynapse.App/OpenSynapse.App.csproj -c Release -p:Platform=x64
```

Run the local build with:

```powershell
& '.\src\OpenSynapse.App\bin\x64\Release\net10.0-windows10.0.26100.0\OpenSynapse.App.exe'
```

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting code or documentation. Keep generated binaries, logs, captures, reverse-engineering workspaces, credentials, and machine-local configuration out of Git.

## License

OpenSynapse is available under the [MIT License](LICENSE). Third-party components and bundled resources keep their own licenses and distribution terms.

The protocol implementation references [OpenRazer](https://github.com/openrazer/openrazer), [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB), and other public implementations. OpenSynapse is not affiliated with or endorsed by Razer Inc. Razer and related product names are trademarks of their respective owners.

Made with ❤ in C# by [A1mAssist](https://github.com/A1mAssist).
