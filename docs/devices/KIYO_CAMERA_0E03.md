# Razer Kiyo 相机页（0E03 / 0E05 / 0E06 / 0E08 / 0E0A / 0E0B / 0E0C）

## 证据来源

- 每个 PID 的 middleware `manifest.json` 声明对应 native DLL：`RzNative_0E03/0E05/0E06/0E08.dll`，0E0A/B/C 使用共享 `RzNative_GenericCamera.dll`。
- DLL 从 `app-assets.razer.com` 取得，仅用于静态 ABI 分析，不随 OpenSynapse 分发。
- 接口签名来自 `rzCamKiyo` / `rzCamKiyoPro` / `rzCamKiyoProUltra` / `rzCamKimberly` 的 `ConfigureFFI` 表；所有支持的 DLL 都导出基础 getter/setter 和 `DisplayCameraPropertiesDialog`。
- getter 的 JSON 由反汇编确认：`{"value":"50", "isAuto":"false"}`，`value` 和 `isAuto` 都是字符串；结尾有空格和 CRLF。
- container id 是 Windows `DEVPKEY_Device_ContainerId`，按 `{GUID}` 大写形式传给 DLL；枚举使用 SetupAPI，匹配硬件 ID 里的 `VID_1532&PID_0E0X`。

## 页面行为

- 显示相机友好名、VID/PID、亮度、对比度、饱和度、白平衡、固件和序列号。
- 白平衡的 Auto/Manual 通过 `SetWhiteBalance(value, isAuto)` 写入，并立即读回；不匹配时回滚原值。
- 亮度/对比度/饱和度的数值区间没有 bundle 或 DLL 证据，因此不写死滑杆，写入使用 `DisplayCameraPropertiesDialog` 打开雷云原生相机设置页。
- 相机存在但 DLL 缺失或初始化失败时保留设备入口并显示错误；没有相机时不显示入口，不影响其他设备刷新。
- 找不到某个 DLL 的导出时只隐藏对应字段；不会把其他型号的 DLL 当作回退。

## 边界

- 以上是 bundle + DLL 静态对齐和单元测试覆盖；没有相机实机，未做 `DeviceInitialize`、真实读写或设置对话框实测。
- native DLL 是 Razer 签名的原生组件，只能使用用户本机已安装的副本，不打包、不重新分发。
