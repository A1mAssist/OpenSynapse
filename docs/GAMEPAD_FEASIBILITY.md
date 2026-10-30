# 手柄协议可行性：Wolverine V3/V4、Raiju V3、Kitsune

扫描日期：2026-10-28

范围：`0A45`、`0A4C`、`0A57`、`0A5A`、`0A74`、`0A7C`、`1025`、`1030`、`1013`。Stream Controller（`0D06`/`0D09`）不在本次范围。

方法：静态读取 `artifacts/razer-mw-full-cdn/bundles/<PID>/` 下的 `main.*.js`、`2620.*.js`、`rzDevice25Sage*.js`，不执行下载的 JavaScript；再与 `src/OpenSynapse.Windows/Protocols/RazerFeatureTransport.cs`、`RazerFeatureReport.cs` 对照。本文的“支持”指 middleware 命令表里存在该命令，不等于该 PID 的硬件一定暴露对应 UI。

## 结论

**已实现：report-id 变体（2026-10-28）**

- 这些手柄的配置通道是 **91 字节 HID feature report**，middleware 标记 `protocol:"25"`；不是 XInput，也不是 64 字节 output report。
- 帧格式与现有 `RazerFeatureReport` 相同：`[1]` status、`[2]` transaction、`[6]` dataSize、`[7]` class、`[8]` command、`[9..]` arguments、`[89]` XOR CRC、`[90]` 0。
- 但 **静态包中的 report id 为 4**，不是现有 transport 的 0。`rzDevice25Sage` 和 `rzDevice25SagePC` 都显式设置 `reportId=4`、`reportLength=91`；构造参数理论上可以覆盖，但静态包里没有找到传入 0 的调用点。
- 现有 `RazerFeatureTransport` 在发送、读取、校验三处写死 0，不能直接使用：
  - `RazerFeatureReport.CreateRequestCore` 不写 `report[0]`，保持 0；
  - `QueryAsync` / `QueryPreparedAsync` 传 `responseReportId: 0`；
  - `ValidatePreparedRequest` 要求 `report[0] == 0x00`；
  - `MatchesReportId(response, 0)` 只接受 `response[0] == 0`。
- 这不是 `NotReachable`：命令表清楚。2026-10-28 已把 report id 参数化；`RazerGamepadProtocol` 发送 report id 4（0A57/0A74/0A7C 为 10），91 字节帧和 CRC 继续复用。实机描述符仍需确认。

## 1. 每个 PID 的 class:id

### 1.1 共享的 0x0C 表

9 个 bundle 的 `2620.*.js` 都包含同一组 32 个 `[dataSize, class, id]` 三元组；0x0C 手柄命令逐项相同。以 `0A4C` 为例，常量块是：

```js
d=[80,12,128], r=[4,12,129], s=[3,12,130], l=[3,12,2],
c=[9,12,133], v=[9,12,5], A=[3,12,134], m=[3,12,6],
g=[4,12,7], C=[2,12,145], f=[2,12,17], M=[3,12,146],
h=[3,12,18], E=[4,12,147], N=[4,12,19], D=[3,12,149],
L=[3,12,21], T=[4,12,150], x=[4,12,22], z=[2,12,151],
j=[2,12,23], k=[2,12,155], O=[2,12,27], b=[2,12,156],
J=[2,12,28], P=[80,12,152], G=[51,12,153], B=[51,12,25]
```

压缩后的变量名在每个 bundle 里会变，三元组不变。`12` 是十进制 class `0x0C`；`128` 及以上是 get 变体，`2..28` 是 set 变体。

### 1.2 功能与命令

| 功能 | Get | Set | dataSize | 请求 payload | 响应 |
|---|---|---|---|---|---|
| 控制 ID 列表 | `0C:80` | — | 80 | 空 | `total, idList[]` |
| 模拟控制描述 | `0C:81` | — | 4 | `[profileId, controlId, index]` | `classId, controlId, type, index` |
| 摇杆灵敏度 | `0C:82` | `0C:02` | 3 | set `[profileId, controlId, sensitivity]`；get `[profileId, controlId]` | `classId, controlId, sensitivity` |
| 死区/行程区 | `0C:85` | `0C:05` | 9 | set `[profileId, controlId, deadZoneBE16, softBumperBE16, deltaThresholdBE16, preventDoubleDeadzone]`；get `[profileId, controlId]` | 同 9 字节结构 |
| 震动缩放 | `0C:86` | `0C:06` | 3 | set `[profileId, motorId, scaling]`；get `[profileId, motorId]` | `classId, controlId, scaling` |
| 震动播放一次 | — | `0C:07` | 4 | 压缩参数与调用不一致，暂不作为可验证命令 | — |
| ADC masking | `0C:91` | `0C:11` | 2 | set `[maskBE16]`；get 空 | `maskBE16` |
| 摇杆圆形度 | `0C:92` | `0C:12` | 3 | `[profileId, controlId, option]` | `classId, controlId, option` |
| 扳机模拟行程 | `0C:93` | `0C:13` | 4 | `[profileId, controlId, startRange, endRange]` | 同 4 字节结构 |
| 扳机模式 | `0C:95` | `0C:15` | 3 | `[profileId, controlId, mode]`；`0=analog`、`1=digital` | `classId, controlId, mode` |
| 扳机数字调整 | `0C:96` | `0C:16` | 4 | `[profileId, controlId, actuationPoint, isRapidTrigger]` | 同 4 字节结构 |
| 忽略 D-pad 斜向 | `0C:97` | `0C:17` | 2 | `[profileId, option]` | `classId, option` |
| SOCD cleaning | `0C:9B` | `0C:1B` | 2 | `[profileId, option]` | `classId, option` |
| 控制器模式切换 | `0C:9C` | `0C:1C` | 2 | `[profileId, option]` | `classId, option` |
| 原始模拟输入 | `0C:98` | — | 80 | JS 把命令元组 `[80,12,152]` 本身当参数块 | `Lx, Ly, Rx, Ry, Lt, Rt` 各 BE16 |
| 校准数据 | `0C:99` | `0C:19` | 51 | 复杂结构，本次不展开 | 同结构 |
| 按键 ID 列表 | `02:84` | — | 80 | 空 | `total, idList[]` |
| 单键映射 | `02:8C` | `02:0C` | 80 | get `[profileId, buttonId, mode]`；set `[profileId, buttonId, mode, functionId, dataSize, ...data]` | `classId, buttonId, mode, functionId, dataSize, data[5]` |

`BE16` 是高字节在前：setter 先写 `E4(value)` 再写 `l7(value)`，parser 用 `Jz(data[n], data[n+1])` 读回。`0C:07` 的 `setAnalogVibrationMotorPlayOnce` 在压缩代码里出现 `o[0]=0,o[1]=n,o[2]=E4(i),o[3]=l7(i)`，但外层只传两个参数，参数语义不闭合，因此不列入逐字节可验证集合。

### 1.3 每个 PID

下表逐 PID 列出同一组共享命令的取值；没有发现任何 PID 使用不同的手柄 class:id。

| PID | 产品 | 灵敏度 (Get/Set) | 死区 (Get/Set) | 扳机 (Get/Set) | 震动 (Get/Set) | 按键映射 | 备注 |
|---|---|---|---|---|---|---|---|
| `0A45` | Wolverine V3 TE | `0C:82/02` | `0C:85/05` | `0C:93/13`, `0C:95/15`, `0C:96/16` | `0C:86/06` | `02:0C` | SageT2X；包内同时带 Sage/SagePC |
| `0A4C` | Wolverine V3 Pro | `0C:82/02` | `0C:85/05` | `0C:93/13`, `0C:95/15`, `0C:96/16` | `0C:86/06` | `02:0C` | SageT1X；包内同时带 Sage/SagePC |
| `0A57` | Wolverine V3 Pro PC | `0C:82/02` | `0C:85/05` | `0C:93/13`, `0C:95/15`, `0C:96/16` | `0C:86/06` | `02:0C` | SageT1PC；SagePC |
| `0A5A` | Wolverine V3 TE PC | `0C:82/02` | `0C:85/05` | `0C:93/13`, `0C:95/15`, `0C:96/16` | `0C:86/06` | `02:0C` | SageT2PC；SagePC |
| `0A74` | Wolverine V4 Pro | `0C:82/02` | `0C:85/05` | `0C:93/13`, `0C:95/15`, `0C:96/16` | `0C:86/06` | `02:0C` | SilverT1X；另有 `3975 Perfect180Feature`（UI feature，无 sendCommand） |
| `0A7C` | Wolverine V4 TE | `0C:82/02` | `0C:85/05` | `0C:93/13`, `0C:95/15`, `0C:96/16` | `0C:86/06` | `02:0C` | SilverT2X；同上 |
| `1025` | Wolverine V3 Pro PS | `0C:82/02` | `0C:85/05` | `0C:93/13`, `0C:95/15`, `0C:96/16` | `0C:86/06` | `02:0C` | SageT1PS；包内同时带 Sage/SagePC |
| `1030` | Raiju V3 Pro Signature | `0C:82/02` | `0C:85/05` | `0C:93/13`, `0C:95/15`, `0C:96/16` | `0C:86/06` | `02:0C` | 另有 `1840 rzSerial`（串口 connect/send/disconnect，无 sendCommand） |
| `1013` | Kitsune | N/A（无摇杆） | N/A | N/A（无扳机） | N/A（无马达） | `02:0C` + SOCD `0C:9B/1B` | `juliana` 默认含 `controllerDPadSOCD:"neutral"`；0x0C 表随包但模拟量不适用 |

按键映射的 `02:0C` / `02:8C` 来自 `rzDevice25` 基类（`main.*.js`，模块 `87969`）。`2620.*.js` 只包含 `rzDevice25Controller`，它继承模块 `87969` 的默认导出；把两个模块合并后，setter 和 getter 的常量块才能对上：

```js
b = new Uint8Array([80, 2, 12]);   // Set Single Button Assignment
N = new Uint8Array([80, 2, 140]);  // Get Single Button Assignment
```

setter 的 payload 是 `[profileId, buttonId, mode, functionId, dataSize, ...data]`，整体补齐到 80 字节；getter 的 payload 是 `[profileId, buttonId, mode]`。响应统一为 `[classId, buttonId, mode, functionId, dataSize, data[5]]`。
按键 ID 和功能 ID 来自由模块 `35922` 导出的 `Bx` 和 `rA`；`mode` 来自 `zN`（`NormalMode=0`、`HypershiftMode=1`）。

之前把 `02:8D` 当成 getter 是压缩常量块相邻关系造成的误判；`02:8D` 实际属于 `getSingleKeyAssignment`（`k=[80,2,141]`）。`02:0C`/`02:8C` 这一组现在按调用点逐字节闭合。

现有 `RazerButtonAssignmentProtocol` 用的是 class `0x08`（protocol 30），不要套到这批 protocol-25 手柄上；手柄 bundle 的 setter 明确用 `[80,2,12]`。

### 1.4 共享表与设备专属

**共享：**

- `0x0C` 手柄表：9 个 bundle 的 `2620.*.js` 中 32 个三元组逐项相同；灵敏度、死区、扳机、震动、SOCD、校准、原始输入都在这一张表里。
- `0x02` 按键映射表：来自 protocol-25 的 `rzDevice25` 基类，和键盘/鼠标共用；不是手柄专属。
- `0x00:0xD3/0x53`（USB enumerate）和 `0x00:0xD6/0x56`（PC mode LED）也随包，属于通用设备命令。

**设备专属：**

- 没有发现任何手柄专属的额外 class:id。`0A74`/`0A7C` 的 `3975.*.js` 只导出 `Perfect180Feature`，内部委托 `RzGenericFeatureManager`，没有 `sendCommand`、没有 `new Uint8Array([...])` 命令三元组。
- `1030` 的 `1840.*.js` 只导出 `rzSerial`（串口 connect/send/disconnect），没有 `sendCommand`，不能作为 HID 控制命令表。
- `1013` 的 `main.*.js` 有 `controllerDPadSOCD:"neutral"` 默认值，但 SOCD 仍走共享的 `0C:9B/1B`。
- 设备类差异在 `rzDevice25Sage` 与 `rzDevice25SagePC` 的灯光/电池/序列号辅助方法，不在控制器命令表。每个 bundle 同时带这两个类；静态包没有给出 PID→具体类的可读映射，但两者 `reportId=4`、`reportLength=91` 相同，不影响上面的集合。

## 2. 传输层

### 2.1 91 字节、report id 4

`rzDevice25Sage`（`0A4C` 中 `rzDevice25Sage.b9b57f19edfe6aa9f049.js`）：

```js
this.name="rzDevice25Sage";
this.reportLength=91;
this.reportId=void 0!==(null==c?void 0:c.reportId)?c.reportId:4;
```

`rzDevice25SagePC`（`rzDevice25SagePC.cc168544af1bfecc2162.js`）同样：

```js
this.reportLength=91;
this.reportId=void 0!==(null==d?void 0:d.reportId)?d.reportId:4;
```

`c.reportId` / `d.reportId` 来自构造参数；静态包中默认值和回退值都是 4，没有找到上层传入 0 的调用点。如果实机配置显式覆盖成 0，需要重新跑一次描述符/抓包确认。

`connectDevice` 把 report id 放在最前面：

```js
const t=[this.reportId,...a];
connectHidDevice({..., claimInterface:this.claimInterface, queryData:t})
```

`sendCommand` 的 payload 是 90 字节 `dataSend`，report id 单独传：

```js
const u=this._createDataSend(e,n,o);
...
payload:{dataSend:c.dataSend, reportId:this.reportId,
         reportLength:this.reportLength, protocol:"25"}
```

`_createDataSend` 明确创建 90 字节：

```js
const o=new Uint8Array(90);
o[0]=0; o[1]=txn; o[2]=0; o[3]=0; o[4]=0;
o[5]=e[0]; o[6]=e[1]; o[7]=e[2];
... o[88]=crc;
```

读取时 `hid.getFeatureReport` 也带 `reportId:this.reportId, reportLength:91`，返回 90 字节，再按 `c[1]` transaction、`c[5]` dataSize、`c[6]` class、`c[7]` command 校验。`[0]` 是 report id，`[1]` 是 status；这正好对应 `RazerFeatureReport` 的 91 字节布局，只是 `[0]` 从 0 变成 4。

### 2.2 claimInterface

`rzDevice` 基类构造函数：

```js
this.claimInterface=t;
void 0===this.claimInterface&&(this.claimInterface=0);
```

`rzDevice25Sage` / `SagePC` 没有覆盖 `claimInterface`，所以默认是 0；`connectDevice` 把它传给 `connectHidDevice`。如果实机把 vendor feature collection 放在别的 interface，静态包不能证明；需要 HID 描述符确认。

### 2.3 传输层现在按 prepared 请求的首字节取 report id

- `RazerFeatureReport.CreateRequestCore` 仍然保持 `report[0]=0`，protocol 25 现有设备行为不变。
- `RazerGamepadProtocol.Create` 把 `report[0]` 设成 4，再由 `QueryPreparedAsync` 从请求首字节取 `responseReportId`。
- `ValidatePreparedRequest` 不再要求首字节为 0；CRC、事务号、class/id 校验仍复用同一条 91 字节路径。
- `QueryPreparedAsync` 已用于现有 protocol 25 设备和新手柄；`SendBatchAsync` 不参与需要读回的配置命令。

### 2.4 不是 XInput / 64 字节 output

- `main.*.js` 里没有 `XInput` 字符串；配置路径是 `hid.sendFeatureReport` / `hid.getFeatureReport`，payload 带 `protocol:"25"`、`reportLength:91`。
- 没有发现手柄配置走 64 字节 output report。bundle 里的 64/65 字节类是 `rzDevice25AudioCamy`（音频），和手柄无关。
- 手柄的**游戏输入**仍可能走 XInput；本次只讨论配置/控制通道，不把 XInput 当作配置接口。

## 3. 卡点与下一步

1. **实机 HID 描述符**
   - 需要确认 vendor feature collection 的 report id 4、report length 91、interface number、usage page/usage。
   - 建议在连接 Wolverine/Raiju 的机器上读 `IOCTL_HID_GET_REPORT_DESCRIPTOR`，或抓一次 Synapse 的 `HidD_SetFeature`/`HidD_GetFeature`。

2. **按键映射的写入语义**
   - `02:0C`/`02:8C` 已闭合；但每个 `functionId` 的 `data` 含义不同（键码、宏 ID、Razer key 等），需要继续按功能逐个解析后才能做写入 UI。

3. **硬件功能存在性**
   - 命令表存在不等于每个 PID 都暴露该 UI。Kitsune 没有摇杆/扳机/马达，模拟量命令不适用；TE 是否有震动马达也应由设备 profile 或实机确认。

## 4. 证据索引

- `artifacts/razer-mw-full-cdn/bundles/0A4C/2620.82f73bd688720e4613d7.js`：`rzDevice25Controller` 类；32 个命令三元组；`setAnalogControlSensitivity`、`setAnalogControlZones`、`setAnalogVibrationMotorScaling`、`setAnalogTriggerRangeAdjustment`、`setTriggerMode`、`setDigitalTriggerAdjustment` 的实现和响应 parser。
- `artifacts/razer-mw-full-cdn/bundles/0A4C/rzDevice25Sage.b9b57f19edfe6aa9f049.js`：`reportLength=91`、`reportId=4`、`[this.reportId,...a]`。
- `artifacts/razer-mw-full-cdn/bundles/0A4C/rzDevice25SagePC.cc168544af1bfecc2162.js`：同样的 `reportLength=91`、`reportId=4`。
- `artifacts/razer-mw-full-cdn/bundles/0A4C/main.f0ca95429464cd09cbdb.js`：`_createDataSend` 的 90 字节布局、`sendCommand` 的 `reportId/reportLength/protocol:"25"`、`setSingleButtonAssignment` 的 `b=[80,2,12]`。
- 同一 `main.f0ca95429464cd09cbdb.js` 的模块 `87969`：`rzDevice25` 基类；`setSingleButtonAssignment` 用 `b=[80,2,12]`，`getSingleButtonAssignment` 用 `N=[80,2,140]`，响应 parser 为 `[classId, buttonId, mode, functionId, dataSize, data[5]]`。
- 同一 `main.f0ca95429464cd09cbdb.js` 的模块 `35922`：`Bx` 按键 ID、`rA` 功能 ID、`zN` 模式（`NormalMode=0`、`HypershiftMode=1`）。
- 同一 `main.f0ca95429464cd09cbdb.js` 的模块 `4442`：`YZ`/`CS`/`m4`/`Yc`/`BP` 等功能编码器；每个函数的数据布局以这里为准。
- 同一 `main.f0ca95429464cd09cbdb.js` 的模块 `7215`：`k4` 组类型、`zK` 手柄按键、`ZI` RazerKey、`r9` 宏字段等枚举。
- 同一 `main.f0ca95429464cd09cbdb.js` 的模块 `44305`：modifier 位标志、电源键、多媒体键、Windows 快捷键和滚动模式表。
- 同一 `main.f0ca95429464cd09cbdb.js` 的模块 `58701`：Xbox/PlayStation 手柄输入的 fwID 表。
- `artifacts/razer-mw-full-cdn/bundles/0A74/3975.6a92c3b6f4c11aacbf7f.js`：`Perfect180Feature`，无 `sendCommand`。
- `artifacts/razer-mw-full-cdn/bundles/1030/1840.79e8045662a8fdce4bed.js`：`rzSerial`，无 `sendCommand`。
- `artifacts/razer-mw-full-cdn/bundles/1013/main.8c755a3c43990d2c3900.js`：`controllerDPadSOCD:"neutral"` 默认值。
- `src/OpenSynapse.Windows/Protocols/RazerFeatureTransport.cs:170-181`：prepared 请求按首字节取响应 report id。
- `src/OpenSynapse.Windows/Protocols/RazerFeatureReport.cs:98-108,221-222`：prepared 请求的通用校验与 report id 匹配。

## 5. 已实现（2026-10-28）

协议层 `RazerGamepadProtocol` 已覆盖 bundle `2620` 的完整 `0C` 表；所有请求是 91 字节、report id 4。

| 功能 | get | set | payload |
|---|---|---|---|
| 控制 ID 列表 | `0C:80` | — | 80 字节零 |
| 模拟控制描述 | `0C:81` | — | `[profileId, controlId, index, 0]` |
| 摇杆灵敏度 | `0C:82` | `0C:02` | `[profileId, controlId, value]` |
| 死区/行程区 | `0C:85` | `0C:05` | `[profileId, controlId, dzBE16, sbBE16, dtBE16, prevent]` |
| 震动缩放 | `0C:86` | `0C:06` | `[profileId, motorId, scale]` |
| 扳机行程 | `0C:93` | `0C:13` | `[profileId, controlId, start, end]` |
| 扳机模式 | `0C:95` | `0C:15` | `[profileId, controlId, mode]` |
| 数字扳机 | `0C:96` | `0C:16` | `[profileId, controlId, actuationPoint, rapid]` |
| 摇杆圆形度 | `0C:92` | `0C:12` | `[profileId, controlId, 0=standard/1=circular]` |
| D-pad 斜向 | `0C:97` | `0C:17` | `[profileId, option]` |
| SOCD | `0C:9B` | `0C:1B` | `[profileId, option]` |
| 控制器模式 | `0C:9C` | `0C:1C` | `[profileId, option]` |
| ADC masking | `0C:91` | `0C:11` | `[maskBE16]`，无 profile/control |
| 原始输入 | `0C:98` | — | JS 把 `[80,12,152]` 本身当参数块；六个 BE16 |
| 校准数据 | `0C:99` | `0C:19` | 51 字节，`analogId` + 16 个 BE16 |
| 按键 ID 列表 | `02:84` | — | 80 字节零 |
| 单键映射 | `02:8C` | `02:0C` | 80 字节；get `[profileId, buttonId, mode]`，set `[profileId, buttonId, mode, functionId, dataSize, ...data]` |

响应解析按 bundle 的 parser 逐字段对应：`[classId, controlId, value...]`，16 位值全部大端。

传输层同步改成「prepared 请求自带 report id」：`QueryPreparedAsync` 用请求首字节作为响应 id，
`ValidatePreparedRequest` 不再要求 report id 为 0。现有 protocol 25 请求仍然是 0，行为不变。

设备层 `RazerGamepadService` 已按 9 个 PID 发现设备，读活动 profile、整个 `0C` 表和 `02:80` 按键列表；
`ReadButtonAssignmentsAsync` 可按 Normal/Hypershift 逐个读取 `02:8C`。写入做读回校验，失败时恢复旧值。
`RazerGamepadService.PollingProfile.cs` 已覆盖两套轮询率读写、Profile 列表和切换；0A57/0A74/0A7C 使用
report id 10，活动 profile 读取失败时不会在多 Profile 型号上猜测 profile 1。
`RazerGamepadService.Lighting.cs` 已按运行时 `0F:80/81/82/02/84/04` 探测区域、效果和亮度，只对 bundle
声明 `supportsLighting:true` 的 0A4C / 1013 开放。页面层已加入 `DeviceCategory.Gamepad`、Gamepad 面板、
连接设置和灯效卡；缺项自动折叠。

**仍缺**：实机 HID 描述符验证；1013 的矩阵行列与逐帧显示方向；0A4C/1013 灯效写入的实机接受度；9 个 PID 的轮询率与 Profile 读写在实机上的确认。

## 6. 功能编码（2026-10-28）

`RazerGamepadFunctionEncoder` 按模块 `4442` 的编码器逐项实现。所有函数共用 5 字节 `fnDataByte`，真正有效的字节数由 `dataSize` 指定。

| fnId | 功能 | dataSize | data 布局 |
|---|---|---|---|
| 0 | Off | 0 | — |
| 1 | ButtonCode | 1 | `[buttonId]` |
| 2 | KeyCode | 2 | `[modifierMask, hidUsage]` |
| 3 | MacroTypeI | 3 | `[macroIdBE16, repeatCount]`；Once 时 repeatCount=1 |
| 4 | MacroTypeII | 2 | `[macroIdBE16]`；按住循环 |
| 5 | MacroTypeIII | 2 | `[macroIdBE16]`；切换循环 |
| 6 | DPI | 1 或 5 | `[action]`；Clutch 时 `[5, xBE16, yBE16]` |
| 7 | Profile | 1 或 2 | `[action]`；Specific 时 `[3, profileId]` |
| 9 | PowerKeys | 1 | `[powerKey]` |
| 10 | MediaKeys | 2 | `[mediaCodeBE16]` |
| 11 | DoubleClick | 1 | `[1]` |
| 12 | ModeButtonkey | 1 | `[1]` |
| 13 | TurboModeKey | 4 | `[modifierMask, hidUsage, intervalBE16]` |
| 14 | TurboModeButton | 3 | `[buttonId, intervalBE16]` |
| 15 | MacroTypeIV | 2 | `[macroIdBE16]`；序列宏 |
| 16 | Controller | 1 | `[controllerFwId]` |
| 17 | RazerKey | 1 | `[razerKey]` |
| 18 | ScrollWheelMode | 1 | `[scrollAction]` |
| 21 | Win8ShortcutsKey | 1 | `[shortcutCode]` |

`intervalBE16 = floor(1000 / keysPerSecond)`；默认 7 次/秒时是 142（`0x008E`）。

`modifierMask` 是位标志：`LeftCtrl=1`、`LeftShift=2`、`LeftAlt=4`、`LeftGUI=8`、`RightCtrl=16`、`RightShift=32`、`RightAlt=64`、`RightGUI=128`。N-key rollover 打开且按键本身是 HID 224..231 时，模块会把 `data[0]` 写成对应的位标志、`data[1]=0`。

| 表 | 值 |
|---|---|
| DPI action | `1=Up`、`2=Down`、`5=Clutch`、`6=CycleUp`、`7=CycleDown` |
| Profile action | `1=Up`、`2=Down`、`3=Specific`、`4=CycleUp`、`5=CycleDown` |
| Scroll action | `1=ModeSwitch`、`2=Up`、`3=Down`、`4=CycleUp`、`5=CycleDown` |
| Power key | `129=SystemPower`、`130=Sleep`、`131=Wake` |
| Windows shortcut | `99=Screenshot`、`100=XboxGameBar`、`101=CycleApps` |
| Media key | `184=Eject`、`226=Mute`、`234=VolumeDown`、`233=VolumeUp`、`182=Previous`、`183=Stop`、`205=PlayPause`、`181=Next`、`671=CycleApps`、`111=BrightnessUp`、`112=BrightnessDown`、`545=Spotlight` |

Controller fwID（模块 `58701`；`1025`、`1030`、`1013` 使用 PlayStation 表，其余 Xbox 表）：

| 按键 | fwID |
|---|---|
| M1..M6 | 16..21 |
| A/Cross、B/Circle、X/Square、Y/Triangle | 32..35 |
| 左/右肩键、左/右扳机 | 36..39 |
| 左/右摇杆按下 | 40、41 |
| D-pad 上/下/左/右 | 44..47 |
| View/Create、Menu/Options、Touchpad | 52、53、59 |
| 左/右/全局灵敏度离合 | 55、56、58 |
| 左摇杆上/下/左/右 | 128..131 |
| 右摇杆上/下/左/右 | 132..135 |

`RazerKey` 表来自模块 `7215` 的 `ZI`：`Fn=1`、`RazerKey=2`、`GameMode=3`、`MacroRecord=4`、`BrightnessUp/Down=6/7`、`BacklightUp/Down=8/9`、`ProfileSwitch=80`、`Sniper=81`、`HyperShift=89`、`SnapTap=117`、`MicMute=216` 等。

