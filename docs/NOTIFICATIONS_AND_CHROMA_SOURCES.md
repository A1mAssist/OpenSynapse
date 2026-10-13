# Peripheral battery notifications and Chroma sources

These controls are available in source/test builds after v1.5.1. They are not part of the v1.5.1 release packages.

## Peripheral low battery notifications

Settings has a **Peripheral low battery notifications** switch, independent of mode-change notifications. It is enabled by default.

- A fresh, successful battery reading at or below 20% sends a Windows notification with the device name and percentage. A later reading at or below 10% sends a second notification.
- The first reading at or below 10% sends only the critical notification, not both levels.
- Notifications are tracked independently per device and saved across application restarts. Disconnecting or reconnecting does not reset them. A valid reading of at least 25% starts a new episode.
- Charging readings suppress alerts. Unknown charging state on a device without a working charging query does not prevent a valid battery reading from notifying.
- Missing, failed, out-of-range, and zero readings do not notify or reset an episode. Zero is ambiguous on receivers whose peripheral is asleep or switched off.

The application reuses Viper telemetry and successful OpenRazer basic-state reads for every device category that exposes battery data; it does not add discovery scans, a battery polling timer, or wake-up commands. **Alerts arrive only when existing device reads happen.** In particular, unselected OpenRazer devices are not queried solely for notifications, and a hidden application does not continuously monitor battery drain. Opening a device page or requesting a refresh obtains a new reading through the existing path.

Windows notification settings, Do not disturb, and notification permissions still apply. These software notifications do not change the device firmware's low-battery threshold.

## Chroma sources

With Chroma REST enabled, Settings lists sources that have connected during the current host run and saved blocked sources. It identifies the current lighting owner, connected sources waiting for control, and disconnected sources.

- New sources are allowed by default. Switch off a source to block it, or switch it on to allow it again.
- Matching ignores capitalization and leading/trailing spaces. The blocked list survives restarts and disabling/re-enabling Chroma REST.
- Blocking the active source releases its ownership immediately after any in-flight frame finishes. Cleanup and restoration are serialized with frame output, so an old source cannot write over the restored lighting. Restoration follows **Restore lighting after external control**.
- A blocked source may connect and heartbeat, but lighting requests return Chroma error `5` (access denied) without reaching any lighting target. Allowing it again lets its next valid frame compete for ownership normally.
- The existing global Chroma switch and individual device override switches continue to apply. No native Chroma DLL/SDK interface is added.

Source names are self-reported client titles. This is a lighting preference, not authentication or a security boundary. Remembered allowed sources last for the current host run; blocked preferences are persistent.

---

# 外设低电量通知与 Chroma 来源管理

这些控件位于 v1.5.1 之后的源码／测试构建中，不包含在已发布的 v1.5.1 安装包里。

## 外设低电量通知

设置页新增独立的“外设低电量通知”开关，默认开启，不受模式切换通知开关影响。

- 成功读到电量降至 20% 时发送带设备名称和电量的 Windows 通知，进一步降至 10% 时再提醒一次。首次读数已经低于 10% 时只发送一次严重低电量提醒。
- 每台设备独立记录提醒状态，重启、断开与重连都不会重复提醒。成功读到电量恢复至至少 25% 后，才开始下一轮提醒。
- 充电时不提醒；没有可用充电查询的设备，仍可根据有效电量提醒。读取失败、未知、越界以及含义不明确的 0% 不会触发或重置提醒。

复用现有 Viper 遥测和 OpenRazer 状态读取，覆盖具有电量读取能力的设备类别，不增加扫描、电量定时轮询或主动唤醒命令。**提醒仅在现有链路取得新读数时发生**；不会专门查询未选中的 OpenRazer 设备，也不会在隐藏后台持续监测电量变化。打开设备页面或请求刷新会沿现有链路取得新读数。

Windows 通知权限和“请勿打扰”设置仍然有效；此功能与设备固件里的低电量阈值是独立的。

## Chroma 来源

开启 Chroma REST 后，设置页列出本轮运行连接过的来源及已保存的拒绝来源，并标明正在接管、已连接等待接管或未连接状态。

新来源默认允许，关闭对应开关即可拒绝，再打开即可允许。名称匹配忽略大小写和首尾空格，拒绝名单会保存，重启及关闭／重开 Chroma 后仍有效。已允许来源的历史列表仅保留至本轮 REST 服务结束。

拒绝当前来源时，会在正在发送的帧结束后立即释放接管；恢复灯效与帧输出共用串行锁，避免旧帧覆盖恢复结果。是否恢复用户灯效仍遵循“外部接管结束后恢复灯效”设置。拒绝来源仍可连接及发送心跳，但灯光请求返回 `5`，不会输出到设备。重新允许后，在下一帧正常申请接管。

全局 Chroma 开关和设备独立的接管开关仍然有效，本次没有增加原生 SDK／DLL 适配。来源名称由客户端自报，这是灯光偏好管理，不是经过验证的身份或安全访问控制。
