# 飞利浦 Hue 发现与配对（Razer Synapse Hue 模块）

本文依据 Razer 官方 CDN 当前 Hue 模块 `3.10.0630.062011` 做静态分析，不执行安装程序，也不连接任何桥。
早期 `3.4.0711` 模块用于交叉验证，两者的发现方式和托管接口一致。

## 来源与文件

当前安装包 `RazerPhilipsHueSetup_v3.10.0630.062011.exe`（NSIS，3,933,152 B）由 Razer 的
LWI 产品清单接口返回，下载地址为：

```
https://cdn.razersynapse.com/1750405942xTCFDCgDRazerPhilipsHueSetup_v3.10.0630.062011.exe
```

取得清单的路径是：

1. `GET https://discovery.razerapi.com/user/endpoints`，返回 `{"name":"prod","hash":"O1MFnpKa"}`。
2. `POST https://manifest.razerapi.com/api/legacy/O1MFnpKa/prod/lwiproductlist/get`，请求体为
   `<COP><Platform><Arch>64</Arch><Locale>en-US</Locale><Mfr>Razer</Mfr><Model>Blade</Model><OS>WINDOWS</OS><OSVer>11</OSVer><SKU></SKU></Platform></COP>`。
3. 返回的 `Emily3` 产品下列出 `HUE` 模块及上面的 `DownloadURL`。

解包后的关键文件：

| 文件 | 作用 |
|---|---|
| `Lib\PhilipsHue\Release\RzDetMgrHue.dll` | 2,586,936 B，版本 3.0.0.8；发现、配对、桥 HTTP 客户端与 Entertainment 流；静态链接 mbedTLS 和 mDNSResponder |
| `Bin\PhilipsHue\Common.NativePhilipsHueWrapper\Common.NativePhilipsHueWrapper.dll` | 201,928 B，C++/CLI 包装，暴露 `DiscoverBridges` / `FindBridge` / `PairBridge` / `UnPairBridge` / `GetBridgeStatus` / `GetEntertainmentGroupList` / `GetLightList` / `SelectGroup` / `Abort` |
| `Bin\PhilipsHue\Hub.PhilipsHue`、`Controller.PhilipsHue`、`Contract.PhilipsHue`、`Configurator.PhilipsHue` | 服务端命令层：`PutDiscover` / `PutPair` / `PutFindBridge` / `PutSelectGroup` / `PutUnPair` |
| `Devices\Philips Hue\Bin\DD_Philips Hue.rzdd`、`RSy3_DeviceStatus.dll` | 设备描述与状态上报 |

Synapse 4 中间件 JS 里的 `ConfigureFFI` 声明 `philipshuev2.dll`，调用面与上面的包装 DLL 一致；
模块路径是 `%LOCALAPPDATA%\Razer\Apps\Synapse\philipshuev2.dll`。Synapse 4 安装包本身不携带这个文件，
它是按设备/模块单独下载的旧版兼容组件。

## 发现：四种方式

`RzDetMgrHue.dll` 同时实现四种发现，任何一个先命中就回调：

1. **SSDP / UPnP**（UDP，目标组播 `239.255.255.250`，SSDP 默认端口 1900）
   - 请求：`M-SEARCH * HTTP/1.1`、`HOST: …`、`Man:"ssdp:discover"`、`ST:ssdp:all`
   - 响应按以下正则解析，取出 IP、固件版本、12 位桥 ID 和 `hue-bridgeid`：
     ```
     LOCATION: http://(.+):.+\r\nSERVER:.+IpBridge/(.+)\r\nST:.+\r\nUSN: uuid:.+-([0-9a-zA-Z]{12})::upnp:rootdevice(\r\nhue-bridgeid:\s*([0-9a-zA-Z]*))?
     LOCATION: http://(.+):.+\r\nSERVER:.+IpBridge/(.+)\r\nhue-bridgeid:\s*([0-9a-zA-Z].+)\r\n
     ```
2. **N-UPnP**：`GET https://discovery.meethue.com`（代码里也保留 `/api/nupnp`），读取
   `internalipaddress`；失败时记录 `BridgeDiscoveryNupnpTask: http request failed`。
3. **mDNS**：查询服务 `_hue._tcp`，使用内置的 mDNSResponder 实现（没有外部 Bonjour 依赖）。
4. **网段扫描**：`IPHLPAPI` 枚举网卡，`BridgeDiscoveryIpscan` 生成候选地址，
   `BridgeDiscoveryCheckIpArrayTask` 逐个探测；没有可用网卡时报
   `BridgeDiscoveryIpscan: no valid network interface available`。

扫描可被 `Abort()` 中止；引擎侧 `scanDevice()` 有 12 秒超时，找不到时允许用户手输 IP，走
`FindBridge(ip)`。

## 配对与控制

- 配对前读取 `GET /api/nouser/config`。
- 配对：按桥上的 push-link 按钮后 `POST /api`，请求体以 `{"devicetype": "` 开头；成功后保存返回的
  `username`（即 app key）。
- 控制走 CLIP v1：`/api/<username>/lights`、`/api/<username>/groups`、`/lights/(\d+)`。
- 配置写到 `bridge.json`（`BridgeFileName`），保存桥列表、当前桥和固定下来的证书。

## Entertainment 流

- 使用飞利浦 **Hue Entertainment SDK**（字符串里是 `huestream.*`：`Bridge`、`Group`、`Light`、
  `Scene`、`AreaEffect` 等）。
- 传输是 **DTLS**（mbedTLS，源码路径泄露为 `philipshue_edk_1.33.0_Dev\3rd_party\mbedtls`），
  所以需要先在 Hue App 里建好娱乐区域并选择娱乐组。
- 桥使用自签证书，客户端做证书固定：日志字符串
  `BridgeHttpClient: Pinning: Using persisted certificate for bridge …`，首次连接记录
  `Connecting to a bridge without a pinned certificate: SSL verification is disabled`。
- 圆形桥不支持 Entertainment；桥固件过旧或未建娱乐区域时会给出对应提示。

## 对 OpenSynapse 的意义

当前接入（`PhilipsHueDiscovery` + `PhilipsHueViewModel`）：

- 发现：SSDP `M-SEARCH`（`ST:ssdp:all`）解析 `LOCATION` / `hue-bridgeid`，同时请求
  `https://discovery.meethue.com`（含 `/api/nupnp`）作为跨网段兜底，按桥地址合并结果；
- 配对：CLIP v2 的 `POST /api` 流程，保存返回的 `username`；
- 控制：`/clip/v2/resource/light` 逐灯读写；
- 出现在设备列表的条件：已保存桥地址，或发现桥；未发现时不显示占位卡；
- 不做后台轮询，没有后台网络或唤醒成本；
- 只在进入设备页、设备页可见时窗口重新显示、或点“重新探测”时扫一次；
- 桥在设备页打开期间上线时，点“重新探测”或离开再进入设备页即可发现；
- 没有已保存地址且发现不到桥时，设备页不提供 Hue 入口；手动 IP 只在 Hue 面板内可见
  （面板由已出现的 Hue 卡片进入）；
- mDNS `_hue._tcp` 与 `IPHLPAPI` 网段扫描尚未实现；SSDP + N-UPnP 覆盖常规局域网。

Entertainment/DTLS 是独立的一大块，只在需要低延迟流光时再做；它不影响发现和逐灯控制。

## 显示逻辑（对齐雷云）

雷云把 Hue 做成可选的 Configurator 模块（安装包清单里 `IsOptionalModule=true`、
`RequiredCapability=DoNotPreselect`），不是普通设备卡。模块自己的 UI 状态包括
`ScanAndPair`、`BridgeNotFound`、`BridgeConnected`、`BridgeNotConnected`、`UpdateRequired`
（见 `Razer.Synapse3.HueUI.dll`），未连接时的“扫描/配对”界面留在 Hue 模块页里。

OpenSynapse 没有模块页，因此等价规则是：不做后台轮询，只在设备页可见时发现；只有发现桥
或已有保存地址时才在设备列表出现 Hue 卡片；没有桥时设备列表里不出现任何 Hue 占位项。

## 证据边界

- 分析对象是 Razer 官方 CDN 当前 Hue 模块（安装包 3.10.0630.062011，DLL 版本 3.0.0.8），
  并用 2019 年的 3.4.0711 模块交叉验证。
- Synapse 4 安装包本身不含 `philipshuev2.dll`；中间件指向的 `Apps\Synapse` 组件来自单独的
  兼容模块下载，当前抓到的 3.10 模块暴露的调用面与中间件声明一致。
- 结论来自字符串、导入表和托管层日志字符串，没有动态抓包，也没有连接真实桥。
