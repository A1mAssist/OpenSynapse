# Blade 第一批控制项设计

日期：2026-08-13

## 目标

在现有“设备 > Blade”页面接出三个已有后端能力：Blade 性能模式、Blade 充电上限和内置屏刷新率。保持当前设备页层级，不新增页面、控制服务或轮询。

## 界面

- 性能区：使用原生 `ComboBox` 选择平衡、性能、自定义、静音、电池或 HyperBoost，并通过独立“应用”按钮提交。
- 电池区：使用原生 `ComboBox` 选择 50%、55%、60%、65%、70%、75%、80% 或“关闭限制（100%）”，并通过独立“应用”按钮提交。
- 显示区：使用原生 `ComboBox` 绑定 `InternalDisplayRefreshRates`，并通过独立“应用”按钮提交。
- 保留当前只读摘要；控件使用现有资源和样式，适应窄窗口且不嵌套卡片。

## 状态与数据流

- `MainViewModel` 为性能模式和充电上限增加编辑值、最后确认值、`CanSet...` 和 `Apply...Async`。
- 合法选项由 ViewModel 暴露，XAML 不复制协议常量或解析遥测文本。
- 每次成功读取遥测时同步编辑值与最后确认值。
- 应用操作调用现有 `IRazerDeviceTelemetryReader` 强类型 setter；界面展示并持久化 setter 返回的实际读回值，不直接信任请求值。
- 刷新、设备断开或遥测不可用时清空编辑状态并禁用对应提交。
- 刷新率继续复用现有 `InternalDisplayRefreshRates`、`InternalDisplayRefreshRateHertz`、`CanSetInternalDisplayRefreshRate` 和应用方法，只补 XAML 与点击路由。

## 错误处理

- 三项操作复用 `RunDeviceOperationAsync`、全局 `IsBusy` 和设备页持久 `InfoBar`。
- 写入失败后恢复到最后确认的选择值。
- 不吞掉后端关于读回不一致或原值恢复失败的错误信息。

## Profile

- 性能模式和充电上限成功后更新当前 Profile 中已有字段，并沿用当前保存流程。
- 内置屏刷新率沿用现有显示控制与 Profile 流程。
- 本次不改变 Profile 模型、解析器或自动应用器。

## 范围外

CPU/GPU Boost、Max Fan、Logo 和 Viper DPI 档位不在本批范围内；不开放任何未验证的风扇、灯效或显示控制。

## 验证

- 添加或调整最小 ViewModel 测试，覆盖成功读回、失败回滚和禁用门禁。
- 运行非硬件测试，不设置 `OPENSYNAPSE_HARDWARE_TEST`。
- x64 构建要求 0 warning、0 error。
- 启动应用检查设备页布局、窄窗口文字、按钮门禁和错误可见性。

