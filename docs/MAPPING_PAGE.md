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

## 设备图与键位（运行时）

设备图不是打包资源。映射卡第一次打开某个 PID 时按设备类别取：

- **鼠标**：先读 `https://apps.razer.com/synapse/products/{decimalPid}/ui/asset-manifest.json`，从 `main.js` 取该产品自己的 `drawLines()` 锚点，从 `static/media/prd-3x.*.avif` 取产品图；AVIF 由打包的 libavif + dav1d 解码，保留 alpha。
- **键盘**：优先读 `DeviceManifest_{PID}_0.json` 的灯光工作室 SVG，直接画 `productfill` / `productoutline` / `characters` / LED 键位，不画引线。`edition 0` 不存在时再试 `129`；清单在线获取并写入 ETag 磁盘缓存，没有缓存时回退行表格。现有 catalog 里 edition 129 的样本不一定是键盘，这个兜底没有实机验证，不能当键盘专用版本。
- **Keypad**：清单 `category=KEYPAD` 时走 `StudioKeyboardVisualBuilder.BuildKeypad`。SVG 的 `selection-N` 路径按出现顺序编号为 `key1..keyN`，`scroll` 单独识别；按键 id 按 bundle 的 `counter` 1..20 和 `ScrollClick` 的 3 绑定。D-pad 不在该 SVG 里，仍保留行表格，不猜坐标。
- **键盘没有 studio SVG**：回退产品 bundle 的 3x 图 + `drawLines` 锚点，和鼠标共用一套 callout。多数键盘 bundle 没有 `drawLines` 或 `prd-3x`，这种型号仍然只显示行表格。
- **没有对应资源**：只显示行表格；映射读写不受影响。

AVIF 由随包分发的 libavif 1.3.0 + dav1d 1.4.3 解码：输出 BGRA8，alpha 原样保留，再编码成 PNG 交给 `BitmapImage`。Windows 自带的 AVIF 解码器会丢掉 alpha，半透明投影会变成不透明黑底或矩形条纹，所以映射图不走平台 AVIF 解码，也不做亮度提亮或颜色抠图。

映射页不打包逐型号的预生成图片或锚点，00B8 的图片和锚点同样来自产品 184 的在线 bundle；下载或解码失败时只显示行表格。产品 184 的 3x AVIF 在映射页截图中 alpha 边缘干净，没有矩形或条纹。

没有 `drawLines` 的型号仍能显示设备图：manifest 有 `prd-3x` 时按实际图片尺寸显示；只有 `static/media/prd.svg` 时按 SVG 宽高显示。两种情况都不生成热点，行编辑保留在下方表格里，不把图片上的任意位置伪造成按钮。

`drawLines(index, this.props.buttonList[N], x, y, !isLeft)` 里的 `N` 指向当前 `groupList` 的按钮对象。新 bundle 也会先写 `const e=this.props.buttonList||[]`，再调用 `drawLine(i, e[i]||t, x, y, !isLeft)`；解析器识别这种 buttonList 别名，仍按同一按钮表解析。`counter` 是 bundle 的界面序号，不是固件按钮 id：00B8 设备实际回读的按钮是 `1,2,3,4,5,9,10,96`，而 bundle 的 `counter` 是 `1..8`。解析锚点时先把标准鼠标键位映射到固件 id（`LeftClick→1`、`RightClick→2`、`ScrollClick→3`、`MouseButton4→4`、`MouseButton5→5`、`ScrollUp→9`、`ScrollDown→10`、`CycleUpSensitivityStages→0x60`），未知键才回退 `counter`；`buttonPos` 是左右列。`drawLine` 实际画圆点的中心是 `x + 3*dotRadius`、`y + dotRadius`，再减去产品图在画布中的居中偏移 `(cvsW - 300) / 2`、`(cvsH - 340) / 2`；`cvsW`、`cvsH`、`dotRadius` 从 bundle 的同一段画布配置读取，缺省为 `310`、`340`、`2.5`。3x 图比例必须是 `300:340`，否则映射卡回退表格，不把锚点画到错误位置。左右列优先用 `buttonPos`，没有时才用 `!isLeft`；省略第五参数时按 `drawLine` 默认值归到左列。没有固件 id 的 `drawLine` 不生成热点。

行标题按同一固件 id 用 bundle 的 `buttonKey` 生成（`ScrollUp → Scroll up`、`MouseButton4 → Button 4`、`CycleUpSensitivityStages → DPI cycle up`）；没有 bundle 名称时才回退通用鼠标表。不要把 `counter` 直接当按钮 id，否则 00B8 的 4/5 会把侧键引线画到滚轮上。

不是每个按钮都有锚点。产品 99 的 bundle 有 9 个 `drawLine` 位置；只有能从按钮对象解析出固件 id 的位置生成热点，其余行保留在下方表格里。没有锚点的行不伪造中心点。

键盘 SVG 的 `characters` 层是键帽文字，`led` 层是键位热区，Razer logo（`KeyName=Logo` 或 `selection-group=logo`）只画不点。键名到固件键 id 用标准 USB HID usage 映射，例如 `A → 4`、`Esc → 41`、`LeftShift → 225`；没有可靠映射的键仍显示，但点击不会硬选一行。键盘不铺 111 条引线，当前选中的键用描边高亮，编辑器在下方，完整行表格也保留。

键帽字符是设计稿里的浅灰 `#D2D2D2`，在深色主题上可见；在白色或透明背景上单独渲染会几乎看不见，这不是字符层丢失。2026-11-06 用真实 590 SVG 做深色等价渲染，两条 `characters` 路径分别是 12729 / 107509 字节，104 个键位，字符像素主色为 `#D2D2D2`。

资源全部运行时获取并缓存：

- 鼠标清单和 `main.js`：`%LOCALAPPDATA%\A1mAssist\OpenSynapse\mapping\devices\{PID}\`
- 键盘清单：`%LOCALAPPDATA%\A1mAssist\OpenSynapse\mapping\lighting-manifests\`
- 清单支持 ETag 和离线缓存；`main.js`、3x 图按文件名缓存。清单 1 MB、脚本 16 MB、图片 4 MB 上限，单次请求 8 秒超时。
- 解析结果按 `(PID, 设备类别)` 在进程内缓存；第二次打开不重复下载。

2026-11-06 的 `AvailableDevices.json` 有 335 个产品 id，其中 174 个有 `prd-3x`，1 个清单 404；没有 3x 图或锚点的型号回退表格。这里不维护静态 PID 名单，也不把 00B9 的整机 SVG 坐标套到其他型号。00B9 的 `DeviceManifest_185_0.json` 只有整机轮廓，没有逐按钮锚点，因此不生成热点。映射页也不读取打包的 `studio-manifests.zip`；键盘图只来自在线清单或它在运行时的磁盘缓存。

BLE 协议 30 当前没有可靠的产品 id，映射页照常显示按钮行，设备图回退表格；不按蓝牙名称猜 PID。HID 鼠标、独立键盘和 Viper 都按各自实际 PID 走上面的运行时资源路径。

## 实时解析探针

`tools/Probe-RazerMappingAssets.ps1` 用来验证某个产品的 3x 图、鼠标锚点和键盘路径锚点是否能从线上 bundle 实时读取。

```powershell
pwsh -NoProfile -File tools/Probe-RazerMappingAssets.ps1 -ProductId 184
pwsh -NoProfile -File tools/Probe-RazerMappingAssets.ps1 -ProductId 590
```

鼠标锚点来自 UI bundle 的 `drawLines()`；当前检查的 590 键盘 bundle 没有 `drawLines`，每个键的 `buttonList[].d` 路径通过 WPF `Geometry.Parse()` 计算包围盒中心。键盘的产品图不是单张 `prd-3x`，而是多张 `img_prods/*-3x.png`；探针会同时列出这些 URL。

2026-11-06 实测：

- 184：8 个鼠标锚点，3x 图可用；冷启动约 1.26 秒，缓存 `main.js` 后约 0.29 秒
- 185：9 个鼠标锚点；冷启动约 1.28 秒，缓存后约 0.29 秒
- 99：9 个 drawLine 位置，其中 5 个能确认按钮 id；冷启动约 1.24 秒，缓存后约 0.24 秒
- 590：111 个键盘路径锚点（按 HID 去重），20 张键盘图片；冷启动约 0.9–1.3 秒，缓存后约 0.3 秒

2026-11-06 对 `AvailableDevices.json` 的 335 个产品 id 做了全量扫描：334 个有 `asset-manifest`，174 个有 `prd-3x`；用 `RazerMappingBundleParser` 实际解析后，71 个鼠标 bundle 有非空锚点，74 个键盘清单能生成键位路径。其余型号多数不是映射设备，或者 bundle 没有 `drawLines` / 键位证据，按设计回退行表格。没有锚点但有图片资源的型号现在只显示装饰图：208/199 用 `prd-3x`，235/226/45061/45063 用 `prd.svg`，555/580 用 studio keypad SVG；555/580 的 20 个按键和滚轮有热点，D-pad 没有 SVG 证据，保留行表格。PID 680 的 `asset-manifest` 返回 404。

`main.js` 缓存在 `%TEMP%\OpenSynapse-RazerMappingProbe`。正式映射页复用同一套解析规则，并把清单、`main.js`、图片和解析结果写入 `%LOCALAPPDATA%` 缓存；冷启动仍需约 1 秒网络时间，命中缓存后只读本地文件。