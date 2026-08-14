# 第二批设备控制项设计

日期：2026-08-13

## 目标

在现有“设备”页面接出五项已有安全后端能力：Blade CPU Boost、GPU Boost、Max Fan、A 面 Logo，以及 Viper DPI 档位。保持 Blade/Viper 设备切换层级，不新增顶级导航、轮询、协议调用或控制服务。

## Blade 自定义性能

在现有 Blade 性能摘要下增加“自定义性能”设置带：

- CPU Boost 使用固定选项：低、中、高、Boost、降压。
- GPU Boost 使用固定选项：低、中、高。
- Max Fan 使用原生 `ToggleSwitch`。
- 三项均显示硬件实际读回状态。
- CPU/GPU Boost 和 Max Fan 只有在当前可信读回的性能模式为 `Custom`，且对应功能本身成功读回后才允许提交。
- 性能模式离开 `Custom` 后立即禁用三项提交；切入 `Custom` 后必须有对应功能的可信读回，不能生成默认值。

每项使用独立应用按钮，避免一次提交多个硬件设置导致部分成功状态不明确。CPU/GPU Boost 仅在当前会话生效。Max Fan 成功后把后端返回的实际值写入当前全局 Profile 的现有 `MaxFanMode` 字段并沿用当前保存流程。

## Blade Logo

在 Blade 灯光区域增加独立“机身灯光”设置单元：

- 只允许“关闭”和“常亮”两个写入目标。
- 若硬件当前读回为 `Breathing`，摘要如实显示“呼吸”，但选择器不提供该目标。
- Logo 仅在本次会话生效，不修改 Profile 模型。
- 写入成功后以 setter 返回的实际读回状态更新摘要与确认值。

## Viper DPI 档位

在 Viper 当前 DPI 卡片下方增加完整档位编辑器：

- 档位数量为 1 到 5。
- 档位号固定连续并从 1 开始，不允许用户编辑编号。
- 活动档位范围始终为 1 到当前档位数量。
- 每档 X/Y 范围为 100 到 30000，步进为 50。
- 一个“应用档位”按钮把当前完整表转换为一个 `ViperDpiStagesTelemetry` 并调用一次 `SetViperDpiStagesAsync`。
- 禁止逐行调用当前 DPI setter。
- 成功后用后端返回的完整表重建编辑器与最后确认快照。
- DPI 档位仅在当前会话生效，不修改 Profile 模型。

档位数量减少时只裁掉末尾档位；增加时复制最后一档作为新行，并使用连续编号。活动档位超出新数量时收敛到最后一档。该行为只修改编辑状态，不会在点击“应用档位”前写入硬件。

## ViewModel 与数据流

- `MainViewModel` 暴露固定显示选项、编辑值、最后确认值、只读摘要、`CanSet...` 和明确的 `Apply...Async` 入口。
- XAML 不解析协议值，不构造 `ViperDpiStagesTelemetry`，不直接访问 Profile JSON。
- 遥测刷新成功时同步摘要、编辑状态和最后确认状态。
- 遥测字段为 `null` 时清空对应状态并禁用提交；设备刷新和断连时同样清空。
- 所有 setter 继续使用当前 `_deviceDescriptors`、窗口生命周期取消令牌和现有后端写入门禁。
- 性能模式应用成功后重新计算 Custom 依赖门禁；只有此前已可信读回的 Boost/Max Fan 值可继续编辑。

## 错误与忙碌状态

- 所有操作复用 `RunDeviceOperationAsync`、全局 `IsBusy` 和设备页持久 `InfoBar`。
- 单项写入失败时恢复该项最后确认值。
- DPI 档位写入失败时恢复档位数量、活动档位和所有 X/Y 的完整确认快照。
- 后端返回的“原值已恢复”“原值恢复失败”或读回不一致信息必须原样保留在错误消息中。
- 全局忙碌时禁用所有第二批提交，控件尺寸保持稳定。

## Profile 边界

- Max Fan：复用既有 `BladeProfileSettings.MaxFanMode`、clone、resolver 和 `VerifiedProfileApplier` 链路。
- CPU Boost、GPU Boost、Logo、Viper DPI 档位：仅当前会话，不新增 Profile 字段，不承诺重启、切 Profile 或电源变化后自动恢复。
- 本次不修改 Profile 模型、resolver 或自动应用器。

## 界面布局

- Blade 自定义性能使用一个横向三列设置带；窄宽度下允许内容换行，字段名称和当前值不截断。
- Logo 是 Blade 灯光区域中的独立设置单元，不作为顶级导航或设备切换项。
- Viper DPI 档位使用紧凑表格，不使用卡片套卡片；行高和列宽保持稳定。
- 应用按钮使用现有图标按钮样式，并提供 Tooltip 与 `AutomationProperties.Name`。
- 颜色、字体、间距继续使用现有应用资源，兼容浅色、深色和 High Contrast。

## 范围外

不开放固定风扇转速、风扇曲线、Logo Breathing 写入、Viper 当前 DPI 的逐档替代写入、低电量阈值、电池类型、按键映射或其它未验证能力。

## 验证

- 为新增的纯状态逻辑留下最小可运行测试，覆盖 DPI 数量变化、连续编号、活动档位收敛和 50 步进校验。
- 复用现有 Core 后端测试验证 Custom 门禁、Logo 目标限制、整表 DPI 提交和硬件失败恢复。
- 运行全部非硬件测试，不设置 `OPENSYNAPSE_HARDWARE_TEST`。
- x64 App 构建要求 0 warning、0 error。
- 启动应用检查 Blade 和 Viper 页面在正常及窄窗口下无文字截断、控件重叠或卡片嵌套。
- 未经设备所有者额外授权，不点击会改变真实硬件状态的应用按钮。

