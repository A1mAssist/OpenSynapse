# Razer THX 原生 DLL 与 BlackShark V2 Pro 通道

证据等级：`static`（雷云中间件 JS + 官方 DLL 反编译）。没有 V2 Pro 实机，HID 写入未验证。

## 结论

1. 旋风黑鲨 V2 Pro（`1532:0528`）的 THX Spatial Audio 不是耳机 HID 命令，而是 THX APO + Windows 服务，`ThxV3Native.dll` 只是客户端。
2. 侧音和游戏/语音平衡是 HID 命令。**2026-10-29 复核修正**：0528 的 `rzDeviceType` 是 `AudioAvneraDeviceOdessaT1`，走 91 字节 feature；下文 64 字节 Valkyrie 通道属于 0576/0579/0A4E/0A51/101E/101F，不适用于 0528。
3. 因此“侧音只能靠原生 DLL”这句话只对 THX APO 成立；耳机硬件侧音和游戏/语音平衡可以直接做，0528 走现有 91 字节通道，Valkyrie 家族才需要 64 字节通道。

## 找出来的 DLL

`ThxV3Native_v1.1.42.1.dll`，来自 0528 中间件 manifest 的 `THXNativeDLLV3Common`：

```
url     https://app-assets.razer.com/files/synapse/devices/common/THX/ThxV3Native_v1.1.42.1.dll
size    3,351,240
sha256  5DE7CBBCDB0DDB70E5622D6570FB2DEBB5186AE1182E693D4F212E5DF1EC835A
```

本机分析副本：`artifacts/reverse-engineering/thx-v3/ThxV3Native_v1.1.42.1.dll`（hash 与 manifest 一致）。

THX 效果本体是另一个 41.6 MB 的 APO 安装包：

```
url     https://app-assets.razer.com/files/synapse/devices/1320/1.0.4.2/THX-0528-BlackSharkV2Pro2020-v3.2.3.0.exe
size    41,619,992
sha256  D886EEF784E9118E7718AD2EA4F621C416178B7EA64CBBE975896A644F3CF446
```

预置包：`THX-0528-Presets-BlackSharkV2Pro2020-v3.2.18.0.exe`
（`sha256 0D81F36D7742078B0BD89A8FC79817630C73028651F2274842DDCA664DB7CB21`）。

## 安装包里装了什么

解包顺序：`THX-0528-...exe`（NSIS）→ Burn bundle → MSI → `packageZip` → `driver.zip`。

- `THXRTSVC.inf`：把 `VSSrv.exe`、`VSSrvInit.exe`、`VSHelper.exe` 装到 `System32`，注册软件组件
  `SWC\VEN_THX&PID_THXSVC`。
- `THXUSBAPO.inf`：匹配 `USB\VID_1532&PID_0528&MI_00`，给扬声器和麦克风端点挂
  `THXOutAPO-SSE2-v3.dll` / `THXMicAPO-SSE2-v3.dll`，并写 `PKEY_THX_DEVICE_TYPE_ID`、
  `PKEY_THX_CONFIG_PATH`（`%16419%\THX\usb\1532\0528\thx_spatial.conf.json`）。
- `THXRTAPO.inf`：APO 组件本体。

所以 THX 是三层：**端点 APO + `VSSrv` 服务 + 客户端 DLL**。缺任何一层都不会有实际音效；
`ThxV3Native.dll` 只负责第三层。中间件 manifest 里这个 APO 包的安装参数是 `/S /LAUNCH`，
Synapse 在设备初始化时会静默安装，用户不需要手动点。

## DLL 的形状

- 231 个导出，C API；依赖 `ole32`、`rpcrt4`、`ws2_32`、`iphlpapi`、`setupapi`，是典型的 COM/RPC + 服务客户端。
- 关键导出：`DeviceInitialize`、`DeviceTerminate`、`GetRenderDeviceList`、`SetRenderDevice`、`GetRenderSpatialProcessing`、`SetRenderSpatialProcessing`、`SetRenderPreset`、`SetRenderEQGains`、`SetCaptureSidetone`、`SetCaptureSidetoneLevel`、`GetCaptureSidetoneLevel`。
- 另有 C++ 接口 `THXV3Client`（`SetSpatialProcessingState`、`SetPreset`、`SetProcessingMode`、`SetListeningMode`、`SetCaptureSidetoneLevel` 等）和 `ITHXSettingsV3` / `IVSTHXSettings`。

Ghidra 反编译确认：

- `SetRenderSpatialProcessing(const char* containerId, bool enabled)` 把调用绑定到 `CThxV3Feature::SetRenderSpatialProcessing(int)`。
- `THXV3Client::SetSpatialProcessingState(int enabled, ITHXSettingsStatusCallback&)` 转发到 `this+8` 的 THX 服务客户端。
- 镜像内字符串包含 `C:\src\apo2\VSSettings\Src\THXV3...`，与 APO 端实现对应。

可复现：

```
D:\Tools\ghidra_12.1.2_PUBLIC_20260605\support\analyzeHeadless.bat ^
  artifacts\reverse-engineering\ghidra-thx ThxV3 ^
  -import artifacts\reverse-engineering\thx-v3\ThxV3Native_v1.1.42.1.dll ^
  -scriptPath artifacts\reverse-engineering\ghidra-scripts ^
  -postScript DumpThx.java
```

输出：`artifacts/reverse-engineering/thx-v3/ghidra-run.log`。

## 这意味着什么

- 只拷 DLL 没有用。必须先装 THX APO 安装包，让 `VSSrv` 服务和 APO 注册到音频端点，DLL 才能读到/写到对应 `containerId`。没有 APO 时，`USB\VID_1532&PID_0528&MI_00` 的端点不会挂 THX 效果，DLL 没有可控制的对象。
- OpenSynapse 现有的 THX 通道（`ThxAudioHelper` + `ThxAudioNativeClient`）是给 Blade 的 `ThxV4Native` 属性接口写的，导出面是 `GetProperty` / `SetProperty`；V3 是扁平 C API，不能直接复用。
- 要在应用里控制 V2 Pro 的 THX，需要：
  1. 检测 THX APO/服务是否已安装（没有就明确提示，不代装 41 MB 驱动包）；
  2. 新增 V3 的 P/Invoke 绑定（加载 Razer 已安装的 DLL，不随应用分发）；
  3. 用 `GetRenderDeviceList` / `SetRenderDevice` 找到耳机端点，再 `SetRenderSpatialProcessing`。
- 不建议把 Razer 的 APO 或 DLL 打包进 OpenSynapse：体积、驱动安装、UAC 和授权都不合适。

## 应用内入口

耳机音频页对 0528 / 0555 显示 THX 状态卡，逻辑与笔记本 THX 面板一致：

- `VSSrv` 未运行：显示“下载 THX 安装包”，打开对应型号的官方安装包：
  - 0528：`THX-0528-BlackSharkV2Pro2020-v3.2.3.0.exe`
  - 0555：`THX-0555-BlackSharkV2Pro2023-v3.2.3.0.exe`（41,618,480 字节，
    sha256 `8FB66381E7548F5C6DCDEA16ADB7289E8C368354F03B4074E83E8FC2F3B37D07`）
  安装包自己请求管理员权限。
- 服务已运行、`ThxV3Native.dll` 缺失或 hash 不符：显示“下载 THX 控制组件”，
  在应用内下载并校验 3.3 MB DLL 到 `%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Apps\Common\thxv3common`。
- 两者都在：显示“THX 组件已安装”。

只检测服务和 DLL，不检查 APO 是否绑定到 0528 的音频端点；这一点需要实机确认。

## 其他独立安装组件

扫描 264 个设备 manifest 的 `resourceManifest.resources`（430 条 `application/x-msdownload`），按组件分类：

| 组件 | 典型资源 | 是否影响已实现功能 | 处理 |
|---|---|---|---|
| THX APO | `APO1320` / `APO1365`（V2 Pro）、`APO13xx`（Nari / Kraken / Barracuda / Nommo / Leviathan） | 只有暴露 THX 控制的设备才需要 | V2 Pro 已加入口；其他型号暂不暴露 THX，不加 |
| THX 预置 | `THXV3APOPreset*` | APO 的预置数据 | 随 APO 安装，不单独加入口 |
| THX V4 游戏配置/补丁 | `THXV4GameProfiles-*` / `THXV4Patcher714-*` | 雷云 THX V4 配置，不影响我们的 HID/客户端调用 | 不加 |
| THX 虚拟音频驱动 | `THXVAD*` | 雷云音频路由 | 不用，不加 |
| Razer 0F1F 过滤驱动 | `DeviceDriver3871` | ARGB 320 字节逐灯 | 已加入口 |
| Razer 0F1B 过滤驱动 | `PID0f1bDrv` | Emote 8×8 矩阵（未实现） | 等矩阵 UI 做了再加 |
| 其他 `DeviceDriver*`（5 个） | Kraken V3 / HyperSense、Audio Mixer、HyperFlux V2、0282 | 雷云专用音频/充电功能 | 不加 |
| `NormalDriver_PID*`（253 个） | 鼠标/键盘/耳机的雷云过滤驱动 | OpenRazer/HID 路径不依赖 | 不加 |
| `RzAudioUtil` / `audCapNative` | 雷云音频效果 | 我们用 HID 读写 | 不加 |
| `PhilipsHueDLL` | 雷云 Hue 桥发现 | 我们直接走 Hue HTTP API | 不加 |
| Blade 超频/固件工具 | `RzIntelOverClock` / `RzAMDOverClock` / `Skip_FirmwareUpdater` | 我们用 HID/WMI 自己实现 | 不加 |

判断规则：只有 OpenSynapse 已经实现的功能真的依赖这个组件时，才加安装入口；
否则会变成点了没用的按钮。

## 0528 的 HID 通道

> 2026-10-29 复核：`bundles/0528/main.*.js` 的 `rzDeviceType` 是
> `AudioAvneraDeviceOdessaT1`，不是 `AudioValkyrie`；0528 走 91 字节 feature 通道。
> 下面这节 64 字节 Valkyrie 帧属于 0576/0579/0A4E/0A51/101E/101F，不要套到 0528。

原先的判断是 0528 的设备模块为 `AudioValkyrie`（已被上面的复核推翻，保留作历史记录）：

```
REPORT_ID              2
REPORT_LENGTH          64
AUDIO_TRANSACTION_ID   0x60
isDongleCommand        true
```

帧（`_createDataSendGeneral`）：

```
offset 0   0x02
offset 1   0
offset 2   0x60
offset 3-5 0
offset 6   header 长度 + payload 长度 + 1
offset 7-8 0
offset 9   0（dongle 命令）
offset 10  class
offset 11  command
offset 12  payload 长度
offset 13  payload
offset 62  XOR(offset 0..61)
offset 63  0
```

命令表（来自模块 `6555` 的常量，`AudioValkyrie` 直接调用）：

| 功能 | 请求 | 响应 | payload |
|---|---|---|---|
| 读侧音音量 | `0x19:0x00` | `0x19:0x01` | 无，响应 1 字节 |
| 写侧音音量 | `0x99:0x00` | `0x99:0x01` | 1 字节 |
| 读游戏/语音平衡 | `0xE5:0x00` | `0xE5:0x01` | 无，响应 1 字节 |
| 写游戏/语音平衡 | `0xE4:0x00` | `0xE4:0x01` | 1 字节 |
| 读 EQ 预设 | `0x13:0x00` | `0x13:0x01` | 无 |
| 写 EQ 预设 | `0x93:0x00` | `0x93:0x01` | 1 字节 |
| 读自定义 EQ（10 段） | `0x15:0x00` | `0x15:0x01` | 无，响应 10 字节 |
| 写自定义 EQ（10/11 段） | `0x95:0x00` | `0x95:0x01` | 10 或 11 字节 |
| 读麦克风 EQ 预设 | `0x16:0x00` | `0x16:0x01` | 无 |
| 写麦克风 EQ 预设 | `0x96:0x00` | `0x96:0x01` | 1 字节 |
| 读麦克风自定义 EQ | `0x17:0x00` | `0x17:0x01` | 无，响应 10 字节 |
| 写麦克风自定义 EQ | `0x97:0x00` | `0x97:0x01` | 10 字节 |

游戏/语音平衡的取值方向：JS 里有枚举可以定死含义——
`main.js` 的 `Game_100_Chat_0:0 … Game_0_Chat_100:20`，即 **index 0 = 全游戏，20 = 全语音**。
`AudioValkyrie` / `AudioValkyrieDongle` / `AudioBillieT2` 直接按 index 写字节；
`AudioBillieT1` 写的是 `20 - index`（读回同样 `20 - raw`）。
所以：设备走 Valkyrie 时我们的原样映射是对的，走 BillieT1 时是反的。
0576 加载哪个模块已确认：`bundles/0576/main.11fdb3ee2d36c7e8efa4.js` 里
`useFeature("rzDeviceType", "AudioValkyrie", {rzDeviceName:"AudioValkyrie",
hardwareEventParserType: AUDIO_VALKYRIE})`，所以 V3 Pro 走 Valkyrie，原样映射正确。
UI 侧已按这个结论加上「游戏 / 语音」两端标注，避免用户误判方向。

## 未验证

- 0528 是否也接受我们当前 91 字节 feature report 的 class `0x08` 命令。Synapse 自己走 64 字节通道；两条路径的关系没有硬件证据。
- 响应帧的 status 偏移和长度校验。实现 64 字节通道前需要从 `_sendCommandOut` 再确认一次，或用一次抓包校准。
- 侧音/游戏语音写入后的回读值与 UI 范围。
- 0555（V2 Pro 2023）不走 `AudioValkyrie`，而是 `AudioMxicDevice` 的 64 字节通道；帧和命令已从 0555 bundle 核对。

## 2026-10-26 耳机 THX 控制器实现

状态：static + 代码实现完成，**未实机验证**。本轮按雷云 JS 的调用形状和 DLL 导出表实现，不把“能编译”当成“设备已接受”。

### 两套协议的分派

| 型号 | PID | 原生版本 | 设备标识 | 侧音 |
|---|---|---|---|---|
| Nari / Nari Ultimate / Nari Essential | 051A / 051C / 051E | V3 | 音频端点 container ID | 有 |
| Kraken Kitty Edition / Kraken Ultimate | 0521 / 0527 | V3 | container ID | 有 |
| BlackShark V2 Pro 2020 / 2023 | 0528 / 0555 | V3 | container ID | 有 |
| Kraken V3 / V3 Pro / V3 HyperSense | 0549 / 052D / 0533 | V3 | container ID | 有 |
| Kraken Kitty V2 Pro | 0554 | V3 | container ID | 有 |
| BlackShark V2 HyperSpeed | 0565 / 056E | V3 | container ID | 有 |
| Kraken V4 / V4 Pro | 056B / 056C / 0567 / 0568 | V3 | container ID | 有 |
| Barracuda / Barracuda Pro / Barracuda 2.4 | 053C / 0583 / 0584 | V3 | container ID | 有 |
| BlackShark V3 Pro / V3 | 0576 / 0579 | V4 | razer_blacksharkv3pro_pc / razer_blacksharkv3_pc + headphones_game | 无 |
| Kraken Kitty V3 Pro | 0587 | V4 | razer_krakenkittyv3pro + headphones_game | 无 |
| Barracuda V2 Pro | 0593 | V4 | razer_barracudav2pro + headphones | 无 |

V3 走扁平导出：DeviceInitialize(containerId)、Get/SetRenderSpatialProcessing、GetRenderPresetList、SetRenderPreset、Get/SetRenderEQGains、Get/SetCaptureSidetone(Level)、Get/SetRenderNormalization/VocalClarity。

V4 走 property API：AddDevice({productId, endpointId})，然后读写 spatial_enabled、eq_enabled、eq_band_N_gain_db、volume_leveling_*、voice_clarity_*，预设组为 profile_presets。

### 组件

- V3 控制 DLL：ThxV3Native_v1.1.42.1.dll，3,351,240 字节，5DE7…835A。
- V4 控制 DLL：ThxV4Native_v1.1.3.0.dll（0576/0579）与 ThxV4Native_v1.1.2.0.dll（0587/0593），13,710,024 字节。
  - 1.1.3.0 0D88…D4D5
  - 1.1.2.0 8EAC…30F4
- 每个型号的 THX APO 安装包 URL 在 ThxHeadsetAdmission 内按 PID 固定；V4 另外提供 GameProfiles 更新器，0576/0579 还有 714 Patcher。
- 所有下载都校验长度和 SHA-256 后才落到 %LOCALAPPDATA%/Razer/RazerAppEngine/User Data/Apps/Common/thx{v3,v4}common。

### UI

耳机页的 THX 卡现在会：

1. 缺 V3 服务时给出官方安装包入口；
2. 缺原生 DLL 时给出应用内下载；
3. 组件齐全时显示空间音效、预设、10 段 EQ、音量标准化、人声清晰度；V3 额外显示侧音；
4. V4 型号额外显示 GameProfiles / Patcher 更新入口。

### 仍未验证

- HID 设备 container GUID 是否等于音频端点 container ID。若不等，V3 的 DeviceInitialize 会失败，需要改用 GetAudioDeviceListBySystem 按端点名匹配。
- V3 SetRenderEQGains 接受的 JSON 形状。当前先读后写，失败时只报错、不回滚；实机需要抓一次雷云请求确认是数组还是对象。
- V3 SetNodeFFIEvent 未调用。控制调用不依赖事件回调，但音量/状态通知不会推送。
- V4 服务名未固定，因此 V4 只以原生 DLL 作为准入；APO 未安装时会在首次读取时报错，卡片仍保留安装入口。
- 所有控件的实际音效、回读值和量纲（尤其侧音 1–3162 的线性映射）都未在耳机上验证。

## 2026-10-27 0555 MXIC 通道

`1532:0555` 的 2.4 GHz 接收器在 `MI_03&COL01` 上暴露供应商 HID 控制口，报告长度为 64 字节（input/output），不是耳机页原先查找的 91 字节 feature report。雷云对应该 PID 的模块是 `AudioMxicDevice`，协议与 0528 的 `AudioValkyrie` 不同。

请求帧（64 字节，`_createDataSend`）：

```
0      02                  report id
1      80
2      command.Length + data.Length + 5
5..6   50 41               请求同步字
7      08 / 0E             Razer / Remote
9      command.Length + data.Length
10..   command
...    data
```

普通命令前先发 Remote Mode：type `0E`，command `[E1]`，data `[01]`，无响应期望。响应输入报告以 `02` 开头，payload 从 byte 2 开始；子响应为 `50 49 <type> <control> ... <lenLow> <lenHigh>`，data 从子响应 byte 10 开始。实际输入报告可能先带一个 `4F` 状态字节，解析器两种布局都接受。

已接入的 0555 命令：

| 动作 | command | 期望头 | 数据 |
|---|---|---|---|
| 固件 | `02 00 00` | `02 01 18` | 24 字节，前 4 字节为版本 |
| 电量 | `21 00 00` | `21 01 01` | 0–100 |
| 充电 | `2A 00 00` | `2A 01 01` | 0 未充、1 充电、2 失败 |
| EQ 预设读 / 写 | `13 00 00` / `93 00 01` | `13 01 01` / `93 01 01` | 0 / 7 / 8 / 9 / 255 |
| EQ 频段读 / 写 | `15 00 00` / `95 00 0A` | `15 01 0A` / `95 01 01` | 10 字节 |
| 麦克风静音读 / 写 | `55 00 00` / `E9 00 01` | `55 01 01` / `E9 01 01` | 1 = 静音 |

0555 的麦克风增益是 0/1/2 三档，不是百分比；耳机页因此只开放静音，不显示原有百分比音量滑块。Bass、音频输入、AI 降噪和 ANC 不属于 MXIC 已核对命令，页面保持隐藏。THX 仍按 0555 的 APO + VSSrv 逻辑显示安装入口。

状态：协议帧和命令来自 middleware bundle，单元测试覆盖帧布局、状态前缀、多子响应匹配和预设失败回滚；**未实机验证**。
