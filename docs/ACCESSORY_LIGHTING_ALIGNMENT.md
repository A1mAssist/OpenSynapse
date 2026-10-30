# Chroma 外设/底座灯光对齐

扫描日期：2026-10-28。命令集取自每个 PID 的 middleware bundle 中实际 `sendCommand` 的 `class:id`，并与 `docs/MIDDLEWARE_DEVICE_PROTOCOLS.md`、`accessories.json` 现有条目对照。

| PID | JS / 官方名称 | 兄弟型号 | 关键 class:id | 结论 |
|---|---|---|---|---|
| `0F36` | Razer Head Cushion Chroma | 无 | `10:03/04/05/08/83/85/88` | Blocked：只有 class `0x10` 标准 Chroma，没有 `0F:*` 扩展矩阵；现有 accessories 条目会发 `0x0F`/`0x03`，加入后会发错命令。该设备已由 `RazerChromaStandardService` 提供灯光页。 |
| `0F43` | RAZER LAPTOP COOLING PAD | 无 | `0D:01/10/81/83/90` + `0F:02/03/04/80/82/84` | Own page：`RazerCoolingPad` 提供转速、固定档、智能曲线和旧版 `0F:*` 灯光；不加入 accessories，避免通用灯光页抢先接管。 |
| `0F45` | Razer Freyja（manifest `Esther`） | 无（P9 命令表相同，但产品不是灯效外设） | `0F:02/03/04/08/10/13/80/82/84/88/90/93` | Blocked：JS 与 master guide 都表明它是 HD 触觉坐垫，设备 profile 没有 Chroma family/quick effects，Chroma 命令只是通用表；没有可证明的灯光矩阵。 |
| `0F51` | Razer Core X V2 | 无 | `10:03/04/05/08/83/85/88` | Blocked：只有 class `0x10`；现有 `accessory-00x` 集合不支持该 class。已由 `RazerChromaStandardService` 提供标准灯光页。 |
| `0F52` | Razer Thunderbolt 5 Dock Chroma | `0F21` | `0F:02/03/04/08/10/13/80/82/84/88/90/93` + `0D:01/02/0D/10/81/82/83/85/8D/90` | Added：`0F` 扩展矩阵命令与 `0F21` 同类；OpenRazer PR #2829 让 TB5 直接继承 TB4 dock 类，矩阵取 `1x12`，复用 `accessory-006`。 |
| `0F59` | Razer Monitor Stand Chroma | `0F21` | `0F:02/03/04/08/10/13/80/82/84/88/90/93` + `0D:*` | Added：P9 组，`0F:03 Set Chroma Frame` 为 80 字节帧；按同组 `0F21` 的 `1x12` 帧布局，复用 `accessory-006`。 |
| `0F5A` | Razer Laptop Monitor Stand Chroma | `0F21` | 与 `0F59` 相同的 `0F:*` 集合，P26 组 | Added：`0F` 灯光命令与 P9 完全一致，复用 `accessory-006`；矩阵 `1x12`。 |
| `0F5C` | Razer Handheld Dock Chroma | `0F21` | 与 `0F59` 相同的 `0F:*` 集合，P9 组 | Added：`0F` 灯光命令与 P9 完全一致，复用 `accessory-006`；矩阵 `1x12`。 |
| `0F64` | Razer Soma Chroma | 无 | `10:03/04/05/08/83/85/88` | Blocked：只有 class `0x10`，没有 `0F` 矩阵命令；已由 `RazerChromaStandardService` 提供标准灯光页。 |
| `0F6A` | Razer Base Station V3 Chroma | 无 | `10:03/04/05/08/83/85/88` | Blocked：只有 class `0x10`，没有 `0F` 矩阵命令；已由 `RazerChromaStandardService` 提供标准灯光页。 |
| `0F6D` | Razer Soma Sensa HD Haptics（manifest `MarciT2H`） | 无（P9 命令表相同，但产品不是灯效外设） | `0F:02/03/04/08/10/13/80/82/84/88/90/93` | Blocked：JS profile 标注 `subCategory:"CHAIR"`，是 Sensa HD 触觉椅/坐垫，没有 Chroma family/quick effects；Chroma 命令是通用表，没有可证明的灯光矩阵。 |
| `0F2C` | Razer Chroma Wireless ARGB Controller（dongle） | `0F2E` | `10:03/04/05/08/131/133/136` | Blocked：走标准 `0x10` 91 字节 HID report，不是 `0F1F` 的 320 字节 `SET_FEATURE` 通道；按用户要求不加入 accessories。 |
| `0F2E` | Razer Chroma Wireless ARGB Controller | `0F2C` | `0F:02/03/04/08/10/13/80/82/84/88/90/93`，另有 `10:*` 声明 | Blocked：实际帧路径是 91 字节 HID report 的 `0F:03 Set Chroma Frame`，3 个 port × 20 LEDs；`sendFeatureReportInBatch` 在本设备上未启用。它不是 `0F1F` 的 320 字节 `DeviceIoControl(0x88883010)` 帧，不能复用 `OpenRazerSpecialLightingService` 的 320 字节实现。 |

## 证据与边界

- `0F52`/`0F59`/`0F5A`/`0F5C` 的 `transactions`、`builderArgumentPrefixes`、`defaultStorage`、`defaultLedZone`、`commandSequences`、`ambiguousTransactions` 均从 `0F21` 整段复制，仅改 PID/名称/矩阵；`capabilitySet` 复用 `accessory-006`。
- `0F52` 的 1x12 有 OpenRazer PR #2829 的直接依据（`RazerThunderbolt5DockChroma` 继承 `RazerThunderbolt4DockChroma`，后者 `MATRIX_DIMS = [1, 12]`）。`0F59`/`0F5A`/`0F5C` 的静态 JS 只暴露 `Set Chroma Frame` 的 `numRow`/`numCol` 运行时参数，没有写死 LED 行列；这里沿用同一 P9 `0F` 帧族和 `0F21` 的 1x12 布局，不把没有依据的尺寸写成其他值。
- `0F36`/`0F51`/`0F6A`/`0F64` 的 class `0x10` 标准 Chroma 由 `RazerChromaStandardService` 处理；把这些 PID 加进 `accessories.json` 会让 `OpenRazerDeviceCatalog` 抢先接管，随后用 `0x0F`/`0x03` 命令发到只认 `0x10` 的设备，因此保持 Blocked。
- `0F2C`/`0F2E` 的 ARGB 帧与 `0F1F` 的 320 字节过滤驱动帧不同；本次只记录，不改代码。
