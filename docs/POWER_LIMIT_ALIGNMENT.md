# Blade 功耗墙（CPU / GPU）实现现状与可行路径

证据等级：`static`（雷云中间件 JS bundle + 中间件 manifest 静态分析 + 本机 `nvidia-smi` 实测）。
没有 Blade 实机，所有 HID 写入结论均为「bundle 对齐」，未做硬件验证。

## 0. 结论

「整机功耗墙」在雷云里不是一个统一控件，而是三条互不相通的通道：

| 通道 | 能否直接用 HID 写 | 我们现状 |
|---|---|---|
| CPU 档位（Boost / HyperBoost 预设） | 能 | 已实现 |
| CPU 瓦特数（PL1 / PL2 / PL2 时间窗） | 不能，走原生 XTU / AMD DLL | 未实现 |
| CPU 功耗开关（Power Mode Control bit 0x01） | **能，而且报文我们已经在读写** | **未解码，见第 5 节** |
| GPU 功耗（TGP 瓦特数） | 雷云根本没有这个控件 | 未实现，可借 `nvidia-smi` 自己做 |

CPU 瓦特数要蹭 Intel XTU / AMD 驱动，成本最高；GPU 瓦特数雷云自己都不提供，
但 `nvidia-smi` 提供了一条干净的官方路径。而第 5 节那条 CPU 功耗开关是本项目里
唯一一条「零协议成本、纯 HID、直接放开 CPU 功耗」的路径。

## 1. 先分清三层「提升功耗」

| 层级 | 含义 | 用户感知 |
|---|---|---|
| L1 档位型 | 切固件预设（Boost 档 / HyperBoost），固件自己决定放多少瓦 | Balanced → HyperBoost 性能差异 |
| L2 瓦特型 | 用户直接拖 PL1 / PL2 / TGP 的瓦特数 | 明确的「+X W」 |
| L3 频率偏移型 | 拖核心 / 显存频率（MHz） | 频率提升，可能不稳 |

雷云在 CPU 侧同时提供 L1 和 L2（L2 只在有 XTU 的机型上），在 GPU 侧只提供 L1 和 L3。

## 2. CPU 侧：雷云怎么做的

### 2.1 L2 瓦特数走原生 DLL FFI，不是 HID

证据：`artifacts/razer-mw-full-cdn/bundles/<PID>/6737.<hash>.js`。

两套包装类，按 CPU 厂商分：

| 类 | 依赖的原生资源 | 用在 |
|---|---|---|
| `rzIntelOverClock` | `RzIntelOverClock_v*.dll` + `RzIntelOverClockDLL_v*.dll` + `RazerXTU-*.exe` | 029F 02A0 02B7 02B8 02C7 02E1 |
| `rzAMDOverClock` | `RzAMDOverClock_v*.dll` + `RzAMDOverClockDLL_v*.dll` + `RazerAMDOverClockSetup-*.exe` | 029D 02B6 02C5 02C6 |

中间件 manifest 里这些资源的实际下载地址（029F 为例）：

```
BladeDLL             1.0.13.1   349,712   .../common/blades/bladeNative_v1.0.13.1.dll
RazerIntelOverClock  1.0.2.0  1,602,600   .../common/blades/RazerIntelOverClock-v1.0.2.0.exe
RazerXTU             1.0.5.3  4,201,112   .../common/blades/RazerXTU-v1.0.5.3.exe
RzIntelOverClock     1.0.21.0   437,960   .../common/blades/RzIntelOverClock_v1.0.21.0.dll
RzIntelOverClockDLL  1.0.21.0   683,208   .../common/blades/RzIntelOverClockDLL_v1.0.21.0.dll
```

### 2.2 DLL 的 FFI action 与 controlId 映射

从 bundle 的 `w=(e,t)=>{...}` 反解出 controlId → 字段对应：

| controlId | 字段 | 含义 |
|---|---|---|
| 48 | `turboBoostPowerMax` | **Turbo Boost Power Max (W)** = PL1 长时功耗墙 |
| 47 | `turboBoostShortPowerMax.value` | **Turbo Boost Short Power Max (W)** = PL2 短时功耗墙 |
| 49 | `turboBoostShortPowerMax.isEnabled` | PL2 开关 |
| 66 | `turboBoostPowerTimeWindow` | **Turbo Boost Power Time Window (s)** = PL2 时间窗 |
| — | `coreVoltageOffset` | Intel 电压偏移 / AMD Curve Optimizer |

对应 DLL action：

```text
GetPowerLimitValueAsync / SetPowerLimitValueAsync      → CPU POWER LIMIT (W)
GetAllTuningControlDataAsync / UpdateAllTuningControlDataAsync  → 上面那组 controlId
CheckSystemIncompatibilityAsync                          → 查 Intel UVP 锁死
Get/SetCurveOptimizerValueAsync, ResetVoltageOffsetAsync,
EnableCPUVoltageOptimizerAsync                           → 电压偏移
SetIntelOverClockDllsPath / SetTargetDLLPath             → 装载 Intel XTU 的 DLL 组
```

### 2.3 生效条件（bundle 原文）

profile 的性能模式必须是 `Custom_Mode` **且** `activePowerPreset === CPUPowerLimit`。
AMD 机型还会先写一次 `setAdvFanMode(1, CPU_Fan, cpuLevel)` 再调 DLL。

### 2.4 用户环境前置（雷云文案原文）

- `CPU Overclocking must be enabled for Intel XTU to function properly.`
- BIOS 需关闭：Under Voltage Protection (UVP)、Intel VMX
- Windows 需关闭：Core Isolation、Hyper-V

这三条不满足，雷云自己的 XTU 通道也不工作。

### 2.5 机型覆盖

bundle `deviceUI` 的 `performance` / `power` 分组里的标志位：

| 标志 | PID |
|---|---|
| `hasIntelCPUOverclocking` | 029F 02A0 02B7 02B8 02C7 02E1 |
| `hasAMDOverclock` | 029D 02B6 02C5 02C6 |
| `hasCpuBoostEnabled` | 028C 029D 02B6（只有这三个在 `main.*.js`） |
| `hasMaxFanSpeedModeEnabled` | 029D 029E 029F 02A0 02B6 02B8 |

## 3. GPU 侧：雷云给的是频率偏移，不是瓦特数

### 3.1 雷云 GPU 侧的全部三项

| 雷云功能 | 通道 | 我们现状 |
|---|---|---|
| GPU Boost 档位（Low/Medium/High） | `setAdvFanModeWattage(1, GPU_Fan, level)` → HID `0D:07` fan=0x02，读 `0D:87` | 已有 |
| GPU Clock Offset / Memory Clock Offset (MHz) | 原生 `BladeNativeExe` 传参 `SetGPUValue gpu vram`，或 `BladeDLL` FFI action | 非 HID，未实现 |
| HyperBoost | 切 Gaming 预设：`setOverClockingMode(Gaming)` → 读 `0D:83` 拿出厂 GPU/VRAM 值 → `SetGPUValue` 复位 | 已有 |

搜遍全部 bundle：**没有任何 GPU 瓦特数字段**。翻译文件里 GPU 相关文案只有
`GPU Clock Offset (MHz)` / `Memory Clock Offset (MHz)`。

### 3.2 我们的 `0D:83` 已经握着出厂 GPU/VRAM 值

`BladeThermalProtocol.ParseFanInformation` 解出的 `BladeThermalFanInformation` 里
`Gpu` / `Vram` 两个 16 位大端字段，就是雷云 `getThermalFanInformation` 的
`jsonData.GPU` / `jsonData.VRAM` —— 雷云切 Gaming 预设时用它们调 `SetGPUValue`。
**这段读取我们已经有了，只是没有"用"它去写 GPU 参数。**

### 3.3 nvidia-smi 实测（本机 RTX 5070 Laptop）

```text
nvidia-smi --query-gpu=power.default_limit,power.max_limit
→ 50.00 W, 125.00 W

nvidia-smi --query-gpu=power.limit
→ [N/A]

nvidia-smi -pl 60
→ Changing power management limit is not supported in current scope for GPU: 00000000:C5:00.0.

nvidia-smi -lgc 200,2000
→ The current user does not have permission to change clocks for GPU 00000000:C5:00.0.
```

三个确定事实：

1. **范围值能读** —— `power.default_limit` / `power.max_limit` 可直接拿来给滑杆定界。
2. **写要管理员** —— 我们的 `GpuModeService` 是 LocalSystem SYSTEM 服务，权限现成。
3. **Hybrid 模式下 `-pl` 被拒** —— 笔记本 dGPU 在 Optimus 下 TGP 由 EC 统一管，
   NV 驱动不开放该 scope。**这不是权限问题，是 MUX 模式问题。**

### 3.4 我们已有的现成件

- `blade-671.json` 等 manifest 已有 `display-mux.get/set`（0D:89 / 0D:09）→ 切直连能力现成
- `WindowsPerformanceMonitor` 已经在调 `nvidia-smi`，但只取 `power.draw`（瞬时功耗），
  没有取 limit 字段
- `NvidiaSmiOutputParser` 是 7 字段固定解析，加 limit 字段要改

## 4. 路线与取舍

| 路线 | 内容 | 成本 | 风险 |
|---|---|---|---|
| **A. 档位型**（推荐先做） | 补齐已有 CPU/GPU Boost 档位 + HyperBoost 预设的 UI | 低 | 无。纯 HID，已实现 |
| **B. CPU 瓦特型** | 自己接 Intel XTU SDK / AMD 驱动，复刻 controlId 语义 | 高 | 要驱动、要权限、要机型兼容；还要处理 BIOS/Win 三条前置；雷云那 6 个原生资源的再分发许可未知 |
| **C. GPU 瓦特型** | SYSTEM 服务跑 `nvidia-smi -pl <W>`，范围取 `default_limit`~`max_limit` | 中 | **必须 MUX 切 Discrete 之后**才有意义；本机 Hybrid 下已被拒，切直连后能否写未验证 |
| **D. GPU 频率偏移** | 用 `nvidia-smi -lgc` 近似雷云的 GPU Clock Offset | — | **不建议**。雷云那个 exe 还管 VRAM 偏移和恢复逻辑，`-lgc` 是另一个东西，语义对不上 |

### 建议顺序

**先做 A + 第 5 节那条 CPU 功耗开关，C 等一次实机验证。**

- A 的第一个缺口（`artifacts/device-audit/laptops/gaps.md` 第 6 节）：7 台机型
  `0245 02B7 02B8 02C5 02C7 02E0 02E1` 的 `CpuBoostModes` 目前是 `None`，
  但 bundle 的 `custom:{cpu:1,gpu:0}` 明确存在。最小修法是给它们 `[Low, Medium, High]` 基线。
- C 的唯一未知点：**Blade 16 切 MUX 直连后 `nvidia-smi -pl` 是否能写**。
  这个试一次就出结果 —— 通再写代码，不通整条砍掉。
  范围读值（`default_limit` / `max_limit`）现在就能做，不依赖那个未知。

## 5. 一条被漏掉的 CPU 功耗开关（零协议成本）

雷云的 Power Mode Control 报告是一个 4 bit 掩码。bundle 里的解码原文
（`bundles/0046/6120.<hash>.js`，290 个 bundle 同款）：

```js
jsonData = {
  cpuBoost:        1 & data[0],
  maxFanSpeedMode: 2 & data[0],
  oneTimeOverride: 4 & data[0],
  localDimming:    8 & data[0],
}
```

我们的 `BladePowerModeProtocol` 只认了后三个：

```csharp
public const byte MaxFanBit            = 0x02;
public const byte OneTimeFullChargeBit = 0x04;
public const byte LocalDimmingBit      = 0x08;
```

**bit 0x01 = `cpuBoost`，我们没解码。** 它的语义（雷云英文文案原文）：

> `CPU Boost` — *Allows the CPU to draw more power when GPU is idle*

这是一条**独立的 HID 开关**，和我们的 Boost 档位（0D:87 / 0D:07，固件档位）是两套东西：
档位决定「跑在第几档」，这个 bit 决定「允许 CPU 在 GPU 空闲时多拿功耗」。

**为什么说它是零协议成本**：我们已经在读这条报告、也已经会写回去。

- 读：`BladePowerModeProtocol.ParsePowerModeMask` → `ReadBladePowerModeMaskAsync`
- 写：`BladePowerModeProtocol.CreateSetPowerModeMaskRequest`

报文的 wire 三元组就是 `07:8F`（0x8F = 143）/ `07:0F`（0x0F = 15），
和我们的 `power-mode.get` / `power-mode.set` manifest 完全一致。
在 `bundles/029F/rzDevice25.*.js` 里，雷云甚至把 `Get/Set CPU Boost Control` 和
`Get/Set Power Mode Control` 两个别名指向了同一对三元组 `[1,7,143]` / `[1,7,15]`。

写回时也要注意保持另外三个 bit 不丢 —— 我们的写入路径已经是「读当前掩码 →
改目标 bit → 写回」，所以加一个 bit 只是多一个常量。雷云的写法是
`setPowerModeControl(cpuBoost | localDimming | maxFanSpeedMode | oneTimeOverride)`，
因为四个解码值本身就是掩码（`1&`、`2&`、`4&`、`8&` 各自保留原 bit），OR 起来等于原字节。

### 待确认的边界（写代码前必须解决）

1. **机型准入。** `hasCpuBoostEnabled` 只出现在 028C / 029D / 02B6 三个机型的
   `main.*.js` 里。报文存在 ≠ 该机型支持 —— 我们对 Max Fan 就是这么处理的，
   必须按机型契约准入，不能因为读回干净就开。
2. **别名路径。** 在 `bundles/0046/6120.*.js` 里，中间件有两条不同的写路径：
   一条是 4 bit 掩码写（`setPowerModeControl`），另一条是布尔写
   （`n[0] = t ? 1 : 0`，回读 `{enabled: data[0]}`）。两者在 029F 的 rzDevice25 chunk
   里共用同一对三元组，在 0046 的 chunk 里却来自不同的模块常量。
   **写代码前要用实机确认 bit 0x01 到底认哪条路径**，不要猜。
3. **028C 目前没有机型契约。** `BladeModelCapabilities` 的 39 条契约里没有
   `[0x028C]` 条目，而 028C 的 bundle 恰恰带 `hasCpuBoostEnabled`。
   要做这条开关，得先给 028C 建契约。

## 6. 待决

**C 的滑杆上界该取什么？** 用 `nvidia-smi` 报的 `max_limit`（125W），
还是按整机 EC 策略留余量只放到 80%？本地没有依据定死，要看实机在 100% TGP 下的温度表现。

## 7. 证据索引

| 内容 | 位置 |
|---|---|
| CPU XTU / AMD FFI action 与 controlId | `artifacts/razer-mw-full-cdn/bundles/<PID>/6737.<hash>.js` |
| CPU 功耗墙 UI 文案 | `bundles/029F/trans-en.001a8fd13823659cd812.js` |
| 原生资源下载地址 | `bundles/<PID>/manifest.json` → `resourceManifest.resources` |
| GPU `SetGPUValue` 调用点 | `bundles/029F/rzDevice25LedMatrixBlade.44210ae56dcdfbe53eb0.js` |
| Power Mode Control 四个 bit | `bundles/0046/6120.7fff31f083f844383e26.js` |
| 机型审计缺口 | `artifacts/device-audit/laptops/gaps.md` |
| 现有 Boost 协议 | `src/OpenSynapse.Windows/Protocols/BladeBoostProtocol.cs` |
| 现有功率模式协议 | `src/OpenSynapse.Windows/Protocols/BladePowerModeProtocol.cs` |
| GPU 遥测 | `src/OpenSynapse.Windows/Sensors/WindowsPerformanceMonitor.cs`、`NvidiaSmiOutputParser.cs` |
