# Razer Kiyo 系列灯环对齐

扫描日期：2026-10-28。包根目录：`artifacts/razer-mw-full-cdn/bundles/<PID>/`。
本地 bundle 只落了入口 chunk；缺失的动态 chunk 按 `main.*.js` 里的 chunk hash 从
`https://apps.razer.com/synapse/products/<decimal>/mw/<name>.<hash>.js` 补取后，
只分析 `useFeature("rzDeviceType", ...)` 实际选中的 camera module。

| PID | 名称（manifest en） | 灯环有无 | 关键 class:id | 结论 |
|---|---|---|---|---|
| `0E03` | Razer Kiyo | 有：12 颗白光 LED，硬件旋钮调光 | `—`：`rzCamKiyo` 中 `sendCommand=0`、`commandClass=0`、class `0x10` 出现 0 次 | Skipped：灯环不是软件 Chroma；官方手册要求旋转 Ring light adjustment knob，bundle 没有 HID 灯光命令 |
| `0E05` | Razer Kiyo Pro | 无 | `—`：`rzCamKiyoPro` 中 `sendCommand=0`、`commandClass=0`、class `0x10` 出现 0 次 | Skipped：没有灯环；只有 UVC/DLL 摄像头参数 |
| `0E06` | Razer Kiyo X | 无 | `—`：模块 `28836`（`rzCamKiyoX`）中 `sendCommand=0`、`commandClass=0`、class `0x10` 出现 0 次 | Skipped：没有灯环；只有 UVC/DLL 摄像头参数 |
| `0E08` | Razer Kiyo Pro Ultra | 无 | `—`：`rzCamKiyoProUltra` 中 `sendCommand=0`、`commandClass=0`、class `0x10` 出现 0 次 | Skipped：没有灯环；只有 UVC/DLL 摄像头参数 |
| `0E0A` | Razer Kiyo V2 Pro | 无 | `—`：`rzCamKimberly` 中 `sendCommand=0`、`commandClass=0`、class `0x10` 出现 0 次 | Skipped：官方功能/规格没有灯环；只有 UVC/DLL 摄像头参数 |
| `0E0B` | Razer Kiyo V2 | 无 | `—`：`rzCamKimberly` 中 `sendCommand=0`、`commandClass=0`、class `0x10` 出现 0 次 | Skipped：官方功能/规格没有灯环；只有 UVC/DLL 摄像头参数 |
| `0E0C` | Razer Kiyo V2 X | 无 | `—`：`rzCamKimberly` 中 `sendCommand=0`、`commandClass=0`、class `0x10` 出现 0 次 | Skipped：官方功能/规格没有灯环；只有 UVC/DLL 摄像头参数 |

## 证据与边界

- `0E03`：`main.59efe85af8015457a473.js` 使用 `useFeature("rzDeviceType","rzCamKiyo")`，且 runtimeData 为 `isChromaDevice:!1`、`lightingEffects:{canSyncQuickEffect:!1}`；实际 camera chunk `rzCamKiyo.f06094a76b2f0559cebc.js` 只有 `GetBrightness`、`GetContrast`、`GetSaturation`、`GetWhiteBalance`、`SetWhiteBalance`、`SetCameraFocus` 等 `callDLLApi` 摄像头动作。Razer Kiyo master guide 明确写的是 12 颗白光 LED + 旋钮调光，不经过 Chroma。
- `0E05`：`main.0c2a1be42f0414e22659.js` 选中 `rzCamKiyoPro`；`rzCamKiyoPro.9d1dcc13617e46bf46f9.js` 同样没有 `sendCommand`、`commandClass`、`Chroma` 或 `0x10`。产品页把它定位为自适应光传感器 + HDR + FOV，没有灯环。
- `0E06`：`main.48235a32a00742140113.js` 选中 `rzCamKiyoX`；该类是 `6120.1c9140951d7633a5df88.js` 内的 module `28836`，没有 `sendCommand`。Kiyo X 只有 UVC 摄像头参数。
- `0E08`：`main.d22061960b49b1064eba.js` 选中 `rzCamKiyoProUltra`；`rzCamKiyoProUltra.1daa9c2dbea30936c698.js` 没有 `sendCommand`。该型号没有灯环。
- `0E0A` / `0E0B` / `0E0C`：三个 `main.*.js` 都选中 `rzCamKimberly`；对应 chunk 分别为 `rzCamKimberly.5f8231d265c0b932b7a2.js`、`rzCamKimberly.70808b8fad24939bffe8.js`、`rzCamKimberly.38ef1ae91c20a35597d0.js`，均没有 `sendCommand`、`commandClass`、`Chroma` 或 `0x10`。Kiyo V2 Pro、Kiyo V2、Kiyo V2 X 的官方功能页都没有灯环。
- `out9/protocols.json` 会给这些 PID 带出共享 bundle 里的 `8:4`/`8:132` EQ 命令，或 `rzDevice25*` 的通用 Chroma 命令；那些来自未被 `rzDeviceType` 选中的音频/外设模块，不能当作这些摄像头的灯光能力。
- 摄像头非灯光参数（曝光、视野、白平衡、HDR 等）走 UVC/Media Foundation，本次明确不做；本次也没有向 `RazerChromaStandardService.Products` 添加任何 Kiyo PID。
