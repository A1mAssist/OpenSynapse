# 单手键盘映射

单手键盘按协议分为三组。主机改键保存在设备的本地配置中，需要 OpenSynapse 运行；Tartarus V2 Pro 的普通按键使用设备自身的映射表。

| 型号 | PID | 可编辑输入 | 映射路径 |
| --- | --- | ---: | --- |
| Nostromo | `0111` | 23 | 旧版 rzudd capture → 主机运行时 |
| Orbweaver | `0113` | 26 | 旧版 rzudd capture → 主机运行时 |
| Tartarus | `0201` | 21 | 旧版 rzudd capture → 主机运行时 |
| Orbweaver Chroma | `0207` | 26 | 旧版 rzudd capture → 主机运行时 |
| Tartarus Chroma | `0208` | 21 | 旧版 rzudd capture → 主机运行时 |
| Tartarus V2 | `022B` | 28 | RZCONTROL 按设备拦截 → 主机运行时 |
| Tartarus Pro | `0244` | 28 | RZCONTROL 按设备拦截 → 主机运行时 |
| Tartarus V2 Pro | `02EB` | 27 | OBM `02:9E` 读取、`02:0D` 写入 |

输入数包含可区分的方向和滚轮动作。旧款方向帽的斜向由两个方向键同时输入；中心和四个斜向没有独立映射 ID。Nostromo 的滚轮包含按下、向上、向下三项。02EB 的 Fn、配置切换、keymap 切换和摇杆按下不属于普通可编辑键；摇杆轴也不按滚轮处理。

## 使用条件与失败提示

七款主机映射设备支持键盘组合键、鼠标按钮与滚轮、媒体键、禁用、HyperShift、双击和软件宏。普通层与 HyperShift 层分别保存；按键抬起沿用按下时的层，避免切层后卡键。宏触发同时绑定 PID、设备实例、按键和层。

需要安装对应型号的 Razer 过滤驱动，并保留安装目录中的 `OpenSynapse.RecoveryHost.exe`。缺驱动、缺恢复程序、输入通道停止时，映射页报告失败并禁止保存。旧五款使用官方 Common Driver 的 rzudd，不能套用 022B/0244 的现代驱动接口。

旧驱动只提供 opaque identity，不能直接关联 HID ContainerId。启动时要求该 PID 只有一台 HID 实体和一个驱动身份，并拒绝端点名碰撞；同型号接入两台时会给出提示。启动前检查 capture 是否已被占用，已有占用时需退出 Synapse。运行期间也应保持 Synapse 退出。

旧款通过 Windows SendInput 输出改键，目标程序的权限级别会影响注入；以管理员权限运行的目标仍需相同权限的输入程序。022B/0244 使用其匹配设备的过滤驱动输出。

## 旧驱动输入与恢复

`RazerLegacyKeypadControl` 通过 `\\.\RzUdd` 的 `0x88883000` 枚举 PID 和 opaque64。24 字节请求在偏移 8、12、16 写入 DWORD `2`、`1`、索引；返回身份在偏移 8，PID 在偏移 16。设备端点名按官方 `%016x` 格式取身份低 32 位并补齐 16 个十六进制字符。

`0x88883004` 的 capture 请求长 16 字节，偏移 8 为 DWORD `3`，偏移 12 为命令。以下命令为十进制：

| 输入 | 查询 | 开启 | 关闭 |
| --- | ---: | ---: | ---: |
| 键盘 | 21 | 22 | 23 |
| 鼠标按钮和滚轮 | 28 | 29 | 30 |

控制回复前 8 字节为 sign-extended NTSTATUS；查询结果在偏移 8。每次改变状态后读回确认。仅 Nostromo 开启鼠标 capture。

事件使用 `0x88883008`，in64/out64、OVERLAPPED。先投递 10 个读取，再开启 capture；按投递顺序处理并补充读取。64 字节事件没有状态前缀，类型在偏移 4，capture action `0x8000000E` 在偏移 8。键盘 scanCode/flags 在偏移 12/14，驱动把 flags 归一化为 down=0、up=1，四方向的 E0 根据该型号默认键表补回。鼠标偏移 12/14 为 ButtonFlags/有符号滚轮量，逐个处理组合位。

关闭文件句柄不会关闭 capture。正常退出和启动失败先关闭本次 capture，再取消并等待所有读取完成，最后释放 native buffer。未知捕获输入会停止映射并触发恢复。

恢复标记版本 5 记录旧款身份；版本 2、3、4 继续可读。独立 RecoveryHost 在主进程退出后重新枚举，只有 exact opaque64+PID 和唯一低 32 位端点仍匹配时才修改 capture。设备已移除时释放注入的按键。HID 仍在但驱动枚举没有该型号时保留恢复标记并报告失败，避免把枚举异常当作恢复成功。

旧 `P2` 映射代码曾使用错误的 header/record 偏移，并把其他鼠标型号的方向表套入单手键盘，现已移除。旧配置中虚构的 `F4..F8` 行保留在磁盘上，但不执行；对应未解析宏也不从普通键盘触发。

## 02EB 的目标 keymap

固件普通键 ID 为：

```text
1 2 3 4 5 6
16 17 18 19 20 21
30 31 32 33 34 35
44 46 47 48 49
58 50 24 61
```

读写前解析当前 profile，通过 `02:B6` 读取 active keymap，通过 `02:B7` 保存原 target，再用 `02:37` 选择 active keymap。单次读写和整页操作都进入此范围，并在 `finally` 恢复原 target。普通层为 0，HyperShift 为 1。固件写入后读回核对，不使用旧 analogV1 型号的 `02:12`。

## 协议来源

- 官方 `Razer_Common_Driver_v1.38.03_Win10.exe`，Win10/amd64 `rzudd.sys`。`rzuddkb.inf` 绑定 0111/0113/0201/0208，`rz0207kb.inf` 绑定 0207，均使用 rzudd UpperFilters。驱动中的 `sub_121BC` 是枚举、`sub_15074` 是 capture 分发、`sub_29B94`/`sub_29E2C` 是事件订阅与投递、`sub_1320C` 是设备路径和无操作 file-close 回调。
- 官方 Synapse 2 DLL 的 `FUN_100e26d0` 使用 `Razer_%016x` 端点、10 个 64 字节事件请求；`FUN_100e2c40` 完成后重新提交请求。
- OpenRazer `daemon/openrazer_daemon/keyboard.py` 的 `TARTARUS_EVENT_MAPPING` 和 `ORBWEAVER_EVENT_MAPPING`，以及官方 Nostromo Config 1.07.03 的默认 profile XML，确认五款旧设备的默认键位。
- 官方 022B bundle `main.4bb8ca24a9c5fd596485.js` 声明主机映射；02EB bundle `main.8ae72880bd6c9af82381.js`、`rzDevice25.1d66ba643a5baed64904.js` 和 `obmEngineKeypadCalMaxMem.12c410504ff95fe91796.js` 提供 analogV3/OBM、27 键及 target keymap 命令。

## 验证与实机验收

本地定向测试覆盖八款的路由、有效输入、普通/HyperShift 层、宏实例绑定与迁移、保存失败回滚、事件字节布局、连续事件顺序、部分启动失败、读取失败、退出排空和恢复标记兼容。WinUI QA 使用真实映射页面与模拟 transport，检查八款的键位数量、保存与回滚，以及七款主机映射的宏操作。

2026-11-28 本轮结果：定向测试 308 项通过；`dotnet build OpenSynapse.slnx -c Release --no-restore` 通过，零警告、零错误。UI 场景和截图在本地 `artifacts/keypad-family/ui-<PID>/`，八款报告均为 `passed`。测试树按仓库约定保留在本地，不随源码发布。

尚需各型号实体设备验证默认键位、快速连续输入、组合键、滚轮、退出和强制结束后的恢复、拔插重连及目标游戏接收输入。官方旧驱动的协议静态证据来自 1.38.03；其他已安装版本的实际返回长度和 Windows 11 驱动加载情况需在实机确认。
