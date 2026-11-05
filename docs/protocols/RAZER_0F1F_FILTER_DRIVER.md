# Razer 0F1F 过滤驱动：ARGB 帧的 Windows 通道

证据等级：`static`（驱动反编译 + Razer 官方驱动包）。没有 0F1F 实机，写入路径未做硬件验证。

## 结论

Windows 能发 320 字节 ARGB 帧，不需要 WinUSB，也不需要替换驱动绑定。

1. Synapse 的 0F1F 中间件自带一个驱动包，安装后 `RzDev_0f1f.sys` 作为 **upper filter** 挂在
   `HID\VID_1532&PID_0F1F&MI_00` 和 `MI_01` 上，同时在 `USB\VID_1532&PID_0F1F&MI_02` 上作为 **lower filter**。
2. `MI_00` 的 INF 写了 `ControlDevice=1`、`DeviceType=3`，驱动因此创建设备
   `RZCONTROL\VID_1532&PID_0F1F&MI_00`，由 `RzCommon.sys` 当函数驱动。
3. `RzCommon` 把 IOCTL `0x88883010` 转给 `RzDev_0f1f`，后者直接调用
   `WdfIoTargetSendIoctlSynchronously(IOCTL_HID_SET_FEATURE = 0x000B0191, input, inputLength)`。
   代码里没有任何长度上限，320 字节原样进 HID 栈。
4. `0x88883014` 是 `IOCTL_HID_GET_FEATURE = 0x000B0192` 的透传。
5. 设备接口 GUID 是 `E3BE005D-D130-4910-88FF-09AE02F680E9`，INF 的 SDDL 对 `WD` 放开
   `GRGWGX`，普通用户可以直接 `DeviceIoControl`，不需要管理员。

## 帧格式

```
offset 0   report_id   通道 < 5 → 0x04；通道 >= 5 → 0x84
offset 1   channel_1
offset 2   channel_2   （与 channel_1 相同）
offset 3   pad = 0
offset 4   last_idx = 本帧颜色数 - 1
offset 5.. 315 字节 RGB，协议上限 105 颗灯（105 × 3 = 315）
```

总长固定 320 字节。旧文档里的“325 字节 / 320 字节 RGB”是错的：`struct razer_argb_report` 的
`color_data` 是 `u8[315]`，`static_assert(sizeof(...) == 320)`。

## 发送

```
CreateFileW(@"\\?\RZCONTROL#VID_1532&PID_0F1F&MI_00#...#{E3BE005D-D130-4910-88FF-09AE02F680E9}",
            0, FILE_SHARE_READ|FILE_SHARE_WRITE, ..., OPEN_EXISTING, 0, 0)
DeviceIoControl(handle, 0x88883010, frame, 320, nullptr, 0, &returned, nullptr)
```

代码落点：

- `src/OpenSynapse.Windows/Protocols/OpenRazerArgbFrame.cs` — 组帧
- `src/OpenSynapse.Windows/Protocols/RazerFilterControlTransport.cs` — IOCTL 透传
- `src/OpenSynapse.Windows/Devices/RazerFilterEndpointDiscovery.cs` — 按 PID 找 `RZCONTROL` 接口
- `src/OpenSynapse.Windows/Devices/OpenRazerSpecialLightingService.cs` — `SetArgbFrameAsync`
- `src/OpenSynapse.Windows/Devices/RazerChromaStandardService.cs` — 0F1F 进灯光页，`SetCustomFrameModeAsync`
- `src/OpenSynapse.App/ViewModels/RazerChromaViewModel.cs` + `MainWindow.xaml` — 「逐灯控制」卡片
- `src/OpenSynapse.App/ArgbStripSettings.cs` — 每端口灯珠数量持久化

## 应用内入口

0F1F 在场但 `RZCONTROL\VID_1532&PID_0F1F&MI_00` 不存在时，灯光页显示「ARGB 驱动」卡片，
按钮打开雷云官方驱动包：

```
https://app-assets.razer.com/files/synapse/devices/3871/1.0.0.0/PID0f1fDrv_v0.1.0.0.exe
size    2,786,648
sha256  850B55348774C1B9C8F8780AAA2061766CC1817CE5690F73859171D856888DAA
```

驱动是 `RzDev_0f1f.sys` upper filter，安装后需要重新枚举设备；卡片提示点“刷新”重新检测。

## 逐灯 UI

灯光页的「逐灯控制」卡片按 **6 端口 × 80 颗** 显示，每端口灯珠数量可改并保存在
`%LOCALAPPDATA%\A1mAssist\OpenSynapse\argb-led-count.txt`。点格子涂色后「应用」才发帧；
写帧前先发 `0F:02`（data size 12，args `00 00 08`，transaction `0x3F`）切 custom-frame 模式，
该步失败只记诊断日志，不阻断帧写入。

驱动没装时 `DiscoverAsync` 仍会列出设备，但 `Error` 会写明缺 `PID0f1fDrv`，`SetArgbFrameAsync` 直接报错，不会静默失败。

## 证据链

驱动包（从中间件 manifest 里拿的官方地址，sha256 与 manifest 一致）：

```
https://app-assets.razer.com/files/synapse/devices/3871/1.0.0.0/PID0f1fDrv_v0.1.0.0.exe
sha256 850B55348774C1B9C8F8780AAA2061766CC1817CE5690F73859171D856888DAA
```

解包内容：

```
Drivers\x64\RzDev_0f1f.sys          50584 bytes   1.0.0.68
Drivers\x64\RzDev_0f1f_Hid.inf      MI_00 / MI_01 upper filter，MI_00 带 ControlDevice=1
Drivers\x64\Lower\RzDev_0f1f_Dkm.inf  MI_02 lower filter
```

manifest 出处：`artifacts/razer-mw-all-bundles-v2/0F1F/manifest.json`
（`resourceName: DeviceDriver3871`）。

`RzCommon.sys`（本机已装，1.0.0.76）：

- IOCTL 分发 `FUN_140002e6c`：`0x88883004/18/1c/20/24/28/2c/30/34/38/3c/40/44/48/4c/80..98/c0/100/104/108/110/144`，
  未命中的码转给下层回调。
- 设备接口 GUID `E3BE005D-D130-4910-88FF-09AE02F680E9` 在驱动镜像内（文件偏移 `0x7d70`）。

`RzDev_0f1f.sys`：

- 分发 `FUN_140001bd4`：`0x88883010` → `FUN_140003604`，`0x88883014` → `FUN_140002438`。
- `FUN_14000354c` 组 `WDF_MEMORY_DESCRIPTOR`（type=1、buffer、length），IOCTL 分别是 `0x000B0191` / `0x000B0192`。
- 控制设备名模板 `RZCONTROL\VID_%04X&PID_%04X&MI_%02X`，见 `FUN_140003e08`。

## 未验证的点

- 控制设备由 `MI_00` 创建，所以 `SET_FEATURE` 落在 `MI_00` 这条 HID 栈上。OpenRazer 在 Linux 上用的是
  `wIndex 1`。如果 0F1F 的 320 字节 feature report 实际挂在 `MI_01`，这一步会返回
  `ERROR_INVALID_PARAMETER`，需要改走 `MI_01` 的其它通道。只有插上 0F1F 才能确认。
- 需要目标机器装过 Synapse（或单独跑过 `PID0f1fDrv` 安装包）。OpenSynapse 不代装 Razer 驱动。
- 通道号 0..5 对应中间件里的 6 个 Port（id 26..31），映射关系按 openrazer 的 `row_id` 处理，未实机核对。

## 复现

Ghidra 工程：`artifacts/reverse-engineering/ghidra-razer-drivers`（`RazerDrivers`，程序
`/driver-samples/RzCommon.sys`、`/driver-samples/RzDev_02c6.sys`、`/RzDev_0f1f.sys`）。
反编译输出：`artifacts/reverse-engineering/rzdev-0f1f-*.txt`、`rzcommon-decomp-a.txt`。

## 顺带：Seiren Emote（0F1B）

同一个中间件包机制给 0F1B 也发了驱动：
`https://app-assets.razer.com/files/synapse/devices/3867/1.0.0.3/PID0f1bDrv_v0.1.0.0.exe`，
里面是 `RzDev_0f1b.sys`，INF 匹配 `HID\Vid_1532&Pid_0f1b`，同样 `ControlDevice=1`、`DeviceType=3`。
所以 Emote 也有 `RZCONTROL` + `0x88883010` 这条 SET_FEATURE 通道——**发**不是问题。

Emote 的 8×8 帧现在走 91 字节 HID feature report，不依赖 `0x88883010`：
四行 `0x0F:0x03`（每行 16 像素）后跟一帧 `0x0F:0x02` latch。帧内容已接到界面，
支持逐像素画布、填充和清屏；显示方向、行序和 5 ms 行延时仍需实机确认。
该过滤驱动对当前实现不是必需组件，因此不加入口。
