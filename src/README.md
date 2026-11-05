# 工程地图

`src` 下的 7 个工程不是 7 个平级模块。它们是 3 个分层工程 + 4 个有独立编译或进程边界的组件。

## 分层

| 工程 | 类型 | 职责 | 允许引用 |
|---|---|---|---|
| `OpenSynapse.Core` | class library | 模型、策略、纯逻辑；不碰 Windows、设备句柄和 UI | 无 |
| `OpenSynapse.Windows` | class library | 设备发现、HID 协议、系统调用、遥测和写入校验 | Core |
| `OpenSynapse.App` | WinExe | WinUI 页面、ViewModel、导航、装配和用户文案 | Core、Windows、下表的组件 |

依赖方向固定为 `App -> Windows -> Core`。`Core` 不反向引用任何工程；`Windows` 不引用 `App`。这两个边界由 `tests/OpenSynapse.Core.Tests/Architecture/LayeringTests.cs` 检查。

## 有独立边界的组件

| 工程 | 为什么不能并进 App/Windows |
|---|---|
| `OpenSynapse.ChromaSdk.Protocol` | App、Windows、Shim 和测试共用的管道协议；目标框架是 `net10.0`，不能绑到 Windows 层 |
| `OpenSynapse.ChromaSdk.Shim` | 产物必须是 NativeAOT 的 `RzChromaSDK64.dll`，不能和 WinUI 主程序合并 |
| `OpenSynapse.RecoveryHost` | 独立进程；主程序异常退出后仍要负责恢复 Blade 的 M/K 状态 |
| `OpenSynapse.ThxHelper` | 独立进程；把 Razer 原生 THX DLL 的加载和崩溃隔离在 App 之外 |

这些工程的物理位置保持平铺，是因为路径同时被 csproj、发布脚本和安装包引用。除非编译或发布边界发生变化，不要为了目录好看搬动它们。

更完整的架构约束见 [`docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md)。
