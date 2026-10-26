# 中间件鼠标定义对照（DeviceInfo 审计）

本文记录 `OpenRazerDeviceCatalog` 里由雷云 bundle 推导、或复用兄弟型号协议的那批鼠标，
和产品 bundle 自带 `DeviceInfo` 的逐项对照结果（`00C0`、`00B8` 是原生条目，列为对照基准）。

## 数据来源

- `artifacts/razer-mw-all-bundles-v2/<PID>/main.*.js`：每个产品的 `DeviceInfo`（DPI 范围、步进、轮询率、OBM 档位、claimInterface）。
- `artifacts/razer-mw-all-protocols-v2/protocols.json`：每个 bundle 定义和实际引用的 HID 命令。
- 抽取脚本：`tools/extract_razer_device_definitions.py`。

## 对照结果

| PID | 设备 | DPI | 轮询率 | OBM 档位 | 目录状态 |
|---|---|---|---|---|---|
| 0063 | Jugan | 200–7200 / 100 | 无 | 0 | 一致 |
| 0069 | Mamba Hyperflux | 100–16000 / 50 | 125/500/1000 | 4 | 一致 |
| 0074 | Abyssus Lite | 200–6400 / 100 | 125/500/1000 | 0 | 原缺轮询率，已补 |
| 0075 | Turret Mouse | 100–16000 / 50 | 125/500/1000 | 4 | 一致，板载宏 |
| 0093 | Naga Classic | 100–16000 / 50 | 125/500/1000 | 4 | 一致，板载宏 |
| 00DD | Krakoff | 100–45000 / 1 | 125–8000 | 1 | 一致 |
| 00E0 | Orochi V2 | 100–18000 / 50 | 125/500/1000 | 1 | 一致 |
| 00E2/00E3 | Basilisk V4 Pro | 100–50000 / 1 | 125–8000 | 4 | 一致 |
| 00E5/00E6 | Viper V4 Pro | 100–50000 / 50 | 125–8000 | 1 | 一致；无灯光，已从灯光表移除 |
| 00E7/00E8 | Naga V3 Pro | 100–30000 / 50 | 125/500/1000 | 4 | 一致 |
| 00EA/00EB | Basilisk V4 HyperSpeed | 100–33000 / 1 | 125–8000 | 4 | 一致 |
| 00EF/00F0 | DeathAdder V4 Pro CF | 100–50000 / 50 | 125–8000 | 1 | 一致；无灯光 |
| 00DE/00DF | Viper V3 Pro SE | 100–35000 / 1 | 125/500/1000 | 1 | 最大 DPI 与轮询率一致；步进为兄弟型号的 50 |

## 命令集说明

产品 bundle 的 `definedCommands` 是共享引擎的并集，不能据此判断单台设备是否支持某条命令：

- `00B8` 的 bundle 只定义 class `0x0B/0x05/0x01`，但实机日志证明它同样应答 `04:85`、`04:86`（38 字节）、`07:80`、`07:83`、`00:85`。
- 因此复用兄弟型号的旧命令族是可行的，但最终以运行时读探针为准：读不回来的控件保持隐藏，不会盲写。

已知的帧差异（沿用上游 OpenRazer，不改动）：

- 轮询率读取 `00:C0`：上游用 data size 1，雷云 bundle 声明 2。
- DPI 档位 `04:86/04:06`：上游用 38 字节，雷云 `smartPixart` 引擎声明 80 字节。

## 未覆盖

- DPI 步进只进了文档，界面 NumberBox 仍按 100 递增；输入框可以键入任意值，但用微调按钮时无法按 1 或 50 调整。
- `0063`、`0074` 的 bundle 没有 `supportedFeature` 块，灯光能力只能靠 Chroma 探测确认。
