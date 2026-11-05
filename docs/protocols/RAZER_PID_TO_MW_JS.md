# Razer PID → Middleware JS

从 Razer 设备的 USB PID 定位 Synapse product middleware，并从中提取 HID 命令定义的工作流。

## 适用范围

适用于把 product UI、middleware 和 HID 命令放在 `apps.razer.com/synapse/products/<productId>/mw/` 下的 Razer 产品。Blade 16 2025 已验证可从 CDN 解析。

不适用于：

- 没有 `mw` 目录、只提供驱动或 native DLL 的产品；
- 已经在本地安装 Synapse、需要看当前机器实际缓存版本的情况（优先用 `tools/Collect-RazerProductBundle.ps1`）；
- 只想调用 Windows 官方 `DisplayMuxDevice` 的场景。

本文只做只读提取，不执行下载到的 JavaScript，也不把 Razer 专有 JS 提交到仓库。

## 核心规则

Synapse 网页资源里的 product id 是 **USB PID 的十进制值**，不是十六进制字符串。

```text
USB PID:       0x02C6
十进制:        710
product 路径:  https://apps.razer.com/synapse/products/710/
```

已验证的映射：

| 设备 | USB VID:PID | 十进制 product id |
|---|---|---:|
| Razer Blade 14 (2023) | `1532:029D` | `669` |
| Razer Blade 14 (2024) | `1532:02B6` | `694` |
| Razer Blade 16 (2024) | `1532:02B7` | `695` |
| Razer Blade 18 (2024) | `1532:02B8` | `696` |
| Razer Blade 14 (2025) | `1532:02C5` | `709` |
| Razer Blade 16 (2025) | `1532:02C6` | `710` |
| Razer Blade 18 (2025) | `1532:02C7` | `711` |
| Razer Blade 16 (2023) | `1532:029F` | `671` |
| Razer Blade 15 Base (Early 2020) | `1532:0255` | `597` |
| Razer Viper V3 HyperSpeed | `1532:00B8` | `184` |

`Razer Blade 16` 这个名字本身不够唯一，必须结合年份、RZ09 型号或 USB PID。

## 1. 统一 PID 输入

用户可能提供三种形式：

```text
02C6                 -> 0x02C6 -> 710
1532:02C6            -> 0x02C6 -> 710
USB\VID_1532&PID_02C6... -> 0x02C6 -> 710
```

如果是本机设备，直接枚举 HID，不要依赖用户手输：

```powershell
Get-PnpDevice -PresentOnly |
  Where-Object { $_.InstanceId -match 'VID_1532&PID_' } |
  ForEach-Object {
    $m = [regex]::Match($_.InstanceId, 'PID_([0-9A-F]{4})')
    [pscustomobject]@{
      Name      = $_.FriendlyName
      PID       = "0x$($m.Groups[1].Value)"
      ProductId = [Convert]::ToInt32($m.Groups[1].Value, 16)
    }
  } | Sort-Object ProductId -Unique
```

只接受 Razer 的 VID `0x1532`。复合 HID 设备会有多个接口，但同一产品的 PID 相同。

## 2. 检查 product middleware 入口

```text
GET https://apps.razer.com/synapse/products/{productId}/mw/index.html
GET https://apps.razer.com/synapse/products/{productId}/mw/manifest.json
```

- `index.html` 是 middleware 的入口，里面引用当前版本的 `main.<hash>.js`。
- `manifest.json` 用于确认 `deviceName`、版本和依赖，不包含 chunk hash。
- 任一入口返回 404，或没有 `mw` 目录：停止，这个产品不走这套 Web middleware。

以 710 为例：

```text
https://apps.razer.com/synapse/products/710/mw/index.html
  -> ./main.d7c1244bf00ff7462e7c.js

https://apps.razer.com/synapse/products/710/mw/manifest.json
  -> deviceName: "Razer Blade 16"
```

注意：`/synapse/products/710` 目录本身不可浏览，只有具体静态文件可访问。不要把目录路径当成产品搜索接口。

## 3. 从 main bundle 解析 chunk

`main.<hash>.js` 包含 webpack 的 chunk 名称与 hash 映射。不要硬编码 710 的文件名；每次从当前 `index.html` 和 `main.js` 重新解析。

需要找的目标通常是：

```text
rzDevice25.<hash>.js   低层设备命令、sendCommand 封装、响应解析
6737.<hash>.js         产品功能处理器和 UI 消息到设备调用的链路
main.<hash>.js         产品配置、功能开关、chunk 映射
ui/static/js/main.<hash>.js  UI 常量与 BroadcastChannel 消息，通常不直接包含 HID 命令
```

710 当前解析结果：

```text
main.js chunk map:
  1694 -> rzDevice25.9e83fa81c0c26aa84ab0.js
  6737 -> 6737.0a09be13ed46538a373d.js
```

URL 拼接：

```text
https://apps.razer.com/synapse/products/710/mw/rzDevice25.9e83fa81c0c26aa84ab0.js
https://apps.razer.com/synapse/products/710/mw/6737.0a09be13ed46538a373d.js
```

## 4. 最小验证脚本

这个脚本只下载和搜索文本，不执行 JS：

```powershell
$productId = 710
$base = "https://apps.razer.com/synapse/products/$productId/mw"

$index = Invoke-WebRequest "$base/index.html" -UseBasicParsing
$mainName = [regex]::Match($index.Content, 'src="(\./main\.[^"]+\.js)"').Groups[1].Value.TrimStart('.','/')
$main = Invoke-WebRequest "$base/$mainName" -UseBasicParsing

$hash = [regex]::Match($main.Content, '1694:"([0-9a-f]{20})"').Groups[1].Value
$chunk = Invoke-WebRequest "$base/rzDevice25.$hash.js" -UseBasicParsing

$chunk.Content.Contains('Set VGA Configure Setting')
```

预期返回 `True`。同样可以解析 `6737` 的 hash：

```powershell
$handlerHash = [regex]::Match($main.Content, '6737:"([0-9a-f]{20})"').Groups[1].Value
$handler = Invoke-WebRequest "$base/6737.$handlerHash.js" -UseBasicParsing
$handler.Content.Contains('taskRunnerSetGpuMode')
```

## 5. 从 JS 找命令

先找稳定的命令描述字符串，再沿着函数和常量回溯。不要依赖压缩后的变量名。

Blade 16 2025 的 MUX 相关搜索词：

```text
Set VGA Configure Setting
Get VGA Configure Setting
Get VGA Hardware Information
Get Synapse UI Setting
taskRunnerSetGpuMode
ON_SET_GPU_MODE
```

710 的 MUX 结果：

```text
Get VGA Configure:  [2, 0x0D, 0x89]   args [0, 0]
Set VGA Configure:  [2, 0x0D, 0x09]   args [0, mode]

mode 0 = Microsoft Hybrid / NVIDIA Optimus
mode 1 = Discrete MUX / dGPU 直连
mode 2 = DDS only
```

以上是 JS 源码中的定义，不等于当前机器固件一定支持。写入前仍要读取 capability 和当前模式，并在本机验证。

2026-10-25 本机（Razer Blade 16 2025，1532:02C6）只读实测：

```text
Get VGA Configure:         data[1] = 2 (DDS only)
Get VGA Hardware Info:     data[1] = 0x06 (Dedicated dGPU + DDS)
Get Synapse UI Setting:    switchInterface = 1
```

这台机器没有报告 Microsoft Hybrid 位，因此可用模式是 DDS 和独显直连；实现不能硬性要求 `0x01`。

能力位：

```text
[2, 0x0D, 0x8A] Get VGA Hardware Information
  data[1]:
    0x01 Microsoft Hybrid
    0x02 Dedicated dGPU only
    0x04 Dynamic Display Switch (DDS)
    0x08 Dedicated iGPU only
```

UI 设置：

```text
[2, 0x0D, 0x8B] Get Synapse UI Setting
  data[1] & 0x0F = switchInterface
```

注意：`[3, 0x0D, 0x10]` / `[3, 0x0D, 0x90]` 是 Thermal Fan Cooling Mode 的 set/get，不是 MUX。不要因为它们在同一个文件里就误用。

## 5.1 Blade 15 Base (Early 2020), 1532:0255 / product 597

`manifest.json` 的 `deviceName` 是 `Razer Blade 15 Base Model`，`main.js` 注入的
`deviceUI.config` 只有 `brightness: hasTwoMode`、`performance: hasPerformanceMode +
hasFanSpeed`、`power: hasSystemPower`；没有 `hasGPUmode`、`hasMaxFanSpeedModeEnabled`
或 `hasCpuBoostEnabled`。因此 597 不显示 MUX 卡片，也不开放最大风扇或 CPU 档位。

middleware 的 transaction id 由 `_getTransactionId()` 在 `0..30` 循环，`31` 被跳过；
OpenSynapse 的 597 manifest 使用 `01`。读取到的 thermal 命令使用 profile `1`。

已确认的命令：

```text
0D:80  Get Thermal Fan Id List        dataSize 0x50
0D:81  Get Thermal Fan Speed          dataSize 0x03  args [1, thermalId]
0D:01  Set Thermal Fan Speed          dataSize 0x03  args [1, thermalId, percent]
0D:82  Get Thermal Fan Mode           dataSize 0x04  args [1, thermalId]
0D:02  Set Thermal Fan Mode           dataSize 0x04  args [1, thermalId, mode, fanModeValue]
0D:88  Get Thermal Fan Current Speed  dataSize 0x03  args [1, thermalId]
0F:84  Get Chroma Brightness          dataSize 0x03  args [1, 5]
0F:04  Set Chroma Brightness          dataSize 0x03  args [1, 5, raw]
07:8F  Get Power Mode Control         dataSize 0x01
07:0F  Set Power Mode Control         dataSize 0x01
07:92  Get Charge Limiter             dataSize 0x01
07:12  Set Charge Limiter             dataSize 0x01
07:8C  Get Adapter Wattage            dataSize 0x02
0D:8E  Get Native Panel Resolution    dataSize 0x01
0D:8F  Get SKU Hardware Configuration dataSize 0x01
```

`07:8F/0F` 是同一个位图：bit0 CPU Boost、bit1 Max Fan、bit2 One-time Override、
bit3 Local Dimming。`07:92/12` 才是充电上限。`0D:89/09/8A` 的 MUX 命令虽然存在，
但注入配置没有 `hasGPUmode`，OpenSynapse 不把它暴露给 597。

当前 manifest 已登记 `adapter-power.get`、`charge-limit.get/set`、`power-mode.get/set`、
`native-display-mode.get/set`、`sku-hardware-configuration.get`、`thermal-state.get/set`、
`current-fan-rpm.get` 和键盘亮度读写。`0D:81` 的 fan speed 是百分比而不是 RPM，
OpenSynapse 不用 `fan-target.get` 把它伪装成 RPM 目标。

## 6. 读取链路时要确认的事

1. **命令描述和命令号对应**：从导出常量表确认 `sendCommand` 实际使用的数组，不要只看附近的字符串变量。
2. **payload 布局**：数组首字节通常是 payload 长度；后续字节才是 class、command、args。
3. **响应解析**：确认读取的是 `data[0]`、`data[1]` 还是位掩码，不要用经验猜。
4. **能力位**：命令存在不等于硬件支持。先读 capability，再做写入。
5. **生效方式**：有些设置需要重启，有些立即生效。Blade 16 MUX 官方流程会设置 `REQUIRED_RESTART_SYSTEM` 并重启。

## 7. 安全与发布边界

- 只下载、解析文本，不执行来自 CDN 的 JavaScript。
- 不把 Razer 原始 JS、DLL、固件、安装包提交到公共仓库；只保留审查过的命令常量、测试和证据摘要。
- 写操作必须有当前值读取、同值或最小变更测试、回读和恢复路径。
- 对显示 MUX，切换前记录原模式；切换后按官方要求重启，并准备恢复路径。

## 8. 常见失败

| 现象 | 原因 | 处理 |
|---|---|---|
| `mw/index.html` 404 | 产品没有 Web middleware | 停止，改查本地缓存、native DLL 或 OpenRazer |
| `main` 里找不到 target chunk | 版本更新或 chunk 改了名 | 重新解析当前 `main.js`，不要用旧 hash |
| 找到命令但写入无效 | 硬件不支持 / 需要重启 / payload 不对 | 先读 capability 和当前值，确认 payload 与响应 |
| 名字对应错产品 | Blade 16 不同年份 PID 不同 | 用 USB PID 或 RZ09 型号，不用通用商品名 |
| 把 fan cooling 当 MUX | 命令号和字符串相邻但导出映射不同 | 以实际 `sendCommand` 的导出常量为准 |

## 9. 最短检查清单

- [ ] PID 已从 USB PID 转成十进制 product id
- [ ] `mw/index.html` 返回 200
- [ ] `main.<hash>.js` 已定位
- [ ] target chunk 已从当前 chunk map 解析
- [ ] 命令描述字符串已找到
- [ ] `sendCommand` 的 class / command / payload 已核对
- [ ] response parser 和 capability 位已确认
- [ ] 写路径有原值、回读和恢复

需要把这套提取结果接入桌面端 MUX 设置时，继续看 [GPU MUX HID 适配流程](../blade/GPU_MUX_HID_ADAPTATION.zh-CN.md)。
## 5.2 2024/2025 Blade 16 与 597 写入

`Razer Blade 16 (2024)` 的 USB PID 是 `1532:02B7`，product id 为 `695`，middleware 的
`short_name` 是 `Sonya10_MW`。它的 `main.js` 注入配置启用了 `hasPerformanceMode`、
`hasFanSpeed`、`hasNativeDisplayMode`、`hasLocalDimming`、`hasGPUMode`、
`hasSystemTrackpad` 和 Intel overclocking，但没有 `hasMaxFanSpeedModeEnabled`。
因此它复用 `blade-710` 的命令表，但 Max Fan 控件仍按 PID 关闭。

02B7 与 02C6 共用的关键命令：

```text
0D:82  Get Thermal Fan Mode         dataSize 0x04  args [profileId, thermalId]
0D:02  Set Thermal Fan Mode         dataSize 0x04  args [profileId, thermalId, mode, fanModeValue]
0D:81  Get Thermal Fan Target       dataSize 0x03
0D:01  Set Thermal Fan Target       dataSize 0x03
0D:88  Get Thermal Current Speed    dataSize 0x03
0F:84  Get Chroma Brightness        dataSize 0x03  args [profileId, regionId]
0F:04  Set Chroma Brightness        dataSize 0x03  args [profileId, regionId, raw]
0D:89  Get VGA Configure Setting    dataSize 0x02  args [0, 0]
0D:09  Set VGA Configure Setting    dataSize 0x02  args [0, mode]
0D:8A  Get VGA Hardware Information dataSize 0x02
07:8F  Get CPU/Power Mode Control   dataSize 0x01
07:0F  Set CPU/Power Mode Control   dataSize 0x01
07:92  Get Charge Limiter           dataSize 0x01
07:12  Set Charge Limiter           dataSize 0x01
```

597 的写入和读取路径来自同一份 `rzDevice25` 命令表，已确认：

```text
0D:02  Set Thermal Fan Mode         dataSize 0x04  args [1, thermalId, performanceMode, fanMode]
07:12  Set Charge Limiter           dataSize 0x01
0F:04  Set Chroma Brightness        dataSize 0x03  args [1, 5, raw 0..255]
0F:84  Get Chroma Brightness        dataSize 0x03  args [1, 5]
07:8C  Get Adapter Wattage          dataSize 0x02
07:8F  Get Power Mode Control       dataSize 0x01
0D:8E  Get Native Panel Resolution   dataSize 0x01
0D:8F  Get SKU Hardware Configuration dataSize 0x01
```

597 的亮度读取把 Chroma 的 `raw / 255` 映射到百分比；写入使用
`raw = floor(percent * 255 / 100)`，这样 50% 写入后会回读为 50%。597 的
`0D:81` 返回的是百分比，不是 02C6 的 RPM 编码，因此 OpenSynapse 不把
597 的风扇目标暴露成 RPM 控件。597 不显示 MUX，因为注入配置没有 `hasGPUmode`；
低层命令存在不等于产品 UI 已开放。

## 5.3 Blade 2023-2025 矩阵尺寸

| PID | product | 矩阵 |
|---|---:|---:|
| `029D` | 669 | 6×16 |
| `02B6` | 694 | 6×16 |
| `02B7` | 695 | 6×17 |
| `02B8` | 696 | 6×16 |
| `02C5` | 709 | 6×16 |
| `02C6` | 710 | 6×17 |
| `02C7` | 711 | 6×19 |

middleware 的帧接口按调用方传入的 `numRow/numCol` 发包；上面的尺寸来自各产品
`onLoad.js` 的键盘布局或已审计的 Blade 16 HID 矩阵。6×16 / 6×19 当前由 6×17
逻辑画面做最近邻缩放，实体键位对齐仍需对应机型的真实布局数据。
