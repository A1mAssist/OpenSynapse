# Razer Leviathan V2 X (054A) 协议记录

## 来源

- USB PID：`1532:054A`
- Synapse product：`1354`
- Middleware 入口：`https://apps.razer.com/synapse/products/1354/mw/`
- 解析时 main bundle：`main.386a94db6ca3acf63ef3.js`
- 产品实现 chunk：`rzDevice25NoaT3.977553ee2e353e94c38c.js`
- 共享协议 chunk：`6259.1e438d99ab14b43bc809.js`

原始 JavaScript 只用于本地文本解析，不执行、不提交。以下常量来自源码中的实际 `sendCommand` 调用。

## 报文

```text
Windows feature report: 91 bytes
report[0] = 0x07 (report ID)
report[1..90] = v25 logical report

logical[0]  = 0
logical[1]  = transaction id，0..31 循环
logical[2..4] = 0
logical[5]  = packet size
logical[6]  = command class
logical[7]  = command
logical[8..] = arguments
logical[88] = XOR of logical[2..87]
logical[89] = 0
```

响应按逻辑报文的 `[0]` 状态、`[1]` transaction、`[5]` size、`[6]` class、`[7]` command 读取；Windows 层对应为 report ID 后的 `[1]`、`[2]`、`[6]`、`[7]`、`[8]`。`rzDevice25._getTransactionId()` 是 `0..31` 递增并回绕，不是固定值；当前实现由 `RazerFeatureTransport` 对 `054A` 使用 report ID `0x07`，并为每个设备路径分配循环 transaction。

## 已实现命令

| 功能 | GET | SET | 参数 / 响应 |
|---|---|---|---|
| EQ 预设 | `[2, 0x08, 0x82]` | `[2, 0x08, 0x02]` | `[profile, preset]` |
| 10 段 EQ | `[11, 0x08, 0x84]` | `[11, 0x08, 0x04]` | `[profile, band0..band9]`，0 到 100 |
| 低音 | `[2, 0x08, 0x87]` | `[2, 0x08, 0x07]` | `[profile, level]`，level 1 到 7 |
| 输入源 | `[2, 0x08, 0x8A]` | 未开放 | 响应 `[profile, source]` |
| 音频接口状态 | `[5, 0x08, 0x8B]` | 无 | `headphone, usb, aux, spdif, bluetooth` |
| 固件 | `[2, 0x00, 0x81]` | 无 | `[major, minor]` |
| Edition 信息 | `[3, 0x00, 0x86]` | 无 | `[keyboardLayout, edition, firmwareId]` |
| Chroma region 列表 | `[80, 0x0F, 0x80]` | 无 | 每 5 bytes 一组，首 byte 是 region id |
| 亮度读取 | `[3, 0x0F, 0x84]` | `[3, 0x0F, 0x04]` | `[profile 0, region]` / `[profile 0, region, 0..255]` |

EQ 预设值：`Default=0`、`Game=1`、`Music=2`、`Movie=3`、`Treble=4`、`Custom=0x10`。输入源：`None=0`、`Optical=1`、`AUX=2`、`Bluetooth=3`、`USB=4`。

EQ 频段来自 middleware 默认曲线：`31, 63, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz`。UI 的 0 到 100 与固件字节一致，不额外做 dB 换算。

## 已提取但未开放

- 音量 / 静音：当前 `1354` 主入口使用 Windows Core Audio `simpleService`，没有使用 HID 的 master-volume / mute 命令。
- 音频输出路由：`[2, 0x08, 0x91]` / `[2, 0x08, 0x11]`，可切换 soundbar / headset；未在实机确认切换后的恢复路径。
- Chroma 效果和逐帧：需要先读取 region/effect 能力表，当前只开放亮度。
- 电源：没有直接的 power on/off HID 命令；`[2, 0x00, 0x84]` 是 device mode，不等于电源开关。
- Treble 没有独立 setter，只出现在未使用的 aggregate `Get Device Level` 中。

## 实现边界

- 所有 edition 共用同一协议，没有 edition 分支；连接后读取 `[3, 0x00, 0x86]`，用 edition 值显示 Pikachu / Kuromi 名称。
- 设备选择按 `VID_1532&PID_054A`、91-byte feature collection 和物理设备分组，不使用用户机器上的 `COMPUTER\{...}` 容器 ID。
- 写入先读当前值，写后回读；失败时尝试恢复原值。按 middleware 的实际调用：预设读取/写入和 Bass/自定义 EQ 读取使用 profile 0，Bass/自定义 EQ 写入使用 profile 1。只有原预设为 Custom 时才回写自定义 EQ 频段；非 Custom 预设无法读出已存的自定义曲线，因此不做猜测性恢复。
- 亮度先读取 region 列表，再用 profile 0 和第一个 region 读取/写入；这不依赖固定的 All-region。
- 当前为源码支持的候选实现，尚未在任何 054A 实机完成读写、回读和恢复验证。

## 实机验收顺序

1. 只读：固件、序列号、亮度、EQ 预设、10 段 EQ、低音、输入源、音频接口状态。
2. 同值写入：选择当前 EQ 预设并应用，确认不改变其它状态。
3. 最小变化：低音 `n -> n+1 -> n`，确认回读和恢复。
4. 自定义 EQ：只改一个频段，应用后回读，再恢复原曲线。
5. 两个 edition 各跑一次；如果同时连接，确认两个物理设备分别出现、名称对应 edition，且互不影响。
6. 记录失败时的 diagnostic `leviathan-054A` / `openrazer-054A` 行。
