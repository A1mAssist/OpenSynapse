# Changelog

All notable user-visible changes to OpenSynapse are documented here.

## [Unreleased]

### 新增 / Added

- 键盘页补上 class `0x0F` 的灯光通道，覆盖 16 款只有这套命令的键盘（Turret、BlackWidow X、Huntsman Essential / V3 X / V3 Pro 系列、BlackWidow V4 Low-profile、Reclusa、Tartarus V2 Pro 等）：可选关闭/静态/呼吸/光谱并写入 1–2 种颜色。这套通道没有亮度命令，亮度控件自动隐藏；设备能应答 `0F:130` 时读回校验，不能应答时按雷云同样的方式直接写入。
- The keyboard page now drives the class 0x0F lighting channel used by 16 keyboards that have no class 0x10 support (Turret, BlackWidow X, Huntsman Essential / V3 X / V3 Pro series, BlackWidow V4 Low-profile, Reclusa, Tartarus V2 Pro and others): off/static/breathing/spectrum with one or two colours. The channel has no brightness command, so the brightness controls hide themselves; writes are read back when the firmware answers 0F:130 and applied without a readback otherwise, matching the middleware.
- 补上两台边缘设备：Razer Marci（音频）接入音频页，Razer Wireless Control Pod 接入灯光页。至此中间件里能识别出命令的 119 款设备中，117 款已有对应页面；剩下 Philips Hue（走网络桥，不是 HID）和一款名字为空的 0F1B 原型设备。
- Two edge cases are now covered: Razer Marci (audio) on the audio page and the Razer Wireless Control Pod on the lighting page. Of the 119 devices with identifiable middleware commands, 117 now have a page; the remaining two are Philips Hue (a network bridge, not HID) and an unnamed 0F1B prototype.
- 键盘页再扩 12 款：BlackWidow X / V4 Pro 75% / V4 Low-profile（含 TKL）、Huntsman Essential / V3 X / V3 Pro Low-profile、Reclusa Mini 与 Reclusa X Mini、Pro Type Ergo、Tartarus V2 Pro、Turret Keyboard。能应答的功能（固件、序列号、游戏模式、轮询率、电量）自动出现，其余隐藏。
- 音频页再扩 5 款：Seiren V2 Pro / V2 X / V3 Mini 麦克风、Leviathan V2 Pro 音箱和 Kraken V4 X 耳机，并新增“麦克风”设备分类。
- The keyboard page now covers 12 more models: BlackWidow X, V4 Pro 75%, V4 Low-profile (including TKL), Huntsman Essential, V3 X and V3 Pro Low-profile, Reclusa Mini and Reclusa X Mini, Pro Type Ergo, Tartarus V2 Pro, and the Turret keyboard. Features the device answers (firmware, serial, game mode, polling rate, battery) appear automatically; the rest stay hidden.
- The audio page now covers 5 more devices: Seiren V2 Pro / V2 X / V3 Mini microphones, the Leviathan V2 Pro speaker, and the Kraken V4 X headset, plus a new microphone device category.
- 新增通用灯光页，覆盖 38 款此前没有页面的设备：Viper V4 / Basilisk V4 / DeathAdder V4 / Naga V3 等鼠标，Key Light、Aether 灯具系列，Chroma ARGB 控制器，Thunderbolt 5 Dock、Core X V2、显示器支架等配件。可选择效果（关闭/静态/呼吸/光谱）、亮度和颜色，写入后读回校验。与 OpenRazer 目录、音频页、键盘页和 320 字节 ARGB 通道自动去重。
- Added a shared lighting page for 38 devices that had no page: Viper V4 / Basilisk V4 / DeathAdder V4 / Naga V3 mice, the Key Light and Aether lamp family, Chroma ARGB controllers, and accessories such as Thunderbolt 5 Dock, Core X V2 and monitor stands. It offers effect (off/static/breathing/spectrum), brightness and colour, verified by reading the value back, and skips anything already owned by the OpenRazer catalogue, audio page, keyboard page or the 320-byte ARGB channel.
- 键盘页新增灯光卡：效果（关闭/静态/呼吸/光谱）、亮度与一到两种颜色，写入后读回校验。底层实现了雷云 class `0x10` 的 Chroma 命令（效果、亮度），这套命令同时被 51 款鼠标、灯具、ARGB 控制器和扩展坞使用，后续页面可以直接复用。
- Added a lighting card to the keyboard page: effect (off/static/breathing/spectrum), brightness, and one or two colors, each verified by reading the value back. The underlying class 0x10 Chroma commands (effect and brightness) are also used by 51 mice, lamps, ARGB controllers, and docks, so later pages can reuse them directly.
- 音频页扩展到另外 11 款使用同一套 protocol 25 音频命令的设备：Kraken BT（Sanrio / Kitty）、Kraken X USB、Kraken V3 X、Kraken Kitty V2（含 BT Quartz）以及 Leviathan V2、Nommo V2 / V2 Pro / V2 X。音箱现在按音箱分类显示，没有 ANC 的型号会自动隐藏降噪卡片。
- The audio page now covers 11 more devices that use the same protocol 25 audio commands: Kraken BT (Sanrio / Kitty), Kraken X USB, Kraken V3 X, Kraken Kitty V2 (including BT Quartz), plus Leviathan V2 and Nommo V2 / V2 Pro / V2 X. Speakers are labelled as speakers, and models without ANC hide the noise-cancelling card.
- 新增 11 款键盘（Huntsman V3 系列、Pro Type 系列、BlackWidow V4 75% Xbox 版等）的设备页：固件、序列号、电量与充电、游戏模式、轮询率（最高 8000 Hz）。每个项目先探测设备是否应答，不支持的命令自动隐藏，避免出现空白控件。
- Added a device page for 11 keyboards the OpenRazer catalogue does not cover (Huntsman V3 series, Pro Type series, BlackWidow V4 75% Xbox edition): firmware, serial number, battery and charging, game mode, and polling rate up to 8000 Hz. Each item probes the device first, and unsupported commands are hidden instead of leaving an empty control.
- 新增 18 款 protocol 25 游戏耳机（Nari、Kraken V3/V4、Barracuda、BlackShark V2 Pro、USB 音频控制器等）的音频页：固件、电量、充电状态、EQ 预设与 8/10 段调节、低音、麦克风静音与音量、音频输入源、AI 降噪和 ANC。每次写入都会先读当前值、写完读回校验，失败时自动恢复原值。
- Added an audio page for the 18 protocol 25 gaming headsets (Nari, Kraken V3/V4, Barracuda, BlackShark V2 Pro, USB audio controllers): firmware, battery, charging state, EQ presets and 8/10-band curves, bass, microphone mute and volume, audio source, AI noise suppression, and ANC. Every write reads the current value first, verifies the readback, and restores the previous value when the device does not accept the change.

- 新增“仅核显（禁用独显）”显示开关。机器级 MSI 安装会注册受保护的 GPU 服务，安装时请求一次管理员权限，之后切换不再弹 UAC；当前用户安装和 Portable 仍回退到逐次 UAC。机器级安装会迁移旧的当前用户版本，保留配置并更新开机启动项。用户配置已迁到独立的 `%LOCALAPPDATA%\A1mAssist\OpenSynapse`，不会再被 MSI 升级清理。
- Added an integrated-graphics-only display switch. The machine-wide MSI install registers a protected GPU service, asks for administrator permission once during installation, and then switches without UAC; per-user and portable installs still fall back to a UAC prompt per change. The machine-wide install migrates an older per-user install, preserving settings and updating its startup entry. User data now lives in `%LOCALAPPDATA%\A1mAssist\OpenSynapse`, outside the directories removed by MSI upgrades.

### 修复 / Fixed

- Kraken Tournament Edition / Ultimate / Kitty V2（`0520`/`0527`/`0560`）此前同时出现在音频页和 37 字节灯光页，现在只保留音频页一张卡片，灯光效果并入音频页；其余 Kraken（7.1、7.1 Chroma 等）仍保留独立灯光页。
- Kraken Tournament Edition, Ultimate and Kitty V2 (0x0520/0x0527/0x0560) used to appear twice — once on the audio page and once on the 37-byte lighting page. They now keep a single card on the audio page with the lighting merged in; the other Kraken models (7.1, 7.1 Chroma, …) keep their lighting page.
- 修正耳机与键盘的电量读数：中间件的原始值是 0–255，按比例换算成百分比再显示；充电状态同样从第一个数据字节读取，不再整体偏移一位。
- Fixed battery readings for headsets and keyboards: the middleware reports 0–255, now scaled to percent, and the charging flag is read from the first data byte instead of being off by one.
- 窗口最小宽度从 1080 降到 700 逻辑像素，恢复小窗口使用；页面内容下限为 640 像素加 48 像素导航栏，再小需要重排页面布局。
- The window minimum width is now 700 logical pixels instead of 1080, so the app can be resized small again; page content still has a 640-pixel floor plus the 48-pixel navigation pane, and going smaller would require reflowing the pages.
- 修复旧版每用户安装目录在机器级安装后长期残留的问题。安装器产物（`Update.exe`、`packages\`）现在在每次启动时清理，不再只在安装那一刻清一次；用户配置、旧备份和日志一律保留。本机残留从 69.75 MB 降到 1.47 MB。
- Fixed the legacy per-user install directory lingering after a machine-wide install. Installer artifacts (`Update.exe`, `packages\`) are now cleaned on every launch instead of only once during installation; user settings, backups, and logs are always kept.
- 修复从每用户安装升级到机器级安装后，桌面和开始菜单的快捷方式丢失的问题。仍指向旧目录的快捷方式会被改写到新的机器级启动器，用户主动删除的快捷方式不会被重建。
- Fixed the desktop and Start-menu shortcuts disappearing when upgrading from a per-user install to a machine-wide install. Shortcuts still pointing at the old directory are redirected to the new launcher; shortcuts the user deleted are not recreated.
- 修复过渡目录清理过于激进的问题。旧目录里每个文件都迁移到新目录后才删除整个目录，仍有未迁移的文件时保留目录交给下次启动继续搬。
- Fixed the transitional data root being removed too eagerly. The directory is deleted only once every file in it has been migrated; files still pending are kept for the next launch.
- 修复 GPU 模式切换在服务刚被唤醒时可能退回 UAC 弹窗的问题。客户端在管道连接失败时重试一次，把常见的唤醒竞态挡在 UAC 之前。
- Fixed the GPU-mode switch falling back to a UAC prompt when the service was still waking up. The client retries once when the pipe connection fails, so the common wake-up race no longer reaches UAC.

### 改进 / Improved

- OpenRazer 设备目录升级到上游 `477f4d6`，新增 Razer Joro、Cobra HyperSpeed（有线/无线）、Blade 16 (2026) 和 Blade 18 (2026)，目录共 259 款。
- The OpenRazer catalogue is upgraded to upstream `477f4d6`, adding Razer Joro, Cobra HyperSpeed (wired/wireless), Blade 16 (2026), and Blade 18 (2026), for 259 entries in total.
- 新增《中间件设备协议参考》文档，列出 119 款雷云产品在自身中间件里实际调用的 HID 命令，按 33 组命令 profile 归类。OpenRazer 目录未收录的设备可以据此判断功能是否可实现。
- Added the middleware device protocol reference, listing the HID commands 119 Synapse products actually call from their own middleware, grouped into 33 command profiles. It shows what is implementable for devices the OpenRazer catalogue does not cover.
- 冷却垫协议改用 0F43 实际使用的通用 `rzDevice25` 热控布局：速度是 8 位单字节、风扇信息命令负载为 12 字节、可用范围按固件的 125–2600 RPM 收口。之前的实现套用了 PWM 风扇控制器（0F3C）的 16 位大端布局。
- The cooling-pad protocol now uses the generic `rzDevice25` thermal layout that 0F43 actually uses: an 8-bit speed byte, a 12-byte fan-information payload, and a 125–2600 RPM range taken from the firmware. The previous implementation applied the 16-bit big-endian layout of the PWM fan controller (0F3C).

## [1.5.5] - 2026-10-23

### 新增 / Added

- 新增宏页面与全尺寸宏编辑器，按雷云的操作结构提供宏列表、动作序列、触发键绑定、录制、单次/重复/持续循环播放，以及每步延迟和按住时间设置。编辑器支持键盘与鼠标选择器，录制或编辑时按下的键位会捕获为完整动作。
- Added a macro page and full-size editor with the Synapse-style macro list, action sequence, trigger binding, recording, once/repeat/continuous playback, and per-step delay and hold settings. The editor includes keyboard and mouse pickers; a captured key or mouse button becomes a complete action.
- 手动宏动作支持鼠标移动轨迹编辑：可在图上添加、拖动和删除轨迹点，精确输入 X/Y 坐标与时间；确认应用前不会修改现有宏。
- Manual macro steps can include a mouse-move path: add, drag, and delete points on the plot, or enter exact X/Y and time values. The existing macro is unchanged until the edit is applied.
- 托盘菜单打开时会读取已连接外设的电量，显示鼠标/键盘图标，点击一行可直接打开对应设备页；不显示电脑节点。
- The tray menu now reads battery levels for connected peripherals when opened, shows mouse/keyboard icons, and opens the clicked device page; the computer itself is not listed.
- 风扇控制改为自动、固定转速、手动曲线三选一。固定转速输入框只在固定模式下出现，手动曲线仍在独立窗口编辑。
- Fan control is now a three-way picker for automatic, fixed RPM, and manual curve. The RPM editor appears only in fixed mode; the manual curve remains in its own window.
- 新增 Razer 蓝牙 HID 设备识别与控制接口回退，覆盖 `BTHLE`、`BTHENUM` 和 GATT HID 路径。
- Added Razer Bluetooth HID discovery and control-interface fallback for `BTHLE`, `BTHENUM`, and GATT HID paths.
- 新增 Blade 14 (2022) `1532:028C` 的基线设备页准入和 CPU 性能档位显示；最大风扇与风扇曲线仍未开放。
- Added baseline device-page admission and CPU performance levels for the Blade 14 (2022) `1532:028C`; maximum fan and the fan curve remain closed.
- 新增 DeathAdder 2000 `1532:005F` / `1532:004D` 的只读识别入口；在收到对应硬件回报前，不开放写入控制。
- Added read-only recognition for the DeathAdder 2000 `1532:005F` and `1532:004D`; controls remain read-only until hardware evidence is available.

### 修复 / Fixed

- 修复 Blade 16 (2025) 在自定义性能模式中 CPU 档位整列消失的问题；现在按机型能力显示，不再依赖固件最后一次回报值。
- Fixed the entire CPU performance column disappearing in Blade 16 (2025) Custom mode; visibility now follows the model capability contract instead of the last firmware readback.
- 修复风扇固定转速写入、目标读取和手动模式切换的回归，包括 Blade 15 (Mid 2019) `0245` 的固定转速门控。
- Fixed fan fixed-RPM writes, target readback, and the switch into manual mode, including the fixed-RPM gate on the Blade 15 (Mid 2019) `0245`.
- 修复蓝牙 HID 设备找不到控制接口，Naga Pro 等蓝牙设备现在可以进入发现流程。
- Fixed Bluetooth HID devices failing to resolve a control interface; devices such as the Naga Pro can now enter the discovery flow.
- 修复托盘菜单电量行的图标、点击目标和键盘/鼠标图标显示。
- Fixed the tray battery row icons, click targets, and keyboard/mouse glyphs.
- 修复宏编辑器在录制中关闭会误提交录制内容、编辑器内点击被录入宏、步进按钮文案覆盖图标，以及窄窗口下键盘布局不可用的问题。
- Fixed macro recording being committed when the editor closed mid-recording, editor clicks entering the macro, localized button content replacing step icons, and the keyboard layout becoming unusable in narrow windows.
- 修复固件更新页面跳转到不稳定的雷蛇下载页或触发 401 页面。
- Fixed firmware update links opening an unstable Razer download page or a 401 answer page.
- 修复宏页面和鼠标选择器的布局、滚轮位置、区域高亮与按钮分隔。
- Fixed macro-page and mouse-picker layout, wheel position, region highlighting, and button separators.

### 改进 / Improved

- OpenRazer 诊断现在在命令到达 HID 层前记录请求，并在失败时记录预期的命令号、事务号、长度和实际响应字段。
- OpenRazer diagnostics now log requests before they reach the HID layer and record the expected command, transaction, length, and actual response fields on failure.
- OpenRazer 写入后会显示设备实际保持的值，方便区分“写入成功”和“设备接受但立即回退”。
- OpenRazer writes now show the value the device actually holds, distinguishing a successful write from one the device accepts and immediately reverts.
- 宏编辑器使用独立全尺寸窗口，键盘、鼠标和属性面板在较窄的主窗口下仍可编辑。
- The macro editor uses a full-size window, so the keyboard, mouse, and properties panels remain usable when the main window is narrow.
- 诊断中的设备查询和恢复状态会随设备刷新更新，已恢复的错误不再一直显示为失败。
- Device query and recovery state in diagnostics now refresh with the device, so recovered errors stop appearing as permanent failures.

本版本未签名，仅支持 Windows 11 x64。CPU 核心电压偏移、GPU 时钟/显存偏移和 HyperBoost 仍未实现（#6）；手动风扇曲线仍只对已适配的 Blade 16 (2025) 开放；#14 的 1.5.4 风扇调速闪退报告仍在等待用户日志，若复现请附带 `%LOCALAPPDATA%\OpenSynapse\logs\opensynapse.log`。DeathAdder 2000 `005F`/`004D` 目前只有只读识别。
This release is unsigned and targets Windows 11 x64. CPU core-voltage offset, GPU clock/memory offset, and HyperBoost are not implemented yet (#6); the manual fan curve remains limited to the admitted Blade 16 (2025); the 1.5.4 fan-speed crash report in #14 is still awaiting the reporter's log, and a reproduction should include `%LOCALAPPDATA%\OpenSynapse\logs\opensynapse.log`. DeathAdder 2000 `005F`/`004D` currently have read-only recognition.

## [1.5.4] - 2026-10-21

### 新增 / Added

- Blade 16 (2025) 的风扇控制现提供自动、固定转速和手动曲线三种模式。手动曲线在独立窗口中编辑，支持拖拽 CPU/GPU 曲线点、精确输入温度与 RPM、增加或删除档位、恢复默认。
- Blade 16 (2025) fan control now offers automatic, fixed-RPM, and manual-curve modes. The curve editor opens in a separate window with draggable CPU/GPU points, exact temperature and RPM inputs, tier add/remove, and reset-to-default.
- 新增 Razer BlackShark V3 Pro 支持（2.4 GHz `1532:101B`、USB `1532:101C`）：电量、充电状态、侧音、THX Spatial Audio、主动降噪和游戏/语音平衡。
- Added Razer BlackShark V3 Pro support (2.4 GHz `1532:101B`, USB `1532:101C`): battery, charging state, sidetone, THX Spatial Audio, ANC, and game/chat balance.
- 系统遥测的 CPU/GPU 温度、功耗和频率现在支持悬停查看本次运行期间的最小值与最大值。
- The CPU and GPU temperature, power, and frequency readings in the system telemetry card now show the lowest and highest values seen while OpenSynapse was running when hovered.
- 在机型白名单中开放 Blade 15 (Mid 2019) `1532:0245` 的固定风扇目标，并同时读取两路风扇目标值。
- Fixed fan targets are now admitted for the Blade 15 (Mid 2019) `1532:0245`, with both fan-target channels read back.

### 修复 / Fixed

- 修复自动风扇模式仍显示“目标转速”输入框的问题；输入框和应用按钮现在只在固定转速模式出现，手动曲线模式只显示“编辑”。
- Fixed the target-RPM input appearing while automatic fan mode was selected. The RPM input and Apply button now appear only in fixed mode, while manual curve mode shows only Edit.
- 修复面板模式在不支持该能力的机器上仍显示的问题；现在只有 SKU 硬件配置明确报告 Mini-LED 双分辨率面板时才展示。
- Fixed the panel-mode card appearing on machines without that capability. It now appears only when the SKU hardware configuration explicitly reports a dual-resolution mini-LED panel.
- 修复 OpenRazer 高回报率鼠标的轮询率读取错误，避免显示错误档位。
- Fixed polling-rate reads for high-rate OpenRazer mice so the displayed rate no longer lands on the wrong step.
- 修复 DeathAdder V2 X HyperSpeed 的电量和 DPI 控制缺失，改用与 OpenRazer 一致的事务 ID。
- Fixed missing battery and DPI controls on the DeathAdder V2 X HyperSpeed by using the transaction IDs OpenRazer sends.
- 修复 DeathAdder V4 Pro 在 2.4 GHz 链路断开后没有恢复驱动模式的问题。
- Fixed the DeathAdder V4 Pro not restoring driver mode after its 2.4 GHz link went down.
- OpenRazer 设备遇到一条无响应命令时不再整台设备变成不可用；已恢复的错误也会从诊断状态中清除。
- An unanswered OpenRazer command no longer disables the whole device, and recovered errors are cleared from diagnostics.
- 修复 BlackShark V3 Pro 控制接口选择错误以及部分保存操作卡住的问题，USB 与 2.4 GHz 通道均可正常识别。
- Fixed BlackShark V3 Pro control-interface selection and saves that could stall; both USB and 2.4 GHz channels are now recognized.
- 修复只有外接显示器时笔记本被误报为显示错误的问题。
- Fixed an external-display-only laptop being reported as a display error.
- 修复 Blade Fn 过滤端点不存在时仍反复重试的问题。
- Fixed repeated retries when the Blade Fn filter endpoint is absent.
- 修复 Viper 按键映射选择状态丢失，并补充 DPI 循环操作。
- Fixed Viper button-mapping selection being lost and added the DPI-cycle action.
- 修复托盘菜单左右留白过宽、底部裁切和右侧空白过多。
- Fixed excessive tray-menu side padding, bottom clipping, and empty space on the right.
- 未获得机型证据的 Blade 控制项现在会隐藏，不再显示无法执行的开关。
- Blade controls without model evidence are now hidden instead of showing switches that cannot work.

### 改进 / Improved

- 风扇曲线图补充温度横轴、转速纵轴、刻度、网格、单位标签和每个档位的数据点。
- The fan-curve chart now includes a temperature X axis, RPM Y axis, ticks, grid, unit labels, and a point marker for every tier.
- 风扇转速与风扇控制卡片恢复等宽布局，底部性能、风扇和充电上限卡片保持三列对齐。
- The fan-speed and fan-control cards use equal widths again, aligned with the three-column performance, fan, and charge-limit row below.
- OpenRazer 写入后会显示设备实际保持的值，并在诊断中记录命令、预期响应和实际响应参数。
- OpenRazer now reports the value the device actually holds after a write, with command, expected-response, and actual-response arguments recorded in diagnostics.
- 新增设备适配协作指南与支持 issue 模板，便于用户采集证据、协助新增机型。
- Added a device-adaptation guide and device-support issue template so users can collect evidence and help add new models.

本版本未签名，仅支持 Windows 11 x64。BlackShark V3 Pro 暂不包含 EQ、麦克风增益、固件更新和指示灯模式；手动风扇曲线仅对已适配的 Blade 16 (2025) 开放。
This release is unsigned and targets Windows 11 x64. BlackShark V3 Pro does not yet include EQ, microphone gain, firmware updates, or indicator-LED modes; the manual fan curve is available only on the admitted Blade 16 (2025).

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
