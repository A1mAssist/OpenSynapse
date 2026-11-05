# JS 协议对齐审计

本文件记录 OpenSynapse 从雷云官方 JavaScript 移植的 HID 协议，与 JS 原始实现的逐条对照结果。

## 判定依据

雷云把命令写成三元组 `[dataSize, commandClass, commandId]`，通过
`sendCommand(header, description, data, packetSizeSet)` 发往设备。`packetSizeSet`
存在且为正数时**覆盖** `dataSize`，所以线上报文的 `data_size` 有两个来源：

- 三元组里的第一个数字（绝大多数命令）；
- `sendCommand` 的第四个参数（`setDPIStages` 用 `7*stageCount+3` 覆盖 80 字节）。

因此「完全对齐」可以机械校验：命令三元组 + `data_size` + 负载布局。

## 基准数据

基准是 `artifacts/razer-mw-full-cdn/out7/protocols.json`，由
`tools/extract_razer_middleware_protocols.py` 从 **264 个产品 bundle**（每台 60~90 个
webpack chunk）静态提取，共 **144 119** 条命令定义、**35 265** 条被实际调用的命令。

```powershell
python tools\extract_razer_middleware_protocols.py --bundles artifacts\razer-mw-full-cdn\bundles --out artifacts\razer-mw-full-cdn\out7
python tools\check_razer_js_alignment.py --protocols artifacts\razer-mw-full-cdn\out7\protocols.json
```

校验器逐条比对每个「机型 × builder」的 `data_size`，退出码非 0 表示存在不一致。

## 提取器修复（基准本身的问题）

基准的准确性决定了整个审计的结论，所以先修提取器。

### 1. 基准只覆盖 121/278 个机型

原基准来自 `artifacts/razer-mw-all-protocols-v2`，157 个 bundle 提取出 0 条命令
——那些 bundle 只下载了 `main.js`，没有承载命令表的共享 chunk。
改用完整 bundle 树后覆盖 **262/264** 个机型。

### 2. 压缩后的 JS 复用同一个短变量名

原来的解析器只保留每个变量名的**第一次**赋值，于是 `const c = n(44231)`（导入别名）
会覆盖后面真正的 `const c = new Uint8Array([7,4,133])`（命令三元组）。
修复：保留同一名字的**全部**赋值，解析时取使用点之前**最近的那次**声明。

### 3. `harvest_declarations` 把 Get/Set 配错了（本轮最关键的提取器 bug）

雷云把一组命令声明在同一条 `const` 链里，读取与写入的配对规则有三种形态：

```js
// 形态一：两个独立三元组 + 两个名字
r = new Uint8Array([1,7,129]), a = new Uint8Array([1,7,1]),
"Get Battery Critical Level", "Set Battery Critical Level"
// → Get 取第一个，Set 取第二个

// 形态二：回退组 tuple + 独立三元组 + 两个名字
T = (new Uint8Array([3,7,137]), new Uint8Array([3,7,9]),
     new Uint8Array([2,7,138]), new Uint8Array([2,7,10]),
     new Uint8Array([5,7,139])), D = new Uint8Array([5,7,11]),
A = "Get Time To Dim", O = "Set Time To Dim"
// → Get 取 tuple 的最后一个成员 [5,7,139]，Set 取 tuple 之后那个独立三元组 [5,7,11]
```

原实现无视 tuple，把 Get 和 Set 都配到了最后一个独立三元组上，于是
「Get Time To Dim」被记成 `07:0B`（实际是 Set 的值），「Get Auto Sleep Mode」
被记成 `07:08`。校验器因此报出大量**假阳性**。

修复后按源码顺序扫描 `const` 链，用 `find_matching_paren` 配对圆括号（原有的
`find_matching` 只配对花括号），用 `BARE_ARRAY` 识别 tuple 里**没有变量名**的
`new Uint8Array([...])` 成员。

JS 原文核对无误：

```text
Get Auto Sleep Mode        07:88 size=2      Set Auto Sleep Mode        07:08 size=2
Get Time To Dim            07:8B size=5      Set Time To Dim            07:0B size=5
Get Indicator Led Config   07:90 size=1      Set Indicator Led Config   07:10 size=1
Get Battery Charge Limiter 07:92 size=1      Set Battery Charge Limiter 07:12 size=1
Get Battery Level          07:80 size=2      Get Device Charging Status 07:84 size=2
```

### 4. `const` 链长度上限截断了命令表

雷云的命令表是一条 4126 字符的 `const` 链，原正则的上限是 4000，整条链被丢弃。
上限提到 20000 后，「Get Device Polling Period」等命令才进入基准。

### 5. USB 与蓝牙两套命名空间被混在一起

同一台设备的 bundle 里同时存在两个命名空间：

| chunk | 命名空间 | 例：Get Battery Level |
|---|---|---|
| `6120` / `main` / `7846` / `763` | HID（USB） | class `07`, id `80`, size `2` |
| `rzDevice30*` | 配对（蓝牙） | class `05`, id `82`, size `0` |

两边的**命令名相同**，`class:id` 不同。更隐蔽的一例（0203 BlackWidow Chroma）：
USB `07:8B` = "Get Time To Dim"（size 5），配对 `07:8B` = "Get Function Key Swap"（size 0）。
OpenRazer 目录驱动的是 USB，所以校验器只采信 HID 命名空间。

## 协议错误（本轮修复）

### 6. Smart Reel 的 data_size 错了

雷云的 Smart Reel 开关走的是 **Scroll Mode Auto Switch State**：

```text
getScrollModeAutoSwitchState(classId) -> 02:97，声明 80 字节，负载 [classId]
setScrollModeAutoSwitchState(classId, state) -> 02:17，声明 80 字节，负载 [classId, state]
```

我们之前声明 2 字节，设备不会生效。已改为 80 字节（`0x50`）。

### 7. DPI 档位缺 Z 轴、档位 id 语义相反

JS 的 DPI Class Data 每档 **7 字节**，含 Z 轴：

```text
setDPIStages: dataSize = 7*numStages+3（覆盖声明的 80）
  [0] = classId   [1] = activeStage   [2] = numStages
  每档起点 = 3 + 7*i：
    [off+0] = 档位 id   [off+1..2] = X(high,low)
    [off+3..4] = Y(high,low)   [off+5..6] = Z(high,low)
```

我们之前每档只写 5 字节（没有 Z），并且把 `stage.Number` 写成 0 起的下标，
而 JS 写的是档位自带的 id。已改为 7 字节/档 + Z 轴 + 原样使用档位 id，
`data_size` 用 80（`MiddlewareDpiStageFrameSize`）。

字节序经 JS 工具函数核对：`J z(high, low) = (high << 8) | low`，即**大端**。

### 8. USB 高轮询率读取的 data_size 错了

`Get USB High Speed Polling Period` 在所有带该命令的 bundle 里都声明 **2 字节**
（`00:C0`），我们默认按 1 字节发。已改为 2 字节。

### 9. 充电限制的位域方向搞反了

JS 原文：

```js
// SET
const i = new Uint8Array(ce[0]); i[0] = e << 7 | n;
// GET parse
jsonData = { value: 127 & t.data[0], enabled: (128 & t.data[0]) >> 7 };
```

即 **enabled 在最高位（bit7），value 在低 7 位**。我们先前写成
`level << 7 | enabled`，等于把两个字段的位置颠倒，设备收到的是完全不同的值。

已修正为：

```csharp
[(byte)((enabled ? 0x80 : 0x00) | (level & 0x7F))]
```

回归测试见 `OpenRazerMiddlewareAlignmentTests.BatteryChargeLimiterPacksTheEnabledFlagIntoTheHighBit`。

### 10. 00AA / 00AB / 00C0 / 00C1 的目录条目与 JS 不符

Basilisk V3 Pro（00AA 有线 / 00AB 无线）与 Viper V3 Pro（00C0 有线 / 00C1 无线）
的 DPI 档位在 JS 里是 80 字节（`Get Profile DPI Class Data` = `04:86`，
`Set` = `04:06`），轮询率是 `[{content:"125"},{content:"500"},{content:"1000"}]`。
我们目录里的前缀、轮询率、DPI storage 与之不符，已按 JS 原文 + OpenRazer 上游
transaction id 重建。

## 补齐的能力

以下能力雷云在几十到两百多个机型上实际调用，之前 USB 侧没有对应实现：

| 能力 | 命令（读/写） | JS 使用的机型数 | 状态 |
|---|---|---:|---|
| Auto Sleep Mode | `07:88` / `07:08` | 257 | 协议 + 目录 + UI |
| Macro Storage Info | `06:8E` | 206 | 协议 + 目录 + UI（只读） |
| Battery Charge Limiter | `07:92` / `07:12` | 166 | 协议 + 目录 + UI |
| Time To Dim | `07:8B` / `07:0B` | 166 | 协议 + 目录 + UI |
| Indicator LED Configuration | `07:90` / `07:10` | 166 | 协议 + 目录 + UI |
| Force Chroma Ctrl | `0F:90` / `0F:10` | 101 | 协议 + 目录 + UI |
| Profile Debounce Time | `02:95` / `02:15` | 95 | 协议 + 目录 + UI |
| LK Scroll Wheel Mode | `15:8B` / `15:0B` | 85 | 协议 + 目录 + UI |
| Chroma Tap | `0F:93` / `0F:13` | 85 | 协议 + 目录 + UI |
| GPU Card Detection | `03:A0` / `03:20` | 139 | 协议 + 目录 + UI |
| Battery Discharge Curve | `07:94` / `07:14` | 56 | 协议 + 目录 + UI |

九个能力的报文已按 JS 源码逐条核对（不是按提取器产物核对）：

```text
能力                 GET 解析                                        SET payload
Time To Dim          {profileId,enabled,timeToDim:U1(d2,d3),pct}   [t,n,ft(s),Zz(s),pct]
Indicator LED        {status:d[0]}                                   [t]
Battery Discharge    {status:d[0]}                                   [e]
GPU Card Detection   {ledClassId,enabled,status}                    [t,n]
Profile Debounce     {profileId,make,makeMask,break,breakMask}      [t,n,i,o,r]
Chroma Tap           {profileId,status,tapMode,functionId}           [n,e,o,a]
Force Chroma Ctrl    {profileId,enabled}                             [t]
Macro Storage Info   7 个 u16                                        只读
LK Scroll Wheel      {classId,mode}                                  [t,n]
```

字节序由 JS 工具函数源码确定：`U1=(255&e)<<8|255&t`（大端）、
`ft=e>>8&255`（高字节）、`Zz=255&e`（低字节）。

`Force Chroma Ctrl` 的 GET 声明 1 字节而响应有 2 字节 —— GET 与 SET 共用同一个
解析器 `{profileId,enabled}`，所以读的是 `data[1]`。先前读 `data[0]` 拿到的是
profileId，状态恒为「开启」。

Auto Sleep 与 Battery Charge Limiter 的目录事务声明覆盖 149 / 122 个机型，
UI 复用既有的「电源管理」卡片（空闲休眠、低电量提醒旁边）。

**这 11 个能力上游 OpenRazer 完全没有**——`daemon/openrazer_daemon/hardware/` 里
既没有 `get_auto_sleep` 也没有 `get_battery_charge_limiter`。所以它们的 transaction id
没有上游依据，目录里按同设备 class 7 电池族已有的 transaction id 填（该族在每个
设备上完全一致，190 个设备自洽）。

按「同 class 的既有 builder」逐条核对，986 个「机型 × 新能力」组合里 912 个的
transaction 落在该设备同 class 的既有值集合内；其余 74 个分属不同 command class
（`0F`/`15`/`21`/`06`/`03`/`02` 各自有独立的 transaction 家族），同样自洽。

## payload 内容核对

`check_razer_js_alignment.py` 只证明每条命令线上发几个字节。位域方向、字段顺序、
大小端写反时长度完全不变，所以这一轮补的是内容：`extract_razer_middleware_protocols.py`
现在会在每条 `sendCommand` 调用点记下第三个参数（payload 数组），并把所在代码块里
逐索引的最后一次写入一起存进 `payload` 字段；`tools/audit_razer_payload_layout.py`
把它和 C# 侧的构造表达式并排打印。

范围是 `src/OpenSynapse.Windows/Protocols` 里 31 个会发非空 payload 的 builder
（另外 16 个只发空数组，内容无从错）。

| 结果 | 数量 | 说明 |
|---|---:|---|
| 内容与 JS 一致 | 24 | 见下方清单 |
| 内容不一致并修复 | 2 | 11 / 12 两条 |
| JS 侧取不到构造表达式 | 5 | 见「未验证项」 |

### 已核对一致

```text
00:05  轮询率码 1000→1 / 500→2 / 125→8（JS DO.RATE_*）
00:40  高轮询率码 8000→1 … 125→64
00:C0  GET 只发 profileId
02:14  [classId, mode]
02:15  去抖 5 字节 [profileId, make, makeMask, break, breakMask]
02:16  [classId, state]
02:17  [classId, state]（80 字节）
02:94  GET 只发 classId
02:95  GET 只发 profileId
02:96  GET 只发 classId
02:97  GET 只发 classId
03:20  [ledClassId, enabled]
03:A0  GET 只发 ledClassId
04:05  [storage, X高, X低, Y高, Y低, Z高, Z低]，Z 缺省 0
07:03  空闲秒数两字节大端
07:08  [profileId, enabled]
07:0B  [profileId, enabled, 秒高, 秒低, 百分比]
07:10  [configuration]
07:12  enabled 在 bit7、档位在低 7 位
07:14  [chemistry]
07:8B  GET 只发 profileId
0F:10  [0/1]
0F:13  [profileId, module, tapMode, functionId]
15:0B  [profileId, mode]
15:8B  GET 只发 profileId
```

### 11. 低电量阈值的取整方向和官方相反

JS 原文：

```js
t > 255 && (t = 100);
n[0] = Math.ceil(t / 100 * 255);
// GET: batteryLevel = Math.floor(data[1] / 255 * 100)
```

我们之前用 `percent * 255 / 100`（截断）发送、`Math.Round` 回读。长度都是 1 字节，
长度校验器报一致，但四档里三档和官方 app 差 1：

| 设置 | JS 发的字节 | 之前发的 |
|---|---:|---:|
| 5% | 13 | 12 |
| 10% | 26 | 25 |
| 15% | 39 | 38 |
| 25% | 64 | 63 |

已改为 `Math.Ceiling` 发送、`Math.Floor` 回读。回归测试
`OpenRazerBackendTests.LowBatteryThresholdMatchesMiddlewareScaling`。

### 12. 声明 250 Hz 的机型根本无法设置轮询率

JS 的码表是 `RATE_1000Hz:1, 500:2, 333:3, 250:4, 200:5, 125:8, 100:10`，
我们只实现了 1000/500/125。目录里 11 个机型声明 250 Hz，其中 Pro Click V2
竖版与标准版（`00C7`/`00C8`/`00D0`/`00D1`）只走 legacy `00:05` 命令，
选 250 Hz 会直接抛 `ArgumentOutOfRangeException`；设备恰好停在 250 Hz 时，
`ParsePollingRate` 也不认识 `0x04`，整条轮询率读取失败。

已补齐 333/250/200/100 四个码。回归测试
`OpenRazerMiddlewareAlignmentTests.LegacyPollingRateUsesTheMiddlewareRateTable`。

### 跨 264 个 bundle 的交叉核对

31 条里 25 条在多个机型上都能取到构造表达式，逐条比对后字节布局完全一致；
表面上的多组差异都是压缩后的变量改名（`n[0]=t` 与 `e[0]=n` 同义）。

唯一的语义差异是 `07:08 Set Auto Sleep Mode`：52 个机型取到 3 字节
`[profileId, enabled, 0]`，其余是 2 字节。追到调用点是
`rzDevice25Carol.*.js` 里的 `setAutoSleepMode(e, t, n = 0)` —— 属于 Carol 家族
（Carol T2，尚未支持），它和这些机型共用同一个 bundle 目录，才被归到它们名下。
这 52 个 pid 里有 36 个是我们支持的耳机（BlackShark V2 Pro、Kraken V3/V4、
Nari、Barracuda、Nommo、Leviathan V2），但它们自己的模块是 `rzDevice25.*`，
2 字节。

所以现状没有影响：auto sleep 只挂在 OpenRazer 的 `AutoSleepWrite` 能力上，
OpenRazer 目录（259 台：鼠标/键盘/笔记本/显示器/配件，不含耳机）之外的设备
不会发 `07:08`。要做耳机页的自动休眠时，Carol 家族必须用 3 字节。

## 判定结果

### 机型覆盖

264 个雷云机型里 **243 个已有页面**，分属三条通道：

| 通道 | 覆盖 |
|---|---|
| OpenRazer HID 目录 | 鼠标、键盘、笔记本、灯带、配件 |
| 专用 Manifest 页 | Blade 各代（`blade-0245`…`blade-711`）、Viper V3 HyperSpeed（`viper-184`） |
| 专用服务页 | 键盘（`RazerKeyboardService`）、Chroma Standard 配件、耳机音频（`RazerHeadsetAudioService`）、Kraken 7.1 灯光、BlackShark V3 Pro、Leviathan V2 X |

剩下 21 个机型没有页面：

- **17 个音频设备**（Kraken V3 X、Seiren V3、Clio、Carol T2、Mako、Hammerhead V3、
  Barracuda X …）：雷云 bundle 里确实有完整的 HID 命令集（含本文档的九个能力），
  但要支持它们需要另外 **247 条**我们没有的命令 —— 音频 EQ、DND、麦克风通道、
  侧音、ANC。这些属于音频子系统，不是 HID 控件，单独排期。
- **Philips Hue 桥（`0301`）**：已有 Hue 页面，但不是 Razer 设备页。
- **Turret 纪念版（`0A22` / `0A23`）**：雷云 bundle 里没有任何命令，无法判断能力。

### 数据规模

对 **2322** 个「机型 × builder」组合逐一比对 `data_size`：

```text
checked 2322 device/builder pairs against 264 javascript bundles
mismatches: 0
not verifiable from javascript: 13 (command class absent from the bundle)
```

**`data_size` 不一致：0 条。**

13 条「不可验证」分两类，都不是缺陷：

- **class 整体缺失**（10 条）：0084 DeathAdder V2 的 class 4（DPI）、00BE
  DeathAdder V4 Pro 的 class 4 与 class 7，这两台机器的 JS bundle 完全没有
  相应 class，固件只走蓝牙路径，USB 侧无从比对。
- **上游扩展**（3 条）：0084 DeathAdder V2 与 00BE DeathAdder V4 Pro 的 USB
  命名空间只声明了 `00:53`/`00:56`/`00:D3`/`00:D6` 四个 class 0 命令，
  轮询命令只在配对命名空间。目录里的 `00:85`/`00:05`/`00:C0` 来自 OpenRazer
  驱动，是 USB 侧的能力扩展，不与 JS 冲突。

## 没有 port 的原因

- **class `0x0E`（Blade 亮度）**：JS 完全没有这个 class，Blade 亮度在 JS 里是
  `0F:84`/`0F:04`。`0E` 是 OpenRazer 的私有路径，保留。
- **`0B:xx` DPI Class Setting**：OLED 屏设备的 DPI class 命令，目标是带屏鼠标，
  不是通用 DPI 档位。
- **class `0x08` 音频 EQ / class `0x17` OLED**：属于耳机和带屏设备的专属 UI，
  我们的对应页面另走 `RazerHeadsetAudioService` / `RazerBleProtocol30`。

## 未验证项

- 所有结论来自 JS 源码和 OpenRazer 源码的静态对照，**没有实机验证**。
- Auto Sleep、充电限制、Time To Dim、Profile Debounce 需要一台对应机型才能确认生效。
- 提取器仍会漏掉「既没有 `sendCommand` 调用点、也不在同一 `const` 链里声明」
  的命令；这类命令占比很小。
- payload 内容的 5 条未验证项：`03:0B` 自定义矩阵帧只确认了头 `[70,3,11]`
  和「第四参数按 `4+颜色数` 覆盖长度」，首字节是否 `0xFF` 没取到构造点；
  `04:01` legacy DPI 的 `dpi / 6750 * 255` 比例来自 OpenRazer，JS 侧没取到；
  `04:85` GET 的 `[storage]` 同样没取到；`0F:93` GET 在 JS 里写 `r[0]` 和 `r[2]`，
  我们固定发 `[profileId,0,0,0]`，`r[2]` 的形参语义未确认；`02:14` 首字节
  JS 传 `hardSwitchId`，我们固定 `0x01`。
