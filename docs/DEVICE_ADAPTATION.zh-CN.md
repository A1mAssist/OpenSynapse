# 设备适配协作指南

这份指南分成“设备拥有者三步”和“维护者参考”两部分。普通用户不需要看懂协议部分也能帮忙。

## 设备拥有者：三步

### 1. 提交设备支持请求

使用[设备支持 issue 模板](https://github.com/A1mAssist/OpenSynapse/issues/new?template=device-support.yml)，填写：

- 具体型号和 SKU；
- USB PID；找不到就写 `unknown`；
- Windows build 和 Synapse 版本；
- 最想优先适配的功能；
- 是否愿意安装临时测试构建。

如果型号已经在 [SUPPORTED_DEVICES.md](SUPPORTED_DEVICES.md) 里，说明是哪个能力缺失，不用当成全新设备。

### 2. 方便的话运行采集脚本

脚本是只读的，只会在本地寻找 Synapse 产品 JavaScript，生成 ZIP 和 manifest。下载 [Collect-RazerProductBundle.ps1](https://raw.githubusercontent.com/A1mAssist/OpenSynapse/main/tools/Collect-RazerProductBundle.ps1)，保存到 `Downloads` 文件夹，然后运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$HOME\Downloads\Collect-RazerProductBundle.ps1"
```

如果粘贴后出现 `>>` 提示，说明末尾的续行符被一起复制了；按 Ctrl+C，重新复制上面这一行。

如果提示找不到脚本，确认文件确实在 `Downloads`；下载目录被 OneDrive 或浏览器改过时，把路径换成实际保存位置。

不用填参数。脚本会自动识别当前连接的 Razer 设备，并按设备名称和 USB PID 匹配。如果识别不到你的设备，再加 `-Sku` 或 `-UsbPid`。脚本会在 `%TEMP%` 下生成 ZIP，并打印路径。

如果找不到文件，先在 Synapse 里打开一次该设备页面，等待加载完成后再运行。

不想运行 PowerShell 也可以跳过，在 issue 里说明即可，维护者会再指导。

### 3. 发送结果并测试构建

不要把 Razer 专有文件、序列号或账号数据直接发到公开 issue。把 ZIP 放到临时网盘后发链接，或者只粘贴 manifest 摘要。

维护者会提取协议并发送临时构建。先测试只读功能；愿意的话再测试写入。每次写入测试都会包含读回和恢复。

## 接下来会发生什么

1. 维护者先检查 OpenRazer。
2. OpenRazer 没有覆盖时，再看本地 Synapse 产品文件。
3. 新功能先以只读候选加入。
4. 给设备拥有者发送测试构建。
5. 设备拥有者先测试读取；有写入时，再测试安全写入、读回和恢复。
6. 全部通过后才开放正式控制。

## 隐私

- 不要把 Razer JavaScript、DLL、驱动、固件、安装包、序列号、token 或账号数据发到公开 issue。
- 采集脚本只打包匹配到的 JavaScript 缓存候选文件，不执行 JavaScript。
- 原始产品文件和 USB 抓包留在 Git 外。
- 仓库只提交审查过的协议常量和测试，不提交原始专有文件。

## 维护者参考

### 证据顺序

按成本从低到高：

1. **OpenRazer 源码或目录**：优先复用已有实现和 manifest。
2. **本地 Synapse 产品 JavaScript**：产品主 bundle 和 `rzDevice25` 或公共协议 chunk。
3. **OpenSynapse 诊断读取/扫描**：设备已经能被打开时，用读取按钮或命令扫描记录 baseline，确认候选 GET 是否有响应。
4. **USB 抓包**：源码和诊断都回答不了时再用；一次只改一个可见设置，操作完恢复原值。
5. **Native DLL 或服务**：只在产品 JavaScript、诊断和 HID 抓包都覆盖不到时使用。

诊断是确认工具，不会生成 manifest、发现命令语义或升级能力。对于 OpenSynapse 还打不开的设备，先做最小只读连接，或者退回有边界的抓包。

鼠标不再开放 HID 命令扫描。部分无线鼠标会把这些探测当成接收器或配对指令，后续只能使用已记录的 OpenRazer 读取命令，并先恢复 2.4 GHz 连接再测试。

### 采集脚本参数

- 不填参数：自动识别当前连接的 Razer 设备，按设备名称和 USB PID 匹配；没有识别结果时回退到包含 `rzDevice25` 的文件。
- `-Sku`：可选，SKU 或型号编号前缀。
- `-UsbPid`：可选，`0246` 或 `1532:0246` 两种格式都支持。
- `-ProductId`：可选，Synapse Product ID。
- `-Root`：可选，Razer AppEngine 用户数据目录。
- `-MaxFiles`：可选，限制打包文件数量。

### 维护者流程

1. **分诊**：确认型号、PID、当前支持状态和用户想要的功能。
2. **收集**：索取 collector manifest 或临时链接。
3. **提取**：把产品调用映射成报文头、参数、响应和能力边界。
4. **读取与基线**：设备能打开时，用诊断读取/扫描记录当前值和候选 GET 响应。
5. **做候选适配**：加入 manifest 或 protocol builder/parser，新能力标记为 `SourceBacked`，先不开放生产写入。
6. **测试构建**：先只读，再做同值写入、最小变化写入、读回和恢复。
7. **升级状态**：只有当前设备证据和恢复都通过，才标记 `Verified`，并更新设备列表和 changelog。
8. **保留证据**：保留审查过的常量、测试和简短证据说明；不要提交原始专有文件或未审查抓包。

### 能力状态

| 状态 | 含义 |
|---|---|
| `SourceBacked` | 已从源码或本地产品文件确认命令和布局，仅开放只读探测。 |
| `TestBuild` | 已有候选构建，当前设备验证尚未完成。 |
| `Verified` | 已在当前设备完成读取、写入、读回或确定性行为验证，并成功恢复。 |
| `Blocked` | 知道有该能力，但缺少安全命令、响应、持久化或恢复路径。 |

只有 ACK 不代表 `Verified`。

`SourceBacked` 是开发阶段的证据状态，不是诊断页运行时生成的。诊断页只展示最终的能力或错误状态。

### 安全与验收

每个写入路径必须具备：

- 当前设备的匹配读取，或明确说明为什么没有读取命令；
- 同值或最小变化的写入测试；
- 读回或确定性行为确认；
- `finally` 或等价恢复机制；
- 灯光等可见效果的目视确认；
- 不提供任意 raw HID 控制台、命令枚举或猜测值域。

适配收尾前确认：UI 不展示不支持的控制项，设备拥有者已经测试对应构建，文档和 changelog 准确描述支持边界。
