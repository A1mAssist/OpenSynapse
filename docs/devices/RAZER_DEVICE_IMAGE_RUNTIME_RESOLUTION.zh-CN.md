# 雷蛇设备图片运行时解析（暂缓实现）

- 状态：调研完成，未实现
- 日期：2026-10-28
- 适用：设备页可视化映射（雷云「自定义」页样式）
- 结论：可行。图片和 `PID -> URL` 清单都不进安装包；运行时根据设备名和类别生成产品页地址，解析产品页 JSON-LD 的图片直链，下载后缓存在用户目录。

> 本文只记录方案。当前按用户要求先不实现；恢复开发时以本文第 7 节为入口。

## 1. 要解决的问题

设备页的可视化映射需要一张设备图，图上叠加按钮热点、引线和当前动作文本。现有实现只有 `MainWindow.xaml` 里的表格映射卡，没有设备图。

直接把所有设备的图片打进安装包不可接受：按 268 台、每张约 100 KB 估算，会增加约 20–50 MB；而安装包当前约 65 MB。

目标是不打包图片，也不打包 `PID -> URL` 清单。首次打开某台设备的页面时，在用户设备上完成：

1. 由设备名和类别生成雷蛇产品页地址；
2. 请求产品页，解析 JSON-LD 中的图片直链；
3. 下载图片并缓存；
4. 后续直接使用缓存。

## 2. 已核实的事实

### 2.1 雷云 middleware manifest 没有产品图

`https://apps.razer.com/synapse/products/185/mw/manifest.json`（`185` = `0x00B9`，Razer Basilisk V3 X HyperSpeed）只有分类图标：

```json
"icons": [
  {
    "src": "/synapse/assets/imgs/favicon/MOUSE.svg",
    "sizes": "64x64 32x32 24x24 16x16",
    "type": "image/svg+xml"
  }
]
```

没有设备渲染图，也没有图片地址字段。不能按 PID 直接拼出产品图。

### 2.2 官网产品页有干净的 500×500 渲染图

`00B9` 的公开图片地址：

```text
https://medias-p1.phoenix.razer.com/sys-master-phoenix-images-container/h3a/hb0/9529652609054/basilisk-v3-x-hyperspeed-2-500x500.png
```

2026-10-28 实测：

```text
HTTP 200
Content-Type: image/png
Content-Length: 119695
Cache-Control: public, max-age=31536000
```

图片是透明底产品图，适合放在映射面板里。

### 2.3 产品页 JSON-LD 提供图片地址

产品页 `https://www.razer.com/gaming-mice/razer-basilisk-v3-x-hyperspeed` 的 JSON-LD 中有 `"image"` 字段，指向 `assets3.razerzone.com` 的缩放代理，代理地址里编码了上面的原图地址。

因此图片直链不能从 PID 或名称计算，必须请求产品页并从 JSON-LD 读取。

### 2.4 产品页地址可以由设备名和类别生成

`Razer Basilisk V3 X HyperSpeed` + `MOUSE`：

```text
https://www.razer.com/gaming-mice/razer-basilisk-v3-x-hyperspeed
```

2026-10-28 抽样验证 6 个常见设备，5 个直接返回 200：

| 设备 | 生成的产品页 | 结果 |
|---|---|---|
| Razer Basilisk V3 X HyperSpeed | `/gaming-mice/razer-basilisk-v3-x-hyperspeed` | 200 |
| Razer BlackWidow V4 Pro | `/gaming-keyboards/razer-blackwidow-v4-pro` | 200 |
| Razer Kraken V4 | `/gaming-headsets/razer-kraken-v4` | 200 |
| Razer Blade 18 | `/gaming-laptops/razer-blade-18` | 200 |
| Razer Goliathus Extended Chroma | `/gaming-mouse-mats/razer-goliathus-extended-chroma` | 200 |
| Razer Seiren V3 Mini | `/gaming-microphones/razer-seiren-v3-mini` | 404 |

Seiren V3 Mini 的正确类别是 `streaming-microphones`，不是 `gaming-microphones`。这说明类别映射需要单独维护，并且 404 必须有兜底。

## 3. 方案

新增一个运行时解析器（名称暂定 `RazerProductImageResolver`）：

输入：

- `deviceName`：设备显示名，例如 `Razer Basilisk V3 X HyperSpeed`
- `category`：设备类别，例如 `Mouse`
- `productId`：USB PID，例如 `0x00B9`
- `editionId`：可选，用于区分黑色/白色/联名版

输出：

- 产品页地址
- 图片直链
- 本地缓存路径

处理流程：

1. 把设备名规范化为产品页 slug。
2. 把类别映射为官网路径段。
3. 拼接 `https://www.razer.com/{categorySlug}/{productSlug}`。
4. `GET` 产品页，限制超时和响应大小。
5. 用 `JsonDocument` 解析所有 `application/ld+json` 块，找到 `@type` 含 `Product` 且带 `image` 的对象。
6. 取 `image`（字符串或数组的第一项），转为绝对 HTTPS 地址。
7. 下载图片，校验 `Content-Type` 和大小。
8. 原子写入缓存：图片 + sidecar JSON。
9. 返回缓存路径给可视化视图。

### 3.1 产品页 URL 生成规则

名称规范化：

- 转小写；
- 空格转连字符；
- 去掉除连字符外的标点；
- 合并连续连字符；
- 保留开头的 `razer-`，因为官网 slug 包含它。

示例：

```text
Razer Basilisk V3 X HyperSpeed
-> razer-basilisk-v3-x-hyperspeed
```

类别映射初始表：

| `DeviceCategory` | 官网路径段 | 状态 |
|---|---|---|
| Mouse | `gaming-mice` | 已验证 |
| Keyboard | `gaming-keyboards` | 已验证 |
| Headset | `gaming-headsets` | 已验证 |
| Laptop | `gaming-laptops` | 已验证 |
| MouseMat | `gaming-mouse-mats` | 已验证 |
| Microphone | `streaming-microphones` | 已验证（Seiren V3 Mini） |
| Accessory | `gaming-accessories` | 待验证 |
| Monitor | `gaming-monitors` | 待验证 |
| Speaker | `gaming-speakers` | 待验证 |

类别表是代码，不是数据文件；不随设备数量增长。

### 3.2 产品页解析

不要用正则直接从 HTML 抠图片地址。产品页是 Angular 应用，图片可能在懒加载资源里；可靠的是首屏 HTML 里的 JSON-LD。

建议：

- 只接受 `https://www.razer.com` 的产品页；
- 最多 3 次重定向，只允许 `razer.com` / `razerzone.com` 域；
- HTML 最大 4 MB；
- 使用 `JsonDocument` 解析，不执行页面脚本；
- 校验 JSON-LD 的 `@type` 包含 `Product`；
- 可选用 `name` 与设备名做归一化比对，防止拿到错误型号的页面。

### 3.3 图片下载

- 只接受 HTTPS；
- 只接受 `image/png` 或 `image/webp`；
- 不接受远程 SVG；
- 单张最大 2 MB；
- 超时 10 秒；
- 使用浏览器风格 `User-Agent`，不携带设备序列号；
- 先写临时文件，成功后原子替换，避免半张图。

### 3.4 缓存

缓存目录使用现有 `AppDataPaths`：

```text
%LOCALAPPDATA%\A1mAssist\OpenSynapse\device-images\
```

文件：

```text
<PID>-<edition>.png
<PID>-<edition>.json
```

sidecar JSON 记录：

```json
{
  "schemaVersion": 1,
  "productId": "00B9",
  "editionId": 0,
  "deviceName": "Razer Basilisk V3 X HyperSpeed",
  "productUrl": "https://www.razer.com/gaming-mice/razer-basilisk-v3-x-hyperspeed",
  "imageUrl": "https://medias-p1.phoenix.razer.com/.../basilisk-v3-x-hyperspeed-2-500x500.png",
  "fetchedAtUtc": "2026-10-28T00:00:00Z"
}
```

规则：

- 图片已存在且非空：直接使用，不再请求官网；
- 图片不存在：解析并下载；
- 解析或下载失败：记录一次日志，回退线稿/表格，不要反复重试；
- 失败后至少冷却 7 天，或应用版本变化后再试；
- 不把缓存写入 Program Files。

### 3.5 失败与回退

以下情况都回退，不阻塞设备页：

- 产品页 404/403；
- 页面没有 JSON-LD 或没有 `image`；
- 图片 404/403 或内容类型不对；
- 网络超时、离线；
- 用户取消页面加载。

回退顺序：

1. 本地通用线稿（例如鼠标俯视轮廓）；
2. 现有表格映射卡；
3. 不显示图片，只显示按钮列表。

不要在设备页主线程同步等待图片；图片下载是可选增强，不能影响映射读写。

## 4. 边界情况

### 4.1 同 PID 多 edition

`00B9` 的 manifest 有 `editionId: [0, 128]`，分别对应黑色和白色版。缓存 key 必须带 edition；否则白色版会显示黑色图。

如果运行时拿不到 edition，先用 `0` 的图，并在 sidecar 里记录 `editionUnknown: true`。

### 4.2 同名多代

`Razer Blade 18` 在 2023/2024/2025/2026 都叫这个名字。产品页 `razer-blade-18` 通常展示最新一代，拿到的图可能不是用户手里的那一代。

处理方式：

- 优先使用 manifest 的 `version` / `releaseDate` / `editionId` 辅助判断；
- 判断不了就用家族通用图或线稿；
- 不要为了凑图把最新一代的图硬套到老款上。

### 4.3 名称与官网 slug 不一致

官网 slug 不一定等于设备显示名。例如：

- 白色版可能带 `-white-edition`；
- 联名版可能带额外后缀；
- 麦克风类别可能是 `streaming-microphones`；
- 老设备可能已经下架。

当前方案不维护逐设备 URL 表，因此必须有回退。若 404 比例过高，再考虑加一个很小的手动覆盖表（只存例外，不存全量）。

### 4.4 版权与条款

从 Razer 服务器拉图，不等于获得了再分发授权。图片仍由 Razer 提供，应用只是在展示。开源分发需要确认：

- 是否属于官方 press assets 允许的使用范围；
- 是否需要保留版权声明；
- 是否应改用自己绘制的线稿。

这是产品/法务决策，不是技术问题。技术上可做，不代表可以直接做。

## 5. 安全与隐私

- 只允许 `www.razer.com` 产品页和 `*.razerzone.com` / `*.razer.com` 图片域；
- 不接受设备数据里传来的任意 URL；
- 限制重定向域名；
- 限制 HTML 4 MB、图片 2 MB；
- 不执行远程 HTML/JS/SVG；
- 不发送设备序列号、用户名、机器名；
- 只在当前设备页请求，不在启动时批量扫描；
- 失败日志不记录完整响应体。

## 6. 为什么现在不做

按当前决定，先记录方案，不改代码。原因：

- 可视化映射页本身还没做，图片解析没有落点；
- 按钮热点坐标仍需逐设备整理，图片解析只解决“长什么样”；
- 需要先确认版权/条款；
- 运行时解析依赖 Razer 官网结构，属于可选增强，不应阻塞当前版本。

## 7. 恢复开发时的实现清单

1. 在 `OpenSynapse.Windows/Devices` 增加 `RazerProductImageResolver`。
2. 增加 `RazerProductPageParser`，只做 JSON-LD 解析。
3. 增加 `DeviceImageCache`，使用 `AppDataPaths.Combine("device-images", ...)`。
4. 在可视化映射视图首次显示当前设备时调用解析器；不要在启动时调用。
5. 失败回退到本地线稿/表格；不弹错误条。
6. 单元测试使用保存的 HTML 样本，不访问网络：
   - 名称 slug 生成；
   - 类别映射；
   - JSON-LD 字符串/数组图片；
   - 404/无 JSON-LD；
   - 缓存命中；
   - 错误 content-type 拒绝。

建议的入口文件：

- `src/OpenSynapse.App/MainWindow.xaml`：现有映射卡在约 752 行；
- `src/OpenSynapse.App/ViewModels/OpenRazerMappingViewModel.cs`：现有按钮行和动作选择；
- `src/OpenSynapse.Windows/Protocols/OpenRazerMappingProtocol.cs`：`02:84/8C/0C` 按钮映射协议；
- `src/OpenSynapse.Core/Storage/AppDataPaths.cs`：用户缓存目录。

## 8. 验证记录

2026-10-28：

- `00B9` middleware manifest 无产品图，只有 `MOUSE.svg`；
- `00B9` 500×500 PNG 返回 200、`image/png`、119695 字节、`Cache-Control: public, max-age=31536000`；
- 产品页 JSON-LD 含 `image`；
- 6 个产品页 URL 抽样，5 个 200，Seiren V3 Mini 的类别映射需要修正；
- 以上均为网络/静态数据验证，未在应用内实现，未在实机设备页验证。
