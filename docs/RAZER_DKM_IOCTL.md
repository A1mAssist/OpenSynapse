# Razer RzDev / RzCommon：DKM 与虚拟 HID 的 IOCTL 接口

证据等级：`static`。样本为 `RzCommon.sys` 1.0.0.76、`RzDev_02c6.sys` 1.0.0.78 和同源
`RzDev_00b8.sys`；没有在 Blade 16 实机上执行这些新命令。本文只记录反汇编能证明的字段，
没有证据的字段标为“未知”。

## 结论

1. 用户态 `0x88883xxx` 命令的真正分发器在 `RzCommon!FUN_140002e6c`，不是
   `RzDev_02c6`。`RzDev_02c6!FUN_140003250` 只处理 `0x88883010`、`0x88883014` 和
   `0x88883140`；其余命令由 `RzCommon` 直接处理，或者转发给按设备选择的回调。
2. 没有找到“创建/销毁虚拟 HID 设备”的 IOCTL。四个虚拟 HID 子设备在
   `EvtDeviceAdd` 初始化阶段按注册表值创建；删除走 WDF/PnP 生命周期，命令表里没有对应的
   用户态删除码。`0x88883140` 关闭的是 HID I/O target，不是虚拟 HID 子设备。
3. 与 DKM 状态面最接近的是 `0x88883104`（写 256 槽状态表中的一个槽）和
   `0x88883108`（一次读回 256 字节槽表）。这里的 DKM 关联是推断：`RzCommon` 没有
   `Dkm` 字符串或导出符号能直接命名这两个码；现有证据只有 256 槽表、`0x0B` 记录类型，
   以及 02C6 的 DKM INF 注册了 `DkmKeyDevice` / `DkmMouseDevice`。256 个字节的语义
   仍未确定。`0x88883040/44/48` 是另一组 20 字节键域 + 变长数据的通用 keyed-record
   接口；它们是否参与 DKM 尚未证明。

## 1. 分发链

`RzCommon.sys` 的已确认调用链：

```
entry 0x140007c00
  -> FUN_140007c2c  WdfVersionBind
  -> FUN_14000d000  WdfDriverCreate（间接调用点 0x14000d135）
  -> FUN_14000c3a0  EvtDriverDeviceAdd
  -> FUN_14000c000  WdfDeviceCreate（间接调用点 0x14000c12e）
  -> FUN_14000c2a8  WdfIoQueueCreate（间接调用点 0x14000c33f）
       EvtIoDeviceControl = FUN_140001a10
  -> FUN_140001a10  取 WDF request 的 input/output buffer
  -> FUN_140002e6c  IOCTL switch
```

`FUN_140001a10` 在 `0x140001b84` 调用 `FUN_140002e6c`，把 IOCTL code、输入缓冲区指针、
输入长度、输出缓冲区指针和输出长度传下去。`FUN_14000c2a8` 在 `0x14000c2e5` 把
`FUN_140001a10` 放进 queue 配置，然后在 `0x14000c33f` 调用 WDF 的 queue-create 函数。

`RzDev_02c6.sys` 的 `FUN_140003250` 是三个设备侧命令的 handler：

| 码 | 处理函数 | 证据 |
|---|---|---|
| `0x88883010` | `FUN_140003c80` | `0x14000325c` 比较，`0x140003293` 调用 |
| `0x88883014` | `FUN_1400025bc` | `0x140003264` 比较，`0x14000327d` 调用 |
| `0x88883140` | `FUN_140002004` | `0x14000326c` 比较，`0x140003274` 跳转 |

`RzDev_00b8.sys` 同样只有这三个码，比较地址为 `0x140001a35`、`0x140001a3d`、
`0x140001a45`；这支持 02C6 与 00B8 使用同一套设备侧命令边界。

`RzDev_02c6` 的 `FUN_1400032b0` 是 HID filter 的 `EvtIoInternalDeviceControl`，处理
`0xb000b`、`0xb000f`、`0xb0203` 等 HID 内部请求；它不是用户态 `0x88883xxx` 的
dispatch。旧文档把它和用户 IOCTL dispatch 混在一起时，需要按这个边界修正。

## 2. 命令码表

`in` 是驱动检查的最小输入长度，`out` 是最小/固定输出长度。`0` 表示该分支不读取输入或
不写输出。变长命令的长度写在“推测用途”列。证据列是 `FUN_140002e6c` 中的比较/取常量
地址，设备侧三个码标注在 `RzDev_02c6` 中。这些常量直接作为 `DeviceIoControl` 的
`dwIoControlCode` 使用，不是标准 `CTL_CODE(FILE_DEVICE_..., function, ...)` 的拆位值。
表中的名称是本文按调用行为起的语义名，不是驱动导出符号；`SetDkmSlot` / `GetDkmTable`
的 DKM 前缀同样是推断，依据见 3.3。

| 码 | 名称 | in | out | 推测用途 | 证据 |
|---|---|---:|---:|---|---|
| `0x88883004` | SetInputEnable | 8 | 0 | 把第一个 DWORD 的低字节写到 `ctx+0x549`；这是输入注入/转发的总开关 | `0x140002fad` |
| `0x88883010` | SetFeature | 变长 | 0 | 转发为 `IOCTL_HID_SET_FEATURE` (`0x000B0191`)；由设备侧实现 | `0x14000325c` |
| `0x88883014` | GetFeature | 0 | 变长 | 转发为 `IOCTL_HID_GET_FEATURE` (`0x000B0192`)；由设备侧实现 | `0x140003264` |
| `0x88883018` | ReadInput | 0 | `0x130` | 投递/完成输入读取，输出 0x130 字节帧 | `0x140002fba` |
| `0x8888301C` | EnableInputRedirect | 5 | 0 | `u32` 类别 + `u8` 开关；类别 1/2/4 分别改 `ctx+0x54A/54B/0x153` | `0x140002fc7` |
| `0x88883020` | SubmitInput | `0x20` | 0 | 提交一条输入记录；offset 0 是目标 device id，offset 4 是输入类型 | `0x140002fd4` |
| `0x88883024` | SetInputHook | `0x124` | 0 | 插入一条 0x124 字节 hook/路由记录 | `0x140002fe1` |
| `0x88883028` | FindInputHook | `0x20` | `0x124` | 按输入键查找匹配 hook，返回完整记录 | `0x140002fee` |
| `0x8888302C` | ClearInputHook | `0x20` | 0 | 按输入键删除 hook | `0x140002ffb` |
| `0x88883030` | GetInputHookByIndex | 4 | `0x124` | 用 4 字节索引取 hook 数组中的一项 | `0x140002f99` |
| `0x88883034` | EnableInputHooks | 4 | 0 | 开关 hook 处理；写入 `ctx+0x54D` | `0x1400035d5` |
| `0x88883038` | EnableInputNotify | 4 | 0 | 开关输入通知；写入 `ctx+0x54E` | `0x1400035e2` |
| `0x8888303C` | EnableInputNotify3 | 4 | 0 | 第三路输入通知开关；写入 `ctx+0x54F`，未找到其他读者 | `0x1400035ef` |
| `0x88883040` | AddKeyedRecord | `0x18+len` | 0 | 添加一条 0x14 字节键、`u32` 数据长度和变长数据的记录 | `0x1400035fc` |
| `0x88883044` | GetKeyedRecord | `0x18` | `0x18+len` | 按键查找记录，复制数据并回写实际长度 | `0x140003605` |
| `0x88883048` | DeleteKeyedRecord | `0x18` | 0 | 按键删除记录 | `0x14000360e` |
| `0x8888304C` | EnumKeyedRecord | `0x1C` | `0x1C` | 按 offset 0x10 的键和 offset 0x18 的序号枚举记录 | `0x140002f80` |
| `0x88883080` | SetConfigMaskA | 4 | 0 | `ctx+0x598 |= u32` | `0x140003822` |
| `0x88883084` | ClearConfigMaskA | 4 | 0 | `ctx+0x598 &= ~u32` | `0x14000382f` |
| `0x88883088` | SetClientMask | 4 | 0 | `client+0x18 |= u32` | `0x14000383c` |
| `0x8888308C` | ClearClientMask | 4 | 0 | `client+0x18 &= ~u32` | `0x140003849` |
| `0x88883090` | GetClientMask | 0 | 4 | 返回 `client+0x18` | `0x140003852` |
| `0x88883094` | SetConfigMaskB | 4 | 0 | `ctx+0x59C |= u32` | `0x14000385b` |
| `0x88883098` | ClearConfigMaskB | 4 | 0 | `ctx+0x59C &= ~u32` | `0x14000380e` |
| `0x888830C0` | LatchStatus | 0 | 1 | 返回 `ctx+0x550`，随后把它置 1 | `0x14000395c` |
| `0x88883100` | FlushInputState | 4 | 0 | 调用两个队列/状态清理函数 `FUN_1400040bc`、`FUN_140004240` | `0x140003969` |
| `0x88883104` | SetDkmSlot | `0x20`（非严格） | 0 | 写 256 槽状态表的一个槽；记录类型必须为 `0x0B` | `0x140003976` |
| `0x88883108` | GetDkmTable | 0 | `0x100` | 复制 `ctx+0x29CA` 的 256 字节槽表 | `0x140003983` |
| `0x88883110` | GetDriverState | 0 | 1 | 返回 `ctx+0x548` | `0x140003990` |
| `0x88883140` | CloseHidTarget | 0 | 0 | 设备侧关闭并释放 `ctx+0x460` 的 HID I/O target | `0x14000326c` |
| `0x88883144` | SendDeviceCommand | `0x20` | 0 | 按 offset 0 的 device id 选目标，按 offset 4 的子类型下发命令 | `0x14000399d` |

`0x88883010`、`0x88883014`、`0x88883140` 在 `RzCommon` 的 switch 中不单独出现：它们落到
`LAB_1400039a6` 的设备回调，最终进入 `RzDev_02c6!FUN_140003250`。因此“命令码存在”
不等于“每个码都由 `RzCommon` 自己处理”。

命令表共 31 个唯一码。已在 `RazerFilterInputProtocol.cs` 使用的 9 个是
`0x88883010`、`0x88883014`、`0x88883018`、`0x8888301C`、`0x88883020`、
`0x88883024`、`0x8888302C`、`0x88883034`、`0x88883038`；其余 22 个是本次静态分析
补出的候选命令，其中 `0x88883140` 由 `RzDev_02c6` 设备侧处理。

## 3. 结构体

### 3.1 输入命令头（`0x88883020`、`0x88883144`）

`0x88883020` 和 `0x88883144` 把输入缓冲区前两个 DWORD 当作目标头：

```c
typedef struct _RZ_TARGET_HEADER {
    uint32_t target_id;    // 0x00，0 表示当前/默认设备
    uint32_t command_type; // 0x04，命令子类型
    uint8_t  payload[];    // 0x08
} RZ_TARGET_HEADER;
```

证据：

- `0x88883020` 用 `*param_3` 选择目标 client（非 0 且非当前设备时会递归到目标 client），
  再用 `param_3[1]` 选择输入分支。
- `0x88883144` 同样先用 `*param_3` 选择目标，再在类型 `10` 时调用
  `FUN_140004f5c`。

`0x88883104` 不是这个目标头布局：该分支不读取 offset 0x00，只要求 offset 0x04
等于 `0x0B`，然后把 offset 0x08..0x13 作为一条 12 字节槽记录（DKM 关联见 3.3）。因此
不能把这里的 offset 0x00 命名为 `target_id`；其含义未知。

`0x88883020` 的 `command_type` 已证明的取值：

| 值 | 处理函数 | 证据 |
|---:|---|---|
| 1 | `FUN_1400077d0` + `FUN_140004734` | `0x140003282` 之后的分支 |
| 2 | `FUN_140004c3c` | 同上 |
| 3 | `FUN_1400077d0` | 同上 |
| 5、6、8、9 | `FUN_1400043ac` | 同上 |
| 10 | `FUN_140004338` | 同上 |
| 12 | `FUN_140004fd0` | 同上 |

这些类型名没有字符串可直接对应，本文不把 1/2/3 等数字硬命名为“键盘/鼠标”；只记录驱动
对它们的实际处理路径。

### 3.2 Input hook 记录（`0x88883024` / `0x88883028` / `0x8888302C` / `0x88883030`）

`0x88883024` 接收 0x124 字节记录，驱动另外分配 0x128 字节并在尾部写内部标志。
`FUN_140006778` 和 `FUN_140007630` 对字段的比较给出了下列已证明布局：

```c
typedef struct _RZ_INPUT_HOOK {
    uint32_t flags;       // 0x000，具体位义未知
    uint32_t kind;        // 0x004，已见 1/2/3/0x0B
    uint16_t match_id;    // 0x008，0xABCD 是匹配逻辑中的特殊值
    uint16_t code;        // 0x00A，键码/扫描码
    uint16_t code_flags;  // 0x00C，匹配时只比较低 15 位
    uint16_t reserved0;   // 0x00E
    uint32_t value;       // 0x010，按 kind 解释
    uint8_t  unknown[0x110]; // 0x014..0x123
    // 驱动内部字段不来自用户输入：
    // uint32_t internal_flags; // 0x124
} RZ_INPUT_HOOK;
```

证据：

- `OpenSynapse.Windows/Protocols/RazerFilterInputProtocol.cs` 的
  `CreateKeyboardHook` 在 offset 4 写 1、offset 0x0A 写 scancode、offset 0x0C 写
  flag。这与驱动 `FUN_140006778` 对 kind=1 的比较字段一致。
- `FUN_140006778` 对 kind=1/3 比较 `+0x0A`、`+0x0C & 0xFFFE`、`+0x10`；kind=2 还比较
  `+0x1C`；kind=0x0B 比较 `+0x0A`、`+0x10`。0xABCD 在 `+0x08` 作为通配/特殊匹配值。
- `FUN_140007630` 把 0x124 字节复制到新分配的 0x128 记录，并在 `+0x124` 写内部位。

`0x88883030` 通过 `FUN_140006698` 直接按索引取记录；`0x88883028` 通过
`FUN_140006778` 按键匹配取记录。两者都输出 0x124 字节，且不输出内部 `+0x124` 字段。

### 3.3 DKM 槽更新（`0x88883104`，DKM 关联为推断）

输入前 8 字节之后只有一条 12 字节记录。`FUN_1400025f4` 的槽表写入分支要求
`ctx+0x54E` 非零：

`DKM` 是本文按 02C6 的 DKM INF（`DkmKeyDevice` / `DkmMouseDevice`）和 256 槽表形状
给的名字；`RzCommon` 内没有 `Dkm` 字符串。以下只证明一个 256 字节数组的读写，不证明
数组就是 DKM 键表。

```c
((uint8_t *)ctx)[0x29CA + record->slot_index] = record->slot_value;
```

已证明的字段：

```c
typedef struct _RZ_SLOT_RECORD {
    uint16_t unknown0;     // 0x00，未知，作为事件载荷的一部分原样复制
    uint16_t slot_index;   // 0x02，写入 256 字节表的索引，仅低 8 位参与寻址
    uint8_t  slot_value;   // 0x04，写入表的字节
    uint8_t  unknown1[3];  // 0x05
    uint32_t event_value;  // 0x08，原样复制到通知结构
} RZ_SLOT_RECORD;
```

证据：`FUN_1400025f4` 在 `0x1400026cc` 读取 `record+2` 作为索引，在 `0x1400026d4`
读取 `record+4` 作为值，在 `0x1400026ee` 写入 `ctx+0x29CA+index`；同一函数在
`0x1400026ba` 读取 `record+8` 的 DWORD 作为事件字段。记录总长由循环步长 0x0C 确定。

`0x88883104` 的 dispatcher 只把 `param_3+2` 到 `param_3+5` 交给该函数，即恰好一条
记录。这里的边界判断不是严格的 `param_4 >= 0x20`：长度小于 0x20 时，只要输入指针
非空仍会继续，因此不能把它当成完整的长度校验。安全用法应发送至少 0x20 字节；
记录只占 0x08..0x13，0x14..0x1F 未被该分支读取。

### 3.4 槽表查询（`0x88883108`，DKM 关联同 3.3）

输出是 `ctx+0x29CA` 起始的 256 字节数组，dispatch 在 `0x140003AB2` 后的复制循环
按 16 字节一块搬 256 字节：

```c
typedef struct _RZ_SLOT_TABLE {
    uint8_t slot[256]; // 0x00，每个槽一个字节
} RZ_SLOT_TABLE;       // sizeof == 0x100
```

证据：`FUN_1400015c8` 在 `0x140001694` 把该区域清零 0x100 字节；`0x88883108`
分支要求输出长度至少 0x100，并把整块 0x100 字节复制到输出。每个字节的具体含义
（启用状态、映射值还是别的状态）未知。
这里同样只证明“256 字节数组可被读回”，没有证明它就是 DKM 状态表。

### 3.5 Keyed record（`0x88883040` / `0x88883044` / `0x88883048` / `0x8888304C`）

`FUN_140001490`、`FUN_14000112c`、`FUN_140001328` 组成一个按键索引的记录表：

```c
typedef struct _RZ_KEYED_RECORD {
    uint8_t  key[16];     // 0x00，前两个 QWORD
    uint32_t selector;    // 0x10，查找时比较的 DWORD
    uint32_t data_length; // 0x14，数据长度
    uint8_t  data[];      // 0x18，变长
} RZ_KEYED_RECORD;
```

- `0x88883040` 在 `0x140001490` 按 `0x18 + data_length` 分配并复制整条记录，然后
  调用删除函数清掉同键旧项。
- `0x88883044` 在 `0x14000112c` 比较 `key` 的两个 QWORD 和 `selector`，把数据复制到
  输出，输出长度取“调用者容量”和 `data_length` 的较小值，并回写实际长度。
- `0x88883048` 在 `0x140001328` 找到同键项后删除。
- `0x8888304C` 在 `FUN_140001004` 中先按 `selector`（offset 0x10）匹配，再按
  `param_2[6]`（offset 0x18）选择第 N 项；返回 0x1C 字节头部副本。

这组接口是不是 DKM 的“键表”，还是更通用的设备配置表，现有证据不足；因此本文把它
单独命名，不把它和 3.3/3.4 的 256 字节槽表混为一谈。

## 4. 虚拟 HID 设备

### 4.1 创建入口

虚拟 HID 设备由 `RzDev_02c6` 在 `EvtDeviceAdd` 初始化时创建，不是由用户态 IOCTL 创建。
调用链：

```
FUN_1400027b0              // 设备上下文初始化
  -> FUN_140005fe4(WDFDEVICE parent, WDFDEVICE *out, int type)
       // 失败路径的日志字符串：RzDevCreateVirtualHidDevice
```

四个调用点：

| 注册表值 | 调用点 | type | 设备路径前缀 |
|---|---|---:|---|
| `Virtual_Kbd` 或 `Virtual_Kbd_%04X` | `0x140002d57` | 2 | `RAZER\VKBDDevice` |
| `Virtual_Mou` 或 `Virtual_Mou_%04X` | `0x140002dd4` | 1 | `RAZER\VMOUDevice` |
| `Virtual_Js` 或 `Virtual_Js_%04X` | `0x140002e51` | 6 | `RAZER\VJSDevice` |
| `Virtual_Con` 或 `Virtual_Con_%04X` | `0x140002ece` | 8 | `RAZER\VCONDevice` |

`FUN_140005fe4` 的参数：

- `param_1`：父 WDF device。调用点传的是初始化上下文里的 `param_1[4]`。
- `param_2`：输出 `WDFDEVICE` 句柄的指针。
- `param_3`：设备类型，已证明 1=mouse、2=keyboard、6=joystick、8=consumer。

函数内部用 `RZVIRTUAL\VID_%04X&PID_%04X&MI_%02X&Col%02X` 生成虚拟设备实例路径，并用
`RzVirt_%02d` 生成实例名。`RzDevCreateVirtualHidDevice` 只出现在失败日志调用的字符串
参数里（`0x140006a8b`），Ghidra 没有 PDB 名称，所以本文把 `FUN_140005fe4` 按这个
字符串称呼，而不是声称它是导出函数。

`Virtual_Con_02c6` 这个具体名字来自 02C6 的 DKM INF；代码先试 `Virtual_Con`，失败后
用 `Virtual_Con_%04X`，`%04X` 来自设备 PID，因此 `Virtual_Con_02C6` 会命中后缀形式。

### 4.2 销毁入口

没有在 31 个命令中找到销毁虚拟 HID 子设备的 IOCTL。虚拟设备通过
`WdfDeviceCreate` 挂在父设备下，生命周期由 WDF/PnP 的删除路径处理。

容易误认成“destroy virtual HID”的 `0x88883140` 实际是 `CloseHidTarget`：

```
0x88883140
  -> RzDev_02c6!FUN_140003250 (0x14000326c)
  -> FUN_140002004
       读取 ctx+0x460，置 0
       删除并释放该 WDF I/O target
```

`ctx+0x460` 只在 `FUN_140003898` 中懒创建，随后由 `FUN_140003bb0` 用来向 HID 设备发送
`IOCTL_HID_SET_FEATURE`/`GET_FEATURE` 之类的同步 IOCTL。因此 `0x88883140` 是关闭 HID
target，不是删除 `RAZER\VKBDDevice` 等虚拟子设备。

### 4.3 注册表开关

`FUN_1400027b0` 先读以下值，再把它们映射到 `ctx+0x448` 的位图：

| 注册表值 | 位 |
|---|---:|
| `ControlDevice` | `0x1` |
| `DkmMouseDevice` | `0x8` |
| `DkmKeyDevice` | `0x10` |
| `MPosMouseDevice` | `0x20` |
| `DkmJsDevice` | `0x40` |
| `MouseExDevice` | `0x80` |
| `ConsumerControlDevice` | `0x100` |
| `MouseEx2Device` | `0x2000` |

`VBusDevice`、`Mou2Device`、`Kbd2Device` 也在这个函数中读取，但它们的后续用途没有
完全追完；不要据此推断更多设备类型。

## 5. 未确定

1. `0x88883040/44/48` 的 20 字节键字段没有语义名称。只知道 `+0x10` 是查找时比较的
   DWORD，`+0x14` 是数据长度。不能证明它就是 DKM 键表。
2. `0x88883080/84/94/98` 写入 `ctx+0x598` / `ctx+0x59C`，但在 `RzCommon` 的全部
   121 个已分析函数中没有找到读者。它们可能是遗留配置字段，也可能是被未识别回调使用；
   目前不能命名其位义。
3. `0x88883104` 的 `unknown0`、`unknown1[3]`、`event_value` 没有证据说明含义。只有
   `slot_index` 和 `slot_value` 的写入路径是确定的；`0x88883104/108` 与 DKM 的直接
   绑定也未证明。
4. `0x88883108` 的 256 个槽字节表示“状态”“映射 id”还是“开关”尚未确定；本文只称
   它为槽表。
5. `0x88883028` 的正式名称无法从字符串或导出表确认；它的行为和 `0x88883030` 一样，
   都是按 0x124 字节 hook 记录查询，但一个按键匹配、一个按索引取。
6. `0x88883144` 的 offset 4 子类型 10 只证明了会构造 `02 xx xx` 三字节命令并经
   `ctx+0xD0` 回调下发；该三字节命令在硬件协议里的含义未知。
7. `0x88883140` 的精确生命周期语义没有硬件验证。可以确定它删除 `ctx+0x460` 的 I/O
   target；如果 Synapse 在别的对象上复用了同一个码，本文没有覆盖到。
8. 没有在 Blade 16 上调用任何新命令，所有长度和字段均来自静态反汇编。

## 6. 复现

### 6.1 Ghidra 项目

```
Ghidra:  D:\Tools\ghidra_12.1.2_PUBLIC_20260605
Project: D:\Workspaces\OpenSynapse-dkm\artifacts\reverse-engineering\ghidra-dkm\RazerDkm
Samples: D:\Workspaces\OpenSynapse\artifacts\reverse-engineering\driver-samples
```

项目内程序路径为 `/driver-samples/RzCommon.sys`、`/driver-samples/RzDev_02c6.sys`、
`/driver-samples/RzDev_00b8.sys`。

### 6.2 导入和默认分析

```powershell
& 'D:\Tools\ghidra_12.1.2_PUBLIC_20260605\support\analyzeHeadless.bat' `
  'D:\Workspaces\OpenSynapse-dkm\artifacts\reverse-engineering\ghidra-dkm' `
  'RazerDkm' `
  -import 'D:\Workspaces\OpenSynapse\artifacts\reverse-engineering\driver-samples' `
  -recursive -analysisTimeoutPerFile 1800
```

`RzCommon` 和 `RzDev_02c6` 的默认分析各约 7 秒完成，日志在
`artifacts/reverse-engineering/analysis-dkm/import.log`。

### 6.3 导出证据

脚本目录：

```
artifacts/reverse-engineering/ghidra-scripts-dkm
```

脚本：

- `DumpReverseEvidence.java`：导出函数、字符串、字符串 xref、所有 `0x8888xxxx` 立即数。
- `DecompileAddresses.java`：对指定地址输出反编译 C 和完整反汇编。
- `DumpAllDecomp.java`：导出全部函数反编译文本，用于搜索结构偏移。

运行示例（`-process` 使用项目子目录，避免 Windows 下前导 `/` 被当成参数）：

```powershell
& 'D:\Tools\ghidra_12.1.2_PUBLIC_20260605\support\analyzeHeadless.bat' `
  'D:\Workspaces\OpenSynapse-dkm\artifacts\reverse-engineering\ghidra-dkm' `
  'RazerDkm/driver-samples' -process 'RzCommon.sys' -noanalysis `
  -scriptPath 'D:\Workspaces\OpenSynapse-dkm\artifacts\reverse-engineering\ghidra-scripts-dkm' `
  -postScript 'DecompileAddresses.java' `
  'D:\Workspaces\OpenSynapse-dkm\artifacts\reverse-engineering\analysis-dkm\RzCommon.core.txt' `
  '140002e6c,140001004,140003e2c,140004734,1400025f4'
```

### 6.4 关键证据文件

```
analysis-dkm/RzCommon.evidence.txt      函数/字符串/IOCTL 立即数
analysis-dkm/RzCommon.core.txt          dispatcher 和核心槽表/记录函数
analysis-dkm/RzCommon.dispatchchain.txt queue 注册和 EvtIoDeviceControl
analysis-dkm/RzDev_02c6.core.txt        SetFeature/GetFeature、初始化、虚拟 HID
analysis-dkm/RzDev_02c6.all.txt         全部函数反编译，用于交叉搜索结构偏移
```
