# 灯光工作室（Lighting Studio）

日期：2026-11-03
状态：v2 功能阶段已落地；v1 闭环仍可运行。侧边栏独立入口已接入。真机与 Chroma 客户端边界见第 5 节。
目标：在 OpenSynapse 内重建 Chroma Studio 的核心工作流，而不是复刻它的皮肤。

> v2 更新（2026-11-03）：设备画布、区域蒙版、14 个效果、配置档/组、撤销/重做、`.ChromaEffects` 导入导出、多设备矩阵输出、Ambient/Audio 采样、Chroma Generate 非 AI 路径均已接入。详细阶段与验证证据见 `docs/archive/LIGHTING_STUDIO_V2_PLAN.zh-CN.md` 第 11 节；以下 v1 章节保留为基线说明。

## 1. 已实现

| 层 | 内容 |
|---|---|
| 侧边栏 | `灯光工作室` 顶层入口，独立页面 `LightingStudioPage` |
| 文档模型 | `StudioDocument` + `StudioLayer`，schema v1，JSON 持久化 |
| 图层 | 新增/删除/上移/下移、启用、不透明度、混合模式 |
| 效果 | 纯色、呼吸、光谱、波浪、渐变、火焰 |
| 混合 | 正常、叠加、正片叠底、滤色 |
| 预览 | Blade 710/710-chroma 7×16 逻辑画布；671 `LedMatrixBlade` 直接 6×16；33 ms 刷新，播放/暂停 |
| 下发 | Blade：`StudioBladeFrameSourceV2` → `IBladeLightingController.ApplyExternalAsync`；HID：`StudioHidOutputRouter`；ARGB：`StudioArgbOutputRouter` |
| 恢复 | Blade/HID 停止时调用 `StopAsync()`；有可读写基线才恢复原效果，否则写 manifest 的 `quickEffectOnExit`（Spectrum）；ARGB 走 `Off -> Spectrum`，失败时显式报错 |
| 保存 | `%LOCALAPPDATA%\A1mAssist\OpenSynapse\lighting-studio\studio.json` |

## 2. 代码落位

```text
src/OpenSynapse.Core/Lighting/
  StudioRgb.cs          颜色与 HSV
  StudioLayer.cs        图层、效果/混合枚举、不可变快照
  StudioDocument.cs     文档、默认值、JSON codec
  StudioCompositor.cs   纯函数合成器（无设备/UI 依赖）
  StudioArgbDevice.cs   0F1F 的 6×80 布局、区域 26..31 映射与逐通道切片

src/OpenSynapse.Windows/Lighting/
  StudioBladeFrameSource.cs   7×16 逻辑帧 → 6×17 设备帧（710/710-chroma）
  StudioHidOutputRouter.cs    每设备独立矩阵帧、失败隔离与停止恢复
  StudioArgbOutputRouter.cs   六通道 1×N 条带、320 字节帧与重连去重

src/OpenSynapse.App/
  ViewModels/LightingStudioViewModel.cs
  Views/LightingStudioPage.xaml(.cs)
  Views/StudioValueConverters.cs
```

边界遵守 `docs/ARCHITECTURE.md`：Core 不碰设备/UI；Windows 只做帧适配；App 只做状态与页面。

## 3. 渲染语义

- 图层列表 **顶部优先**。合成器从列表末尾向前绘制，顶部图层覆盖底部。
- 每层先按效果生成一帧，再按 `Opacity` 与 `BlendMode` 混入目标帧。
- 时间驱动效果使用 `elapsed`，同一时间点的输出是确定的；火焰用坐标/帧号哈希，不依赖随机数。
- 预览和硬件共用同一个 `StudioCompositor` 和同一份 `StudioLayerSnapshot[]`，避免两套逻辑漂移。

## 4. 设备路径

工作室按设备类型复用三条输出路径，共用同一份图层快照，但各自持有帧源、时钟与去重状态：

```text
Blade:
  StudioDocument.Snapshot()
    → StudioCompositor.Render(7×16)
    → BladeLightingLayout.MapToDeviceFrame(6×17)
    → BladeMatrixFramePump

RazerHid matrix:
  StudioLayerRender[]（按 manifest 尺寸）
    → StudioHidOutputRouter
    → StudioDeviceFrameSource（每设备独立布局/时钟）
    → SetCustomFrameAsync

ARGB 0F1F:
  StudioLayerRender[]（6×80）
    → StudioArgbOutputRouter
    → StudioArgbDevice.SliceChannel（每通道 1×N）
    → RazerHidArgbFrame（320 字节）
```

`ApplyExternalAsync` 复用 Blade controller 的软件模式租约、帧泵、失败恢复与停止恢复，不新开 HID 通道。HID 与 ARGB 由同一个 33 ms 输出泵依次驱动，路由之间不共享帧缓冲；一个通道写失败只清空该通道的去重状态，下一帧只重写该通道。

### ARGB 设备选择与映射

`MainWindow.BuildStudioDevices` 把 `RazerHidSpecialLightingConnection`（PID `0x0F1F`，`Kind=Argb320`）作为独立设备加入工作室列表：六行、80 列，`StudioArgbDevice.Layout` 的每个 LED 同时给出矩阵坐标和通道坐标。官方 manifest `DeviceManifest_3871_0.json` 给出的证据是 `MatrixMaxRow=6`、`DeviceMaxCol=80`、480 颗灯，区域 id 为 `26..31`；`StudioArgbDevice.ChannelFromLegacyInstance` 只把这些旧实例 id 映射到 0..5 行，不生成额外协议字节。

### 停止恢复覆盖

- **ARGB 0F1F**：没有可读回效果/颜色；`StopAsync` 只停止帧泵并清空去重状态，控制器保留最后一帧。这是当前协议证据下的已知边界，不伪造恢复。
- **RazerHid 矩阵设备**：目录里只有 `get_macro_effect`/`get_logo_active` 合成的 classic effect 读取，且这些 zone 没有可写回的效果或颜色；`lighting` 能力快照中没有同时具备“可读回 + 可恢复”的 zone。因此真实设备在 `Stop` 时不会捕获基线，也不会写恢复帧；`RestoreFailed` 只在确实捕获到基线且恢复写失败时触发。
- 单元测试用合成 definition 覆盖了“可读回并恢复”的代码路径；`CatalogDevicesExposeNoRestorableReadZone` 锁定目录当前没有真实可恢复 zone，避免把测试能力当成硬件能力。

## 5. 当前边界（2026-11-04）

1. **多设备输出**：`StudioHidOutputRouter` 已按设备独立 source/mask/clock，并覆盖重连、失败隔离与 1024 LED 合成测试；ARGB 0F1F 已进入工作室设备列表与合并输出，六通道按 `26..31` 映射到 6×80，320 字节帧由 fake transport 覆盖。本机 PnP 枚举没有 0F1F（2026-11-04：仅 Blade 02C6、Viper 00B8、Dock 0F4E），固件接受度、时序和实际灯效仍未真机验证。
2. **Chroma 客户端互操作**：真实 `.ChromaEffects` 样本已完成导入/导出/派生结构校验，但未在 Chroma Studio 客户端中实际点击导入；样本受第三方授权限制，未提交进仓库。
3. **媒体与视频**：Media Foundation 会缓存最多 240 帧（250 ms 间隔），渲染按时间取帧；真实 64×64 H.264 解码测试通过，单帧 5 秒超时后回退 shell 缩略图，避免 32×32 文件挂死。5/30 分钟 H.264 soak 与 HEVC 缺失时的缩略图回退已验证；真实 HEVC 解码器本机缺失，未验收。
4. **Ambient 自定义区域**：X/Y/W/H 数值与 16:9 可视化选区双向同步，支持拖动移动、右下角缩放；真实鼠标拖拽手感尚未自动验证。
5. **画布几何**：XAML `Canvas` 使用 10000×10000 设计坐标、40px 网格、10/25/50/100/200/300/400 七档缩放；Fit 只框设备，设备拖动按 40px 吸附。UIA 不能注入 pointer drag，拖拽吸附的端到端路径仍只有数学单测和源码验证。
6. **性能边界**：XAML 画布 1000 LED 在 Debug 下 30/60fps 掉帧 2.7–2.9%；2000 LED 60fps 掉帧 39.2%，瓶颈是逐 LED XAML 元素合成，不是 `SetFrames`。Release 与真机写帧未测。
7. **AI 配色**：按约定不做推理；入口保留为禁用状态。
8. **平台适配器**：OpenRazer ARGB、Chroma SDK、LampArray/WDL 的独立帧接收器仍未接入。
9. **单区设备**：32 台无 `MatrixFrameWrite` 但有 `Static` zone 的 HID 设备以 1×1 代表色进入工作室；`SetLightingAsync(Static)` 路径只有 fake transport 测试，30fps 重写和真机固件接受度未验证。
10. **效果边界**：Ripple 在 6×17/7×23/9×26 及额外 4 个矩阵上已 0.00 MAE；Breathing/Reactive/Spectrum 在 3×3 oracle 上均 0.00 MAE；Tidal 3×3 为 0.00，大矩阵仅剩 0.04–0.49 的单像素 25 fps tick 边界。Starlight 已移植原生状态机，单星、多星和 RandomColor 路径在注入官方 seed 后均 49/49 帧逐值一致；Fire 已按 3 组官方 oracle（默认 3×3、6×17/seed40/rate80、9×26/seed25/rate120）逐像素对齐，容差 0。预览端故意不采用 20.8 天 wall-clock 桶，保证工程可复现。
11. **无输出设备**：10 台有矩阵但既无 `MatrixFrameWrite` 也无 Static zone 的 HID 仍可在画布预览，但设备卡会提示“无逐像素输出”；应用这些设备会被跳过。本机没有对应型号，提示未做真机截图。
12. **Blade 6×16 直出**：`LedMatrixBlade` 的 6×16 设备（023A/0234/024B/024C/024D/0253/0256/026D/026E/0270/0276/0279）已进入 Studio，按 manifest 直接渲染/写帧；`blade-671.json`（029F，Blade 16 2023）实际是 `FirmwareEffect`，不列入。6×16 只有 fake transport 和精确报文测试，本机无对应实机。
13. **热插拔**：Studio 页面可见时监听 HID/Blade/ARGB 设备集合变化，dispatcher 合并刷新，页面刷新走 single-flight（进行中再触发只补跑一次）；隐藏时只记 dirty，进入页面刷新一次。未做真实插拔 UI 验收。
14. **HID 键盘触发**：Raw Input 扫描码按 manifest `(kbd, scan code, extended)` 映射到 `StudioHidOutputRouter.HandleInput`；停止后输入被忽略；鼠标、`dkm`/0x04 私有报告未映射，避免猜键位。
15. **`.ChromaEffects` 字段**：profile device 的 `parentId/parentX/parentY/placementIndex/ledRef` 已建模并 round-trip；`parent*` 没有真实样本，`ledRef` 只保真，不按 OpenSynapse 当前选 LED 重写。
16. **跨布局导入**：设备替换时按官方规则重映射 LED：keyboard/system 用 `inputType`+`inputData`，其他设备用 `regionId`+`regionIndex`，匹配不到回退目标设备全部 LED；目标 layout 缺失时保留原 `ledRef`。选区/画笔也会按当前 layout 附带键位或区域身份，新画的区域导出时不是裸 `row`/`col`。真实样本只有 keyboard，region 规则用真实鼠标 manifest + 合成 layout 测试。

## 6. 验证

- App x64 Debug/Release：0 警告、0 错误；全量 3452/3452（2026-11-04）；结构预算通过。
- `.ChromaEffects`：真实样本导入/导出后再导入结构一致；按派生结构校验（非 Razer 官方 schema）检查。
- WinUI QA：侧边栏进入、区域创建与区域级独立效果、Generate 专用控件（偏移/缩放/镜像/图案/混合）、截图均通过。
- 多设备：路由单测覆盖不同尺寸、同尺寸、独立时钟、设备移除/重连、单设备失败、部分写入失败、ARGB 六通道切片、ARGB 重连与合并路由隔离；真实目录的恢复覆盖由 `CatalogDevicesExposeNoRestorableReadZone` 和 `CatalogDeviceWithoutReadbackSkipsRestore` 锁定。
- 实机：Blade 单设备 `应用到设备` / `停止` 命令已验证；停止优先写 manifest 的 exit effect（Spectrum），绿色帧仅兜底，未读取原灯效。ARGB 控制器真机仍未验证（本机枚举不到 0F1F）。

## 7. 约束

- 官方 DLL、TFL3 模型、设备 manifest SVG 只作为本机行为参考，不进入开源仓库或安装包。
- 新增 UI 文案必须走 `AppStrings` / `Localized.Uid`；`src/**/*.cs` 不允许出现 CJK 字面量（`BottomLayerLocalizationTests` 会拦）。
- 新增文件遵守 `.structure-budget.json`：单文件 ≤600 行、≤100 个 public 成员。
