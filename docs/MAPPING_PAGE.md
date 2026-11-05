# 映射页与板载宏

## 页面边界

映射页只做一件事：把设备实际接受的按键映射列出来，允许用户改目标动作，并在保存后回读确认。页面行来自设备返回的 button list，不来自型号白名单。

- RazerHid 鼠标/键盘：`0x02:0x84` 返回至少一个非零按钮 id 时，`ProbeMiddlewareCapabilitiesAsync` 才加入 `MappingRead` / `MappingWrite`。映射 GET 也是端点探测的最后一条只读回退，避免只回答映射的固件在身份探测阶段被丢掉。
- 独立键盘页（`RazerKeyboardService` 已收录的 23 个型号）：`FindAllAsync` 按物理设备枚举全部已连接键盘，每个键盘各自探测 `0x02:0x84`；不回答时再试 protocol 30 的 `0x08:0x80`，两者都返回非零按钮 id 才显示映射卡。
- Viper 00B8：沿用现有 `viper-184` 端点，读 `0x02:0x84 / 0x02:0x8C`，写 `0x02:0x0C`，通过 `RazerViperMappingTransport` 接入同一个行模型。
- BLE 设备：`0x08:0x80` 返回非空按钮列表时显示映射卡。
- 没有映射能力的设备不显示映射卡。
- 页面不按 PID 增加准入名单；遇到“有按钮列表却看不到页面”的问题，修运行时探测条件，不加静态 PID。

设备卡按 PnP/发现结果和 `Access == Available` 保留。无线鼠标睡眠时遥测会失败，但设备仍在；页面显示“不可读/查询失败”，不把设备卡和映射入口一起隐藏。映射能力在重新扫描后会参与连接比较；晚到的 `MappingRead` 会重建设备行。

## 共享传输层

`IRazerMappingTransport` 把读按钮、读映射、写映射收敛成三个调用：

```text
GetButtonIdsAsync
GetAssignmentAsync(buttonId, mode)
SetAssignmentAsync(buttonId, mode, functionId, data)
```

HID 适配器复用 `RazerHidDeviceService` 的 `02:84 / 02:8C / 02:0C`；独立键盘适配器复用 `RazerKeyboardService` 的同一组 middleware 命令，并在必要时使用 protocol 30 `08:80 / 08:84 / 08:04`；Viper 适配器复用 `IRazerDeviceTelemetryReader` 的 Product 184 读/写/回读路径；BLE 适配器复用 `RazerBleDeviceService` 的 `08:80 / 08:84 / 08:04`。

这些传输共用 `RazerHidMappingViewModel` 和行模型。动作目录通过 `IRazerMappingActionCatalog` 分开：HID/BLE 用 `RazerHidMappingActionCatalog`，Viper 用 bundle 已核实的 `RazerViperMappingActionCatalog`，独立键盘用 `RazerKeyboardMappingActionCatalog`（只把按钮名改成键位，不另造动作编码）。BLE 只保留自己的传输适配，不再维护另一套动作目录。

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

当前只对协议 25 的 HID 设备读写名称结构。BLE 协议 30 的宏槽语义没有足够证据，不冒充支持。独立键盘页的板载宏卡沿用同一服务；不在当前主机上的型号仍需实机验证。

## 独立键盘的 0x08 回退

`RazerButtonAssignmentProtocol` 的 `08:80 / 08:84 / 08:04` 帧来自现有 bundle 解码，测试覆盖的是帧布局和解析。独立键盘先尝试更常见的 `02:84`；只有它失败时才用 `08:80`（class id 2）。当前主机没有这 23 个独立键盘中的任何一台，因此这部分是协议/假传输验证，不是实机验证；如果实机不回答 `08:80`，映射卡会按“没有映射能力”隐藏，不会伪造行。

## 宏槽写入策略

映射行的 `Macro` 组显示本地 `MacroDefinition` 名称，不显示固定预设动作。

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

没有板载宏能力的设备不显示 `Macro` 组。独立键盘页在设备实际回答存储探测时显示宏组，不再用静态型号名单决定是否显示；Viper 当前没有接入板载宏名称结构，因此不显示宏组。软件宏触发器需要单独证明物理 button id 的绑定路径，不在本页伪装成设备宏。

Viper 保存后会用设备回读的 16 条 assignment 更新 profile，避免下次启动时旧 profile 把用户改动覆盖回去。

## 设备图

设备图是可选装饰。映射卡按 PID 查找 `Assets/MappingLayouts/<PID>.svg` 或 `.png`，以资产实际存在为准；没有设备图的设备只显示行表格。图片按绝对文件路径加载，发布目录里不依赖 `ms-appx:///` 解析。

当前包含 `00B8.png`：来自 Razer 产品 184 的官方 dashboard 图（`https://apps.razer.com/synapse/products/184/mw/184_0/PluginImages/184_0_0_dashboard3x.png`），仅作 Viper 00B8 的设备装饰，不提供热点坐标。

当前不伪造热点：

- 不从其他型号复制坐标。
- 没有逐型号热点证据就不做点击热区。
- 00B9 的 `DeviceManifest_185_0.json` SVG 可作为装饰资产，但不把草图坐标当热点证据。
