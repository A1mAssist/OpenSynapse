# Changelog

All notable user-visible changes to OpenSynapse are documented here.

## [Unreleased]

### 新增 / Added

- 新增 Razer BlackShark V3 Pro（`1532:0576` 有线、`1532:0577` 2.4 GHz）支持：电量、充电状态、侧音、THX Spatial Audio、主动降噪和游戏/语音平衡。
- Added Razer BlackShark V3 Pro support (`1532:0576` wired, `1532:0577` 2.4 GHz): battery, charging state, sidetone, THX Spatial Audio, ANC, and game/chat balance.

## [1.5.3] - 2026-10-19

### 新增 / Added

- 新增 Razer Blade 16 (2023) `1532:029F` 支持：键盘亮度、固件渲染灯效（关闭、呼吸、光谱、波动）、性能模式、自定义 CPU/GPU 档位（含 CPU Boost、Max Fan）、2000–5000 RPM 固定风扇目标、充电上限、一次充满和游戏模式。
- Added support for the Razer Blade 16 (2023) `1532:029F`: keyboard brightness, firmware-rendered effects (Off, Breathing, Spectrum, Wave), performance modes, Custom CPU/GPU levels with CPU Boost and Max Fan, fixed fan targets from 2000 to 5000 RPM, charge limit, one-time full charge, and Gaming mode.
- 新增受支持机型的 THX Spatial Audio 控制：扬声器/耳机选择、空间音频、EQ 预设与频段、人声清晰度和扬声器音量标准化。厂商原生组件不随包分发，缺失时应用会提供来自 Razer 官方服务器的可校验下载。
- Added THX Spatial Audio controls for supported Blade 16 and Blade 14 models: speaker/headphone selection, spatial audio, EQ presets and bands, voice clarity, and speaker volume normalization. The vendor native component is not bundled; if missing, the app offers a verified download from Razer's official server.
- 新增面板模式和局部调光卡片，仅在设备确实具备对应能力时显示。
- Added panel mode and local dimming cards, hidden when the device does not expose the corresponding capability.

### 修复 / Fixed

- 修复 Blade 16 (2023) 键盘灯效报文：写入时误用了亮度查询命令，且缺少存储与区域选择器，导致每次亮度写入都失败。
- Fixed the Blade 16 (2023) keyboard lighting reports, which used the brightness query command for writes and omitted the store and region selector, so every brightness write failed.
- 修复灯效选择器在 Blade 16 (2023) 上选错效果：现在按该机型实际渲染的效果列表解析所选索引。
- Fixed the lighting effect selector picking the wrong effect on the Blade 16 (2023): the selected index is now resolved against the effect list that model actually renders.
- 修复 Blade 16 (2023) 的灯效开关、亮度和已保存配置无效：它们此前走了该代机型不存在的逐键矩阵灯效通道。息屏与唤醒现在同样会恢复其灯效。
- Fixed the Blade 16 (2023) lighting switch, brightness, and saved profiles having no effect, because they were routed through the per-key matrix lighting path that generation does not have. Screen-off and wake now restore its lighting as well.
- 修复 Blade 16 (2023) 已保存的灯效配置会写入配置文件读取器拒绝的效果键，导致下次启动失效。
- Saved lighting profiles on the Blade 16 (2023) no longer store an effect key that the profile reader rejects on the next launch.
- .chroma 导入按钮和波动方向选择器在 Blade 16 (2023) 上不再显示：该机型既不能播放 .chroma 文件，也不支持改变波动方向。
- The .chroma import button and the wave direction selector are now hidden on the Blade 16 (2023), which can neither play a .chroma file nor change the wave direction.
- 设备能力改为按精确机型准入，不再以读取成功与否判断。Blade 16 (2025) `1532:02C6` 不再提供 Max Fan、CPU Boost 和降压档位。
- Blade capabilities are now admitted per exact model instead of per successful readback. Blade 16 (2025) `1532:02C6` no longer offers Max Fan, CPU Boost, or the undervolt level.
- 在 Max Fan 机型上退出自定义性能模式时，会先关闭 Max Fan 并确认，避免切换预设后风扇持续保持最高转速。
- Leaving Custom performance mode on a Max Fan model now turns Max Fan off and confirms it first, so a preset change cannot leave the fans pinned at maximum.
- 其他电源模式控制项开启时拒绝启用 Max Fan，因为该写入会替换整个共享控制字节。
- Enabling Max Fan is refused while other Power Mode Control settings are on, because the write replaces the whole shared control byte.
- 修复托盘菜单左右留白过宽、底部被裁切：菜单项间距已与 AskKey 对齐，窗口高度按实际内容计算。
- Fixed the tray menu being too wide on the sides and clipped at the bottom: item spacing now matches AskKey and the window height follows the real content.
- 修复托盘菜单右侧留白过多：宽度改为按当前语言下最宽的一条菜单文案计算，中英文各自贴合。
- Fixed the tray menu leaving excess space on the right: the width now follows the widest localized label, so Chinese and English both sit flush.
- 修复托盘菜单在指针离开任务栏时会被关闭；它现在渲染在不抢焦点的独立窗口中，仅在外部点击、Esc、选择菜单项或退出应用时关闭。
- Fixed the tray menu closing when the pointer left the taskbar. It renders in its own window that never takes focus and closes only on an outside click, Esc, a menu selection, or app exit.

### 改进 / Improved

- THX 控制项移入 Blade 设备页的音频卡片，改用可复用的弹出窗口，不再占用独立导航页。
- THX controls now live behind an Audio card in the Blade device view and open in a reusable popout window instead of occupying a separate navigation page.
- 游戏模式、开机动画和一次充满拆分为三张独立卡片；原始 SKU 标志不再显示在设备 UI 中，仅用于能力检测。
- Blade game mode, startup animation, and one-time full charge are shown as three separate cards. Raw SKU flags are no longer shown in the device UI; the value is used only for capability detection.
- 新增规则：发版说明与 CHANGELOG 条目必须同时提供中文和英文。
- Added the rule that release notes and changelog entries must be written in both Chinese and English.

## [1.5.2] - 2026-10-13

### Fixed

- Fixed device discovery stopping during startup when validating the saved Blade logo mode, restoring OpenRazer device cards and their controls.

## [1.5.1] - 2026-10-09

### Fixed

- Blade keyboard brightness now responds while dragging the slider, including pointer events handled by the slider's internal thumb.
- Pending brightness changes are merged into the latest requested value, and older readback results no longer overwrite newer slider settings.
- Multi-color lighting presets are hidden for single-color effects across Blade, OpenRazer, and Kraken. Compatible dual-color Starlight presets remain available.

## [1.5.0] - 2026-10-08

### Added

- Added built-in color presets and custom three-color palettes for compatible Blade and OpenRazer keyboard lighting effects.
- Blade Wave and Spectrum now offer firmware and software-frame rendering; the saved palette determines which path is restored. OpenRazer keyboards show the choice only where both paths are available for the selected lighting zone.

### Improved

- Added speed controls for supported lighting effects and descriptive labels for the four reactive speed levels.
- Aligned color, speed, direction, and preset controls across Blade and OpenRazer lighting pages. The Blade direction selector now uses the full settings-column width.
- Firmware rendering hides color controls that the device firmware cannot apply.

## [1.4.5] - 2026-10-07

### Fixed

- OpenRazer and Kraken query failures now participate in the global hardware error summary while remaining grouped by device in Diagnostics.
- Audio-reactive lighting now turns the keyboard fully off at the silence floor instead of leaving the leftmost column lit by residual smoothing noise.
- OpenRazer custom lighting validates the requested zone against the device's actual custom-frame endpoint.

### Improved

- OpenRazer Chroma frame output skips unchanged frames and clears stale device state when targets disappear.
- Device discovery is event-driven during normal operation, with refreshes triggered by startup, device changes, resume, power changes, and explicit requests.
- Audio meter attack/release smoothing and ripple fade transitions are less abrupt.
- Power-source wording is consistent across Chinese and English UI (`供电` / `AC power`).

## [1.4.4] - 2026-10-06

### Fixed

- Updates now preserve the user's desktop and Start-menu shortcut choices. Existing shortcuts are updated to the new installed version; deleted shortcuts are not recreated.

## [1.4.3] - 2026-10-06

### Fixed

- The introduction walkthrough now highlights the first complete device card, matching the current full-card click interaction.
- Updated the Chinese and English walkthrough copy to describe selecting a device card instead of switching between fixed device types.

## [1.4.2] - 2026-10-06

### Fixed

- Diagnostics now includes OpenRazer devices again, groups errors by device, and keeps the details collapsed until expanded.
- Device diagnostics now show VID/PID alongside the HID collection so devices using the same HID usage and feature length remain distinguishable.

## [1.4.1] - 2026-09-30

### Fixed

- Blade lighting now turns off in one controlled transaction when the display is turned off, without briefly falling back to the firmware's WASD/number-key pattern.
- Blade lighting restores the active effect after display wake while preserving the HID software-mode lifecycle.
- Screen-off uses Windows' native input wake path instead of an application-level wake loop, avoiding delayed or repeated wake behavior.

### Improved

- Removed the startup-launch toggle from the tray context menu.
- Release packaging remains self-contained so the published application can start without a separately installed .NET runtime.

## [1.4.0] - 2026-09-26

### Fixed

- Blade `R`, `T`, `B`, and `P` input now passes ordinary unmapped keys back through the verified Razer filter-driver path instead of synthetic Win32 input, fixing input methods and applications that rejected the previous injection path.
- Preserved press/release pairing across Fn and HyperShift layer changes and released any driver-submitted keys during shutdown to prevent stuck keys.

### Improved

- Added protocol, host, mapping-state, and shutdown regression coverage for the filter-driver keyboard pass-through path.

## [1.3.10] - 2026-09-21

### Fixed

- Blade native lighting keepalive now runs every five seconds, survives transient HID read failures, stops on unsupported commands, and no longer holds the lighting controller lock during HID I/O.

## [1.3.9] - 2026-09-20

### Fixed

- Firmware-native Blade lighting effects now remain active while the display is on by using a low-frequency read-only keepalive that stops on display-off, suspend, effect changes, and application shutdown.

## [1.3.8] - 2026-09-19

### Fixed

- Keyboard lighting and mute indicators now follow display availability and restore the active brightness and effect after wake.

### Improved

- Split the main ViewModel into focused Blade, Viper, telemetry, display, lighting, fan, OpenRazer, refresh, operations, and profile partials without changing the runtime contract.

## [1.3.7] - 2026-09-16

### Fixed

- Keyboard lighting now remains off while the console display is unavailable and restores the active profile when the display returns.
- Velopack restarts now resolve runtime files from the deployed application directory instead of inheriting an unrelated working directory.

### Improved

- Core Audio mute indicators now use Windows endpoint notifications instead of polling every 250 milliseconds.
- System telemetry runs only while the overview is visible and the display is available.
- Background device discovery is event-driven; foreground fallback scans avoid full telemetry and UI rebuilds when the device fingerprint is unchanged.
- Smart fan curves sample only their required CPU or GPU temperatures, with a throttled NVIDIA fallback when native temperature data is unavailable.
- Chroma REST session cleanup sleeps until the actual expiry deadline instead of polling every 100 milliseconds.
- Stable OpenRazer connections retain their existing UI state instead of being recreated during refreshes.

## [1.3.6] - 2026-09-13

### Fixed

- Blade Starlight now sends its complete color-mode, speed, and RGB payload, so single-color and dual-color modes no longer fall back to random colors.

### Improved

- Blade lighting colors now use a compact quick palette with recently used colors and an expandable custom picker.
- The expanded custom color picker can extend beyond the main window instead of being compressed into a scrolling panel.

## [1.3.5] - 2026-09-13

### Fixed

- Blade Audio Meter now renders the power key and M1-M5 as part of the shared rightmost column instead of pulsing them independently.

## [1.3.4] - 2026-09-12

### Added

- Blade native firmware lighting controls now expose Reactive speed and Starlight speed, color mode, and dual-color parameters.

### Fixed

- Native Blade lighting effects are explicitly turned off before application shutdown, including emergency exit cleanup.

### Improved

- Blade lighting parameters use a consistent two-column layout and show localized labels.

## [1.3.3] - 2026-08-30

### Fixed

- Keyboard lighting now turns off before Windows suspends instead of freezing on the last frame.
- Lighting resumes normally after wake, including rebuilding the HID channel and restoring the active profile.
- The app no longer remains unresponsive after a suspend/resume cycle.

### Improved

- Suspend and resume now use the native Windows power notification callback so the timing is aligned with device availability.

## [1.3.2] - 2026-08-30

### Added

- Keyboard lighting turns off automatically when Windows enters sleep or turns off the display.

### Improved

- Suspend is handled through the window power broadcast path before the HID device becomes unavailable.
- The software-lighting frame pump sends a complete black matrix frame before closing its persistent HID session.
- Duplicate suspend notifications are coalesced.
- Lighting runtime resources and the Software Mode lease are released even if suspend shutdown reports an error.

## [1.3.1] - 2026-08-29

### Fixed

- Plugged-in and battery keyboard-lighting and performance settings are edited independently.
- Switching lighting effects no longer carries unrelated effect parameters into the resolved profile.
- Editing the inactive power profile no longer overwrites the hardware-confirmed state.

## [1.3.0] - 2026-08-29

### Improved

- Device discovery backs off while the window is hidden and wakes immediately on device changes.
- Chroma REST status refresh pauses outside the visible Settings page.
- Software lighting adapts between 60 and 30 FPS when HID delivery falls behind.
- Disk capacity sampling is cached for 30 seconds, and NVIDIA detailed telemetry is sampled at most once every five seconds.
