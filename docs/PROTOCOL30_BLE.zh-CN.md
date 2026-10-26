<!-- English summary: Reconstructed the Razer BLE protocol 30 wire format from the unpacked middleware bundles: GATT UUIDs, 8-byte request header, 20-byte response header with status at byte 7, command table, polling-rate and DPI encodings, plus connection/lock/retry semantics. -->

# Razer BLE Protocol 30 规格（rzDevice30）

本文只依据已解包的雷云中间件 JS 反推，不依据命令名猜字段。主证据是 `00E5/rzDevice30.e71aec827ca608fc8120.js`，并用 `00EF`、`00B8`、`00DE`、`0566` 下同名模块交叉核对。

## 0. 结论速览

- 基类 `rzDevice30` 默认 GATT：service `52401523f97c7f900e7f6c6f4e36db1c`，write `52401524...`，read/notify 都是 `52401525...`。
- 请求头固定 8 字节：`tx | len(3, little-endian) | class | cmd | param6 | param7`，payload 从偏移 8 开始，无 CRC。
- 响应头固定 20 字节（`Ht=20`）：status 在偏移 7；payload 从偏移 20 开始；长度字段在偏移 1..3，按 little-endian 解释。
- `transactionId` 为 0..31，31 后回绕到 0；基类构造函数初始化为 0。
- 发送重试 2 次，间隔 30 ms；响应 status=1（Busy）或 3（Failure）会重试，错误文本含 `timeout` 也会重试。
- 轮询率常量：1000/500/333/250/200/125/100 Hz 分别写 1/2/3/4/5/8/10。
- DPI Setting 是 `0B:01`（Set）/`0B:81`（Get），payload 6 字节，X/Y/Z 各 16-bit little-endian。
- **任务描述里“11:84 Set DPI Class Setting”与代码相反**：`0B:84` 是 Get，`0B:04` 才是 Set；见第 7 节。
- `rzDevice30Layla` 是另一套 GATT/帧格式（service `fd65`，8 字节响应头，长度字段 big-endian），不能套用基类解析。

## 1. 证据范围与读法

主要文件：

| 文件 | 作用 |
|---|---|
| `00E5/rzDevice30.e71aec827ca608fc8120.js` | 协议 30 基类、`_createDataSend`、`sendCommand`、命令方法 |
| `00E5/rzBLE.8cf2edd50be89f44a280.js` | `ble.devices` / `ble.send` / `ble.connect` 等 IPC 封装 |
| `00E5/main.e1de570a7adf6656f32f.js` | 常量模块 `35284`（vendor id、status 表）、字节工具模块 `52975` |
| `00E5/6120.65d8621c9f8551e22ecc.js` | 基类 `62023`、枚举模块 `32135`、工具模块 `52975` |
| `00E5/rzDevice30Layla.2ad17581411756d7183c.js` | `rzDevice30Layla` 变体 |
| `00EF/rzDevice30.a151c034893eb3df093e.js` 等 | 同代码交叉验证（常量、帧布局一致） |
| `00A2/main.12e8af9bc3e4a37e6b0a.js` | 旧构建里的 `rzDeviceStandardBLE`，用于对照 `openNotify`，不是 `rzDevice30` |
| `00E5/rzUSB.e6f58dc98cb2198b1d7b.js`、`00E5/rzDevice25Ex.1201a489f3faa584a138.js` | USB/HID 路径（`rzDevice25Ex` 继承 `rzDevice25` 的 HID 传输），不参与 BLE 协议 30；已检视，不作为本规格证据 |

读法约定：

- 命令数组 `[a,b,c]` 的第 0 个元素 `a` 是**本地缓冲区/包长提示**，不会上线；上线的是 `class=b`、`cmd=c`。例如 `[6,11,1]` 上线为 `0B:01`，`6` 只是 6 字节 payload 的提示。
- 所有多字节整数均为无符号；除特别说明外，帧内长度和 DPI 都是 little-endian。
- “未解出”表示在列出的文件里没有找到可支撑该结论的代码，不是猜测。

## 2. GATT 参数

### 2.1 基类默认值

`00E5/rzDevice30.js` 的 `class Ya extends o.Z` 构造函数：

```js
this.serviceUUID = a.Fc;
this.charactWriteUUID = a.Yk;
this.charactReadUUID = a._M;
this.charactNotifyUUID = a._M;
```

常量模块 `73064`：

```js
n.d(e, { Fc:()=>o, Yk:()=>i, _M:()=>a, ... });
const o = "52401523f97c7f900e7f6c6f4e36db1c";
const i = "52401524f97c7f900e7f6c6f4e36db1c";
const a = "52401525f97c7f900e7f6c6f4e36db1c";
```

因此基类默认：

| 角色 | 常量 | 值（32 hex） | 常规 UUID 写法 |
|---|---|---|---|
| Service | `Fc` | `52401523f97c7f900e7f6c6f4e36db1c` | `52401523-f97c-7f90-0e7f-6c6f4e36db1c` |
| Write characteristic | `Yk` | `52401524f97c7f900e7f6c6f4e36db1c` | `52401524-f97c-7f90-0e7f-6c6f4e36db1c` |
| Read characteristic | `_M` | `52401525f97c7f900e7f6c6f4e36db1c` | `52401525-f97c-7f90-0e7f-6c6f4e36db1c` |
| Notify characteristic | `_M` | `52401525f97c7f900e7f6c6f4e36db1c` | `52401525-f97c-7f90-0e7f-6c6f4e36db1c` |

注意：read 和 notify 复用同一个 characteristic。JS 原样把 32 hex 字符串传给 native，不做字节序转换；GATT 层是否需要反转由 native 负责。

### 2.2 交叉验证

| 构建 | 常量模块 | service | write | notify |
|---|---|---|---|---|
| `00E5` | `73064` | `52401523...` | `52401524...` | `52401525...` |
| `00EF` | `73064` | 同上 | 同上 | 同上 |
| `00DE` | `73064` | 同上 | 同上 | 同上 |
| `0566` | `73064` | 同上 | 同上 | 同上 |
| `00B8` | `93752`（常量名不同） | 同上 | 同上 | 同上 |

`00B8` 的模块 id 和导出名不同（`Ij/J$/ex` 等），但字符串值完全一致，说明不是单个构建的巧合。

### 2.3 `setUUID` 与子类覆盖

基类确实定义了：

```js
setUUID(t,e,n,o){
  t && (this.serviceUUID=t);
  e && (this.charactWriteUUID=e);
  n && (this.charactReadUUID=n);
  o && (this.charactNotifyUUID=o);
}
```

但在 `razer-mw-all-bundles-v2` 全量 8679 个 JS 中检索 `.setUUID(` 为 **0 次调用**；只有各构建 `rzDevice30` 里的一份定义。也就是说，现有代码路径没有用 `setUUID` 覆盖默认 UUID。

`rzDevice30Layla` 是直接在构造函数里改字段，不是调用 `setUUID`：

```js
this.serviceUUID = o.a;              // "fd65"
this.charactWriteUUID = o.j1;        // "416d00002d52617a6572424c4501f40a"
this.charactReadUUID = void 0;
this.charactNotifyUUID = o.zg;       // "416d00012d52617a6572424c4501f40a"
```

结论：基类默认 UUID 就是 `Fc/Yk/_M`；`setUUID` 是预留接口但未被使用；Layla 是显式字段覆盖，且使用另一套 UUID/帧格式。

## 3. 发送帧：`_createDataSend(t,e,n,o,i)`

### 3.1 参数

`00E5/rzDevice30.js`：

```js
_createDataSend(t,e,n,o,i){
  i > o.length && console.warn(...);
  i <= 0 && o.length < 255 && (i = o.length);
  const a = new Uint8Array(i + 8).fill(0);
  const r = this._getTransactionId();
  ...
}
```

| 参数 | 含义 |
|---|---|
| `t` | 命令数组 `[sizeHint, class, cmd]`；只用 `t[1]`、`t[2]` |
| `e` | 写入请求头 byte 6 的参数 |
| `n` | 写入请求头 byte 7 的参数 |
| `o` | payload（`Uint8Array`） |
| `i` | `packetSizeSet`；>0 时作为长度字段和缓冲区大小；<=0 且 payload <255 时自动取 payload 长度 |

`sendCommand(t,e,n=0,o=0,s=new Uint8Array([]),c=0,u=false,m=true)` 的前 6 个参数依次是：命令数组、描述、header6、header7、payload、packetSize。`u` 是 `withoutResponse`，`m` 是 `waitForLock`。

### 3.2 字节布局

请求总长 = `8 + packetSize`：

| 偏移 | 长度 | 字段 | 说明 |
|---:|---:|---|---|
| 0 | 1 | transaction id | `_getTransactionId()`，0..31 |
| 1 | 3 | payload length | 24-bit little-endian；**不含** 8 字节头 |
| 4 | 1 | command class | `t[1]` |
| 5 | 1 | command id | `t[2]` |
| 6 | 1 | param 6 | `sendCommand` 第 3 参；语义随命令变化 |
| 7 | 1 | param 7 | `sendCommand` 第 4 参；语义随命令变化 |
| 8 | `packetSize` | payload | 从 `o[0]` 开始；超出 `packetSize` 的部分被截断 |

`transactionId` 的取值与回绕：

```js
_getTransactionId(){
  return 31 === this.transactionId && (this.transactionId = 0),
         this.transactionId++;
}
```

- 初始值在基类 `62023` 构造函数中设为 `0`。
- 序列为 `0,1,...,31,0,1,...`，共 32 个值。
- 没有看到对响应 tx 的回显校验；实现时可以自行校验，但不要假设 JS 会校验。

### 3.3 长度字段编码

`_createDataSend` 原文：

```js
if (i > 65535) {
  const t = d.dV(i), e = d.TK(i);   // dV = i & 0xFFFF, TK = (i >> 16) & 0xFFFF
  a[1] = d.Zz(t);                   // Zz = t & 0xFF
  a[2] = d.ft(t);                   // ft = (t >> 8) & 0xFF
  a[3] = d.Zz(e);
} else if (i > 255) {
  a[1] = d.Zz(i);
  a[2] = d.ft(i);
  a[3] = 0;
} else {
  a[1] = i;
  a[2] = 0;
  a[3] = 0;
}
```

等价算法：

```text
len = packetSize
b1 = len & 0xFF
b2 = (len >> 8) & 0xFF
b3 = (len >> 16) & 0xFF
```

即标准的 24-bit little-endian。`_getDataLengthFromHeader` 也按同样方式读：

```js
_getDataLengthFromHeader(t,e){
  const n = 255 & t[e+0];
  const o = 255 & t[e+1];
  return (255 & t[e+2]) << 16 | o << 8 | n;
}
```

### 3.4 最大 payload

- 长度字段本身可表达 `0xFFFFFF`（16,777,215）字节。
- JS 没有显式上限检查；`new Uint8Array(i + 8)` 会按 `packetSize` 分配。
- 但如果调用方不传 `packetSize`（`i=0`）且 payload 长度 ≥255，`i` 不会被自动设置，长度字段保持 0，缓冲区只有 8 字节，payload 实际被丢弃。**因此“不显式传 packetSize”时，实际可用上限是 254 字节。**
- 需要 ≥255 字节时必须显式传 `packetSize`，例如 DPI Class 的 `7*n+3`。
- 长度字段只有 24 位；`packetSize > 0xFFFFFF` 时高 16 位会被 `Zz` 截断，长度字段回绕，JS 不报错。
- 如果 `packetSize` 大于 payload 长度，JS 只打 warning，长度字段仍写 `packetSize`，实际 payload 不足；不要这样调用。
- BLE 单次写入的真实上限由 native/MTU 决定；JS 侧没有 MTU 常量，见第 8 节。

### 3.5 CRC

在 `razer-mw-all-bundles-v2` 全量 JS 中检索 `crc` / `CRC` 均为 0 命中；`_createDataSend` 也没有 checksum/CRC 字段。帧尾直接是 payload。**JS 侧没有 CRC 计算或校验。**

### 3.6 真实调用示例：Get Device Mode

`getDeviceMode()` → `v.GU(this)` → 内部函数 `x(t)`：

```js
const e = yield t.sendCommand(c, "Get Device Mode", 0, 0);
```

其中 `c = new Uint8Array([0,1,130])`，上线 `class=0x01`、`cmd=0x82`。

下面的帧是按 `_createDataSend` 逐字节推导的，不是 HCI 抓包；tx 取决于调用顺序。假设设备对象刚创建、`transactionId=0`，第一次 `Get Device Mode` 的完整请求帧是 8 字节：

```text
00 00 00 00 01 82 00 00
│  │        │  │  │  └─ param7 = 0
│  │        │  │  └──── param6 = 0
│  │        │  └─────── cmd = 0x82
│  │        └────────── class = 0x01
│  └─────────────────── payload length = 0（24-bit LE）
└────────────────────── transaction id = 0
```

如果先执行了 `connectDevice()`，它会先发 `Get Edition Information`（`0x01:0x86`），消耗 tx=0；随后的 `Get Device Mode` 通常是：

```text
01 00 00 00 01 82 00 00
```

成功响应示例（mode=Normal(0)、param=0、status=2）：

| 偏移 | 字节 | 含义 |
|---:|---|---|
| 0 | `01` | tx |
| 1..3 | `02 00 00` | payload length = 2（24-bit LE） |
| 4 | `01` | class |
| 5 | `82` | cmd |
| 6 | `00` | param6（JS 不解析，示例置 0） |
| 7 | `02` | status = Command Successful |
| 8..19 | `00` × 12 | JS 固定跳过 |
| 20..21 | `00 00` | payload：mode=0、param=0 |

响应总长 22 字节。响应头 20 字节；偏移 8..19 这 12 字节 JS 固定跳过，语义未解出。示例中的 param6 置 0 只是示意；JS 不读它。

## 4. 接收帧：`sendCommand`

### 4.1 成功路径

`00E5/rzDevice30.js`：

```js
v.dataHeader = e.data.slice(0, a.Ht);
v.data = [...e.data.slice(a.Ht)];
v.packetSizeReturned = v.data.length;
const n = this._getDataLengthFromHeader(v.dataHeader, 1);
if (n > 0 && v.packetSizeReturned !== n) {
  const t = 8;
  v.dataHeader = e.data.slice(0, t);
  v.data = [...e.data.slice(t)];
  v.packetSizeReturned = v.data.length;
  if (v.packetSizeReturned !== n) {
    v.status = i.STATUS_FAIL;
    v.error = "Failed to get data";
  }
}
```

| 项 | 值 | 证据 |
|---|---|---|
| 响应头长度 | `a.Ht = 20` | 常量模块 `73064`：`m = 20`，导出 `Ht` |
| payload 起始偏移 | 20 | `e.data.slice(a.Ht)` |
| 状态字节位置 | 偏移 7 | `const n = e.data[7]` |
| payload 长度校验字段 | 偏移 1..3，24-bit little-endian | `_getDataLengthFromHeader(header, 1)` |
| 头内未解析区域 | 偏移 8..19 | 固定跳过 |
| 长度不匹配回退 | 改用 8 字节头 | 上面的 `const t = 8` 分支 |

- JS 不校验响应 tx、class、cmd 是否与请求一致；实现时可以自行校验。
- JS 没有检查 `e.data.length >= 20`；响应短于 20 字节时 `slice` 仍会返回短头，后续字段可能是 `undefined`。实现时应先校验最小长度。

回退逻辑的含义：如果响应头按 20 字节切分后，声明长度大于 0 且与实际 payload 长度不符，再把前 8 字节当头、其余全部当 payload 重试一次；仍不符则 `FAIL: "Failed to get data"`。实现时建议保留同样的两段式解析，否则某些设备变体可能被误判。

### 4.2 成功判定

```js
if (!e.state) { ...失败路径... }
v.status = i.STATUS_SUCCESS;
v.error = "";
```

JS 的成功条件是 native `ble.send` 返回的 `e.state === true`，**不是直接比较 status 字节等于 2**。`RZ_STATUS_DESC[2]` 虽然是 `"Command Successful"`，但只在失败路径用于错误文本。实现时可以把 status=2 视为成功，但不要假设 JS 已经校验了它。

### 4.3 失败路径与重试码

```js
if (!e.state) {
  v.status = i.STATUS_FAIL;
  v.error = e.error;
  if (e.error.includes(a.J2)) continue;       // J2 = "timeout"
  const n = e.data[7];
  v.error = i.RZ_STATUS_DESC[n];
  if (n === a.w$ || n === a.QC) continue;       // w$ = 1, QC = 3
  return v.dataHeader = e.data.slice(0, e.data.length), ...
}
```

常量：

| 常量 | 值 | 含义 |
|---|---:|---|
| `J2` | `"timeout"` | 错误文本包含它时重试 |
| `w$` | 1 | Command Busy |
| `QC` | 3 | Command Failure |
| `mc` | 2 | 发送总尝试次数 |
| `Bx` | 30 | 重试间隔（ms） |

`RZ_STATUS_DESC`（模块 `35284`）完整表：

| status | 文本 |
|---:|---|
| 0 | New Command |
| 1 | Command Busy |
| 2 | Command Successful |
| 3 | Command Failure |
| 4 | Command No Response Timeout1 |
| 5 | Command Not Support (Command or Command Class) |
| 6 | Profile Not Support |
| 7 | Target ID Not Support |
| 8 | Command Grab Control |
| 9 | Command Not Support in current Device Mode |
| 10 | Command Broadcast |
| 11 | Command Unknown |

注意：status=4（No Response Timeout1）**不在** `w$/QC` 里，不会因为状态码本身重试；只有 native 返回的错误文本包含 `"timeout"` 才会重试。status=2 在失败路径会显示 `"Command Successful"`，说明 `e.state` 与 status 字节可能不一致；实现时应以 native 的 `state` 为准，或自己定义“status=2 即成功”。

### 4.4 发送结果对象

`sendCommand` 返回/抛出的对象至少包含：

| 字段 | 含义 |
|---|---|
| `status` | `SUCCESS` / `FAIL` / `SKIP` |
| `error` | 错误文本；成功时为空 |
| `command` / `commandDesc` | 命令数组 / 描述 |
| `dataSend` | 实际发送的完整帧（数组） |
| `dataHeader` | 响应头（通常 20 字节；回退时 8 字节） |
| `data` | 响应 payload（数组） |
| `packetSizeReturned` | `data.length` |
| `jsonData` | 命令方法附加的解析结果 |

失败时抛出 `rzError`，带 `commandClass`、`commandId`、`commandDesc`、`command`、`dataSend`、`productId`。

## 5. 命令方法表

下表只列基类/共享类里实际调用 `sendCommand` 的方法。`class:cmd` 用十六进制表示；括号内是十进制。所有方法都在 `00E5/rzDevice30.e71aec827ca608fc8120.js` 中，模块号见表后说明。

### 5.1 基础与电池命令

| 功能 | JS 方法 / 内部函数 | class:cmd | 方法参数个数 | payload 构造 | 响应 `jsonData` |
|---|---|---|---:|---|---|
| Get Device Mode | `getDeviceMode()` → `x(t)` | `01:82` (1:130) | 0 | 无；header6=0，header7=0 | `mode=data[0]`，`param=data[1]`，`modeEnum` |
| Set Device Mode | `setDeviceMode(mode,param)` → `j(t,e,n)` | `01:02` (1:2) | 2 | `Uint8Array(2)`：`[mode,param]`；header6/7=0 | `mode`，`param`，`modeEnum` |
| Get Serial Number | `getSerialNumber()` → `H(t)` | `01:83` (1:131) | 0 | 无 | `serialNumber=data`（原始数组），`serialNumberAscii`（可打印 ASCII） |
| Get Device Polling Period | `getPollingRate()` → `B(t)` | `01:84` (1:132) | 0 | 无 | `pollingRate`（映射见第 6 节） |
| Set Device Polling Period | `setPollingRate(rate)` → `G(t,e)` | `01:04` (1:4) | 1 | `Uint8Array(1)`：`[rateConstant]` | `pollingRate` |
| Get Firmware Version | `getFirmwareVersion()` | `00:81` (0:129) | 0 | 无；header6/7=0 | `major=data[0]`，`minor=data[1]`，`internal=data[2]`，`reserved=data[3]` |
| Get Extended Firmware | `getExtendedFirmwareVersion()` | `00:81` (0:129) | 0 | 无；header6/7=0 | 同上；**与普通固件命令完全相同** |
| Get Dock Firmware | `getDockFirmwareVersion()` | `00:81` + header7=4 | 0 | 无 | `major=data[0]`，`minor=data[1]` |
| Get Battery List | `getBatteryList(id)` | `05:80` (5:128) | 1 | 无；header7=id | `batteryList=data` |
| Get Battery Level | `getBatteryLevel(id)` | `05:81` (5:129) | 1 | 无；header7=id | `batteryId`，`batteryLevel=floor(data[0]/255*100)` |
| Get Charging Status | `getChargingStatus(id)` | `05:85` (5:133) | 1 | 无；header7=id | `batteryId`，`chargingStatus=data[0]`，`chargingStatusEnum` |
| Get Device Wireless Connection Status | `getDeviceWirelessConnectionStatus()` → `Q(t)` | `01:93` (1:147) | 0 | 无 | `connected=data[0]`（但与另一个解析器冲突，见 5.3） |
| Get Device On Dock Charging Status | `getDeviceOnDockChargingStatus()` → `tt(t)` | `01:90` (1:144) | 0 | 无 | `chargingStatus=data[0]` |

补充：

- `Get Serial Number` 的 `serialNumberAscii` 只保留 `32 <= byte < 127` 的字符；`serialNumber` 本身是完整原始数组，包含尾部 0。
- `Get Extended Firmware` 没有独立的“扩展”命令；`getFirmwareVersion` 和 `getExtendedFirmwareVersion` 都发 `00:81`，解析同样 4 字节。模块 `70060` 里还有一个未被调用的 `m=[0,0,131]` 常量，不能据此认定它是扩展固件命令。
- `getDockFirmwareVersion` 也用 `00:81`，只是 header7=4，并只解析前两字节。
- `getBatteryLevel` 内部 `try/catch` 会吞掉异常并返回 `undefined`；实现时不要把 `undefined` 当成 0%。
- `Get Battery Level` 的 0..255 → 0..100 使用 `Math.floor`；`rzDevice30Oled` 变体用 `Math.round`，两者不要混用。

模式枚举（`r.r1`，用于 `modeEnum`）：

| 值 | 名称 |
|---:|---|
| 0 | Normal_Mode |
| 1 | Bootloader_Mode |
| 2 | Testing_Mode |
| 3 | Driver_Mode |
| 4 | Download_Mode |
| 5 | Pairing_Mode |
| 6 | Standby_Mode |
| 8 | RF_Test_Mode |
| 9 | Retail_Mode |

充电状态枚举（`x`，用于 `chargingStatusEnum`）：

| 值 | 名称 |
|---:|---|
| 0 | NoCharge_BatteryFull |
| 1 | Charging |
| 2 | ChargingFailed |
| 6 | ReachChargingLimit |

`Get Device On Dock Charging Status` 只返回原始 `chargingStatus=data[0]`，没有在这个函数里做枚举映射。

### 5.2 DPI 命令

| 功能 | JS 方法 / 内部函数 | class:cmd | 方法参数个数 | payload 构造 | 响应 `jsonData` |
|---|---|---|---:|---|---|
| Get DPI Setting | `getDpiLevel(profileId)` → `K` | `0B:81` (11:129) | 1 | 无；header6=profileId，header7=0 | `dpiX`，`dpiY`，`dpiZ` |
| Set DPI Setting | `setDpiLevel(profileId,x,y)` → `Y` | `0B:01` (11:1) | 3 | `Uint8Array(6)`：X/Y/Z 各 2 字节 LE（Z 固定 0）；header6=profileId | 无 |
| Get DPI Class Setting | `getDPIStages(profileId)` → `q` | `0B:84` (11:132) | 1 | 无；header6=profileId | `dpiActiveStage`，`dpiNumOfStage`，`dpiStages`（扁平数组） |
| Set DPI Class Setting | `setDPIStages(profileId,activeStage,numStages,array)` → `Q` | `0B:04` (11:4) | 4 | 255 字节缓冲区；实际 `packetSize=7*numStages+3`；header6=profileId | 用 Get 的 `ot` 解析器解析响应 |

**重要更正：** 任务描述里的“11:84 Set DPI Class Setting”与代码相反。`00E5` 以及 `00EF/00B8/00DE/0566` 都是：

```js
q = (..., [0,11,132]);   // Get DPI Class Setting -> 0B:84
Q = [255,11,4];          // Set DPI Class Setting -> 0B:04
```

`q` 的声明里还包含若干未使用的数组（`0B:82`、`0B:83`、`0B:84`、`0B:01`），这是逗号表达式，最终 `q` 取最后一个 `[0,11,132]`。不要被中间的 `[0,11,131]` 误导。

### 5.3 `Get Device Wireless Connection Status` 的解析冲突

同一个命令 `01:93` 在 `95008` 里有两个解析器：

```js
// Q：getDeviceWirelessConnectionStatus() 使用
const e = yield t.sendCommand(_, C, 0, 0);
return e.jsonData = { connected: e.data[0] }, e;

// it：getActiveWirelessConnectionState() 使用
const e = yield t.sendCommand(_, C, 0, 0);
return e.jsonData = {
  type: e.data[0],
  typeEnum: a.oW(r.Rk, e.data[0]),
  status: e.data[1],
  statusEnum: a.oW(r.gW, e.data[1])
}, e;
```

`_ = [0,1,147]`，`C = "Get Device Wireless Connection Status"`。两个方法发的是同一帧，但一个把 `data[0]` 当 `connected`，另一个把 `data[0]` 当 `type`、`data[1]` 当 `status`。

可确认的枚举：

```js
Rk = { NotApplicable:0, "2.4G":1, BLE_PC:2, BLE_MOBIL:3 };
gW = { Disconnected:0, Connected:1 };
```

实现建议：按至少 2 字节 payload 解析 `[type,status]`，用 `status===1` 判定已连接；`connected=data[0]` 很可能是旧解析器/误用。但**哪一个是设备真实语义，JS 证据不足，标为未解出**。不要只读 `data[0]` 就断言“已连接”。

### 5.4 方法所在模块

| 模块号 | 内容 |
|---|---|
| `95008` | 基础命令 helper：Device Mode、Serial、Polling、Wireless、Dock、DPI 等 |
| `70060` | 继承基类的主类 `rzDevice30`，以及固件/电池命令 |
| `62023` | 更底层的 `rzDevice` 基类（`transactionId=0` 等） |
| `73064` | BLE 常量：UUID、`Ht=20`、重试码、sleep |
| `32135` | 枚举：轮询率、模式、无线类型/状态 |
| `52975` | 字节工具：`Zz`、`ft`、`dV`、`TK`、`U1`、`oW` |

## 6. 轮询率映射

常量模块 `32135`：

```js
const o = {
  RATE_1000Hz: 1,
  RATE_500Hz: 2,
  RATE_333Hz: 3,
  RATE_250Hz: 4,
  RATE_200Hz: 5,
  RATE_125Hz: 8,
  RATE_100Hz: 10
};
```

写入（`setPollingRate` → `G`）：

```js
switch (e) {
  case 1000: a = RATE_1000Hz; break;
  case 500:  a = RATE_500Hz;  break;
  case 333:  a = RATE_333Hz;  break;
  case 250:  a = RATE_250Hz;  break;
  case 200:  a = RATE_200Hz;  break;
  case 125:  a = RATE_125Hz;  break;
  default:   a = RATE_100Hz;
}
n[0] = a;
yield t.sendCommand(v, f, 0, 0, n);
```

读取（`getPollingRate` → `B`）：

```js
switch (t.data[0]) {
  case RATE_1000Hz: e = 1000; break;
  case RATE_500Hz:  e = 500;  break;
  case RATE_333Hz:  e = 333;  break;
  case RATE_250Hz:  e = 250;  break;
  case RATE_200Hz:  e = 200;  break;
  case RATE_125Hz:  e = 125;  break;
  case RATE_100Hz:
  default:          e = 100;
}
```

映射表：

| 逻辑速率 | payload 值（十进制/hex） |
|---:|---:|
| 1000 Hz | 1 / `0x01` |
| 500 Hz | 2 / `0x02` |
| 333 Hz | 3 / `0x03` |
| 250 Hz | 4 / `0x04` |
| 200 Hz | 5 / `0x05` |
| 125 Hz | 8 / `0x08` |
| 100 Hz | 10 / `0x0A` |

行为细节：

- Set 只对 `undefined` 抛 `ERROR_INVALID_INPUT_VALUE`；其他未知值（如 800）会落 `default`，实际写 100 Hz，不报错。
- Get 对未知 `data[0]` 也落 `default`，返回 100 Hz。
- Set 返回的 `pollingRate` 是按“实际写进去的值”反查，不是读设备回显。
- `getProfilePollingRate` / `setProfilePollingRate` 用同一套映射，命令是 `01:8C`（Get，header6=profileId）和 `01:04`（Set，header6=profileId，payload 1 字节）。

## 7. DPI

### 7.1 `0B:01` Set DPI Setting：6 字节布局

`setDpiLevel(profileId,x,y)` → 内部函数 `Y`：

```js
const s = new Uint8Array(Y[0]);   // Y[0] = 6
s[0] = d.Zz(x);  s[1] = d.ft(x);  // X low, high
s[2] = d.Zz(y);  s[3] = d.ft(y);  // Y low, high
s[4] = d.Zz(z);  s[5] = d.ft(z);  // Z low, high
yield t.sendCommand(Y, X, profileId, 0, s);
```

| payload 偏移 | 长度 | 字段 | 编码 |
|---:|---:|---|---|
| 0 | 2 | DPI X | uint16 little-endian |
| 2 | 2 | DPI Y | uint16 little-endian |
| 4 | 2 | DPI Z | uint16 little-endian |

- `Zz(v)=v&0xFF`，`ft(v)=(v>>8)&0xFF`。
- header6 = `profileId`，header7 = 0。
- **payload 没有 stage 编号字段。** 代码里唯一的“stage/profile”选择是 header6；`getDpiLevel` / `setDpiLevel` 的调用参数名是 `profileId`。stage 编号只在 DPI Class Setting 的 7 字节记录里出现。
- 公开方法只接收 `profileId`、x、y；内部 Z 参数默认 0，调用方无法通过 `setDpiLevel` 传入非零 Z。
- 没有范围校验/限幅。`setDpiLevel` 只检查 `profileId` 未定义或 `isNaN`、x/y 未定义；不检查 x/y 是否 NaN，也不做范围限幅。0 和大于设备上限的值都会原样编码。**实际 DPI 最小/最大值未解出**（全量 JS 检索 `dpiMax`/`maxDpi`/`DPI_MAX`/`dpiMin`/`DPI_MIN` 均为 0 命中）。

### 7.2 `0B:81` Get DPI Setting：返回布局

`getDpiLevel(profileId)` → `K`：

```js
const e = yield t.sendCommand(K, J, profileId, 0);
t.jsonData = {
  dpiX: d.U1(t.data[1], t.data[0]),
  dpiY: d.U1(t.data[3], t.data[2]),
  dpiZ: d.U1(t.data[5], t.data[4])
};
```

- 响应 payload 6 字节，布局与 Set 相同：X/Y/Z 各 2 字节 LE。
- `U1(high,low) = ((high & 0xFF) << 8) | (low & 0xFF)`；所以 `U1(data[1],data[0])` 是 little-endian。
- header6 = `profileId`。
- 没有检查 `data.length >= 6`；短包会产生 `undefined`/`NaN`。

### 7.3 `0B:84` Get DPI Class Setting

`getDPIStages(profileId)` → `q`，命令 `[0,11,132]`。响应解析器 `ot`：

```js
const e = [];
for (let n = 2; n < t.data.length; n += 7) {
  const o = t.data[n];
  e.push(o);
  e.push(d.U1(t.data[n+2], t.data[n+1]));  // X LE
  e.push(d.U1(t.data[n+4], t.data[n+3]));  // Y LE
  e.push(d.U1(t.data[n+6], t.data[n+5]));  // Z LE
}
t.jsonData = {
  classId: void 0,
  dpiActiveStage: t.data[0],
  dpiNumOfStage: t.data[1],
  dpiStages: e
};
```

响应 payload：

| 偏移 | 长度 | 字段 |
|---:|---:|---|
| 0 | 1 | `dpiActiveStage` |
| 1 | 1 | `dpiNumOfStage` |
| 2 | 7×N | 每个 stage：stage 编号(1) + X(2 LE) + Y(2 LE) + Z(2 LE) |

`dpiStages` 是扁平数组：`[stage0, x0, y0, z0, stage1, x1, y1, z1, ...]`。`classId` 在 JS 里被显式设为 `undefined`，不要把它当有效字段。

### 7.4 `0B:04` Set DPI Class Setting：255 字节布局

`setDPIStages(profileId,activeStage,numStages,array)` → `Q=[255,11,4]`：

```js
const s = 7 * numStages + 3;
const c = new Uint8Array(Q[0]);   // 255 字节缓冲区
c[0] = activeStage;
c[1] = numStages;
for (let t = 0, e = 0; t < numStages; t++, e += 4) {
  c[7*t+2] = array[e];                    // stage 编号
  c[7*t+3] = d.Zz(array[e+1]);            // X low
  c[7*t+4] = d.ft(array[e+1]);            // X high
  c[7*t+5] = d.Zz(array[e+2]);            // Y low
  c[7*t+6] = d.ft(array[e+2]);            // Y high
  c[7*t+7] = d.Zz(array[e+3]);            // Z low
  c[7*t+8] = d.ft(array[e+3]);            // Z high
}
yield t.sendCommand(Q, et, profileId, 0, c, s);
```

| 偏移 | 长度 | 字段 |
|---:|---:|---|
| 0 | 1 | `activeStage` |
| 1 | 1 | `numStages` |
| 2 | 7×N | stage 编号(1) + X(2 LE) + Y(2 LE) + Z(2 LE) |
| `7N+2` | 1 | 未使用/填充 0（因为分配 255 且 `packetSize=7N+3`） |

关键点：

- `Q[0]=255` 是**缓冲区大小**，不是实际发送长度。实际 `packetSize=7*numStages+3`，`_createDataSend` 只发送前 `7N+3` 字节。
- 输入数组 `array` 每个 stage 4 个值：`[stage, X, Y, Z]`。
- 255 字节缓冲区最多容纳 `floor((255-3)/7)=36` 个 stage；超过 36 时 `Uint8Array` 的越界写入会被静默忽略，但 `packetSize` 仍按 `7N+3` 增长。JS 没有检查 `numStages` 上限。
- payload 的 stage 记录从偏移 2 开始，7 字节一条；与 Get 的响应布局一致。
- 没有 DPI 范围校验/限幅（同 7.1）。
- **再次强调：`0B:04` 是 Set，`0B:84` 是 Get。** 如果按任务原文把 84 当 Set，会写到错误的命令。

## 8. BLE 连接前提、锁与重试

### 8.1 设备发现

`rzBLE.js` 的 `getDevices()`：

```js
const n = yield (0, i.wU)({ action: "ble.devices" });
this.devices = n
  .filter(t => r.RAZER_BLE_VENDOR_IDS.includes(t.vendorId))
  .map(t => ({ ...t, productName: t.name }));
```

`RAZER_BLE_VENDOR_IDS`（模块 `35284`）：

```js
const n = 5426;
const r = [5426, 1678];
```

| 十进制 | 十六进制 | 说明 |
|---:|---:|---|
| 5426 | `0x1532` | Razer USB vendor id |
| 1678 | `0x068E` | BLE 枚举中出现的第二个 Razer vendor id |

发现路径：

- `ble.devices` 取当前系统 BLE 设备列表，按 `vendorId` 过滤。
- `ble.startScan` 的 payload 是 `{ scanTime, minRssi }`；`ble.stopScan` 停止扫描。
- 设备对象至少包含 `mac`、`productId`、`name`、`deviceContainerId`、`vendorId`。
- `getDeviceWithContainerId(productId, containerId)`：当 `containerId` 为 `undefined`、空串、`-1`、`"-1"` 或 `0` 时只按 `productId` 匹配；否则按 `productId + deviceContainerId` 匹配。
- 设备事件通过 `rz-ble-system-notify` 推送：`new-ble-device`、`update-ble-device`、`remove-ble-device`、`disconnect-ble-device`、`start-scan-ble`、`stop-scan-ble`。

### 8.2 连接流程

`rzDevice30.connectDevice()`：

```js
const n = [this.serviceUUID];
const o = [this.charactWriteUUID, this.charactNotifyUUID];
yield navigator.locks.request(this.lockName, { ifAvailable: false }, ...);
for (let i = 0; i < a.Fm; i++) {                 // Fm = 2
  const r = yield this.rzBLE.connect(
    this.macAddress, n, o, this.protocolName, this.rzBLE.guid
  );
  if (r.state) { e.result = true; break; }
  e.error = r.error;
  yield d._v(a.Gj);                              // Gj = 150 ms
}
if (e.result) {
  const n = yield v.ki(this);                    // Get Edition Information, 01:86
  if (n.status !== i.STATUS_SUCCESS) {
    e.error = n.error;
    e.result = false;
  }
}
```

要点：

- `ble.connect` 的 payload 是 `{ name, mac, serviceUUIDs:[service], charactUUIDs:[write, notify], guid }`。
- 连接最多 2 次，失败间隔 150 ms。
- 连接成功后还要发 `Get Edition Information`（`01:86`）验证设备可通信；该命令失败则 `connectDevice()` 返回 `result:false`。
- `connectDevice` 没有调用 `rzBLE.openNotify`。`rzBLE.openNotify` 只是定义了 `action:"ble.notify"`，在 `rzDevice30` 路径没有调用点。
- `ble.connect` 把 notify UUID 一起传给 native；native 是否自动订阅无法从这些 JS 证明。**自己实现 GATT 时，必须在 connect 后显式订阅 `charactNotifyUUID`（`52401525...`）再发命令，否则 `sendCommand` 收不到响应。** 这是实现前提，不是从 JS 里读出的字段。

对照：`00A2/main.12e8...` 里的另一个 `rzDeviceStandardBLE` 类（service `fd65`，不是 `rzDevice30`）在 connect 成功后会调用 `this.rzBLE.openNotify(...)`。这说明“connect 后 openNotify”在雷云里确实存在，但不能据此说 `rzDevice30` 也这么做。

### 8.3 MTU

- `00E5` 全部 JS 中检索 `MTU`/`mtu` 为 0 命中；`rzBLE.send` 直接把整个 `dataSend` 交给 native，没有分片逻辑。
- 较新构建（`00EF/00B8/00DE/0566`）有 `this.maxPacketSize = 0`；只有 `>0` 时才把 `{ maxPacketSize }` 作为 `options` 传给 native：

```js
this.maxPacketSize = 0;
...
this.rzBLE.send(..., this.maxPacketSize > 0 ? { maxPacketSize: this.maxPacketSize } : void 0);
```

- 默认 `0` 表示不限制/不传；具体设备是否在别处赋值，未解出。
- 响应头 `Ht=20` 与 BLE 默认 ATT payload（MTU 23 - 3 = 20）一致，但代码没有设置 MTU，不能据此断言 MTU 就是 23。
- **结论：MTU 具体值、native 是否自动协商/分片，未解出。** 已查：`00E5/rzBLE.js`、`00E5/rzDevice30.js`、`00E5/main.js`、`00E5` 全部 JS；以及 `00EF/00B8/00DE/0566` 的 `rzDevice30`。

### 8.4 写入类型

`sendCommand(t,e,n=0,o=0,s=new Uint8Array([]),c=0,u=false,m=true)`：

- `u` = `withoutResponse`，默认 `false` → GATT Write Request（有响应）。
- `m` = `waitForLock`，默认 `true`。
- `u` 原样进入 `rzBLE.send` 的 payload：`{ ..., withoutResponse: u, ... }`。
- 表 5.1/5.2 的命令都省略 `u`，所以默认是“有响应写”。Chroma 帧等少数调用会显式传 `true`，走 Write Without Response。
- 没有看到按命令类型自动切换写法的逻辑；由调用方决定。

### 8.5 锁

- 锁名：`this.lockName = `${this.name}-${this.macAddress}``，基类为 `rzDevice30-<mac>`。
- `sendCommand`：`navigator.locks.request(this.lockName, { ifAvailable: !m }, ...)`。
  - 默认 `m=true` → `ifAvailable:false` → 等待锁，命令串行执行。
  - 调用方传 `m=false` → `ifAvailable:true` → 锁被占用时不等待，直接返回 `status=SKIP`，日志 `rzDevice30 lock is busy , skip <desc>,<command>`，不发送。
- `connectDevice` 始终 `ifAvailable:false`，会等待。
- `SKIP` 不是 `FAIL`，`sendCommand` 不会因此抛 `rzError`；调用方需要自己判断 `status`。

### 8.6 重试语义

`sendCommand`：

- 总尝试次数 `mc=2`（`for (let t=0; t<a.mc; t++)`），即最多 2 次。
- 第 2 次之前 `yield d._v(a.Bx)`，`Bx=30` ms。
- 重试条件：
  1. native 返回的错误文本包含 `"timeout"`（`J2`）；
  2. 响应 status 字节为 1（Command Busy）或 3（Command Failure）。
- 其他失败立即结束，最终抛 `rzError`。
- status=4（No Response Timeout1）本身不触发重试；只有错误文本含 `"timeout"` 才触发。
- 连接重试是另一套：`Fm=2`，间隔 `Gj=150` ms。

## 9. `rzDevice30Layla` 变体

`00E5/rzDevice30Layla.2ad17581411756d7183c.js` 继承协议 30 基类，但重写了 `_createDataSend` / `sendCommand`，并直接覆盖 UUID。它不能套用第 3、4 节的 little-endian/20 字节头解析。

| 项 | `rzDevice30` 基类 | `rzDevice30Layla` |
|---|---|---|
| service | `52401523...` | `"fd65"` |
| write | `52401524...` | `"416d00002d52617a6572424c4501f40a"` |
| notify | `52401525...` | `"416d00012d52617a6572424c4501f40a"` |
| read | `52401525...` | `undefined` |
| 请求长度字段 | 24-bit little-endian | 24-bit big-endian |
| 响应头 | 20 字节 | 8 字节 |
| payload 起点 | 20 | 8 |
| status 位置 | 7 | 7 |
| 长度校验 | 20 字节头失败后回退 8 字节头 | 固定 8 字节头，不匹配直接 FAIL |
| 重试 | 2 次，30 ms；timeout/1/3 | 2 次，30*(t+1) ms；1/3 |

Layla 的长度编码：

```js
if (n > 65535) {
  const t = dV(n), e = TK(n);
  s[1] = Zz(e);   // 高 16 位的低字节
  s[2] = ft(t);   // 低 16 位的高字节
  s[3] = Zz(t);   // 低 16 位的低字节
} else if (n > 255) {
  s[1] = 0;
  s[2] = ft(n);
  s[3] = Zz(n);
} else {
  s[1] = 0;
  s[2] = 0;
  s[3] = n;
}
```

`_getDataLengthFromHeader`：

```js
return (255 & t[e+0]) << 16 | (255 & t[e+1]) << 8 | (255 & t[e+2]);
```

即 big-endian。Layla 的 `sendCommand` 在 `!e.state` 时只对 `e.data[7] === 1 || e.data[7] === 3` 重试，没有检查 `"timeout"` 字符串；`e.data` 为空时也不会重试。

## 10. 未解出 / 证据缺口

以下项目在本次查阅的文件里没有足够证据，实现时不要当成已知事实：

1. **MTU 具体值与 native 分片策略**：JS 无 MTU 常量；`maxPacketSize` 默认 0；已查 `00E5/rzBLE.js`、`00E5/rzDevice30.js`、`00E5/main.js` 及 `00E5` 全部 JS，`00EF/00B8/00DE/0566` 的 `rzDevice30`。
2. **native `ble.connect` 是否自动订阅 notify**：`rzDevice30.connectDevice` 没有 `openNotify` 调用；`rzBLE.openNotify` 只有定义。已查 `00E5/rzBLE.js`、`00E5/rzDevice30.js`。
3. **响应头 byte 6 和 byte 8..19 的语义**：`sendCommand` 只读 byte 7 和 1..3，其余固定跳过；没有代码解释。
4. **DPI 实际最小/最大值**：`setDpiLevel` 无范围校验；全量 JS 无 `dpiMax`/`DPI_MAX`/`dpiMin`/`DPI_MIN` 等常量。理论字段 0..65535，设备真实范围未解出。
5. **11:01 的 stage 语义**：payload 没有 stage 字节；header6 在代码里叫 `profileId`。stage 编号只在 DPI Class 的 7 字节记录里出现。header6 到底是“profile”还是“stage”，证据不足。
6. **扩展固件的独立命令**：`getExtendedFirmwareVersion` 和 `getFirmwareVersion` 都发 `00:81`；模块里未使用的 `[0,0,131]` 没有调用点/名字，不能认定它是扩展固件命令。
7. **`Get Device Wireless Connection Status` 的真实 payload 语义**：同一 `01:93` 有两个解析器，一个读 `connected=data[0]`，一个读 `type=data[0], status=data[1]`；哪个是设备真实语义未解出。
8. **Set DPI Class 的 stage 上限/设备支持数**：255 字节缓冲区理论最多 36 条，但 `numStages` 没有上限检查，设备实际支持多少未解出。
9. **request header byte 6/7 的统一语义**：不同命令分别用作 profileId、batteryId、dock 参数等；没有一张全局定义表。本文按具体命令标注，不假设通用含义。

## 11. 实现检查清单

1. 发现：用 `RAZER_BLE_VENDOR_IDS = [0x1532, 0x068E]` 过滤；按 `productId + containerId`（或仅 `productId`）选设备。
2. 连接：`ble.connect(mac, [service], [write, notify])`；成功后订阅 notify characteristic；再发 `01:86` 验证。
3. 组帧：`tx(0..31)` + `len` 24-bit LE + `class` + `cmd` + `param6` + `param7` + payload；无 CRC。
4. 收帧：先按 20 字节头解析；status 在 7；长度字段在 1..3；payload 从 20 开始；长度不符时回退 8 字节头再试一次。
5. 重试：2 次，30 ms；timeout/status 1/3 重试；锁按 MAC 串行，默认等待。
6. 轮询率：1/2/3/4/5/8/10 对应 1000/500/333/250/200/125/100 Hz；未知值落 100 Hz。
7. DPI：`0B:01` Set 6 字节 X/Y/Z LE；`0B:81` Get 同布局；`0B:84` Get Class；`0B:04` Set Class（255 字节缓冲，实际 `7N+3`）。
8. Layla：改用 `fd65` / `416d...`、big-endian 长度、8 字节响应头；不要复用基类解析器。
9. 不要把 `getBatteryLevel()` 的 `undefined` 当 0%；不要把 `connected=data[0]` 当成唯一的无线连接状态证据。
10. DPI 范围、MTU、native notify 行为需要真机验证后再写死。

## 12. 关键证据索引

| 结论 | 文件 | 函数/代码 |
|---|---|---|
| 默认 UUID | `00E5/rzDevice30.js` | `class Ya` 构造函数：`this.serviceUUID=a.Fc` 等 |
| UUID 常量 | `00E5/rzDevice30.js` | 模块 `73064`：`const o/i/a = "52401523/24/25..."` |
| 请求帧布局 | `00E5/rzDevice30.js` | `_createDataSend(t,e,n,o,i)` |
| tx 回绕 | `00E5/rzDevice30.js` | `_getTransactionId()` |
| 响应头/状态 | `00E5/rzDevice30.js` | `sendCommand`：`e.data[7]`、`slice(0,a.Ht)` |
| 重试码 | `00E5/rzDevice30.js` | 模块 `73064`：`w$=1`、`QC=3`、`J2="timeout"` |
| 状态文本 | `00E5/main.js` | 模块 `35284`：`RZ_STATUS_DESC` |
| 轮询率常量 | `00E5/6120.js` | 模块 `32135`：`DO.RATE_*` |
| 字节工具 | `00E5/main.js` | 模块 `52975`：`Zz/ft/dV/TK/U1/oW` |
| 基础命令表 | `00E5/rzDevice30.js` | 模块 `95008`：`x/j/H/B/G/Q/tt/K/Y/q/Q` |
| 固件/电池命令 | `00E5/rzDevice30.js` | 模块 `70060`：`getFirmwareVersion/getExtendedFirmwareVersion/getBatteryLevel/getChargingStatus` |
| Layla 变体 | `00E5/rzDevice30Layla.js` | 构造函数 + 重写的 `_createDataSend/sendCommand` |
| 发现/连接 | `00E5/rzBLE.js` | `getDevices/send/connect/openNotify` |
| 连接验证 | `00E5/rzDevice30.js` | `connectDevice()` → `v.ki(this)`（`01:86`） |






