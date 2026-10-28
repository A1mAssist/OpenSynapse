# 手柄协议可行性：Wolverine V3/V4、Raiju V3、Kitsune

扫描日期：2026-10-28

范围：`0A45`、`0A4C`、`0A57`、`0A5A`、`0A74`、`0A7C`、`1025`、`1030`、`1013`。Stream Controller（`0D06`/`0D09`）不在本次范围。

方法：静态读取 `artifacts/razer-mw-full-cdn/bundles/<PID>/` 下的 `main.*.js`、`2620.*.js`、`rzDevice25Sage*.js`，不执行下载的 JavaScript；再与 `src/OpenSynapse.Windows/Protocols/RazerFeatureTransport.cs`、`RazerFeatureReport.cs` 对照。本文的“支持”指 middleware 命令表里存在该命令，不等于该 PID 的硬件一定暴露对应 UI。

## 结论

**`NeedsNewTransport`（report-id variant）**

- 这些手柄的配置通道是 **91 字节 HID feature report**，middleware 标记 `protocol:"25"`；不是 XInput，也不是 64 字节 output report。
- 帧格式与现有 `RazerFeatureReport` 相同：`[1]` status、`[2]` transaction、`[6]` dataSize、`[7]` class、`[8]` command、`[9..]` arguments、`[89]` XOR CRC、`[90]` 0。
- 但 **静态包中的 report id 为 4**，不是现有 transport 的 0。`rzDevice25Sage` 和 `rzDevice25SagePC` 都显式设置 `reportId=4`、`reportLength=91`；构造参数理论上可以覆盖，但静态包里没有找到传入 0 的调用点。
- 现有 `RazerFeatureTransport` 在发送、读取、校验三处写死 0，不能直接使用：
  - `RazerFeatureReport.CreateRequestCore` 不写 `report[0]`，保持 0；
  - `QueryAsync` / `QueryPreparedAsync` 传 `responseReportId: 0`；
  - `ValidatePreparedRequest` 要求 `report[0] == 0x00`；
  - `MatchesReportId(response, 0)` 只接受 `response[0] == 0`。
- 这不是 `NotReachable`：命令表清楚，缺的是 report-id-aware 的传输层。最小改动是把 report id 参数化（或新增一个 report 4 的 HID feature transport），91 字节帧和 CRC 本身可以复用。

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
| 死区/行程区 | `0C:85` | `0C:05` | 9 | set `[profileId, controlId, deadZoneLE16, softBumperLE16, deltaThresholdLE16, preventDoubleDeadzone]`；get `[profileId, controlId]` | 同 9 字节结构 |
| 震动缩放 | `0C:86` | `0C:06` | 3 | set `[profileId, motorId, scaling]`；get `[profileId, motorId]` | `classId, controlId, scaling` |
| 震动播放一次 | — | `0C:07` | 4 | 压缩参数与调用不一致，暂不作为可验证命令 | — |
| ADC masking | `0C:91` | `0C:11` | 2 | set `[maskLE16]`；get 空 | `maskLE16` |
| 摇杆圆形度 | `0C:92` | `0C:12` | 3 | `[profileId, controlId, option]` | `classId, controlId, option` |
| 扳机模拟行程 | `0C:93` | `0C:13` | 4 | `[profileId, controlId, startRange, endRange]` | 同 4 字节结构 |
| 扳机模式 | `0C:95` | `0C:15` | 3 | `[profileId, controlId, mode]`；`0=analog`、`1=digital` | `classId, controlId, mode` |
| 扳机数字调整 | `0C:96` | `0C:16` | 4 | `[profileId, controlId, actuationPoint, isRapidTrigger]` | 同 4 字节结构 |
| 忽略 D-pad 斜向 | `0C:97` | `0C:17` | 2 | `[profileId, option]` | `classId, option` |
| SOCD cleaning | `0C:9B` | `0C:1B` | 2 | `[profileId, option]` | `classId, option` |
| 控制器模式切换 | `0C:9C` | `0C:1C` | 2 | `[profileId, option]` | `classId, option` |
| 原始模拟输入 | `0C:98` | — | 80 | 空 | `Lx, Ly, Rx, Ry, Lt, Rt` 各 LE16 |
| 校准数据 | `0C:99` | `0C:19` | 51 | 复杂结构，本次不展开 | 同结构 |

`LE16` 是低字节在前：setter 先写 `E4(value)` 再写 `l7(value)`，parser 用 `Jz(data[n], data[n+1])` 读回。`0C:07` 的 `setAnalogVibrationMotorPlayOnce` 在压缩代码里出现 `o[0]=0,o[1]=n,o[2]=E4(i),o[3]=l7(i)`，但外层只传两个参数，参数语义不闭合，因此不列入逐字节可验证集合。

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

按键映射的 `02:0C` 来自 `rzDevice25` 模块（`main.*.js`，模块 `87969`）的 `setSingleButtonAssignment` 实现：它用 `b=[80,2,12]` 发命令，并把 `w="Set Single Button Assignment"` 作为描述。**注意**：同一常量块把 `R="Get Single Button Assignment"` 放在 `b` 旁边，把 `w` 放在 `k=[80,2,141]`（`02:8D`）旁边，压缩后的 name/array 配对有矛盾；因此只能确认 class 是 `0x02`，setter 运行时用 `0x0C`，但完整 payload 和 get 对应 id 还需要源码或抓包确认。

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

### 2.3 现有 `RazerFeatureTransport` 为什么不能直接用

`src/OpenSynapse.Windows/Protocols/RazerFeatureReport.cs`：

- `public const int Length = 91;`（第 5 行）
- `CreateRequestCore` 不写 `report[0]`，所以 91 字节缓冲的第一字节保持 0（第 108 行起）
- `ValidatePreparedRequest` 要求 `report[0] == 0x00`（第 98-100 行）
- `MatchesReportId` 用 `response[0] == reportId`（第 219 行）

`RazerFeatureTransport.cs`：

- `QueryAsync` 调 `CreateRequest(...)` 后走 `responseReportId: 0`（第 133-138 行）
- `QueryPreparedAsync` 也走 `responseReportId: 0`（第 178-183 行）
- 读取循环里 `MatchesReportId(candidate, responseReportId)`，所以只认 `candidate[0] == 0`（第 400 行）

`SendBatchAsync` 确实可以物理写入一个 `[0]=4` 的 91 字节缓冲（它直接 `HidD_SetFeature`，不校验首字节），但它不读响应；middleware 的 `sendCommand` 每次发送后都会 `getFeatureReport` 校验 transaction/class/command，所以 `SendBatchAsync` 不能作为完整的替代。

### 2.4 不是 XInput / 64 字节 output

- `main.*.js` 里没有 `XInput` 字符串；配置路径是 `hid.sendFeatureReport` / `hid.getFeatureReport`，payload 带 `protocol:"25"`、`reportLength:91`。
- 没有发现手柄配置走 64 字节 output report。bundle 里的 64/65 字节类是 `rzDevice25AudioCamy`（音频），和手柄无关。
- 手柄的**游戏输入**仍可能走 XInput；本次只讨论配置/控制通道，不把 XInput 当作配置接口。

## 3. 卡点与下一步

1. **传输层 report id 4**（当前唯一硬阻塞）
   - 现有 transport 的 91 字节帧、CRC、事务号、class/id 校验都可以复用；最小改动是给 `RazerFeatureTransport` 加 report id 参数，或在它旁边加一个 report-4 变体。
   - 发送时 `report[0]=4`，读取时 `HidD_GetFeature` 期望 `response[0]==4`；`ValidatePreparedRequest` 不能继续要求 0。
   - 这一步完成后，`0x0C` 的灵敏度/死区/扳机/震动命令可以逐字节实现并测试。

2. **实机 HID 描述符**
   - 需要确认 vendor feature collection 的 report id 4、report length 91、interface number、usage page/usage。
   - 建议在连接 Wolverine/Raiju 的机器上读 `IOCTL_HID_GET_REPORT_DESCRIPTOR`，或抓一次 Synapse 的 `HidD_SetFeature`/`HidD_GetFeature`。

3. **按键映射 payload 未闭合**
   - class `0x02` 可确认；setter 运行时用 `02:0C`，但常量块同时把 `02:0C` 标成 get、把 `02:8D` 标成 set，且 `setSingleButtonAssignment` 的 `fnDataByte`/`fnDataByteSize` 实参顺序与实现不一致。
   - 在拿到源码 map 或一次抓包之前，不要写映射命令；先实现四个模拟量功能。

4. **硬件功能存在性**
   - 命令表存在不等于每个 PID 都暴露该 UI。Kitsune 没有摇杆/扳机/马达，模拟量命令不适用；TE 是否有震动马达也应由设备 profile 或实机确认。

5. **本次未写代码、未跑测试**
   - 结论是 `NeedsNewTransport`，按约束只交文档。没有新增 `RazerGamepadProtocol.cs` / `RazerGamepadProtocolTests.cs`，没有改 UI、`DeviceCategory`、`RazerDeviceRegistry` 或设备表。

## 4. 证据索引

- `artifacts/razer-mw-full-cdn/bundles/0A4C/2620.82f73bd688720e4613d7.js`：`rzDevice25Controller` 类；32 个命令三元组；`setAnalogControlSensitivity`、`setAnalogControlZones`、`setAnalogVibrationMotorScaling`、`setAnalogTriggerRangeAdjustment`、`setTriggerMode`、`setDigitalTriggerAdjustment` 的实现和响应 parser。
- `artifacts/razer-mw-full-cdn/bundles/0A4C/rzDevice25Sage.b9b57f19edfe6aa9f049.js`：`reportLength=91`、`reportId=4`、`[this.reportId,...a]`。
- `artifacts/razer-mw-full-cdn/bundles/0A4C/rzDevice25SagePC.cc168544af1bfecc2162.js`：同样的 `reportLength=91`、`reportId=4`。
- `artifacts/razer-mw-full-cdn/bundles/0A4C/main.f0ca95429464cd09cbdb.js`：`_createDataSend` 的 90 字节布局、`sendCommand` 的 `reportId/reportLength/protocol:"25"`、`setSingleButtonAssignment` 的 `b=[80,2,12]`。
- `artifacts/razer-mw-full-cdn/bundles/0A74/3975.6a92c3b6f4c11aacbf7f.js`：`Perfect180Feature`，无 `sendCommand`。
- `artifacts/razer-mw-full-cdn/bundles/1030/1840.79e8045662a8fdce4bed.js`：`rzSerial`，无 `sendCommand`。
- `artifacts/razer-mw-full-cdn/bundles/1013/main.8c755a3c43990d2c3900.js`：`controllerDPadSOCD:"neutral"` 默认值。
- `src/OpenSynapse.Windows/Protocols/RazerFeatureTransport.cs:133-138,178-183,400`：report id 0 的发送/读取/匹配。
- `src/OpenSynapse.Windows/Protocols/RazerFeatureReport.cs:5,98-100,108,219`：91 字节长度、`report[0] == 0` 校验、report id 匹配。

## 5. 已实现（2026-10-28，协议层）

`src/OpenSynapse.Windows/Protocols/RazerGamepadProtocol.cs` 已经落地，覆盖五个 payload 已从 JS 闭合的命令对：

| 功能 | get | set | payload |
|---|---|---|---|
| 摇杆灵敏度 | `0C:82` | `0C:02` | `[controlId, index, value]` |
| 死区/行程区 | `0C:85` | `0C:05` | `[controlId, index, dzHi,dzLo, sbHi,sbLo, dtHi,dtLo, prevent]` |
| 震动缩放 | `0C:86` | `0C:06` | `[controlId, index, scale]` |
| 扳机行程 | `0C:93` | `0C:13` | `[controlId, index, start, end]` |
| 扳机模式 | `0C:95` | `0C:15` | `[controlId, index, mode]` |

响应解析按 bundle 的 parser 逐字段对应：`[classId, controlId, value...]`，zone 的三个 16 位值是大端。
所有请求是 91 字节、report id 4。

传输层同步改成「prepared 请求自带 report id」：`QueryPreparedAsync` 用请求首字节作为响应 id，
`ValidatePreparedRequest` 不再要求 report id 为 0。现有 protocol 25 请求仍然是 0，行为不变。

**仍缺**：`setSingleButtonAssignment` 的 class 0x02 payload（bundle 里 get/set id 互相矛盾）、
`0C:96/16` 数字扳机调整（payload 未确认），以及设备页、`DeviceCategory` 和 discovery。

