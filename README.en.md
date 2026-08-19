# OpenSynapse

English | [简体中文](README.md)

OpenSynapse is a lightweight Razer device controller for Windows 11. It manages verified lighting, performance, and everyday settings without requiring Razer Synapse to remain running.

The current release is `0.1.0` and supports only the exact devices verified on hardware. OpenSynapse does not infer protocols from similar model names and does not send control commands to unknown devices.

## Supported devices

| Device | USB VID:PID | Status |
|---|---|---|
| Razer Blade 16 (2025) | `1532:02C6` | Supported |
| Razer Viper V3 HyperSpeed | `1532:00B8` | Supported |

## Features

### Blade 16 (2025)

- Read CPU, GPU, memory, storage, fan, and device status.
- Adjust keyboard brightness and use 13 keyboard effects: Off, Static, Breathing, Spectrum, Wave, Fire, Reactive, Ripple, Audio Meter, Ambient, Wheel, Starlight, and two-color Tidal.
- Select performance modes and configure CPU Boost, GPU Boost, and Max Fan in Custom mode.
- Set a charge limit from `50%` to `80%`, or disable the limit.
- Select a refresh rate supported by the Windows internal display and toggle the touchpad.
- Handle verified Fn media keys, display-brightness keys, M3 Gaming Mode, and the M5 microphone-mute indicator in the background.
- Show panel mode, SKU, Local Dimming, and other platform fields as read-only state.

### Viper V3 HyperSpeed

- Read battery level, low-battery threshold, polling rate, current DPI, sleep timeout, and DPI stages.
- Set `125 / 500 / 1000 Hz` polling.
- Set X/Y DPI from `100..30000` in steps of `50`, with up to five DPI stages.
- Read and edit Normal / HyperShift onboard mappings in fixed Profile 1.
- Use verified Off, mouse-button, keyboard-key, and double-click mapping actions.

The low-battery threshold is read only. This device does not support `2000 / 4000 / 8000 Hz` HyperPolling.

## Interface preview

![OpenSynapse English device page](screenshots/devices-en.png)

## Install and run

1. Download the latest x64 archive from [Releases](https://github.com/A1mAssist/OpenSynapse/releases).
2. Extract the complete archive. Do not move the executable by itself or remove the adjacent `resources` directory.
3. Run `OpenSynapse.exe` from the archive root.

Exit Razer Synapse before the first device scan. Both applications can contend for the same HID control channel. OpenSynapse reports access failures but never terminates the Synapse process.

Fn media keys and M3/M5 indicator synchronization require Razer AppEngine to be installed on the machine. `mapping_engine.dll` is a proprietary Razer component, so it is not redistributed in this repository or in release bundles. After installing the official Razer Synapse, OpenSynapse looks for:

```text
%ProgramFiles%\Razer\RazerAppEngine\app-*\CommonDLL\mapping_engine.dll
```

The rest of the application remains usable when that component is missing; the dependent background synchronization stays disabled.

## Out of scope

- Firmware updates, Razer accounts, and cloud services.
- THX Spatial Audio, EQ, volume leveling, and voice clarity.
- Chroma Studio, advanced macro editing, GPU MUX, and AMD Curve Optimizer.
- Unverified battery-policy writes, plus Viper low-battery threshold, battery type, and `2K / 4K / 8K` polling writes.

## Build from source

Building requires Windows 11 x64, the .NET 10 SDK, and Windows SDK `10.0.26100`.

```powershell
dotnet restore OpenSynapse.slnx
dotnet build OpenSynapse.slnx -c Release
dotnet build src/OpenSynapse.App/OpenSynapse.App.csproj -c Release -p:Platform=x64
```

Run the local build:

```powershell
& '.\src\OpenSynapse.App\bin\x64\Release\net10.0-windows10.0.26100.0\OpenSynapse.App.exe'
```

## License and acknowledgements

Project code is available under the [MIT License](LICENSE). Third-party components and bundled resources remain subject to their own licenses and distribution terms.

Protocol work references and cross-checks [OpenRazer](https://github.com/openrazer/openrazer), [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB), and other public implementations. OpenSynapse is not affiliated with or endorsed by Razer Inc. Razer and related product names are trademarks of their respective owners.

Made with ❤ in C# by [A1mAssist](https://github.com/A1mAssist).
