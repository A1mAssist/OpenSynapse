# Blade 16 2025 (02C6) M keys and Fn lights

调查日期：2026-10-29。对象是 `0x02C6` 的 Product 710 / Razer Blade 16 2025。

本文只记录能从官方 02C6 bundle 或仓库现有代码直接证明的内容。证据等级：

- **[bundle]**：`artifacts/razer-mw-full-cdn/bundles/02C6/` 里的雷云设备定义或协议代码。
- **[code]**：仓库现有映射、布局或测试。
- **[inference]**：由上面两类证据拼出的结论，但没有单独的 bundle 字段直接命名。
- **[unverified]**：必须实机确认，当前不写成事实。

## 结论摘要

| 问题 | 结论 |
|---|---|
| M1-M5 的 DKM slot | M1=`10`，M2=`11`，M3=`3`，M4=`211`，M5=`212`。[code] M1/M2 不在 02C6 bundle 的 `DKMKEYS` 数组里，但现有 `Product710Mapping.json` 和 02C6 灯光布局都把 `0x0A/0x0B` 放在 M1/M2 位置。 |
| M1-M5 默认动作 | M1/M2=HyperShift [code]；M3=Game Mode、M4=Next Performance Mode、M5=Mic Mute [bundle][code]。 |
| 210 / 211 / 212 的物理键 | 210=AI Launcher（额外的一颗 AI 键，物理丝印是否为 Copilot 不在 bundle 证据内）；211=M4/Performance；212=M5/Mic Mute。[bundle][code][inference] |
| Fn 图标灯 / M 键背光 | 没有找到独立的 Fn 图标 zone 或亮度命令；它们落在主键盘背光矩阵里，官方亮度入口是 `Backlight_LED=5`。[bundle] F1-F12 图标是否完全由同一颗键背光点亮，仍需实机遮挡确认。[unverified] |
| 静音键红灯 | 不是普通亮度 zone。官方走 `Set Audio Mute Status`：class `0x18`、command `0x04`、参数 `[0, 1|2, muted]`，麦克风用 target `2`。[bundle] |
| `BladeMappingInputRuntime` 能否直接吃 kind=3 | 能。解析器和映射表已经直接支持 `BladeMappingInputKind.RazerKey`，不需要新增一层。[code] |

## 1. M1-M5 的 DKM slot 和默认动作

### 1.1 bundle 里的 DKM 对象

证据文件：`artifacts/razer-mw-full-cdn/bundles/02C6/main.d7c1244bf00ff7462e7c.js`，约在字符偏移 `148034` 附近。

```js
T=[
  {inputID:"DKM_03",key:3},
  {inputID:"DKM_D2",key:210},
  {inputID:"DKM_D3",key:211},
  {inputID:"DKM_D4",key:212}
]
```

同一对象里的默认映射片段：

```js
{outputType:`${a.lRc}`,isHyperShift:!1,inputType:"DKMInput",inputID:"DKM_03",dkmInput:"DKM_03"}
{outputType:`${a.MJm}`,isHyperShift:!1,inputType:"DKMInput",inputID:"DKM_D2",keyboardGroup:{keyGroup:0,key:"KEY_F23",modifiers:["KEY_LEFT_SHIFT","KEY_LEFT_GUI"]}}
{outputType:`${a.MJm}`,isHyperShift:!0,inputType:"DKMInput",inputID:"DKM_D2",keyboardGroup:{key:"KEY_APPLICATION",modifiers:[]}}
{outputType:`${a.iS5}`,isHyperShift:!1,inputType:"DKMInput",inputID:"DKM_D3",dkmInput:"DKM_D3",bladePerformanceGroup:{performanceModeAssignment:"nextPerformanceMode"}}
{outputType:`${a.CLP}`,isHyperShift:!1,inputType:"DKMInput",inputID:"DKM_D4",dkmInput:"DKM_D4",multimediaGroup:{multimediaAssignment:"MuteMic"}}
```

这些 `a.*` 名字的解码也在 bundle 里：

```js
I="gameModeGroup"
g="bladePerformanceGroup"
A="multimediaGroup"
_="keyboardGroup"
```

### 1.2 对应表

| 物理键 | DKM slot | 默认动作 | 证据 |
|---|---:|---|---|
| M1 | `10` / `0x0A` | HyperShift | [code] `Product710Mapping.json` 里有 `razerKey 10 -> hypershift`；`WindowsKeyboardLightingAdapter` 把 `0x0A` 放到第 1 行 M1 位置。 |
| M2 | `11` / `0x0B` | HyperShift | 同上，`0x0B` 放到第 2 行 M2 位置。 |
| M3 | `3` | Game Mode | [bundle] `DKM_03` 映射到 `gameModeGroup`；现有 `RazerPrivateKeyCatalog` 也把 02C6 的 `3` 标成 Game Mode。 |
| M4 | `211` / `0xD3` | Next Performance Mode | [bundle] `DKM_D3` 映射到 `bladePerformanceGroup.nextPerformanceMode`；[code] resw 说明 M4 是性能模式循环键。 |
| M5 | `212` / `0xD4` | Mic Mute | [bundle] `DKM_D4` 映射到 `multimediaGroup.MuteMic`；[code] 现有布局把 `0xD4` 放在第 5 行 M5 位置。 |
| AI Launcher | `210` / `0xD2` | 非 HyperShift：`Win+Shift+F23`；HyperShift：Application 键 | [bundle] `DKM_D2` 的两条映射；[code] catalog 把 `210` 标成 AI Launcher。它不是 M1-M5 那五个键。 |

**注意**：02C6 bundle 的 `DKMKEYS` 只列了 `3`、`210`、`211`、`212`，没有列 `10/11`。因此 M1/M2 的 slot 号来自现有 `Product710Mapping.json` 和灯光布局，不是这四行 bundle 对象直接给出的。把 `10/11` 写成“bundle 已命名 M1/M2”不准确；写成“固件/驱动把这些键报告为 10/11，项目现有映射和布局按 M1/M2 处理”才准确。

## 2. 210 / 211 / 212 分别是什么键

### 2.1 210 是 AI Launcher，不是第五个 M 键

bundle 把 `210` 叫 `DKM_D2`，并给它两条默认动作：

- 普通层：`KEY_LEFT_SHIFT + KEY_LEFT_GUI + KEY_F23`。
- HyperShift 层：`KEY_APPLICATION`。

项目 `RazerPrivateKeyCatalog` 把 02C6 的 `210` 命名为 `AI Launcher`。从按键位置看，它不是右侧 M1-M5 那一列；现有 02C6 灯光布局只把 `0x0A/0x0B/0x03/0xD3/0xD4` 放在第 1-5 行的 M1-M5 位置，没有把 `0xD2` 放进这一列。[code][inference]

因此，和 M1-M5 一起讨论时，应把 210 单独写成“AI Launcher 键”，不要把它算成 M4 或 M5。物理丝印是否为 Copilot 或别的图标，bundle 没有给出，必须看实机键盘。

### 2.2 211 = M4 / Performance

bundle 的 `DKM_D3` 默认动作是：

```js
bladePerformanceGroup:{performanceModeAssignment:"nextPerformanceMode"}
```

项目的执行链是：

1. `RazerFilterInputProtocol.TryParseInputFrame` 把 driver 的 `kind=3` 帧解析成 `BladeMappingInputKind.RazerKey`。
2. `BladeMappingInputRuntime` 用 `Product710Mapping.json` 的 `razerKey 211` 规则生成 `BladeCommandMappingAction(BladePerformance, NextPerformanceMode)`。
3. `BladeFnRuntime.ConsumeAsync` 把 action 交给 `BladeMappingActionExecutor`。
4. `App.ExecuteBladeFnLeafOnUiThreadAsync` 调用 `CycleBladePerformanceModeAsync`。
5. `CycleBladePerformanceModeAsync` 走现有的性能模式切换路径，包含风扇/性能模式写入；它和 Fn+P 使用的是同一条应用层动作，而不是另一套协议。

这条链的结论是：211 的默认行为不是直接发一条风扇命令，而是先进入应用的“下一个性能模式”动作；由应用根据当前模式和配置决定风扇/性能模式写入。[code]

### 2.3 212 = M5 / Mic Mute

bundle 的 `DKM_D4` 默认动作是：

```js
multimediaGroup:{multimediaAssignment:"MuteMic"}
```

项目 `Product710Mapping.json` 把它编译成 `audio` 输出，`id="mic"`、`mute=2`、`repeat=1`。`mute=2` 和 `BladeAudioMuteTarget.Microphone = 0x02` 对齐。[code]

`RazerPrivateKeyCatalog` 把 02C6 的 `212` 命名为 `Mic Mute`。现有灯光布局把 `0xD4` 放在第 5 行 M5 位置，所以 212 同时是 M5 键和麦克风静音键；它的红灯就是下面第 3 节里的麦克风静音指示。[code][inference]

## 3. Fn 图标灯、M 键背光和静音指示灯

### 3.1 键盘背光的主入口是 `Backlight_LED = 5`

02C6 bundle 的 LED region 枚举里，键盘背光是 zone `5`：

```js
Backlight_LED:5
```

`setBrightness` 在 `regionId` 为 `0` 时把它替换成 `Backlight_LED`，然后调用 `Set LED Brightness`：

```js
this.setBrightness=({profileId:e,regionId:t,brightness:i})=>
  ... 0==(t=+t)&&(t=r.k9.Backlight_LED), l.dD(this,e,t,i)
```

`Set LED Brightness` 的命令描述是 `[4,3,4]`，即数据长度 `4`、class `0x03`、command `0x04`，参数为 `[profileId, regionId, 0..255]`。[bundle]

**结论**：M1-M5 的键背光和 F1-F12 上的丝印图标都属于主键盘背光表面，使用 `Backlight_LED=5` / 主矩阵；bundle 没有给出单独的“Fn 图标 zone”。[bundle][inference]

需要保留的实机问题：主矩阵有每键 LED，但“F1-F12 小图标是否完全由同一颗 LED 照亮、是否存在只亮图标的第二颗 LED”无法从 bundle 的 zone 枚举单独证明。要确认，必须在 02C6 上做单键遮挡或逐键帧测试。[unverified]

### 3.2 静音红灯不是亮度 zone，而是音频静音状态命令

02C6 bundle 的 `setAudioMuteStatus`：

```js
this.setAudioMuteStatus=(e,t)=>...
  const i=new Uint8Array([3,24,4]),
        o=new Uint8Array(i[0]);
  o[0]=0,o[1]=e,o[2]=t,
  yield this.sendCommand(i,"Set Audio Mute Status",o)
```

解码：

- `e = 1` 是 speaker mute，`e = 2` 是 microphone mute。
- `t = 0/1` 是未静音/静音。
- 命令描述 `[3,24,4]` = 数据长度 `3`、class `0x18`、command `0x04`；payload 是 `[0, target, muted]`。

02C6 的 `functionLighting` 配置也明确打开了麦克风和扬声器静音指示：

```js
d.A.useFeature("functionLighting","config",{gamingMode:!0,micMute:!0,functionKey:!0,speakerMute:!0})
```

官方 handler 在 `microphonevolumechanged` 时调用 `setAudioMuteStatus(2, muted)`，在 `speakervolumechanged` 时调用 `setAudioMuteStatus(1, muted)`。[bundle]

**结论**：麦克风静音红灯由 `0x18:0x04` 的状态命令控制，不是通过 `Backlight_LED` 或 `Mute_LED` 的亮度值控制。它的颜色/亮度不是这里暴露的可配置项；当前协议只传“哪个端点、是否静音”。[bundle]

现有 OpenSynapse 的 `BladeSynapsePolicyProtocol` 已经使用同一组 `0x18:0x04` 命令，`BladeAudioMuteTarget.Microphone = 0x02`，所以协议常量本身不需要重新猜。[code]

### 3.3 zone 38 是独立的 VCLED 通道，但不能直接当成 Fn 图标灯

02C6 bundle 还有一组单独的 `VCLed` 接口：

```js
this.getVcLedState=()=>... l.Vm(this,0,38)
this.setVcLedState=(e,t)=>... l.sP(this,e,38,t)
this.setVcLedStateInSleep=(e,t)=>... l.TV(this,0,38,e,t)
```

对应命令：

```js
Set LED State:            [3,3,0]    // 参数 [profile, ledId, on]
Set LED State in Sleep:   [4,3,33]   // 参数 [profile, ledId, on, duration]
Set LED Brightness:       [4,3,4]    // 参数 [profile, ledId, 0..255]
```

`setVcLedState` 固定使用 `ledId=38`；亮度路径在 `hasVCLed` 为真时也会写 `regionId=38`。[bundle]

**能确认的**：zone 38 是一个独立于主键盘矩阵的 LED 通道，支持 on/off、睡眠状态和亮度。[bundle]

**不能确认的**：bundle 没有把 zone 38 映射到“F1-F12 图标”“M 键背光”或“静音红灯”中的任何一个。它可能是另一组装饰灯，也可能覆盖其中一部分；在没有实机逐区测试前，不能把 38 写成 Fn 图标灯或静音灯的 zone。[unverified]

另外，通用 LED 枚举里确实有 `Mute_LED:25`，但 02C6 的官方静音状态走的是 `0x18:0x04`，不是 `Set LED Brightness` 的 zone 25。把 25 直接写死成静音灯属于猜协议；只有实机隔离测试通过后才能采用。[bundle][unverified]

### 3.4 灯效部分的小结

| 目标 | 证据支持的结论 | 还需要什么 |
|---|---|---|
| F1-F12 上的 Fn 图标 | 没有独立 zone/亮度命令；属于主键盘背光表面 `Backlight_LED=5` 的候选范围。 | 实机遮挡或逐键矩阵测试，确认图标是否与键帽共用 LED。 |
| M1-M5 键背光 | 属于主矩阵；现有布局把 5 个 M 键放在第 1-5 行最右列。 | 实机确认 M1/M2 的 10/11 事件和背光位置。 |
| 麦克风静音红灯 | `0x18:0x04` 状态命令，target `2`；不是亮度 zone。 | 实机确认该命令能点亮/熄灭 02C6 的红灯，并确认是否还需要初始化。 |
| zone 38 VCLED | 独立通道，支持 `0x03:0x00`、`0x03:0x21`、`0x03:0x04`。 | 必须实机逐区标定它对应哪些物理灯。 |

## 4. `BladeMappingInputRuntime` 对 kind=3 的支持

现有代码已经能直接消费 `kind=3` 的 RazerKey 事件，不需要新增适配层：

1. `RazerFilterInputProtocol.TryParseInputFrame` 接受 `(eventType, kind) = (4, 3)`，输出 `BladeMappingInputKind.RazerKey`。
2. `BladeMappingInputRuntime.ParseInputKey` 接受 `"razerKey"`，读取 JSON 的 `key` 字段。
3. `Product710Mapping.json` 已经包含 210、211、212、3、10、11 的 `razerKey` 规则。
4. `BladeFnRuntime.ConsumeAsync` 对每个输入调用 `mapping.Process(input, out action, ...)`；已知 RazerKey 会生成 `BladeCommandMappingAction`、`BladeAudioMappingAction` 或键盘序列。
5. 动作最终由 `BladeMappingActionExecutor` / `App.ExecuteBladeFnLeafOnUiThreadAsync` 分发。

现有测试也已经覆盖了部分路径：

- `RazerFilterInputProtocolTests` 用真实抓取的 `kind=3` 帧验证解析。
- `BladeMappingInputRuntimeTests` 验证 `RazerKey 3 -> GameMode` 和 `RazerKey 0xD3 -> NextPerformanceMode`。

关键代码位置：

- `src/OpenSynapse.Windows/Protocols/RazerFilterInputProtocol.cs`：`TryParseInputFrame`。
- `src/OpenSynapse.Windows/Protocols/BladeMappingInputRuntime.cs`：`ParseInputKey`、`ProcessCore`。
- `src/OpenSynapse.App/Native/Razer/Product710Mapping.json`：`razerKey` 默认规则。
- `src/OpenSynapse.App/Runtime/BladeFnRuntime.cs`：`ConsumeAsync`。
- `src/OpenSynapse.App/App.xaml.cs`：`ExecuteBladeFnLeafOnUiThreadAsync`。
- `src/OpenSynapse.Windows/Lighting/WindowsKeyboardLightingAdapter.cs`：`RazerKeyMap` 的 M1-M5 行/列。
- `src/OpenSynapse.Windows/Protocols/BladeSynapsePolicyProtocol.cs`：`CreateSetAudioMuteStatusRequest`。

**剩余风险**：`RazerFilterInputHost` 目前只安装 23 个键盘扫描码 hook。解析器能处理 kind=3，不等于当前的 23 个 hook 一定能让固件把所有 RazerKey 帧送上来。这个“驱动是否在当前 hook 集合下持续上报 kind=3”的问题只能靠 02C6 实机日志确认；如果日志里没有 `0x0A/0x0B/0x03/0xD2/0xD3/0xD4`，下一步才是调整 hook/通知策略，而不是在应用层再加一层映射。[code][unverified]

## 5. 本次实现决策

本次不新增生产代码：

- `BladeMappingInputRuntime` 已经能直接吃 kind=3；
- `Product710Mapping.json` 已经有 210/211/212 的映射；
- 静音状态命令已经在 `BladeSynapsePolicyProtocol` 里；
- 灯的 zone 38 / zone 25 缺少物理覆盖证据，写死会变成猜协议。

因此本分支只新增本文档。等实机日志和灯光标定结果出来后，再决定是否需要：

1. 为缺失的 RazerKey 帧调整 filter hook / notify；
2. 为 M1/M2 的 10/11 事件补显式动作或诊断日志；
3. 为已标定的 zone 增加最小亮度/状态接口。

## 6. 必须实机确认的项

1. 在 driver mode 下抓 `kind=3`：依次按 M1-M5、AI Launcher、Mic Mute，确认事件码是否为 `0x0A/0x0B/0x03/0xD2/0xD3/0xD4`。
2. 确认 M3/M4/M5 是否分别触发 Game Mode、Next Performance Mode、Mic Mute；M1/M2 是否仍作为 HyperShift 工作。
3. 调 `0x18:0x04`，target `2`，观察麦克风静音红灯是否随 `0/1` 变化；同时确认是否需要先执行现有初始化或 `0D:02` software mode。
4. 单独写 `Backlight_LED=5` 的亮度，确认 F1-F12 图标和 M1-M5 背光是否一起变化；用遮挡法确认图标是否与键帽共用 LED。
5. 单独写 zone `38` 的 `0x03:0x04`，记录哪些物理灯变化。未记录前不要把 38 写进任何面向用户的灯光映射。
6. 如果要做静音灯的 zone 方案，单独测试通用枚举里的 `Mute_LED=25`，确认是否可写、是否与 `0x18:0x04` 冲突；未通过前保持现状。
