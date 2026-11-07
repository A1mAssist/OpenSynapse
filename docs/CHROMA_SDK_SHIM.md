# Razer Chroma SDK 4.0.1 ABI 与 OpenSynapse Shim

本文是给后续维护者的技术参考，不是营销文案。它描述两件事：

1. 官方 Razer Chroma SDK 4.0.1 x64 DLL 的二进制接口。
2. OpenSynapse 的 `RzChromaSDK64.dll` 兼容 shim 如何把调用转成命名管道消息。

## 1. 状态与范围

### 目标

- 提供一个 x64、二进制兼容的 `RzChromaSDK64.dll`，让 Chroma 游戏不需要安装 Razer Synapse。
- shim 作为游戏进程内的 DLL 运行，连接本地命名管道 `\\.\pipe\OpenSynapseChromaSdk`，把效果单向交给 OpenSynapse。
- OpenSynapse 是管道服务端；shim 不向游戏回传设备状态，也不实现完整的 Chroma 设备查询。
- 仅支持 x64。没有 x86 计划。

### 已验证

- 官方 DLL 身份：`C:\Program Files\Razer Chroma SDK\bin\RzChromaSDK64.dll`，PE machine `0x8664`，大小 273,096 字节，15 个导出。
- `FileVersion` 为 `3.8.0.238`，`ProductVersion` 为 `4.0.1.07221608`。FileVersion 落后于产品版本；讨论 SDK 版本时应使用 ProductVersion，只看 FileVersion 会误判。
- 15 个导出名和 ordinal 已用 PE 导出表核对；x64 调用约定按官方 `extern "C"` 声明和 Microsoft x64 ABI 确认。
- `APPINFOTYPE` 在 Unicode 下为 3,592 字节；字段偏移已用 ctypes 实测。
- 官方 DLL 的 Init/UnInit/InitSDK/CreateKeyboardEffect/QueryDevice 返回值已用本机探针核对，见第 2 节。
- OpenSynapse shim 源码已有 15 个 `[UnmanagedCallersOnly]` Chroma 导出；Release AOT 产物也包含这 15 个名字。
- OpenSynapse 已有协议、管道客户端、管道服务端和效果转换代码：
  - `D:\Workspaces\OpenSynapse-chroma\src\OpenSynapse.ChromaSdk.Protocol\ChromaSdkProtocol.cs`
  - `D:\Workspaces\OpenSynapse-chroma\src\OpenSynapse.ChromaSdk.Shim\Exports.cs`
  - `D:\Workspaces\OpenSynapse-chroma\src\OpenSynapse.ChromaSdk.Shim\ChromaEffects.cs`
  - `D:\Workspaces\OpenSynapse-chroma\src\OpenSynapse.ChromaSdk.Shim\ChromaPipeClient.cs`
  - `D:\Workspaces\OpenSynapse-chroma\src\OpenSynapse.Windows\Lighting\ChromaSdkBridgeHost.cs`
  - `D:\Workspaces\OpenSynapse-chroma\src\OpenSynapse.App\Runtime\ChromaSdkEffectApplier.cs`
- 应用启动路径在启用实验性 Chroma 时会启动 `ChromaSdkBridgeHost`；applier 支持键盘、鼠标、耳机、鼠标垫、键区和 Chroma Link 全部六类，host 按会话声明的任一 Chroma 通道接受效果。

### 未验证

- 没有用真实 Chroma 游戏加载过发布版 shim DLL；没有验证游戏是否检查签名、是否受反作弊影响。
- 没有在真实 Razer 键盘/鼠标/耳机/鼠标垫/键区/Chroma Link 上验证最终灯光；六类通道目前只有单元和管道层证据。
- 矩阵设备走 RazerHid 或键盘矩阵输出，单区设备走 class 0x10 或 Kraken 静态色；键区/Chroma Link 已接通，但没有真机验证。
- 通用 `CreateEffect` 和 `QueryDevice` 没有实现；官方 per-device GUID 映射未验证。
- 弃用/复杂效果（Wave、Spectrum、Breathing、Blinking、Reactive 等）没有硬件行为验证。
- 本次已执行 `dotnet build OpenSynapse.slnx -c Debug`（0 error，0 warning；SDK 10.0.302 自带的 ILCompiler 10.0.10 有 NU1903 构建期 advisory，项目按 SDK 约束独立升级会破坏 NativeAOT，因此只在 shim 项目中抑制并记录）和 `dotnet test --filter ChromaSdk`（10 passed）。Release AOT 已发布，并用独立探针加载发布 DLL 走通 Init/Create/Set/UnInit 与静态颜色管道传输；这些仍不能替代真机或真实游戏证据。
- 管道 ACL 没有单独审计；shim 没有应用层认证。

### 设备几何的来源

本文的 6×22 键盘、9×7 鼠标等几何来自 SDK 4.0 头文件/文档。Synapse 2 的设备几何只能作为 OpenSynapse 内部 fallback，不能拿来定义 SDK 契约。

## 2. 证据与复现

### 2.1 官方 DLL 身份

```powershell
$p = 'C:\Program Files\Razer Chroma SDK\bin\RzChromaSDK64.dll'
[Diagnostics.FileVersionInfo]::GetVersionInfo($p) |
  Format-List FileVersion,ProductVersion,FileDescription,CompanyName
(Get-Item -LiteralPath $p).Length
```

结果：

| 项 | 值 |
|---|---|
| 路径 | `C:\Program Files\Razer Chroma SDK\bin\RzChromaSDK64.dll` |
| PE machine | `0x8664` (x64) |
| 大小 | 273,096 字节 |
| FileVersion | `3.8.0.238` |
| ProductVersion | `4.0.1.07221608` |
| FileDescription | `Razer Chroma SDK (Windows PC)` |
| CompanyName | `Razer Inc.` |
| 导出数 | 15 |

导出表可以用 `pefile` 复核：

```powershell
python -c "import pefile; p=r'C:\Program Files\Razer Chroma SDK\bin\RzChromaSDK64.dll'; pe=pefile.PE(p, fast_load=True); pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXPORT']]); print(len(pe.DIRECTORY_ENTRY_EXPORT.symbols)); [print(s.ordinal, s.name.decode()) for s in pe.DIRECTORY_ENTRY_EXPORT.symbols]"
```

### 2.2 动态探针

探针使用 Python 3.15 `ctypes.WinDLL`，`restype = c_long`；`APPINFOTYPE` 按第 4 节布局定义，`GUID` 为 16 字节，`DEVICE_INFO_TYPE` 为两个 `DWORD`。关键调用和结果：

| 调用（在 `Init()` 之后，除非另注） | 结果 | 说明 |
|---|---:|---|
| `Init()` 连续两次 | `0, 0` | 幂等 |
| `UnInit()` 连续两次 | `0, 0` | 幂等 |
| `InitSDK(NULL)` | `87` | 空指针 |
| `InitSDK(按 3592 字节布局构造的 APPINFOTYPE，SupportedDevice=1，Category=2)` | `0` | 独立探针；字段偏移见第 4 节 |
| `CreateKeyboardEffect(0, NULL, &id)` | `0` | `CHROMA_NONE` 接受空参数 |
| `CreateKeyboardEffect(4, NULL, &id)` | `87` | `CHROMA_STATIC` 需要参数 |
| `CreateKeyboardEffect(4, 4 字节 COLORREF 0x0000FF, &id)` | `0` | 随后 `SetEffect(id)`、`DeleteEffect(id)` 均为 `0` |
| `CreateKeyboardEffect(99, 非空 param, &id)` | `50` | 超出枚举范围 |
| `CreateKeyboardEffect(9, 非空 param, &id)` | `0` | DLL 对 `CHROMA_INVALID=9` 意外宽松；不要把它当作有效契约 |
| 未 `Init` 时 `CreateKeyboardEffect(...)` | `5023` | `0x139F`，不是 5923 |
| 未 `Init` 时 `SetEffect(...)` | `5023` | 同上 |
| `SetEffect(非零未知 GUID)` | `1168` | `NotFound` |
| `DeleteEffect(非零未知 GUID)` | `1168` | `NotFound` |
| `SetEffect(全零 GUID)` | `87` | 零 GUID 按非法参数处理 |
| `QueryDevice(全零 GUID, &info)` | `87` | 与公开探针一致 |

`APPINFOTYPE` 尺寸可直接验证：

```python
import ctypes
from ctypes import wintypes

class APPINFO(ctypes.Structure):
    _fields_ = [
        ("Title", wintypes.WCHAR * 256),
        ("Description", wintypes.WCHAR * 1024),
        ("AuthorName", wintypes.WCHAR * 256),
        ("AuthorContact", wintypes.WCHAR * 256),
        ("SupportedDevice", wintypes.DWORD),
        ("Category", wintypes.DWORD),
    ]

assert ctypes.sizeof(APPINFO) == 3592
```

### 2.3 关键修正

- 任务简报里的 `5923` 不是官方 4.0.1 DLL 的未初始化返回值。真实 DLL 在未 `Init` 时返回 `5023`（`0x139F`，`RZRESULT_NOT_VALID_STATE`）。`5923` 来自参考开源 shim，不应作为契约。
- “invalid effect = 50” 对超出范围的 `99` 成立；枚举值 `9`（`CHROMA_INVALID`）在实测中仍返回 `0`，不要用 9 做负例。
- 本地 `D:\Tools\Temp\chroma-research\RzChromaSDKTypes.h` 不是完整的 4.0.1 头文件：它没有 `APPINFOTYPE`，`Mouse::EFFECT_TYPE` 也没有 `CHROMA_STATIC2`。APPINFOTYPE 和 Mouse::STATIC2 以官方 4.x 文档源和 DLL 探针为准。
- `D:\Tools\Temp\chroma-research\RzChromaSDK.h` 只有 `404: Not Found`，不是头文件，不要引用。
- 官方声明与结构体来源：
  - `https://assets.razerzone.com/dev_portal/C%2B%2B/en/_rz_chroma_s_d_k_8h_source.html`
  - `https://assets.razerzone.com/dev_portal/C%2B%2B/en/_rz_chroma_s_d_k_types_8h_source.html`
  - `https://assets.razerzone.com/dev_portal/C%2B%2B/en/struct_chroma_s_d_k_1_1_a_p_p_i_n_f_o_t_y_p_e.html`
  - `https://assets.razerzone.com/dev_portal/C%2B%2B/en/namespace_chroma_s_d_k_1_1_mouse.html`
  文档站当前页面标注 v4.1；本文只用它补齐结构体/枚举，关键返回值以 4.0.1 DLL 探针为准。

## 3. ABI

### 3.1 x64 调用约定

- Windows x64 使用统一的 Microsoft x64 调用约定：前四个整数/指针参数在 `RCX`、`RDX`、`R8`、`R9`，调用方预留 32 字节 shadow space，调用方清理栈。
- x64 下 `__cdecl` 和 `__stdcall` 不再改变寄存器分配；导出名没有 `@N` 后缀。
- `RZRESULT` 是 32 位有符号 `LONG`，shim 用 `int` 返回。
- `RZEFFECTID`/`RZDEVICEID` 是 16 字节 `GUID`。`SetEffect`/`DeleteEffect` 按值传 `GUID`；`Create*` 的 `RZEFFECTID*` 是 16 字节输出缓冲区。
- shim 用 `[UnmanagedCallersOnly(EntryPoint = "...")]` 导出上述 15 个名字；在 x64 上默认 unmanaged 调用约定与官方 DLL 一致。

### 3.2 15 个导出

官方头文件声明（`RzChromaSDK.h`，`extern "C"`）：

| Ordinal | 导出 | C 签名 | shim 行为摘要 |
|---:|---|---|---|
| 1 | `Init` | `RZRESULT Init(void);` | 连接管道；已连接时返回 0；无宿主返回 1062 |
| 2 | `InitSDK` | `RZRESULT InitSDK(ChromaSDK::APPINFOTYPE* pAppInfo);` | 校验后连接管道；见第 4 节 |
| 3 | `UnInit` | `RZRESULT UnInit(void);` | 释放管道、清空 effect 表；返回 0 |
| 4 | `CreateEffect` | `RZRESULT CreateEffect(RZDEVICEID DeviceId, ChromaSDK::EFFECT_TYPE Effect, PRZPARAM pParam, RZEFFECTID* pEffectId);` | 未实现，返回 50 |
| 5 | `SetEffect` | `RZRESULT SetEffect(RZEFFECTID EffectId);` | 查表、入队；无匹配设备时仍返回 0 |
| 6 | `DeleteEffect` | `RZRESULT DeleteEffect(RZEFFECTID EffectId);` | 删除；该设备最后一个 effect 时发送 Clear |
| 7 | `RegisterEventNotification` | `RZRESULT RegisterEventNotification(HWND hWnd);` | 记录窗口；连接时 Post `WM_CHROMA_EVENT` |
| 8 | `UnregisterEventNotification` | `RZRESULT UnregisterEventNotification(void);` | 清空窗口句柄；返回 0 |
| 9 | `CreateKeyboardEffect` | `RZRESULT CreateKeyboardEffect(ChromaSDK::Keyboard::EFFECT_TYPE Effect, PRZPARAM pParam, RZEFFECTID* pEffectId);` | 支持 NONE/STATIC/CUSTOM/CUSTOM_KEY/CUSTOM2 |
| 10 | `CreateMouseEffect` | `RZRESULT CreateMouseEffect(ChromaSDK::Mouse::EFFECT_TYPE Effect, PRZPARAM pParam, RZEFFECTID* pEffectId);` | 支持 NONE/CUSTOM/CUSTOM2/STATIC/STATIC2 |
| 11 | `CreateHeadsetEffect` | `RZRESULT CreateHeadsetEffect(ChromaSDK::Headset::EFFECT_TYPE Effect, PRZPARAM pParam, RZEFFECTID* pEffectId);` | 支持 NONE/STATIC/CUSTOM |
| 12 | `CreateMousepadEffect` | `RZRESULT CreateMousepadEffect(ChromaSDK::Mousepad::EFFECT_TYPE Effect, PRZPARAM pParam, RZEFFECTID* pEffectId);` | 支持 NONE/STATIC/CUSTOM/CUSTOM2 |
| 13 | `CreateKeypadEffect` | `RZRESULT CreateKeypadEffect(ChromaSDK::Keypad::EFFECT_TYPE Effect, PRZPARAM pParam, RZEFFECTID* pEffectId);` | 支持 NONE/STATIC/CUSTOM |
| 14 | `CreateChromaLinkEffect` | `RZRESULT CreateChromaLinkEffect(ChromaSDK::ChromaLink::EFFECT_TYPE Effect, PRZPARAM pParam, RZEFFECTID* pEffectId);` | 支持 NONE/STATIC/CUSTOM |
| 15 | `QueryDevice` | `RZRESULT QueryDevice(RZDEVICEID DeviceId, DEVICE_INFO_TYPE& DeviceInfo);` | 未实现 GUID 映射，返回 50；`DeviceInfo` 必须非空 |

参数说明：

- `PRZPARAM` 是 `void*`。`Create*` 的 `pParam` 指向对应设备的 effect struct，不是通用 `CreateEffect` 的大结构。
- `RZEFFECTID* pEffectId` 是输出参数。shim 要求它非空；为空返回 87。官方 DLL 对空指针探针返回 0，但这是未定义用法；正常调用方必须传有效指针。
- `HWND` 在 ABI 上是指针；`RegisterEventNotification` 只保存句柄并投递 `WM_CHROMA_EVENT = 0x0A000`，不提供完整事件负载。
- `QueryDevice` 官方头文件用 C++ 引用 `DEVICE_INFO_TYPE&`；二进制等价于传指针，shim 用 `IntPtr` 接收。
- 上表 ordinal 是官方 DLL 的；shim AOT 链接器会重新排序导出，shim 的 ordinal 不保证与官方一致。游戏应按名字 `GetProcAddress`；ordinal 只用于核对 PE 导出表。

## 4. APPINFOTYPE 与 InitSDK

### 4.1 布局

`TCHAR` 在官方 Unicode 构建中是 `WCHAR`（2 字节）。布局如下：

| 偏移 | 字段 | 类型 | 字节数 |
|---:|---|---|---:|
| 0 | `Title` | `WCHAR[256]` | 512 |
| 512 | `Description` | `WCHAR[1024]` | 2048 |
| 2560 | `Author.Name` | `WCHAR[256]` | 512 |
| 3072 | `Author.Contact` | `WCHAR[256]` | 512 |
| 3584 | `SupportedDevice` | `DWORD` | 4 |
| 3588 | `Category` | `DWORD` | 4 |
| 3592 | 结构体总大小 | | **3592** |

`SupportedDevice` 是位掩码：

| 位 | 值 | 设备 |
|---:|---:|---|
| 0 | `0x01` | Keyboard |
| 1 | `0x02` | Mouse |
| 2 | `0x04` | Headset |
| 3 | `0x08` | Mousepad |
| 4 | `0x10` | Keypad |
| 5 | `0x20` | ChromaLink |

`Category`：`1 = Utility`，`2 = Game`。

### 4.2 InitSDK 校验

官方 4.0.1 DLL 的实测行为：

- `pAppInfo == NULL` -> 87。
- `Title`、`Description`、`Author.Name`、`Author.Contact` 任一为空字符串 -> 87。
- `SupportedDevice == 0` 或 `> 0x3F` -> 87；`0x3F` 有效。
- `Category` 不是 1 或 2 -> 87。
- 以上都有效 -> 0。
- 独立探针按偏移构造 3,592 字节结构（Title@0、Description@512、Author.Name@2560、Author.Contact@3072、SupportedDevice=1@3584、Category=2@3588）并填入有效字符串，`InitSDK` 返回 0；同一布局把字符串留空则返回 87。

OpenSynapse shim 当前校验：

- `pAppInfo == NULL` -> 87。
- `SupportedDevice` 必须在 `1..0x3F`，`Category` 必须在 `1..2`。
- 按 3,592 字节布局读取 `Title`、`Description`、`Author.Name`、`Author.Contact`，四段均要求非空；与官方探针一致。
- 管道连接失败 -> 1062；已连接时重复 `Init`/`InitSDK` -> 0。

这意味着：用 shim 测试 InitSDK 通过，不等于官方 DLL 也会通过。要严格模拟官方拒绝面，调用方必须填满四个字符串。

## 5. 效果结构

### 5.1 设备矩阵

| 设备 | SDK 类型 | 行 × 列 / LED 数 | 像素/元素 | 结构体字节 | 备注 |
|---|---|---|---:|---:|---:|
| Keyboard | `Keyboard::CUSTOM_EFFECT_TYPE` | 6 × 22 | 132 | 528 | 纯颜色矩阵 |
| Keyboard | `Keyboard::CUSTOM_KEY_EFFECT_TYPE` | 6 × 22 + 6 × 22 | 264 | 1056 | 颜色矩阵后紧跟 key 矩阵 |
| Keyboard | `Keyboard::CHROMA_CUSTOM2` | 8 × 24 | 192 | 768 | SDK 4.x 扩展网格；按 8 行 24 列读取 |
| Mouse | `Mouse::CUSTOM_EFFECT_TYPE` | 30 LEDs | 30 | 120 | 旧式一维 custom |
| Mouse | `Mouse::CUSTOM_EFFECT_TYPE2` | 9 × 7 | 63 | 252 | 协议字段 `width=7, height=9` |
| Mouse | `Mouse::STATIC_EFFECT_TYPE` | — | — | 8 | `RZLED LEDId + COLORREF Color` |
| Mouse | `Mouse::STATIC_EFFECT_TYPE2` | — | — | 4 | 仅 `COLORREF Color` |
| Headset | `Headset::CUSTOM_EFFECT_TYPE` | 5 LEDs | 5 | 20 | 一维 |
| Mousepad | `Mousepad::CUSTOM_EFFECT_TYPE` | 15 LEDs | 15 | 60 | 一维 |
| Mousepad | `Mousepad::v2::CUSTOM_EFFECT_TYPE` | 20 LEDs | 20 | 80 | v2 虚拟 LED 网格；按一维 20 色读取 |
| Keypad | `Keypad::CUSTOM_EFFECT_TYPE` | 4 × 5 | 20 | 80 | 二维 |
| ChromaLink | `ChromaLink::CUSTOM_EFFECT_TYPE` | 5 LEDs | 5 | 20 | 一维 |

设备专用的 static 结构没有 `Size`/`Param` 前导字段，直接是颜色（鼠标 effect 6 例外，带 `LEDId`）。通用 `CreateEffect` 的 `CUSTOM_EFFECT_TYPE` 是 30×30，带 `Size`/`Param`，字节数 3616；shim 不使用它。

### 5.2 COLORREF 与 key mask

- `COLORREF`/`RZCOLOR` 是 32 位 `0x00BBGGRR`：低字节 Red，其次 Green，再次 Blue。
- `Keyboard::CUSTOM_KEY_EFFECT_TYPE` 的 `Key[row][column]` 用 `0x01000000 | Color` 表示该位置有 key effect；`0x01000000` 是掩码位，不是颜色。
- shim 读取键盘 custom-key 参数时，先读 528 字节颜色矩阵，再读 528 字节 key 矩阵；总 1056 字节。

## 6. 返回码

| 码 | 名称 | 官方 4.0.1 实测/来源 | OpenSynapse shim |
|---:|---|---|---|
| 0 | `RZRESULT_SUCCESS` | 成功 | 成功 |
| 50 | `RZRESULT_NOT_SUPPORTED` | 超出范围的效果（如 99） | 未实现/弃用效果、`CreateEffect`、`QueryDevice` |
| 87 | `RZRESULT_INVALID_PARAMETER` | 空参数、非法 APPINFO、零 GUID | 空参数、空 effectId、非法 APPINFO、无效设备 |
| 1062 | `RZRESULT_SERVICE_NOT_ACTIVE` | 服务未运行 | 管道连接失败 |
| 1152 | `RZRESULT_SINGLE_INSTANCE_APP` | 官方错误表 | 当前 bridge host 固定 ack 0，实际路径未触发 |
| 1167 | `RZRESULT_DEVICE_NOT_CONNECTED` | 设备未连接 | shim 不主动返回；host 侧无匹配设备时跳过帧 |
| 1168 | `RZRESULT_NOT_FOUND` | 非零未知 effect GUID | `SetEffect`/`DeleteEffect` 找不到 GUID |
| 5023 | `RZRESULT_NOT_VALID_STATE` | 未 `Init` 时 `Create*`/`SetEffect` | 管道未连接/未初始化 |
| 5923 | 非官方值 | **不是**官方 4.0.1 的返回值 | 不应使用；参考开源 shim 曾误用 |

补充：

- 未 `Init` 时官方 DLL 返回 `5023`；shim 也使用 `5023`。shim 的 `SetEffect` 先查 GUID；未知 GUID 返回 `1168`，有效 effect 在管道断开时返回 `5023`。设备是否存在由 host 侧决定，shim 不因此返回 1167。
- 零 GUID 的 `SetEffect` 返回 `87`；非零未知 GUID 返回 `1168`。
- `CreateKeyboardEffect(99, ...)` 返回 `50`；`CreateKeyboardEffect(9, ...)` 在官方 DLL 上返回 `0`，属于实现宽松，不应依赖。

## 7. 支持的效果

### 7.1 已实现

| 设备 | NONE | STATIC | CUSTOM | CUSTOM_KEY / CUSTOM2 |
|---|---|---|---|---|
| Keyboard | 0 | 4 | 2 | 8 (`CUSTOM_KEY`), 9 (`CUSTOM2`, 8×24) |
| Mouse | 0 | 6, 9 (`STATIC2`) | 3 (30 LEDs) | 8 (`CUSTOM2`, 9×7) |
| Headset | 0 | 1 | 4 | — |
| Mousepad | 0 | 4 | 2 | 6 (`CUSTOM2`, 20 LEDs) |
| Keypad | 0 | 5 | 2 | — |
| ChromaLink | 0 | 2 | 1 | — |

`NONE` 接受 `pParam = NULL`；shim 对其他已支持效果要求非空参数。`CUSTOM_KEY` 的颜色矩阵和 key 矩阵都必须是 6×22。协议保留 `CUSTOM2` kind：键盘按 8×24 读取；鼠标和鼠标垫的 CUSTOM2 变体按设备尺寸读取，不能套用 6×22 键盘矩阵。

鼠标 `STATIC`（effect 6）目前只接受 `LEDId = 0` 或 `0xFFFF`（整只鼠标）；单 LED ID 1..17 返回 50，因为 OpenSynapse 没有经过验证的鼠标 LED 位置映射。需要整只鼠标单色时用 `STATIC2`（effect 9）。

### 7.2 未实现

- 通用 `CreateEffect`：返回 50。它需要 Razer 的 per-device `RZDEVICEID` GUID 映射，当前没有经过验证的映射表。
- `QueryDevice`：返回 50。它需要同一套 GUID 映射和 `DEVICE_INFO_TYPE` 语义；当前只会在非空 `DeviceInfo` 时顺便刷新管道支持的设备掩码，不写设备信息。
- 弃用/复杂效果：Wave、Spectrum、Breathing、Blinking、Reactive、Starlight 等返回 50。没有硬件报文/真机证据，不能凭 Synapse 2 的布局猜测。
- `RegisterEventNotification` 只做最小实现：保存 HWND 并投递 `WM_CHROMA_EVENT`，不提供完整的设备事件流。
- Keypad/ChromaLink 现在和其他通道一样会广告并按 PID 路由；只有存在可达目标时才真正写入，否则 shim 仍返回 0（官方在无设备时也返回 0）。Mouse/Headset/Mousepad 同样按设备实际传输能力应用。

## 8. 部署

### 8.1 产物

- 必须使用 native AOT 产物：`D:\Workspaces\OpenSynapse-chroma\artifacts\chroma-sdk-shim\RzChromaSDK64.dll`（由下面的 publish 命令生成；中间产物在 `src\OpenSynapse.ChromaSdk.Shim\bin\Release\net10.0\win-x64\native\RzChromaSDK64.dll`）。
  ```powershell
  dotnet publish src\OpenSynapse.ChromaSdk.Shim\OpenSynapse.ChromaSdk.Shim.csproj -c Release -r win-x64 -o artifacts\chroma-sdk-shim
  ```
- Debug 的 `bin\Debug\net10.0\RzChromaSDK64.dll` 是托管程序集，PE 没有原生导出，不能作为游戏替换 DLL。
- Release AOT 产物包含 15 个 Chroma 导出，另有一个 `DotNetRuntimeDebugHeader`；后者是 .NET runtime 工件，不属于 Chroma API。
- Release AOT shim 的 no-host 冒烟：`Init` -> 1062，`CreateKeyboardEffect(4, NULL)` -> 87，`CreateKeyboardEffect(4, buf)` -> 5023，`SetEffect`/`DeleteEffect` 未知 GUID -> 1168，`UnInit` -> 0。只证明 DLL 可加载和返回值路径，不证明游戏或硬件。

### 8.2 安装方式

发布包会带上 `ChromaSdkShim\RzChromaSDK64.dll`。设置页的“Chroma 集成 → 游戏兼容层 → 安装”会：

1. 读取 `HKLM\SOFTWARE\WOW6432Node\Razer Chroma SDK` 的 `InstallPath64`，定位 `bin` 目录。
2. 把 Razer 官方 DLL 备份为 `RzChromaSDK64.dll.opensynapse-backup`。
3. 用同目录临时文件原子替换为 OpenSynapse shim。
4. 点“恢复官方 DLL”或卸载 OpenSynapse 时把备份还原。

这个操作会替换系统级 SDK 目录，影响所有从该目录加载 DLL 的 x64 游戏，因此必须由用户明确点击并经过 UAC；应用不会静默替换。若游戏自带 app-local 的 `RzChromaSDK64.dll`，替换 SDK 目录不会生效，需要用 Process Monitor 过滤 `RzChromaSDK64.dll` 的 `Load Image` 事件确认来源。

运行游戏前，OpenSynapse 必须已启动并开启 Chroma 集成；否则 `Init` 返回 1062。仅 x64，没有 x86 shim；shim 未签名，反作弊是否拦截需要按游戏实测。

官方 Chroma SDK Core 安装包（包含 Razer 签名的 `RzChromaSDK64.dll`）：
`https://app-assets.razer.com/files/synapse/Razer_Chroma_SDK_Core_v4.1.0.10151444.exe`

设置页的“下载官方 Chroma SDK”链接指向同一个地址。安装官方 SDK Core 后，Razer 会注册自己的 Chroma 服务；如果后续改用共享内存 broker，需要先停掉 `Razer Chroma SDK Service` 和 `Razer Chroma SDK Server`，由 broker 接管。

## 9. 生命周期与安全

- **一个活跃客户端**：`ChromaSdkBridgeHost` 最多允许 4 个管道连接，但同一时间只让一个 session 应用效果。其他 session 的 `SetEffect` 会被跳过并计入 `FramesSkipped`；shim 已经把调用方返回值置为 0，所以游戏不会知道被跳过。
- **fire-and-forget**：`SetEffect` 只把消息放入 shim 的单槽 pending 队列并立即返回 0；真正的写入由后台线程完成。连续快速调用可能覆盖尚未发送的 pending 消息，这是当前设计的取舍。
- **Clear/UnInit/断开恢复**：`DeleteEffect` 删除某设备最后一个 effect 时发送 `Clear`；`UnInit` 释放管道并发送 `Bye`；管道断开时服务端 `finally` 结束 session。服务端在 session 确实应用过效果且恢复开关开启时调用 `_restoreLighting()`。
- **Clear 的粒度**：shim 按设备发送 Clear；host 将该设备写成黑色并保持当前 session，等整个 session 结束/断开时才恢复 OpenSynapse 灯效。
- **安全边界**：管道是本地命名管道，没有应用层认证或消息签名；依赖 Windows 命名管道默认 ACL。不要把管道名暴露到远程。本次未单独审计 ACL。
- **effect 生命周期**：effect GUID 是进程内随机值，不持久化；`UnInit` 清空。当前 shim 没有 effect 数量上限，长时间运行需要观察内存。

## 10. 仍需完成

1. **真实游戏 DLL 加载测试**：用一款 Chroma 游戏分别测试 app-local 和 SDK bin 替换，确认 15 个导出解析、Init/InitSDK、实际灯光、退出恢复；记录游戏是否检查签名或受反作弊影响。
2. **设备类别扩展**：六类通道都已接通，待真机验证。官方清单里没有 OpenSynapse 端点的旧设备仍不会亮；不能靠猜协议补，需要先取得可验证的报文。
3. **通用 CreateEffect/QueryDevice**：补齐官方 per-device GUID 表和 `DEVICE_INFO_TYPE` 语义；在此之前保持 50。
4. **弃用效果硬件捕获**：为 Wave、Spectrum、Breathing、Blinking、Reactive 等抓取真实设备报文，确认是否值得实现；不要用 Synapse 2 布局猜测。
5. **effect 数量上限/清理策略**：当前 shim 的 effect 字典没有上限，需要评估长时间运行的内存风险。
