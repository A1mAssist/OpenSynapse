# 设备适配协作指南

这份指南用于让其他设备拥有者协助 OpenSynapse 适配新设备或补全已有设备。目标是拿到可审查的协议证据，而不是收集完整的雷云安装包、猜测 HID 命令或公开用户的私人数据。

## 1. 先确认设备是否已经支持

先查 [SUPPORTED_DEVICES.md](SUPPORTED_DEVICES.md)。如果型号已经在列表里，应该按“缺少某个能力”处理，不要当成全新设备。

OpenRazer 目录里有设备记录，只能说明上游已经有实现，不能证明当前固件、当前 Windows HID collection 和每个控制项都已经验证。

## 2. 需要设备拥有者提供什么

- 具体型号和 SKU，例如 `Razer Blade 15 (Mid 2019)` / `RZ09-03006`。
- USB VID/PID，例如 `1532:0246`。
- Windows 版本和 build 号。
- Synapse 版本。
- 希望优先适配的功能，按优先级排列。
- 是否愿意安装临时测试构建并反馈结果。
- 采集脚本生成的 `manifest.txt`，或采集 ZIP 的临时链接。

不要在公开 issue 里发布：

- Razer 专有的 JavaScript、DLL、驱动、固件或安装包；
- 完整的 Razer AppEngine 或 Synapse 安装目录；
- 序列号、账号标识、token、HID 路径或本机绝对路径；
- 未检查过的原始抓包和日志。

采集脚本只会打包匹配到的 JavaScript 缓存候选文件。请用临时网盘链接发送 ZIP，并在审查完成后删除。不要执行别人发来的 JavaScript。

## 3. 证据获取顺序

### 3.1 先用 OpenRazer

如果该型号已经有上游实现，优先复用 OpenRazer 的报文定义、设备 manifest、能力边界和测试。这是成本最低、最容易审查的路径。

### 3.2 再看本地 Synapse 产品 JavaScript

当产品专属能力没有 OpenRazer 覆盖时，使用本地 Synapse 缓存里的产品主 bundle 和 `rzDevice25` 或公共协议 chunk。

这条路径通常可以得到：

- Product ID 和设备信息；
- `[dataSize, class, command]` 报文头；
- 参数顺序和大小端；
- 返回布局和枚举；
- GET/SET 对应关系；
- transaction、CRC、批量发送和互斥规则；
- 产品代码实际调用了哪些方法。

它不能直接证明当前设备接受某个写入、设置能否持久化、灯光效果是否真的可见。这些仍然必须通过真机验证。

### 3.3 需要时再做 USB 抓包

只有在 JavaScript 和现有源码都没有目标命令时才抓包。一次只改变一个可见设置，抓很短的窗口，操作完恢复原值，原始抓包放在仓库外。

抓到一个写入只证明“发生过这个事务”，不证明 OpenSynapse 可以安全地发送同一条命令。正式接入仍然需要读回或确定性行为验证。

### 3.4 最后才看 Native DLL 或服务

只用于产品 JavaScript 和 HID 抓包都覆盖不到的功能，例如 MappingEngine 输入或灯效算法。每个常量都要记录模块版本、函数或偏移，以及对应的动态观察证据。

## 4. 采集脚本

前置条件：Windows、已安装 Razer Synapse，并且至少打开过一次该设备的页面，让产品 bundle 进入缓存。

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Collect-RazerProductBundle.ps1 `
  -DeviceName "Blade 15" `
  -Sku "RZ09-030" `
  -UsbPid "0246"
```

可选参数：

- `-ProductId 710`：已知 Synapse Product ID 时使用。
- `-Root <path>`：Razer AppEngine 用户数据不在默认位置时使用。
- `-MaxFiles 60`：限制打包文件数量。

脚本会：

- 只读扫描本地 Razer AppEngine 缓存；
- 匹配设备名、SKU、USB PID、可选 Product ID 和 `rzDevice25` 引用；
- 只复制匹配到的 JavaScript 候选文件；
- 生成 `manifest.txt` 和 `README.txt`；
- 在 `%TEMP%` 下生成 ZIP。

如果找不到文件，先在 Synapse 里打开该设备页面，等待加载完成后再运行。

## 5. 维护者流程

1. **分诊**：确认型号、PID、当前支持状态和用户想要的功能。
2. **收集**：索取 collector manifest 或临时链接，不要索取完整 Synapse 安装包。
3. **提取**：把产品调用映射成报文头、参数、响应和能力边界，记录来源和日期。
4. **做候选适配**：加入 manifest 或 protocol builder/parser，新能力标记为 `SourceBacked`，先不开放生产写入。
5. **测试构建**：让设备拥有者先做只读检查，再做同值写入、最小变化写入、读回和恢复。
6. **升级状态**：只有当前设备证据和恢复都通过，才标记 `Verified`，并更新设备列表和 changelog。
7. **保留证据**：保留审查过的常量、测试和简短证据说明；不要提交原始专有文件或未审查抓包。

## 6. 能力状态

| 状态 | 含义 |
|---|---|
| `SourceBacked` | 已从源码或本地产品文件确认命令和布局，仅开放只读探测。 |
| `TestBuild` | 已有候选构建，当前设备验证尚未完成。 |
| `Verified` | 已在当前设备完成读取、写入、读回或确定性行为验证，并成功恢复。 |
| `Blocked` | 知道有该能力，但缺少安全命令、响应、持久化或恢复路径。 |

只有 ACK 不代表 `Verified`。

## 7. 安全与验收

每个写入路径必须具备：

- 当前设备的匹配读取，或明确说明为什么没有读取命令；
- 同值或最小变化的写入测试；
- 读回或确定性行为确认；
- `finally` 或等价恢复机制；
- 灯光等可见效果的目视确认；
- 不提供任意 raw HID 控制台、命令枚举或猜测值域。

适配收尾前确认：

- 型号、SKU、PID 和 HID collection 能准确对应；
- 页面只展示设备真实支持的控制项；
- 不支持的功能不显示假按钮；
- 设备拥有者已经测试对应构建；
- 文档和 changelog 准确描述支持边界。

## 8. 设备拥有者回报模板

使用仓库的设备支持 issue 模板，或在新 issue 中包含：

```text
设备型号：
SKU / 型号编号：
USB VID:PID：
Windows build：
Synapse 版本：
希望优先适配的功能：
是否能测试临时构建：
Collector manifest 或临时链接：
当前异常表现：
```

## 9. 隐私与许可

- 不提交、不再分发 Razer 专有文件。
- 不执行用户提供的 JavaScript，只做静态分析。
- 原始文件和原始抓包留在 Git 外。
- 仓库只提交独立编写的协议常量、manifest、测试和审查过的证据说明。
- 发布前移除序列号、账号名、机器名、HID 路径和无关 USB 流量。