# 本地音频 bundle 与实际传输路径

核查日期：2026-11-23。来源是本地 `main*.js` 的 `useFeature("rzDeviceType", ...)`、设备 `DeviceInfo` 的 `dongleId` / `bleId`，以及当前工作区的服务表和调用点。未沿用旧 audio summary 的支持结论。

本地源根为 [bundles](../artifacts/razer-mw-full-cdn/bundles)，共 **337 个 main**；交叉检查了 `artifacts/upstream-reference/razer-mw-full-cdn/bundles`，没有额外的 main PID。本表覆盖 **76 个音频主包**，另列 **7 个 audioProtocol 配件／采集卡主包和 0566 Control Pod**。所有 PID 均为十六进制，JS 的 alias 常量为十进制。表中的主 PID 可直接定位到 `bundles/<PID>/main*.js`，每个目录只有一个 main。

“已分派”表示当前代码有对应传输和消费者，不代表 bundle 的每项功能均已实现或通过实机测试。下面分别标出代码已接通与原始协议缺失；没有以主包存在或产品名代替实际调用链。

## 当前路径索引

| 路径简称 | 实际入口与消费者 |
| --- | --- |
| 通用音频 HID | [RazerHeadsetChannelCatalog](../src/OpenSynapse.Windows/Devices/RazerHeadsetChannelCatalog.cs) → [RazerHeadsetAudioService](../src/OpenSynapse.Windows/Devices/RazerHeadsetAudioService.cs) / [Discovery](../src/OpenSynapse.Windows/Devices/RazerHeadsetAudioService.Discovery.cs) → `RazerHeadsetAudioViewModel.Services.cs` 的实际服务分派；profile 使用 `HeadsetAudioProfileApplier`。必须同时满足产品表、通道及接口条件。 |
| Avnera | `RazerAvneraHeadsetService` / `RazerAvnera65Protocol`，由通用音频页面消费。 |
| Mxic / T3 / Jolie / protocol40 | `RazerMxicHeadsetService` 按 PID 选择 dialect；共用容器不表示共用报文。 |
| Valkyrie / Billie | `RazerValkyrieHeadsetService.GetProfile` 按 PID 选协议，通用音频页面消费。 |
| BlackShark 独立页 | `BlackSharkHeadsetService` / `BlackSharkHeadsetViewModel`，保留独立 THX 页面，不经过通用音频 HID 发现。 |
| NXP serial | `RazerNxpSerialDiscovery` → `RazerNxpSerialTransport` / `RazerNxpHeadsetService` → `RazerHeadsetAudioViewModel.Nxp.cs`；profile 为 `HeadsetAudioProfileApplier.Nxp.cs`。 |
| Audiowise BLE | [RazerAwBleTransport](../src/OpenSynapse.Windows/Devices/RazerAwBleTransport.cs) 的 `RazerAwBleDiscovery` / `WindowsRazerAwBleTransport` → `RazerAwBleHeadsetService` → `MainViewModel.AwBle` / `RazerAwBleHeadsetViewModel`；profile 为 `HeadsetPeripheralProfileApplier`，设备 key 继续使用 `ble:<device-id>`。 |
| Leviathan 054A 独立页 | `RazerHidDeviceCatalog.AddLeviathanV2X` → `RazerHidDeviceService.CreateLeviathanV2XAudio` → `LeviathanV2XAudioService` / `LeviathanV2XAudioViewModel`，挂在原生 HID 页的 `Soundbar`。 |
| Nommo 旧款原生页 | 原生 HID discovery → `RazerHidDeviceViewModel.Speaker` → `RazerSpeakerAccessoryViewModel.Nommo` / `NommoAudioPanel` → `RazerSpeakerAccessoryService.Nommo`；profile 为 `AccessoryProfileReplay` → `RazerHidWorkspace.Nommo.ApplyNommoProfilesAsync`，遍历所有已发现的 0517/0518。复用原生灯光卡，不创建第二张 Chroma 卡。 |

## 76 个音频主包，按实际通道分组

本表 alias 列只列不同 PID 的有线／dongle 身份；BLE aliases 集中列在后面的缺协议表中。`—` 表示没有额外有线／dongle 身份，不表示没有 BLE alias。

| 主包 PID | main 注册的 rzDeviceType | 有线／dongle alias | 当前实际路径与状态 |
| --- | --- | --- | --- |
| 051A、051C、051E、0528 | 前三项 `AudioAvneraDevice`；0528 为 `AudioAvneraDeviceOdessaT1` | — | **已分派** `AvneraFeature65` → Avnera 服务；64-byte output/report 255，65-byte feature 应答。0528 继承 Avnera，仅扩展固件命令。 |
| 0520、0521、0526、0527、0529、0560 | 依次为 `AudioKrakenTE`、`AudioKrakenKitty`、`AudioKrakenXUSB`、`AudioKrakenUltimate`、`AudioKrakenOdessaT2`、`AudioKrakenKittyT3` | — | **已分派** `KrakenBase37` → `RazerHeadsetAudioService.KrakenBase.cs`；37-byte HID write/read 路径。 |
| 0533、0549；0537、056D、0582；0554 | 前两项 `AudioKrystalT2`；中三项 `AudioKrystalT3`；0554 为 `AudioKittyV2Pro` | — | **已分派** `KrakenBase37`；本地类继承 `AudioKrakenBase`，不走 91-byte feature。 |
| 0532、0548 | `rzDevice25Noa` | — | **已分派** `Protocol25Feature65` → `RazerHeadsetAudioService.Protocol25.cs` / `RazerProtocol25FeatureTransport`；0548 的 BLE 0558 单列。 |
| 05A3 | `rzDevice25NoaT2ECR` | — | **已分派** `Protocol25Feature64` → 同上，保留短 feature 格式差异。 |
| 056A、056F、058E、05A6、058C | 依次为 `rzDevice25AudioCamyT3V2`、`rzDevice25AudioCamy`、`rzDevice25AudioMarieT1`、`rzDevice25AudioCamyT2V3R`、`AudioLindaT3` | —；058C dongleId 为自身 | **已分派** `Protocol25Feature65` → protocol25 音频服务。 |
| 0542、0543 | `AudioCamyT1`、`AudioCamyT2` | — | **已分派** `CamyT1` → `RazerHeadsetAudioService.CamyT1.cs` / `RazerCamyT1Protocol`，8-byte HID output。 |
| 0F1B | `AudioCamyT3Device` | — | **已分派** `Feature91` → `RazerHeadsetAudioService`；main 另注册 `audioProtocol:mappingEngine`。 |
| 0539、053B | `AudioMxicDevice` | 0539→053A；053B→053C | **已分派** `Mxic64` → Mxic `MabelT1` dialect，保留该族 report 1 与 `50 41` envelope，不能仅按 channel 名写死 report 2。 |
| 0555、0583、0584 | `AudioMxicDevice` | —；0583/0584 dongleId 为自身 | **已分派** `Mxic64` → Mxic legacy dialect。 |
| 059F | `rzDevice40BillieT3H` | — | **已分派** `Mxic64` → Mxic 的 `Class18` dialect。 |
| 057C、05B6、0A54、1022 | `rzDevice25ValkyrieT3` | 057C→057D；0A54→0A5B；1022→1021 | **已分派** `ValkyrieT3Feature64` → Mxic T3 dialect，64-byte report 7 / class 0x18。0A54 的 BLE 0A56 单列。 |
| 05AB、05AD | `rzDeviceJolie` | — | **已分派** `Jolie64` → Mxic Jolie 分支，64-byte report 7；十段 EQ setter 保留 group/preset 字节。 |
| 0593 | `rzDevice40HeadsetAiroha` | 0593→0592 | **已分派** `Mxic64` → protocol40 dialect；当前能力为电池／充电，不能据此声称已有 EQ／麦克风控制。 |
| 05B9 | `rzDevice40` | —；bleId 为 0 | **已分派** Mxic protocol40 的 **64-byte FF14 feature** 路径；不是普通 output。当前 PID 特例开启 EQ、mic、sidetone、DND。bleId 0 不生成身份。 |
| 0A61、0A64、0A68、0A6C、0A81 | `rzDevice40` | —；DeviceInfo dongleId 均为自身 | **已分派** `Mxic64` → protocol40；EQ/DND、电池按现有能力表处理，mic/sidetone 只对 05B9 开启。它们的 BLE aliases 不复用 HID 路径。 |
| 0579、0A4E、0A51、101E、101F | `AudioValkyrie` | 0579→057A；0A4E→0A55；0A51→0A58；101F→101E | **已分派** `Valkyrie64` → Valkyrie 服务；101E 同时有自己的 main，且 dongleId 为自身。 |
| 0567、056B | `AudioBillieT1`、`AudioBillieT2` | 0567→0568；056B→056C | **已分派** `Billie64` → Valkyrie 服务的 Billie profile。 |
| 0587 | `AudioBastetT1` | 0587→0588 | **已分派** `Billie64` → 共享 BillieT2 命令表；BLE 0589 缺协议。 |
| 056E、0574 | `AudioOdessaT2Wireless`、`AudioMabelT3C` | 056E→0565；0574 dongleId 为自身 | **已分派** `Billie64`；056E 覆盖了继承的 Mxic sender，使用 Billie 格式，0565 不是 BlackShark V3 Pro。 |
| 0576、101C | `AudioValkyrie` | 0576→0577；101C→101B | **独立已分派** `DedicatedBlackShark` → BlackShark 服务及 THX 页。101C 不需要为了通用音频产品表补一条重复卡片；101B 的 alias 有 101C main 的 dongleId:4123 依据。 |
| 053E | `AudioMixer` | — | **独立服务已分派** `Mixer053E` → `RazerMixer053EService`；64-byte feature / 16-byte report 9 collection。 |
| 052D | `AudioNXPDevice` | 052D→052C（dongleId:1324） | **已接 serial** `NxpSerial` → NXP discovery/service/UI/profile；串口接口 GUID `86e0d1e0-8089-11d0-9ce4-08003e301f73`，不以 HID 长度推断协议。 |
| 0309、0530、0534、0562、057E、057F | `AudioAWKittyBLE` | — | **独立已分派** `AudiowiseBle` → AwBle GATT discovery/service/UI/profile/battery。六项均已入 catalog；generic HID `IsSupported` 排除该 channel。 |
| 054A | `rzDevice25NoaT3` | — | **独立已分派** Leviathan V2 X HID `Soundbar` 路径，report 7 / 91-byte feature；没有 generic headset catalog 行属于独立入口设计。BLE 0559 仍缺协议。 |
| 0517、0518 | `rzDevice25`；isAudio 分别为 `nommoChroma` / `nommoPro` | — | **原生入口与 profile 已接通**。共同提供系统端点音量／静音和 HID EQ preset；0518 增加八段 EQ、低音、Dolby、输入源。原生 HID VM 直接创建 Speaker，绕开原来只从 Chroma Body 创建的死入口。 |
| 0570、05A2 | `rzDevice25Carol` | —；05A2 dongleId 为自身 | **已分派** `Protocol25Feature65` → `RazerHeadsetAudioService.Carol` / `RazerHeadsetAudioViewModel.Carol` / `HeadsetAudioProfileApplier.Carol`。两款均按各自协议读取无线在线状态；关闭自动休眠时按十秒周期维护电源，维护失败不覆盖在线状态。 |
| 055A、055C、055E | `rzDevice25NommoV2Pro` | — | **已分派** `Feature91` → `RazerAudioFeatureProtocol` / `RazerHeadsetAudioService.Feature91` / VM 与 profile 的 Feature91 partial。使用 report ID 7，优先选择明确声明该 ID 的 collection；单一合法集合沿用原路径，多集合不明确时用固件查询确认。EQ preset 使用 profile 1，bass 为固件 1–7 级；HID 不提供 THX 自定义 EQ bands。 |
| 0F66 | `rzDevice25MarciT1` | — | **已分派** Soma Pro 的 Feature91 路径：consumer-control usage `000C:0001`、report ID 16、独立 checksum、至少 40 ms 等待，十段 EQ 为有符号十分之一 dB。多集合按同样的描述符优先／只读查询规则选择。 |
| 0F6A | `rzDevice25`；isAudio 为 `augustT2` | — | **缺输入事件协议**：原始消费者证实取下／放回切换播放及录音默认端点；本地缺 HID 输入集合、启用过程、原始报文到 Key93/94 的转换和端点 role 细节，尚未开放该自动切换功能。 |

0A81 main 同时包含共享默认配置 `dongleId:2660`（0A64）和本机 `DeviceInfo productId:2689,dongleId:2689`。本表采用本机 DeviceInfo，**不把共享默认常量算作 0A81 的额外 alias**。

当前表还保留三项没有独立本地主包的身份：**051F**（Avnera/Nari Essential 族）、**058A**（KrakenBase37 / KittyT3）、**05B3**（ValkyrieT3Feature64）。它们是当前实现保留项，本次不能用不存在的 main 为它们补证；也不把它们算入 76 个主包。

## 十四个 BLE alias：身份已证实，GATT 协议仍缺失

以下 16 条父包映射合并为 **14 个不同 BLE PID**，当前 catalog 均保持已知 `Unsupported`。来源列是主包目录及 `bleId` 原值；父包的 HID `rzDeviceType` 已列于上表，不能直接视为 BLE 端的设备类。

| BLE PID | 父 main PID 与 JS bleId | 当前缺口 |
| --- | --- | --- |
| 0558 | 0548：`bleId:1368` | Noa USB 类有 report 8 / 65-byte feature；缺 BLE 端的服务与帧配置。 |
| 0559 | 054A：`bleId:1369` | NoaT3 USB 类有 report 7 / 91-byte feature；独立 Leviathan HID 服务不覆盖此 GATT 身份。 |
| 0575、0578 | 0576：`bleId:1397`；0579：`bleId:1400` | catalog 的 `AudioValkyrieBle` 未找到对应本地实现。 |
| 0A4F、0A52 | 0A4E：`bleId:2639`；0A51：`bleId:2642` | 同上。 |
| 101D | 101C：`bleId:4125` | 同上。 |
| 1020 | 101E、101F 均为 `bleId:4128` | 两个 main 共用一个 BLE 身份；本轮补入已知 Unsupported。 |
| 0589 | 0587：`bleId:1417` | catalog 的 `AudioBastetT1Ble` 未找到对应本地实现。 |
| 0A56 | 0A54：`bleId:2646` | catalog 的 `rzDevice25ValkyrieT3Ble` 未找到对应本地实现，不能复用父包 64-byte HID。 |
| 0A62 | 0A61：`bleId:2658` | Hammerhead BLE 缺具体设备类／UUID 配置；protocol40 HID 不证明 GATT 可复用。 |
| 0A65 | 0A64、0A68 均为 `bleId:2661` | 两个 main 共用 0A65，不能按顺序虚构 0A69。 |
| 0A6D、0A82 | 0A6C：`bleId:2669`；0A81：`bleId:2690` | 同 Hammerhead 缺口。 |

两份本地 bundles 的 JS 中均未找到 `AudioValkyrieBle`、`AudioBastetT1Ble`、`rzDevice25ValkyrieT3Ble` 实现。解包后端 [noble/index.js](../artifacts/reverse-engineering/razer-app-engine/asar-extracted/electron/modules/noble/index.js) 有 `rzDevice30`、`rzDevice30V2`、`rzDevice30Layla`、`rzDevice40`、`AudioAWBLE`、`AudioAWKittyBLE` 策略；策略存在仍不足以给上述 PID 选择服务 UUID 和设备初始化。

当前 `RazerBleDeviceService` 是 protocol30 通用鼠标路径，页面读取 DPI／polling；没有 0558/0559 音频派生路径。因此这两项不能以“已有 RazerBle”计入完成。

**恢复条件**：取得对应 BLE 设备类／配置，包含 service、write、notify UUID、初始化过程、请求帧与应答解码，或取得可验证的实机请求／响应抓包；然后补发现、服务、页面、profile、电池分派及回归测试。所有权由主线程安排后续 BLE 实现者；缺这些输入时保持已知 Unsupported。

## AwBle 六项的可复用依据与本轮修复

六份 `AudioAWKittyBLE.*.js` 的 `connectDevice()` 都以 `[1328,1332,777,1378,1406,1407]`（0530/0534/0309/0562/057E/057F）覆盖服务配置，使用下列 UUID 和 `withoutResponse=true`：

```text
service  416d656c-2d52-617a-6572-424c4501f40a
write    416d0000-2d52-617a-6572-424c4501f40a
notify   416d0001-2d52-617a-6572-424c4501f40a
```

类构造函数默认 `fd65` 会被上述分支覆盖，不能因此扩大发现到所有 fd65 设备。六项复用已有 AwBle 控制及能力探测，补齐 Windows GATT 路径 PID 解析、列表身份、profile descriptor 的真实 PID；路径没有 PID 时保留 0，仍按服务 UUID 工作。

[protocolAudiowise.js](../artifacts/reverse-engineering/razer-app-engine/asar-extracted/electron/modules/noble/strategy/protocolAudiowise.js) 的 `write()` 接受 transfer type **1 或 2**；单独 `notify()` 只广播 type 2。当前 parser 已接受两种应答，三字节零 payload 确认不再等第四字节；type 0 请求回显按完整帧消费，防止 payload 被当作新 header 而卡住后续确认。

`WindowsRazerAwBleTransport.ConnectAsync` 取得 service 后，characteristic 查询、CCCD 订阅或 transport 构造失败／取消都会释放 service；成功返回时由 transport 持有并在 Dispose 释放。每个 transport 的 queue、exchange semaphore 都为实例状态；每次 exchange 使用新的拼包 buffer，并丢弃队列里上一命令剩余的通知。此次没有引入连接间共享 parser 状态。

## 音频桥接配件与采集卡，单独计数

这些 main 的音频声明也纳入扫描，避免把“有灯光卡片”计成“有音频控制”。`simpleService` / `mappingEngine` 是主包注册值；仅凭这个名字不能断言需要哪种硬件写包。

| 主包 PID | main 声明 | 当前实际路径／缺口 |
| --- | --- | --- |
| 0D03 | Ripsaw HD，`rzDeviceKateT2` + `audioProtocol:simpleService`；默认 profile 有 master/pc/hdmi/micVolume | **已分派** `RipsawFeature17` → `RazerRipsawService` / `RazerRipsawTransport` → `RazerHeadsetAudioViewModel.Ripsaw` / `RazerRipsawPanel`，由 `RazerRipsawProfileApplier` 恢复配置。 |
| 0F20、0F21 | Base Station V2 Chroma / Thunderbolt 4 Dock Chroma；`rzDevice25` + `audioProtocol:simpleService` | **缺对应版本消费者**：0F20 缺 `4320.b8627a5c9842d29e8f0e.js`、`6259.09f0d39f3a312e98d9e3.js`；0F21 缺 `4320.de440e1c96084e643f2f.js`、`6259.dbf006b2f2310bd5df32.js`。其他版本 helper 按 container／名称选择专属端点，不能据此套用四款新 Dock 的全局默认端点。保留原生灯光支持。 |
| 0F52、0F59、0F5A、0F5C | Thunderbolt 5 Dock / Monitor Stand / Laptop Monitor Stand / Handheld Dock；`rzDevice25` + `audioProtocol:mappingEngine` | **系统播放端点入口已接**：`RazerAccessoryBodyViewModel.EndpointAudio` → `RazerDockEndpointAudioViewModel` / `RazerDockEndpointAudioService`。控制 Windows 默认 multimedia playback endpoint，写入前校验 endpoint identity 并读回；不是 HID 音频控制，也不随设备 profile 自动覆盖系统音量。 |
| 0566 | Wireless Control Pod，`rzDevice30Layla`；`bleId:1382` 为自身 | **已接通 Layla 路径**：fd65 发现 → `RazerLaylaService` 与同物理容器的 HID 输入 → `RazerLaylaActionExecutor` → 八种手势映射页及 profile 恢复。退出／输入失败时恢复模式 `[5,1]`。此项不算十四个耳机 BLE alias。 |

**0F20、0F21、0F6A** 的后续实现依赖上表列出的原始消费者或可验证事件抓包；取得资料后再接对应端点控制和恢复路径。`simpleService` 注册本身不能确定端点选择和硬件事件契约。

## 旧 Nommo 的实际读写与 profile

0517/0518 主包分别注册 `isAudio:nommoChroma` / `nommoPro`。0518 的 `rzDevice25NommoPro.0649009f42bf66dae45b.js` 和 `rzDevice25NommoChroma.0b115bd6c976924d7824.js` 使用以 containerId 初始化的原生音量 API，UI 0–100 与 OS scalar 0–1 互换。当前 `WindowsSpeakerEndpoint` 按 **同一 HID connection 的 container GUID** 选 render endpoint，不回退到系统默认设备；端点缺失／多义时返回错误，HID 功能仍可独立读取。

`0518/6120.dee1fe110baf2c370677.js` 的 class 08 表证明 preset `82/02`、八段 EQ `84/04`、bass `87/07`、source `8A/0A`、Dolby `8C/0C`；payload 首字节为 profile 0。EQ payload 长度 9，Dolby 长度 11。0518 main 的频率为 `60,250,500,1000,2000,4000,8000,16000 Hz`；同一 6120 chunk 将 UI −9…+9 dB 编码为 `Math.round((dB+9)/18*100)`。Custom preset 为 16，先关 Dolby，再设 preset 和 EQ；普通／Dolby preset 使用 0–3。输入源为 SPDIF=1、AUX=2、Bluetooth=3、USB=4，切换安排在该轮写入最后。

当前页面用已读到的能力开放控件；不为 0517 查询 Pro 专用字段，不发送未证实的麦克风／output-source 命令。profile key 使用 `ProfileResolver.GetDeviceKey(connection.ToDescriptor())`，保留物理实例身份；volume/EQ/bass/Dolby/source 存入 `Headset`，mute 存入该设备的 `Extras["nommo.muted"]`。恢复入口接收 document、descriptor 与 power source，合并 Headset 后单独读取 mute extra，避免把不含 Extras 的 `ResolvedProfile` 当成 `DeviceProfileSettings`。切换 profile 遍历全部 Nommo connections；保存失败恢复内存配置并显示“设备已应用、配置未保存”，不改灯光字段。

## 0566 Layla 的模式会话与主机映射

来源均为本地原始文件：

| 来源 | 确定的契约 |
| --- | --- |
| `0566/main.4f3aaea63065195762e6.js` | 注册 `rzDevice30Layla`，PID/bleId 均为十进制 1382；初始化 `setDeviceMode:[5,0]`，退出 `[5,1]`，`ignoreCheckMode:true`；输入报告 ID 1。 |
| `0566/rzDevice30Layla.a681ae388937f737fefb.js` 与 `rzDevice30.7ae626cdad9704a5d620.js` 模块 73064 | 服务 `0000fd65-0000-1000-8000-00805f9b34fb`，write `416d0000-2d52-617a-6572-424c4501f40a`，notify `416d0001-2d52-617a-6572-424c4501f40a`。8-byte header，bytes 1–3 为大端 payload length，bytes 4/5 为 class/command；mode 使用 `01:82` / `01:02`。 |
| `artifacts/reverse-engineering/razer-app-engine/asar-extracted/electron/modules/noble/strategy/protocol30Layla.js` | 整帧一次 GATT write，按 characteristic 选择 writeWithoutResponse；应答按 transaction ID 匹配、status 位于 byte 7、payload 从 byte 8 开始。不能直接使用现有鼠标 BLE 的小端长度、20-byte reply header、header/payload 分段发送。 |
| `0566/rzHardwareEventsProtocolLaylaParser.877362030d74cbf93a9b.js` 与 `0566/6120.e6211e8479e522eff9a6.js` | 输入 `[1, sourcePress, topPress, rotation]`；source/top 为单击、双击等次数，rotation 1/255 为顺／逆时针。`ControlPodInput` 被转换为主机 `topButton/sourceButton/dial` 事件，映射动作由主机引擎执行。不能把继承的鼠标 08:04 固件映射 API 当成实际旋钮消费路径。 |

`RazerBleDiscovery` 在既有鼠标服务之外枚举 fd65，仅接受 PID 0566，并保留 Windows container identity。`RazerLaylaHidInput` 只绑定同容器、同 PID 的输入集合，避免两台 Pod 串接。会话先打开输入，再切模式 `[5,0]`；正常退出、输入异常或初始化失败都尝试恢复 `[5,1]`，随后释放 HID/GATT。模式恢复失败记录具体错误。

`MainViewModel.Layla` 按连接长期持有会话，切换页面不停止映射。八种手势分别是顶部单击／双击／三击／长按、音源键单击／双击、顺时针／逆时针旋转；媒体动作复用现有输入 sink，其他键盘、鼠标、文本和命令动作可使用宏库，每个手势执行一次。配置按 `ble:<device-id>` 保存；保存失败保留原映射，切换 profile 时更新执行者。

发现、选择、配置回放和退出共用 Layla 生命周期门；开始退出即取消初始化并禁止新增会话，已发出的枚举随后返回也不能重新进入模式 5。模式恢复只在 `[5,1]` 成功应答后清除标记；首次失败时，最终 Dispose 在释放传输前再作有界补偿。宏库变更会先停用旧宏副本，再更新全部 Pod 的映射；删除或禁用的宏不会继续执行。

旧 class 17 OLED 服务不作为 Layla 的支持依据。新增路径复用了现有 BLE transport/session 的独立 Layla 分支，没有把鼠标的小端长度或分段发送套给 Pod。协议、模式进退与映射测试已通过；真实 UI 六项检查覆盖八行绑定、选择、保存失败及恢复。尚无 0566 真机的蓝牙断连／恢复验收。

## 0A49 的来源缺口

**0A49（当前任务所指 Xbox V2 身份）在两份本地 bundles 中都没有目录／main，也没有可核验的本地 Xbox 协议类。** `HeadsetUsbFallbackTests.cs` 的 `No verified Xbox V2 protocol` 注释，以及 `HeadsetDiscoveryTests.MatchingReportLengthsDoNotInventAnAudioProtocol` 记录了当前有意不按相似 HID report 长度接入的行为。测试是现有行为依据，不能充当协议来源。

恢复条件是补到该 PID 的原始设备 bundle／协议类，或实机请求应答及接口证据；由主线程安排实现者确认通道后接发现和完整消费链路。目前保持未支持，不推断产品名、不套用别的 Xbox／Valkyrie 报文。

## 验证记录

协议测试直接对照本地 JS 生成的请求及响应：NXP 覆盖十二种请求、五种响应、关闭震动及两条 MCU 路由；Ripsaw 覆盖十七种方法；Carol 覆盖两个 PID；Feature91 覆盖 checksum、256 个有符号十分之一 dB 值和 Nommo 的七档低音。AwBle 覆盖六个设备、两种应答类型、分片、空确认和请求回显。

音频、profile、多设备发现、BLE 与 Control Pod 整合回归 **1,041 项通过**（`audio-final.trx`）；之后针对审查修复与固件探测追加／复跑的 **89 项通过**（`audio-review-fixes.trx`，与前一组有重叠，不相加）。新增页面事件及资源检查通过，旧全局静态失败单列于下文。

最终 `OpenSynapse.App` Release/x64 构建通过，**0 警告、0 错误**。本轮未生成安装包或修改版本号。

独立 WinUI 预览使用实际 Panel/ViewModel 和替身传输，不连接用户硬件。Ripsaw 的八项交互检查、Nommo 的四项检查、Dock 的五项检查、Control Pod 的六项检查均通过；已查看宽窄窗口截图。检查包含 UI 绑定、写入／回读、profile 保存失败的恢复，以及 Windows 默认播放端点改变后拒绝误写。

多设备运行时以物理连接持有长期 session。普通页面切换不停止其他设备的灯效；拔出设备先取消该连接的操作，再释放 ViewModel。配置与关屏恢复遍历所有已连接设备。Studio 接管时只停止目标连接的灯效；Jolie 按各自 baseline 恢复。

| 对抗检查点 | 处理与证据 |
| --- | --- |
| 同 PID 多台设备被合并，或恢复写到另一台设备 | discovery 按有效 container、USB 父节点与物理 key 关联；串口只替换自身占位；显式 Mixer 路径失配不回退。对应发现、profile 与 Jolie 多设备恢复测试通过。 |
| 断连后旧异步操作启动灯效，或切页释放仍在工作的设备 | `HeadsetAudioSession` 串行操作和断连取消；首帧后再次检查取消；关机先取消运行操作，再直接关灯。session 测试覆盖排队、调用者取消及连接隔离。 |
| AwBle 空确认、回显或 payload 中伪 header 导致串帧 | 按完整帧边界解析，单次 exchange 持有局部拼包 buffer；空 ACK、逐字节回显及 payload header 回归测试通过。 |
| 错误 Feature Report 集合被当成有效音频接口 | 描述符中 report 7／16 是正向证据；无法枚举时保持 unknown，单一合法集合保留原行为，多集合通过 `00:81` 固件查询确认。每集合 750 ms、每物理设备三秒预算，继续校验 report ID、事务、命令、status、长度与产品 checksum 规则。原生 ABI、多集合、取消和超时测试通过，本机 27 接口已只读验证。 |
| 系统默认音频端点改变后误写，或保存失败丢失配置 | Dock 写前比对端点身份；Nommo/Ripsaw 保留或恢复原 profile，错误后需刷新。替身服务与真实 UI 检查通过。 |

GATT 查询／订阅失败会释放已取得的 service；协议／会话测试不替代每款外设的实机取消、重连和固件验收。当前没有全部型号的真机覆盖。

扩展检查曾报告三项全局静态失败：profile 标记审计的四个既有遗漏、`ProfilesPage` 的 `BindableDevices/HasBindableDevices` 绑定断言，以及 Blade 性能卡图标断言。已修本次新增方法的标记，剩余旧失败不计入音频协议验证通过结果。

测试源码按仓库既有 `/tests/` 忽略策略保留在本地；此矩阵与产品代码提交，测试结果存于 `tests/OpenSynapse.Core.Tests/TestResults`。
