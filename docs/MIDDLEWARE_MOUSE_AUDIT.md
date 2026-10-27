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
| 00A2 | Cobra | 200–8500 / 100 | 125/500/1000 | — | 新增，复用 `00A3` Cobra 的定义 |

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

## 板载按键映射（从 bundle 提取）

产品 bundle 里每个鼠标都定义了单键映射命令；应用按这套帧实现，不依赖实机抓包：

| 用途 | 命令（class:cmd） | data size | 负载 |
|---|---|---:|---|
| 按键列表 | `02:84` | 80 | 全 0；回复首字节为数量，随后是按键 ID |
| 读单个映射 | `02:8C` | 80 | `[profileId, buttonId, mode]` |
| 写单个映射 | `02:0C` | 80 | `[profileId, buttonId, mode, fnId, dataSize, data...]` |

- 回复布局：`[buttonId, mode, fnId, dataSize, data0..data4]`（`class` 之后的第一个参数是按键 ID）。
- 请求和回复的 `dataSize` 都是 80，`profileId` 固定 `1`。
- `mode`：普通层 `0`，Shift 层 `1`。
- `fnId` 与 payload 直接取自 bundle 的 `buttonMappingToFnDataByte`（chunk `6120` 的模块 `56415`）：

| fnId | 动作 | payload |
|---:|---|---|
| 0 | 关闭 | 空 |
| 1 | 鼠标按键 | `[buttonId]`（`LeftButton=1`、`RightButton=2`、`ScrollButton=3`、`Button4..8=4..8`、`ScrollUp/Down=9/10`、`DPIUp/Down=11/12`、`SniperButton=15`、`DPI_Cycle_Up/Down=96/97`、`Previous/Next=99/100`、`Vol_Up/Down=101/102`、`Mute=103`、`ScrollLeft/Right=104/105`） |
| 2 | 键盘按键 | `[修饰键位掩码, HID usage]`；位掩码 `LeftCtrl=1`、`LeftShift=2`、`LeftAlt=4`、`LeftGUI=8`。纯修饰键按雷云的写法发 `[224..231, 0]` |
| 6 | DPI | `[1]` 加、`[2]` 减、`[6]` 循环加、`[7]` 循环减；`[5, xH, xL, yH, yL]` 是 DPI 瞬降阈值（大端 16 位） |
| 7 | 配置切换 | `[1]` 上、`[2]` 下、`[3, profileId]` 指定、`[4]` 循环上、`[5]` 循环下 |
| 9 | 电源键 | `[id]`：`129` 电源、`130` 睡眠、`131` 唤醒 |
| 10 | 媒体键 | `[hi, lo]` 大端 16 位：`226` 静音、`233/234` 音量加减、`205` 播放/暂停、`181/182` 上/下一曲、`183` 停止、`184` 弹出、`671` 切换应用、`111/112` 亮度、`545` 聚焦搜索 |
| 11 | 双击 | `[1]` |
| 12 | HyperShift 切换 | `[1]` |
| 17 | Razer 键 | `[id]`：`80` 配置切换、`81` 狙击、`82/83` DPI 循环、`84` 滚轮模式、`89` HyperShift、`216` 麦克风静音等 |
| 18 | 滚轮模式 | `[1]` 切换、`[2]` 滚轮加、`[3]` 滚轮减、`[4]` 循环加、`[5]` 循环减 |
| 21 | Windows 快捷键 | `[id]`：`99` 截图、`100` Xbox Game Bar、`101` 切换应用 |

- `fnId 3/4/5/15` 的宏动作只有在能从设备读到板载宏槽时才开放；当前写入单次播放（`fnId 3`，`[0, macroId, 1]`），未读到的宏槽不会凭空生成。
- 编辑器没有开放的动作，都是缺设备侧数据、写进去只能靠猜的：`13/14`（连发速率）、`16`（手柄按键）、DPI 瞬降阈值、指定配置 ID。
- `fnId 8`（灯光）在雷云自己的 `buttonMappingToFnDataByte` 里没有 case，直接 `break`，所以这条能力本身不存在。

## 宏触发键覆盖

宏触发器分两条输入通道，不能混用：

- **标准 HID / Raw Input**：键盘 `KEY_*`、鼠标左/右/中键、滚轮、`Button4/5`、Naga 的 `SPAD_NUM_1..12` 和 `HPAD_NUM_1..7`。Naga 侧板实际发送数字键；Raw Input 的 device path 用来把它和物理键盘区分开。
- **Razer 私有 key report**：`DKM_M_*`、`DKM_KBMK_*`、`DKM_SB_*`、`DKM_KB_*` 等只出现在 report `4`（无线从设备为 `8`），不会进入 Raw Input。私有的 HID 集合在设备描述符里是 `UsagePage 0x0000/0x0080`，应用只读打开并按 PID 解释 key slot。

`RazerPrivateKeyCatalog.cs` 保存按 PID 分组的 key slot 标签；同一个数字在不同设备上含义不同（例如 `32` 在鼠标上是 DPI+，在 BlackWidow 上是 M1），不能做全局映射。`DKM_M_93/94` 是设备抬起/落座通知，不作为宏触发键；`RepeatScroll*` 和模拟量行程也没有开放为触发器。
