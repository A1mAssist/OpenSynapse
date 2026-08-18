# OpenSynapse WinUI 3 前端增量交接

更新时间：2026-08-18

基线文档：`docs/frontend-handoff.md`

本文只描述基线交接之后新增或改变的前端契约。后端强类型接口和安全门禁已经完成，前端可以调整布局与视觉，但不要复制协议逻辑、绕过 `Can...` 属性，或增加本文未列出的写入口。

## 1. 本轮增量

1. Blade 生产灯效由 6 个增加到 13 个。
2. Blade 新增 6 组只读平台状态。
3. Viper 新增低电量阈值只读展示。
4. Viper 新增固定 Profile 1 的 16 条 Normal/HyperShift 板载映射读取和受限编辑。
5. Viper 当前设备轮询率上限明确为 1000 Hz。
6. THX、EQ、音量均衡和语音清晰度明确排除，不做前端入口。

## 2. Blade 灯效增量

现有绑定保持不变：

```text
BladeLightingModeOptions
BladeLightingModeIndex
BladeLightingColor
BladeLightingSecondColor
BladeWaveDirectionOptions
BladeWaveDirectionIndex
CanSetBladeLighting
```

生产选项及索引顺序：

| 索引 | 显示名称 | 后端模式 | 附加输入 |
|---:|---|---|---|
| 0 | 关闭 | `Off` | 无 |
| 1 | 静态 | `Static` | 颜色 |
| 2 | 呼吸 | `Breathing` | 颜色 |
| 3 | 光谱循环 | `Spectrum` | 无 |
| 4 | 波浪 | `Wave` | 方向 |
| 5 | 火焰 | `Fire` | 无 |
| 6 | 响应 | `Reactive` | 颜色 |
| 7 | 涟漪 | `Ripple` | 颜色 |
| 8 | 音频律动 | `AudioMeter` | 无 |
| 9 | 环境感知 | `Ambient` | 无 |
| 10 | 色轮 | `Wheel` | 方向 |
| 11 | 星光 | `Starlight` | 颜色 |
| 12 | 潮汐 | `Tidal` | 两个颜色 |

提交仍只调用：

```csharp
await viewModel.ApplySelectedBladeLightingEffectAsync(cancellationToken);
```

前端处理规则：

- Static、Breathing、Reactive、Ripple、Starlight、Tidal 显示主颜色控件。
- 仅 Tidal 显示 `BladeLightingSecondColor`。
- Wave 和 Wheel 显示方向控件。
- Audio Meter 使用后端 WASAPI loopback；前端不选择音频设备、不计算音量。
- Ambient 使用后端 Windows Graphics Capture；前端不截屏、不采样颜色。
- 配置切换后 ViewModel 会恢复模式、颜色和方向，前端不要维护第二份灯效状态。
- Starlight 和 Tidal 已完成当前设备视觉/恢复验证并进入生产列表；Tidal 双颜色编辑器已接入。

## 3. Blade 平台状态

以下属性全部只读：

| ViewModel 属性 | 当前显示内容 | 未知值 |
|---|---|---|
| `BladeGameModeText` | 模式、屏蔽位、Lifted 原始值 | `--` |
| `BladeStartupAnimationText` | 已启用/已禁用 | `--` |
| `BladeNativeDisplayModeText` | UHD/FHD | `--` |
| `BladeSkuHardwareText` | 原始 SKU、DDS、MiniLED、Battery flags | `--` |
| `BladeLocalDimmingText` | MiniLED 显示已启用/已禁用；非 MiniLED 显示不适用 | `--` |
| `BladeOneTimeFullChargeText` | 已启用/已禁用 | `--` |

建议继续放在一个不带操作按钮的“平台状态”区域。原始字段使用等宽字体并允许换行，不要把 Raw 值自行翻译成新的业务状态。

本轮不允许添加以下写入口：

- Gaming Mode
- 启动动画
- Native Display Mode
- Local Dimming
- 一次性充满
- SKU flags

这些字段已有读取不代表写入通过了可逆真机验证。

## 4. Viper 低电量阈值

绑定：

```text
ViperLowBatteryThresholdText
```

仅展示后端格式化结果并标记“只读”。不要添加 Slider、NumberBox 或应用按钮；用户已经决定不处理该阈值。

## 5. Viper 板载映射

### 页面级绑定

```text
ViperButtonMappingsText
ViperButtonAssignments
CanReadViperButtonMappings
CanSetViperButtonMappings
```

读取入口：

```csharp
await viewModel.ReadViperButtonMappingsAsync(cancellationToken);
```

只有固定 Profile 1 的 8 个 firmware button ID 和 Normal/HyperShift 两层共 16 条记录全部读取成功，`CanSetViperButtonMappings` 才会为 `true`。

### 行级绑定

`ViperButtonAssignments` 的元素类型为 `ViperButtonAssignmentRowViewModel`：

```text
ButtonText
LayerText
CurrentActionText
ActionOptions
SelectedActionIndex
CanApply
```

提交入口：

```csharp
await viewModel.ApplyViperButtonMappingAsync(row, cancellationToken);
```

当前生产 UI 只生成以下目标：

```text
关闭
鼠标键 1
鼠标键 2
鼠标键 3
鼠标键 4
鼠标键 5
鼠标键 9
鼠标键 10
```

前端处理规则：

- 先显示“读取”按钮；不要在设备刷新时自动写入任何映射。
- 未完整读取 16 条时，整个编辑区保持禁用。
- 每行显示 firmware button ID 和 Normal/HyperShift 层；不要猜测未经确认的物理按键名称。
- Button ID `96` 可显示为“DPI 键”，其他 ID 使用 ViewModel 提供的 `ButtonText`。
- 当前动作不在受支持列表时，`CurrentActionText` 仍会展示，`SelectedActionIndex` 为 `-1`；不要替换为默认动作。
- 只有用户选择了不同且受支持的动作时，`CanApply` 才为 `true`。
- 提交成功后以后端读回值刷新本行；失败时 ViewModel 会恢复上次确认的选择。
- 不提供批量保存、拖拽换位或复制层功能；后端当前事务是一行一次。
- 后端也已允许并验证 `KeyboardKey` 和 `DoubleClick`；增加对应参数编辑器时继续调用同一个行级 setter，不复制协议编码。

以下映射族仍由后端在 transport 前拒绝，前端不得生成选项：

```text
DPI function
MediaKey
HyperShift activator
KeyboardTurbo
MouseTurbo
Macro
Profile
Lighting
Power
Controller
RazerKey
Windows shortcuts
```

## 6. Viper 轮询率边界

当前设备只显示：

```text
125 Hz / 500 Hz / 1000 Hz
```

不要显示 2000/4000/8000 Hz，也不要根据逆向档案中的 HyperPolling 报文动态生成选项。高轮询协议不在当前 `1532:00B8` 产品后端和 UI 范围内。

## 7. 无需前端的后台增量

M5 麦克风静音指示灯同步由应用生命周期自动启动和停止，并与软件灯效协调 Normal 模式所有权。前端不需要开关、状态卡片或轮询；不要新增 F3 扬声器指示灯入口。

## 8. 明确排除

以下功能不做，前端不要预留禁用卡片、占位标签或“即将推出”入口：

- THX Spatial Audio
- EQ
- 音量均衡
- 语音清晰度
- 2000/4000/8000 Hz HyperPolling

逆向资料仅作档案，不是待接入 API。

## 9. 增量验收

- [ ] Blade 灯效下拉框恰好显示 10 个生产模式，索引顺序与 ViewModel 一致。
- [ ] 颜色和方向控件只在对应模式出现，不改变页面尺寸或遮挡其他控件。
- [ ] 6 组 Blade 平台状态均为只读，`--` 状态布局稳定。
- [ ] Viper 低电量阈值只有只读展示。
- [ ] Viper 映射读取失败或不足 16 条时不能编辑。
- [ ] 未知映射动作保持可见，不被前端替换或误提交。
- [ ] 每行映射提交遵守 `CanApply`，失败后恢复选择。
- [ ] Viper 轮询率只显示 125/500/1000 Hz。
- [ ] THX/EQ/音量均衡/语音清晰度没有任何 UI 入口。
- [ ] 设备断开或全局 `IsBusy` 时新增操作立即禁用。
