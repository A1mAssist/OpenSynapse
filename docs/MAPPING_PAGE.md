# 映射页与板载宏

## 页面边界

映射页只做一件事：把设备实际接受的按键映射列出来，允许用户改目标动作，并在保存后回读确认。页面行来自设备返回的 button list，不来自型号白名单。

- HID 设备：`0x02:0x84` 返回非空按钮列表时，`ProbeMiddlewareCapabilitiesAsync` 才加入 `MappingRead` / `MappingWrite`。
- BLE 设备：`0x08:0x80` 返回非空按钮列表时显示映射卡。
- 没有映射能力的设备不显示映射卡。
- 页面不按 PID 增加准入名单；遇到“有按钮列表却看不到页面”的问题，修运行时探测条件，不加静态 PID。

## 共享传输层

`IRazerMappingTransport` 把读按钮、读映射、写映射收敛成三个调用：

```text
GetButtonIdsAsync
GetAssignmentAsync(buttonId, mode)
SetAssignmentAsync(buttonId, mode, functionId, data)
```

HID 适配器复用 `RazerHidDeviceService` 的 `02:84 / 02:8C / 02:0C`；BLE 适配器复用 `RazerBleDeviceService` 的 `08:80 / 08:84 / 08:04`。两层共用 `RazerHidMappingViewModel` 和行模型，BLE 只保留自己的传输适配，不再维护另一套动作目录。

普通层和 HyperShift 层由 `LayerIndex` 切换。切换后旧行会清空，必须重新读取，避免把上一层的动作显示到本层。

## bundle 证据

设备 bundle 的 `main.*.js` 有两个直接相关的表：

- `BUTTON_LIST`：物理输入，包含 `inputID`、`inputType`、`isHyperShift`。
- `DEFAULTMAPPINGS`：默认动作。
- 写设备时 `buttonMappingToFnDataByte` 把动作编码为 `fnId + fnDataByte`。
- 宏组是 `MacroGroup`，播放方式为 `Once / NTimes / ContinuousHeld / ContinuousToggle / Sequence`。
- `macroGroupToFnDataByte` 用用户宏的 `guid` 在设备板载宏列表里找槽位，再把槽位号写进 `fnDataByte[0..1]`。

映射动作的播放方式对应：

| 雷云播放方式 | OpenSynapse function |
|---|---|
| Once / NTimes | `MacroTypeI`，数据 `[hi, lo, repeat]` |
| ContinuousHeld | `MacroTypeII` |
| ContinuousToggle | `MacroTypeIII` |
| Sequence | `MacroTypeIV` |

## 板载宏名称结构

协议 25 的宏数据区和名称区是两套命令：

- 宏数据：`06:08` 分配、`06:09` 写、`06:89` 读、`06:88` 查大小、`06:8B` 查槽列表。
- 宏名称：`06:140`（十六进制 `06:8C`）读、`06:12`（十六进制 `06:0C`）写；命令体固定为 80 字节，每次传 64 字节数据加 6 字节头部，剩余字节补零。读响应兼容 6 字节头部加数据和裸数据两种形状，但槽号、偏移或长度不匹配时直接报错。

名称结构固定 250 字节：

| 偏移 | 长度 | 字段 |
|---:|---:|---|
| 0 | 16 | GUID，按 .NET `Guid.ToByteArray()` 的字节序 |
| 16 | 4 | `numItems`，little-endian uint32 |
| 20 | 100 | UTF-8 名称，NUL 填充 |
| 120 | 16 | extraInfo 原始字节 |
| 136 | 16 | timestamp UTF-8，NUL 填充 |
| 152 | 32 | hash UTF-8，NUL 填充 |
| 184 | 66 | padding |

当前只对协议 25 的 HID 设备读写名称结构。BLE 协议 30 的宏槽语义没有足够证据，不冒充支持。

## 宏槽写入策略

映射行的 `Macro` 组显示本地 `MacroDefinition` 名称，不显示固定老板键动作。

保存顺序：

1. 读取设备槽列表和名称结构。
2. 选一个明确空闲的槽；用户可以改选其他空闲槽。已占用槽不会出现在空闲候选里，除非它正是该行已分配的槽。
3. `RazerOnboardMacroService.WriteAsync` 先写宏数据，再写名称/GUID，并分别回读校验。
4. 槽数据和名称都成功后，才写按钮 assignment。
5. assignment 继续走现有 `MacroTypeI/II/III/IV`，服务写后回读。
6. 任一步失败都抛出可读错误；失败前不写 assignment。

读回规则：

- 设备回读的槽 GUID 匹配本地宏时显示本地宏名。
- 否则显示槽名称；没有名称时显示 `Macro <slot>`。
- 任意组合键不会被伪装成用户宏。

没有板载宏能力的设备不显示 `Macro` 组。软件宏触发器需要单独证明物理 button id 的绑定路径，不在本页伪装成设备宏。

## 设备图

设备图是可选装饰。只有找到 `Assets/MappingLayouts/<PID>.svg` 时才显示；没有 SVG 的设备只显示行表格。

当前不伪造热点：

- 不从其他型号复制坐标。
- 没有逐型号热点证据就不做点击热区。
- 00B9 的 `DeviceManifest_185_0.json` SVG 可作为装饰资产，但不把草图坐标当热点证据。

## 固定 BossKey 的结论

雷云 00B9 bundle 里的 `supportsBossKey` / `bossKey` 是雷云自己的固定 5 动作，不是用户自定义宏。OpenSynapse 不复刻这条固定预设路径，也不在 profile 里保存老板键固定动作。用户要把宏放到鼠标键时，走 `MacroGroup + GUID + 板载槽`。
