# CPU 功耗墙：用户补丁与当前 main 差异分析

- 分析日期：2026-10-31
- 当前分支：`main`，HEAD `c5cbf84`
- 用户补丁：`D:\Downloads\OpenSynapse-补丁-1.6.0-CPU功耗墙.zip`，已解压到 `artifacts/user-cpu-power-patch-20261031/OpenSynapse-补丁-1.6.0-CPU功耗墙/`
- 补丁版本：`OpenSynapse.App.dll` 的 ProductVersion 为 `1.6.0+b41af4d65c04d767fbfe07133b75d11d73db7e51`
- 补丁说明中的功能提交：`bdb9cd4 / 7090053 / 6982456 / 8de3fc4 / b41af4d`

证据边界：

- 当前 main 的结论来自源码和本机单元测试。本文运行时执行了 `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --filter "FullyQualifiedName~CpuPowerLimit|FullyQualifiedName~RyzenSmuTests"`，40 个相关测试通过；这只覆盖编解码、夹紧和服务协议，不覆盖 MSR/SMU 写入。
- 补丁的结论来自 `补丁说明.txt`、payload 文件清单、程序集元数据和字符串，没有运行补丁，没有复制 DLL，没有写入 MSR/SMU。
- 没有 Blade 实机数据。本文不把任何源码结论写成硬件结论；所有“是否真的生效”都留在“仍需实机验证”。

## 一句话结论

补丁的 Intel 侧功能（15 W / 25 W 档位、自动回到基线、基线持久化、三项功耗墙服务 operation）当前 main 已有源码级实现；main 还多出休眠/唤醒重放和 AMD Ryzen SMU 分支。补丁包是 `b41af4d` 的旧 1.6.0 二进制，覆盖当前 main 会降级运行时行为，不建议直接应用。

## 对照总表

| 项目 | 当前 main | 用户补丁包 | 结论 |
|---|---|---|---|
| 15 W / 25 W 档位 | 源码有，单元测试覆盖 15/25 | 补丁说明有，验收点为 15/30 | main 已源码级覆盖；实际限功耗待实机 |
| 自动（固件） | 有持久化基线回写，但只回写 PL1/PL2 瓦特对 | 补丁说明称 b41af4d 把“自动”从空操作改成真实回退 | main 源码级覆盖补丁声明的 150/175 回退；出厂语义有条件，待实机 |
| 基线持久化 | `cpu-power-baseline.txt`，首次读取写入，不覆盖 | 同文件名，补丁说明称保留 | main 已源码级覆盖；首次读取、文件丢失、BIOS/CPU 变化有边界 |
| 休眠/唤醒后重放 | `DisplayPowerNotification` 中有重放路径 | 补丁二进制没有 CPU 墙重放调用 | main 领先；补丁不具备；S3/S0ix 行为待实机 |
| Intel RAPL 与服务通道 | MSR 0x606/0x610/0x611 + 三项服务 operation | 同样有三项 operation 和 IntelMSR.bin | main 已源码级覆盖；MSR 写入和非管理员路径待实机 |
| AMD Ryzen SMU 分支 | 有 RyzenSMU.bin、SMU 命令和 PM table 解码 | payload 无 RyzenSMU.bin，Windows.dll 无 Ryzen 类型 | main 已源码级覆盖；补丁在 AMD 上是回退；AMD 行为待实机 |
| 权限 / PawnIO | PawnIO + 模块 + 机器服务/LocalSystem | 同一套前置条件，脚本负责提权和注册服务 | 源码级路径有；权限矩阵和陈旧服务待实机/本机检查 |
| 与补丁覆盖包的兼容性 | 1.6.0，但含 b41af4d 之后的代码 | 1.6.0+b41af4d 的整包 publish | 覆盖是降级，不是升级；不建议应用到当前 main |

## 1. 补丁包到底是什么

补丁不是只替换一个 DLL，而是把 `b41af4d` 的完整自包含 publish 覆盖到 `C:\Program Files\OpenSynapse\current`：

- `应用补丁.cmd` 先要求管理员权限，停止 `OpenSynapse.App.exe`、`OpenSynapse.RecoveryHost.exe` 和 `OpenSynapseGpuMode` 服务；
- 把当前安装备份到 `C:\ProgramData\OpenSynapse\backup-last`；
- 用 `robocopy payload current /E /IS /IT` 覆盖文件；
- 调 `OpenSynapse.App.exe --install-service`；
- 不写 MSR，安装阶段只复制文件。

payload 中和 CPU 功耗墙直接相关的增量是：

- `OpenSynapse.Core.dll`：`CpuPowerLimit` 位编解码、夹紧、RAPL 单位解码；
- `OpenSynapse.Windows.dll`：`CpuPowerLimitBroker`、`CpuPowerLimitController`、MSR 读写、权限选路；
- `OpenSynapse.App.dll`：`CpuPowerBaselineStore`、功耗墙卡片、档位逻辑；
- `IntelMSR.bin` 和 `IntelMSR-LICENSE.txt`；
- `OpenSynapse.App.pri` 和双语资源。

payload **没有** `RyzenSMU.bin` / `RyzenSMU-LICENSE.txt`。`OpenSynapse.Windows.dll` 中也没有 `CpuPowerLimitLocal`、`RyzenSmuCpuPowerLimit`、`ioctl_get_code_name`、`ioctl_send_smu_command` 等 Ryzen 路径字符串。补丁说明本身也把功能标题写成“CPU 功耗墙（Intel RAPL PL1/PL2）”。

补丁说明里的提交号在当前仓库对象库中不存在（`git log -1 b41af4d`、`bdb9cd4` 等均失败），所以不能做逐行源码 diff。下面的对照是“补丁说明 + payload 静态内容”与“当前 main 源码”的行为对照。

## 2. 15 W / 25 W 档位

当前 main 的档位表在 `src/OpenSynapse.App/ViewModels/MainViewModel.PowerLimit.cs:13-14`：

```csharp
[0, 15, 25, 35, 45, 55, 65, 75, 85, 95]
```

`0` 表示“自动（固件）”，其余是瓦特档位。应用档位时：

```csharp
var pl1 = CpuPowerLimitPresets[index];
CpuPowerLimitBroker.ApplyAsync(new CpuPowerLimit(
    pl1,
    Math.Min(pl1 + 15, CpuPowerLimit.MaximumWatts),
    ...));
```

所以 15 W 会写成 PL1 15 W / PL2 30 W，25 W 会写成 25 W / 40 W；95 W 会写成 95 W / 100 W，因为 `CpuPowerLimit.MaximumWatts = 100`。

源码级证据：

- 档位表：`MainViewModel.PowerLimit.cs:13-14`
- PL2 计算：`MainViewModel.PowerLimit.cs:177-189`
- 用户上限：`src/OpenSynapse.Core/Devices/CpuPowerLimit.cs:26-30`
- 单元测试：`tests/OpenSynapse.Core.Tests/CpuPowerLimitTests.cs:48-64` 明确覆盖 15/25/45/95 的编码和解码。

补丁说明的验收点也是“选 15 W 后数值应变 15 / 30”，档位列表为 15/25/35/45/55/65/75/85/95。按补丁说明和 `CpuPowerLimitPresets` 字符串，补丁与 main 在档位数值上对齐。

同一档位表对 AMD 走 `RyzenSmuCpuPowerLimit`，写的是 SMU 的 STAPM/slow/fast，不是 0x610；AMD 的 15/25 是否被 SMU 接受，见第 7 节。

main 不持久化“档位索引”本身；应用重启后是靠读回当前 PL1、再用 `Array.IndexOf` 映射回下拉框。寄存器仍保持 15/25 时索引正确；如果固件把 15/25 夹到非预设值，`PublishCpuPowerLimit` 会落到 0，也就是显示“自动（固件）”，尽管当前值并不是出厂值。这是 UI 语义上的一个边界。

**仍需实机验证：**

- 固件是否接受 15 W / 25 W 这么低的 PL1；有些机型会把 PL1 夹到更高的固件下限，MSR 读回值可能不是 15/25。
- 0x610 的 PL1 enable / clamp 位由固件保留，main 不会主动改；如果固件把 PL1 关掉，写入瓦特数也可能不限制封装功耗。
- 需要看 package power（0x611 能量计数器或外部功耗计），不能只看卡片上的 MSR 读回值。
- 读回值是否精确等于 15/25；如果固件夹紧到非预设值，下拉框会显示“自动”，需要确认这是否符合用户预期。

## 3. 自动（固件）是否真正恢复出厂基线

### 3.1 main 的实际路径

用户把下拉框切到“自动（固件）”时，`MainViewModel.ApplyCpuPowerLimitAsync` 走的是：

```csharp
requestedIndex == 0
    ? CpuPowerLimitBroker.RestoreAsync(CpuPowerBaselineStore.Instance.Current, ...)
    : ApplyCpuPowerLimitPresetAsync(...)
```

`CpuPowerLimitBroker.RestoreAsync` 在基线不为空时调用 `RestoreTo(pl1, pl2)`；只有基线为空时才调用 `RestoreFirmware()`：

- `CpuPowerLimitBroker.cs:56-83`
- `CpuPowerLimitController.cs:182-207`

`RestoreTo` 的关键点：

- 把值夹到 `[8, 250]` W（`MaximumRestoreWatts = 250`）；
- 用 `EncodeWattsOnly(_firmwareRegister)` 写 0x610；
- **只写 PL1/PL2 两个瓦特字段**，不写 PL1/PL2 的 enable、clamp、时间窗和 lock 位。

所以 main 的“自动”是**把持久化的 PL1/PL2 瓦特对写回去**，不是把整个 0x610 寄存器恢复成出厂快照。补丁说明的验收点“选自动应回到 150 / 175”也只涉及这两个瓦特字段；补丁 Core 里同样有 `EncodeWattsOnly` 字符串，但补丁内部是否逐条等同未反编译。

### 3.2 “出厂基线”的来源

`CpuPowerBaselineStore` 记录的是**首次成功读取到的当前 PL1/PL2**，不是 `FirmwarePl1Watts` / `FirmwarePl2Watts`，也不是每次向固件重新查询的绝对出厂值：

- `src/OpenSynapse.App/CpuPowerBaselineStore.cs:22-56`
- `CpuPowerLimitReport` 的 `Firmware*` 字段注释明确说它是“controller 打开时的寄存器值，不是 factory value”：`CpuPowerLimit.cs:138-150`

正常路径下，应用启动时先读一次（`MainWindow.xaml.cs:67`），读到 150/175 就把它记为基线；用户之后选 15 W，再选自动，才会回到 150/175。这个路径在源码级成立。

但它有几个真实边界：

1. **首次读取时已经被别的工具改过**：例如用户先用 XTU/其他工具把 PL1 设成 45 W，再第一次启动 main；基线会记成 45 W，自动只会回到 45 W。
2. **基线文件丢失后应用已重启**：`Current` 为空，Auto 退回 `RestoreFirmware()`；而 `RestoreFirmware()` 用的是新 controller 打开时的当前值。如果当前值就是上次预设，Auto 可能表现为“空操作”。
3. **基线文件写入失败**：`Capture` 会保留内存中的 `Current`，本次会话 Auto 可用；重启后文件不存在，回到第 2 条。
4. **BIOS 更新或换 CPU**：基线文件没有 CPU 型号、BIOS 版本或校验，仍会恢复旧值。
5. **固件 lock bit 为 1**：`Apply` / `RestoreTo` 会抛“locked by firmware”，Auto 不能强写；UI 会显示锁定提示。
6. **恢复下限是 8 W**：如果某个出厂基线低于 8 W（正常笔记本不会），`RestoreTo` 会把它抬到 8 W。

### 3.3 补丁的自动回退

补丁说明明确把 `b41af4d` 描述为“make automatic a real undo”，验收点是 150/175；补丁 `OpenSynapse.App.dll` 里也有 `CpuPowerBaselineStore` 类型和 `EnsureCaptured` 方法。按说明，补丁的 Auto 也走持久化基线，而不是空操作。

但本文没有反编译 `EnsureCaptured` 的完整语义，也没有在实机上复现 150/175。能确认的是：**main 源码已经覆盖了补丁说明所声明的“Auto 回到 150/175”这一目标；补丁说明不能当作 main 或补丁的硬件证据。**

**仍需实机验证：**

- 目标 Blade 的出厂 PL1/PL2 是否真是 150/175；
- 自动回退后，MSR 读回是否 150/175，package power 是否真的解除限制；
- 固件 lock/clamp/enable 位是否允许这次回退；
- 睡眠/重启后基线是否仍然对应同一台机器的固件默认值。

## 4. 基线持久化

main 的基线文件：

- 路径：`%LOCALAPPDATA%\A1mAssist\OpenSynapse\cpu-power-baseline.txt`（`AppDataPaths.Root` + `CpuPowerBaselineStore.cs:13`）
- 格式：两个 invariant-culture 浮点数，空格分隔
- 写入：首次可用报告时 `File.WriteAllText`，`Current` 非空后不再覆盖
- 读取：进程启动时 `Read()`，解析失败/IO 异常返回 `null`
- 作用域：按 Windows 用户，不按机器/CPU；服务进程不读这个文件，app 在 Restore 请求里把基线值传给服务

源码级边界：

- 没有 CPU 型号、BIOS 版本、时间戳或 schema 版本；换 CPU/BIOS 后可能恢复旧值。
- `File.WriteAllText` 不是原子写；写到一半崩溃会留下坏文件，`Read()` 返回 `null`，Auto 退回 `RestoreFirmware()`。
- 文件被删除后，下一次启动会重新捕获“当前值”；如果当前值是上次预设，基线就不再是出厂值。
- 多用户各自有独立文件，符合 per-user 设计，但也意味着“这台机器的基线”不是全局唯一。

补丁说明说“不删除用户数据；功耗墙出厂基线文件（cpu-power-baseline.txt）保留”。payload 的覆盖脚本只复制文件，不删除 `%LOCALAPPDATA%`，所以按脚本行为不会主动删基线。补丁 `OpenSynapse.App.dll` 也带 `CpuPowerBaselineStore` 和 `cpu-power-baseline.txt` 字符串，文件名与 main 一致；未发现会强制迁移或删除该文件的代码。

**仍需实机验证：**

- 文件在真实用户目录下能否写入（权限、受控文件夹访问、OneDrive 重定向）；
- 写入失败后的本次会话/下次启动行为；
- 覆盖补丁后是否仍读取同一个基线文件（补丁和 main 的 `AppDataPaths` 都带 `A1mAssist` / `OpenSynapseData` 字符串，按静态内容看路径一致，但未做运行验证）。

## 5. 休眠/唤醒后重放

### 5.1 main 的路径

`src/OpenSynapse.App/DisplayPowerNotification.cs` 同时注册了：

- `PowerRegisterSuspendResumeNotification`
- `WM_POWERBROADCAST` 的 `PBT_APMSUSPEND` / `PBT_APMRESUMESUSPEND` / `PBT_APMRESUMEAUTOMATIC`

`ResumeFromSuspend()` 在 `_systemSuspended` 为 true 时：

1. 清掉 suspend 标志；
2. `RequestDeviceRefresh()`；
3. 排队显示过渡；
4. `_dispatcherQueue.TryEnqueue(ReplayCpuPowerLimitAfterResume)`。

`ReplayCpuPowerLimitAfterResumeAsync` 只在 `requestedIndex > 0` 时重放选中的档位；Auto（0）不写，避免覆盖固件默认。重放失败被 `catch (Exception) { }` 吞掉，不弹窗、不写状态栏。

源码位置：

- `DisplayPowerNotification.cs:172-205`
- `MainViewModel.PowerLimit.cs:150-175`

### 5.2 补丁的路径

补丁 `OpenSynapse.App.dll` 的 `MainWindow.ResumeFromSuspend` IL 只调用了：

- `RequestDeviceRefresh`
- `QueueDisplayTransition`

没有调用 `ApplyCpuPowerLimitAsync` / `ApplyCpuPowerLimitPresetAsync`；整个程序集也没有 `ReplayCpuPowerLimitAfterResumeAsync` 方法。补丁说明只承诺“重启应用后数值保持上次设定”，没有承诺休眠唤醒重放。

所以按二进制调用图：**补丁不具备休眠后重写 CPU 功耗墙的代码路径**；如果目标固件在睡眠时把 0x610 重置回默认，补丁用户醒来后会回到固件默认，而不是上次选中的 15/25/45 W。

### 5.3 仍需实机验证

- S3（传统睡眠）和 Modern Standby（S0ix）是否都触发 main 的 `ResumeFromSuspend`；
- 目标 Blade 的固件是否真的在睡眠时重置 0x610；有些平台可能保持，有些可能由 EC 覆盖；
- 重放时服务是否可用（服务可能在睡眠前被系统停止，客户端有 demand-start 逻辑，但没做实机联调）；
- 重放失败被静默吞掉时，用户是否会误以为档位仍然生效；
- 应用在睡眠期间被关闭/崩溃时不会重放，这是设计边界；
- AMD 路径的 SMU 重放是否和 Intel 一样工作。

**结论：main 有源码级重放路径；补丁没有。main 的实机行为仍未确认。**

## 6. Intel RAPL 与服务通道

### 6.1 main 的 Intel 实现

`src/OpenSynapse.Windows/Devices/CpuPowerLimitController.cs`：

- MSR `0x606`：RAPL 单位寄存器（power/energy/time unit）；
- MSR `0x610`：PL1/PL2 墙；
- MSR `0x611`：能量状态，用于 `SamplePackageWatts()` 的 package power 采样；
- 解码：PL1 bits 0-14，PL2 bits 32-46，时间字段 bits 16-23 / 48-55；
- 写入：`EncodeWattsOnly` 只改两个瓦特字段，保留 enable/clamp/time/lock；
- 锁：`0x610` bit 63；`Apply` 同时检查当前寄存器和打开时快照的锁位；
- 用户上限：100 W；恢复上限：250 W；
- `Read()` 返回当前值、打开时快照、上限和锁位。

### 6.2 服务通道

`CpuPowerLimitBroker` 的策略是：

1. 先在 app 进程内用 PawnIO 打开控制器；
2. 只有结果是 `AccessDenied` 时才走 `OpenSynapseGpuMode` 服务；
3. `PawnIoMissing`、`ModuleMissing`、`Failed` 直接返回，不再走服务。

服务协议（`DedicatedGpuModeServiceProtocol.cs:26-28`）：

- `power-limit-status`
- `set-power-limit`
- `restore-power-limit`（可带 PL1/PL2 基线，也可不带）

服务实现 `GpuModeServiceHost.cs:307-322` 用同一个 `CpuPowerLimitLocal.TryOpen`，所以 main 的服务在 AMD 上也会走 Ryzen SMU。服务本身：

- 以 `LocalSystem` 运行（`DedicatedGpuModeServiceInstaller.cs:79-92`）；
- 按需启动，空闲 5 秒后自停（`GpuModeServiceHost.cs:17`）；
- 命名管道 `OpenSynapse.GpuMode.v1`，协议版本 1；
- 管道 ACL 只允许 SYSTEM、Administrators 和安装时授权的用户 SID，拒绝 Network SID（`GpuModeServiceHost.cs:483-505`）。

### 6.3 单元测试覆盖

本机运行的相关测试 40 个全部通过，覆盖：

- 0x610 的典型寄存器解码（150/175）；
- 单位寄存器解码；
- 只写瓦特字段时其他位不变；
- 15/25/45/95 档位编码/解码；
- 夹紧和 PL2 ≥ PL1；
- 恢复值允许高于 100 W 用户上限；
- 锁 bit；
- 服务协议的三项 operation、参数缺失、版本不匹配、JSON 往返。

这些测试**不**覆盖：

- 真实 MSR 读写；
- PawnIO 驱动加载；
- 非管理员 app 走服务；
- 服务命名管道的端到端；
- 固件 lock/clamp/enable 行为。

### 6.4 源码级注意点

`CpuPowerLimit` 把 PL1 时间窗按 bits 16-23 解码/编码；Intel 标准 RAPL 布局中 bit 16 是 PL1 clamp，时间窗是 bits 17-23（PL2 对应 bit 48 clamp / bits 49-55）。当前生产写入只用 `EncodeWattsOnly`，不碰这些位，所以**不影响功耗墙瓦特写入**；但如果未来把时间窗接到 UI，这个掩码需要重新核对，不能直接复用。这个点不影响补丁对照结论，但属于后续改时间窗时最可能踩的坑。

**仍需实机验证：**

- 目标 Blade 的 0x610 锁位是否可写；
- 固件是否把 PL1 enable 关掉，导致写瓦特数不生效；
- 15 W / 25 W 下 package power 是否真的下降；
- 非管理员 app + 机器服务的路径是否实际可用；
- 服务在睡眠/唤醒后是否能被 demand-start 拉起；
- PawnIO 驱动加载是否受 Secure Boot/BIOS/虚拟化影响。

## 7. AMD Ryzen SMU 分支

当前 main 的 AMD 路径是完整分支，不只是“报个不支持”：

- `CpuPowerLimitLocal` 用 `CpuVendor.IsAmd` 选路：AMD 走 `RyzenSmuCpuPowerLimit`，其他走 Intel `CpuPowerLimitController`；
- `RyzenSmuSession` 加载 `RyzenSMU.bin`，调用 `ioctl_get_code_name`、`ioctl_resolve_pm_table`、`ioctl_update_pm_table`、`ioctl_read_pm_table`、`ioctl_send_smu_command`；
- `RyzenSmuCpuPowerLimit` 支持 Renoir、Lucienne、Cezanne、Rembrandt、Phoenix、Phoenix2、Mendocino、HawkPoint、StrixPoint、StrixHalo、KrackanPoint、KrackanPoint2 共 12 个 family；
- 用 MP1 的 `0x14`（STAPM）、`0x15`（fast PPT）、`0x16`（slow PPT）；
- 从 PM table 的 float index 2（fast）和 4（slow）读瓦特值，映射成 PL1=min(slow, fast)、PL2=max(slow, fast)；
- 写入时 STAPM=slow、Slow=slow、Fast=fast；
- `BuildReport` 不报告锁位，`IsLocked` 固定 false。

源码位置：

- `src/OpenSynapse.Windows/Devices/CpuPowerLimitLocal.cs`
- `src/OpenSynapse.Windows/Devices/RyzenSmuCpuPowerLimit.cs:128-189`
- `src/OpenSynapse.Windows/Devices/RyzenSmuSession.cs:51-191`
- `src/OpenSynapse.App/OpenSynapse.App.csproj` 的 `Native/RyzenSMU.bin` 内容项

测试覆盖：

- `tests/OpenSynapse.Core.Tests/RyzenSmuTests.cs` 覆盖 family→命令映射、PM table 解码、异常表、夹紧和毫瓦换算；
- 不覆盖真实 SMU mailbox、PM table 布局或 EC 覆盖。

补丁包没有 AMD 分支：

- payload 没有 `RyzenSMU.bin`；
- `OpenSynapse.Windows.dll` 没有 `CpuPowerLimitLocal`、`RyzenSmuCpuPowerLimit`，也没有 `ioctl_get_code_name` / `ioctl_send_smu_command` 等 Ryzen SMU ioctl 字符串；
- 补丁说明标题明确是 Intel RAPL。

所以：**当前 main 在 AMD 上是新增能力；补丁在 AMD 上是回退。** 把补丁覆盖到当前 main 后，AMD 机器不会得到 Ryzen SMU 功耗墙，最多是 Intel 控制器报不支持/失败。

**仍需实机验证（AMD）：**

- 12 个 family 的 PM table float index 2/4 是否都对应 fast/slow PPT；
- `0x14/0x15/0x16` 是否在所有列出的 family 上都被 SMU 接受；
- STAPM 和 slow PPT 设成同一个值是否符合目标机型的 EC 策略；
- 写入三条命令时如果第二条/第三条失败，前一条已经生效，没有回滚；
- 固件/EC 是否会在短时间后覆盖 SMU 写入；
- 休眠/唤醒重放在 AMD 上是否有效；
- 服务以 LocalSystem 运行时，RyzenSMU.bin 是否能在服务进程里正常加载。

## 8. 权限 / PawnIO 前置条件

两边共同的前置条件：

1. 安装 PawnIO（驱动服务 + `%ProgramFiles%\PawnIO\PawnIOLib.dll`）；
2. app 目录有对应模块：Intel 用 `IntelMSR.bin`，AMD 用 `RyzenSMU.bin`；
3. 调整功耗墙需要能打开 PawnIO executor：
   - app 以管理员运行，或
   - 安装 `OpenSynapseGpuMode` 服务，由 LocalSystem 代写；
4. 安装服务需要管理员权限；补丁脚本会自行提权，main 的 `--install-service` 也要求管理员。

main 的细节：

- `CpuPowerLimitController.TryOpen` 先检查 `%ProgramFiles%\PawnIO\PawnIOLib.dll` 和 app 目录的 `IntelMSR.bin`；
- 打开失败按 `PawnIoMissing` / `ModuleMissing` / `AccessDenied` / `Failed` 分类，UI 分别显示不同文案；
- `CpuPowerLimitBroker` 只对 `AccessDenied` 尝试服务；其他失败是最终结果；
- `DedicatedGpuModeServiceInstaller.EnsureInstalled` 在服务已存在时直接返回 true（`DedicatedGpuModeServiceInstaller.cs:67-70`），不会检查 binPath 或授权用户 SID 是否还是当前安装/当前用户。这是一个真实的“陈旧服务”风险；
- 服务管道 ACL 只允许授权用户、Administrators、SYSTEM，拒绝 Network；这比“任意本地用户可写”安全，但如果服务是用另一个用户安装的，当前用户会连不上；
- main 的功耗墙读取只在主窗口构造时发起一次（`MainWindow.xaml.cs:67`）；没有看到安装 PawnIO/服务后的自动重试。按补丁说明和 main 的行为，装完前置条件后应重启应用。

补丁的细节：

- 补丁说明要求已安装 OpenSynapse 1.6.0 和 PawnIO；未装 PawnIO 时卡片提示，功能不可用但不影响其它部分；
- 应用脚本第 5 步执行 `--install-service`，失败时提示“可以不用服务，每次以管理员运行应用”；
- 补丁脚本没有安装 PawnIO，也没有检查 PawnIO 是否存在；它只检查 payload 和目标 app 是否存在。

**仍需实机/本机验证：**

- 管理员 app、不开服务：本地 PawnIO 是否能打开；
- 非管理员 app、服务已安装：服务路径是否可用；
- 服务未安装：UI 是否明确提示，且不误报“已锁定”；
- 服务已存在但 binPath 指向旧目录/旧用户：`--install-service` 是否会误报成功；
- PawnIO 已安装但 `PawnIOLib.dll` 在 x86 目录（Intel 路径只查 ProgramFiles，Ryzen 路径查两个根）；
- PawnIO 驱动被 BIOS/Secure Boot/虚拟化阻止时的错误分类。

## 9. 与补丁二进制覆盖包的兼容性

### 9.1 版本关系

- 当前 main：`1.6.0`，HEAD `c5cbf84`，包含 `b41af4d` 之后的 CPU 功耗墙、AMD、休眠重放、Curve Optimizer 和设备修复；
- 补丁：`1.6.0+b41af4d`，是较早的 1.6.0 构建；
- 补丁脚本只显示目标 `ProductVersion`，不校验它是否等于 1.6.0，也不阻止覆盖更高版本。

补丁没有改 Velopack 包版本；按版本号看仍是 1.6.0，自动更新不会把它识别成需要修复的版本。后续同版本自动更新不会修复，必须等更高版本或手动回滚/重装。

### 9.2 会丢什么

因为 payload 是整包 publish，覆盖 `OpenSynapse.App.dll`、`OpenSynapse.Core.dll`、`OpenSynapse.Windows.dll`、`OpenSynapse.App.exe`、`OpenSynapse.App.pri` 等文件，以下当前 main 的运行时能力会回到 `b41af4d` 状态：

- **AMD Ryzen SMU 功耗墙**：补丁没有 Ryzen 类型、没有 `RyzenSMU.bin`；
- **休眠/唤醒后重放**：补丁的 `ResumeFromSuspend` 不调用 CPU 墙写入；
- **Curve Optimizer**：补丁的 App/Windows DLL 没有当前 main 的 `CpuCurve` / `RyzenSmuCurveOptimizer` 类型；补丁服务也没有对应的 curve operation；
- **b41af4d 之后的设备/UI 修复**：例如当前 main 的 R3/Viper、cooling pad、headset 等改动，都会从被覆盖的 app/windows DLL 中消失；
- **当前 main 的界面文案/PRI**：会被补丁版本的资源覆盖。

### 9.3 文件层面

`robocopy /E /IS /IT` 只覆盖同名文件，不删除目标目录里的多余文件。因此：

- 目标目录可能继续保留当前 main 的 `RyzenSMU.bin`、`RyzenSMU-LICENSE.txt` 等文件，但补丁 DLL 不会使用它们；
- 如果当前 main 的某些依赖 DLL 版本比补丁 payload 新，覆盖后可能留下“旧 deps.json + 新 DLL”或“新 DLL 仍在但不再被引用”的混合状态；
- 补丁 payload 是 b41af4d 的完整 publish，理论上自包含，但没有在“覆盖当前 main”这种混合目录上做过启动验证。

### 9.4 服务层面

- 补丁覆盖后用 `current\OpenSynapse.App.exe --install-service` 注册服务；如果服务已存在，main 的 `EnsureInstalled` 直接返回 true，不会更新 binPath；补丁的安装器按同一套 `--install-service` 逻辑，是否与当前 main 完全一致未逐条比对；
- 如果原服务指向 `current\OpenSynapse.App.exe`，覆盖后服务会运行补丁二进制；如果原服务指向别的目录，补丁脚本不会修复；
- CPU 三项 operation（status/set/restore）名称一致，按协议形状看有跨版本对接的可能；但补丁服务没有当前 main 的 Curve Optimizer operation，main app 调 curve 时会得到不支持；
- 没有做“补丁 app ↔ main 服务”或“main app ↔ 补丁服务”的跨版本联调。

### 9.5 建议

- **不要把补丁覆盖到当前 main 的工作安装上**。它是旧二进制整包，不是只加 CPU 功耗墙的增量；会把 AMD、休眠重放、Curve Optimizer 和后续设备修复一起降级。
- 如果必须复现补丁的二进制行为，用干净的 OpenSynapse 1.6.0 + Intel 机器的 VM/测试机，按补丁说明走；不要在包含当前 main 改动的安装目录上覆盖。
- 如果已经覆盖，优先用 `C:\ProgramData\OpenSynapse\backup-last` 回滚（补丁只保留最近一次备份），或重新安装当前 main 的发布包。

## 10. 源码级覆盖 vs 实机验证清单

| 能力 | main 源码级 | 相关单元测试 | 实机 |
|---|---|---|---|
| 15 W / 25 W 档位 | 有 | 有（15/25/45/95 编解码） | 待测：实际 package power |
| 自动回到基线 | 有（瓦特对） | 部分（Restore 协议/编码） | 待测：150/175、lock/clamp |
| 基线持久化 | 有 | 无专门测试 | 待测：写入权限、文件丢失、BIOS 更新 |
| 休眠/唤醒重放 | 有 | 无 | 待测：S3/S0ix、服务、固件重置 |
| Intel RAPL | 有 | 有（编解码/协议） | 待测：MSR 写入、PawnIO、非管理员路径 |
| 服务通道 | 有 | 有（协议路由/JSON） | 待测：命名管道端到端、LocalSystem、demand-start |
| AMD Ryzen SMU | 有 | 有（family/解码/命令） | 待测：真实 SMU、PM table、EC 覆盖 |
| 权限 / PawnIO | 有 | 无 | 待测：权限矩阵、陈旧服务、驱动加载 |
| 补丁覆盖兼容 | 静态分析 | 无 | 未应用；不建议在当前 main 上应用 |

## 11. 最可能翻车的 5 个点

1. **把“首次读取值”当“出厂值”**：如果用户先用别的工具改过功耗墙，或删了基线文件后重启，Auto 会把当前值当基线，表现为回不到 150/175，甚至空操作。
2. **MSR 写进去不等于功耗被限制**：main 只改瓦特字段，保留 enable/clamp 位；固件可能把 PL1 关掉、把 15 W 夹到更高下限，或由 EC 覆盖。必须看 package power，不能只看卡片读回。
3. **休眠重放静默失败**：main 失败时 `catch` 吞掉，不提示；服务没起来、固件锁定、应用在睡眠期间退出，都会让用户以为档位还在。
4. **AMD 的 PM table/命令语义没有实机依据**：index 2/4、STAPM/slow/fast 的组合、三条命令的中途失败，都可能只在部分 family 上成立；补丁更是完全没有 AMD 路径。
5. **覆盖包是整包降级**：同版本 1.6.0 不会触发 Velopack 修复，AMD/休眠重放/Curve Optimizer/后续设备修复一起回退；`robocopy` 还可能在目标目录留下混合版本文件。

## 12. 建议

- 以当前 main 为准，不应用补丁覆盖包。补丁声明的 Intel 侧能力已经在 main 源码里；main 还多出 AMD 和休眠重放。
- 若目标是“确认补丁二进制在目标机器上的行为”，用干净的 1.6.0 + Intel 测试机/VM，按补丁说明的验收点做；不要在当前 main 的工作安装上覆盖。
- 若目标是“确认 main 的实现”，按第 10 节清单做实机：先测 Intel 15/25/自动/重启/睡眠，再测非管理员+服务，最后测 AMD（如果目标机器是 Ryzen）。
- 无论走哪条，先把基线文件和 `C:\ProgramData\OpenSynapse\backup-last` 纳入备份/回滚计划；补丁只保留最近一次备份。

## 13. 证据索引

| 内容 | 位置 |
|---|---|
| main 档位表、Auto、重放 | `src/OpenSynapse.App/ViewModels/MainViewModel.PowerLimit.cs:13-14`, `:128-132`, `:155-175` |
| main 基线持久化 | `src/OpenSynapse.App/CpuPowerBaselineStore.cs:13`, `:22-56`, `:58-92` |
| main Intel RAPL | `src/OpenSynapse.Windows/Devices/CpuPowerLimitController.cs:13-15`, `:163-207`, `:260-273` |
| main 权限选路 | `src/OpenSynapse.Windows/Devices/CpuPowerLimitBroker.cs:13-83` |
| main 休眠重放接线 | `src/OpenSynapse.App/DisplayPowerNotification.cs:172-205` |
| main 服务 operation / ACL | `src/OpenSynapse.App/GpuModeServiceHost.cs:307-322`, `:392-425`, `:483-505` |
| main 服务安装 | `src/OpenSynapse.Windows/Lifecycle/DedicatedGpuModeServiceInstaller.cs:57-113` |
| main AMD SMU | `src/OpenSynapse.Windows/Devices/RyzenSmuCpuPowerLimit.cs:128-189`, `src/OpenSynapse.Windows/Devices/RyzenSmuSession.cs:51-191` |
| main 单元测试 | `tests/OpenSynapse.Core.Tests/CpuPowerLimitTests.cs`, `CpuPowerLimitProtocolTests.cs`, `RyzenSmuTests.cs` |
| 补丁说明 | `artifacts/user-cpu-power-patch-20261031/OpenSynapse-补丁-1.6.0-CPU功耗墙/补丁说明.txt` |
| 补丁脚本 | 同目录 `应用补丁.cmd`、`回滚补丁.cmd` |
| 补丁二进制证据 | `payload/OpenSynapse.App.dll` ProductVersion `1.6.0+b41af4d...`；`payload/OpenSynapse.Windows.dll` 无 Ryzen 类型/字符串；`payload` 无 `RyzenSMU.bin` |
