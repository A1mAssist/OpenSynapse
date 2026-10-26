# Changelog

English · [Chinese](CHANGELOG.zh-CN.md)

All notable user-visible changes to OpenSynapse are documented here.

## [1.6.0] - 2026-10-26

OpenSynapse now goes beyond recognizing devices: it controls them.

### What this release is about

- **All 119 Synapse middleware products are implemented.** We reverse-engineered the HID commands each product actually calls from Razer's own product bundles and grouped them into 33 command profiles. Devices that the OpenRazer catalogue never covered can now use their middleware protocol. Every one of the 278 products with a middleware bundle has a page, and the OpenRazer catalogue is pinned to upstream `477f4d6` with 259 entries.
- **Device pages grew from 5 categories to 9.** Keyboard, headset (including microphones), Chroma lighting and BLE device pages are new, and Philips Hue has its own lighting page. The device table lists 268 models: 112 mice, 72 keyboards, 48 laptops, 16 accessories, 8 mouse mats and 1 monitor.
- **Audio controls cover 34 devices.** Eighteen protocol 25 gaming headsets (Nari, Kraken V3/V4, Barracuda, BlackShark V2 Pro, USB audio controllers and others) get EQ presets and 8/10-band curves, bass, microphone mute and volume, input selection, AI noise suppression and ANC. Another 11 Kraken, Leviathan and Nommo devices and 5 Seiren microphones (V2 Pro, V2 X and V3 Mini) are covered too.
- **Lighting now covers 38 devices that previously had no page.** Viper V4, Basilisk V4, DeathAdder V4 and Naga V3 mice, the Key Light and Aether lamp families, Thunderbolt 5 Dock, Core X V2 and monitor stands all get effects, brightness and colour with readback verification.
- **The keyboard page adds 39 devices.** That includes 16 keyboards with only the class `0x0F` lighting channel (Turret, BlackWidow X, Huntsman Essential, V4 Low-profile, Reclusa, Tartarus V2 Pro and others), 12 new device pages and 11 protocol 25 keyboard pages. Firmware, serial number, battery, game mode and polling rate up to 8000 Hz are probed per device; unsupported commands hide themselves instead of leaving empty controls.
- **Onboard macros are readable and writable.** The `Set/Get Single Button Assignment` commands (class `0x02`, `0x0C`/`0x8C`) expose the button list and current assignments. A normal or HyperShift layer button can be remapped to another mouse button, a keyboard key with Ctrl/Shift/Alt/Win, a DPI action, profile navigation, a media key, a power key, double click, a scroll-wheel mode, a Razer key, a Windows shortcut or HyperShift, with readback verification.
- **The macro editor reads Synapse macros.** Synapse 3 and Synapse 4 XML exports import directly; the root `<Version>` selects the format. All 13 real export files passed.
- **The Chroma ARGB controller (`0F1F`) is per-LED addressable.** Pick a port and LED count, then paint cells directly; fill or clear a strip and apply it. Frames use the 320-byte channel exposed by Razer's filter driver and switch the controller into custom-frame mode first. The hardware is 6 ports x 80 LEDs; the 105-LED frame limit is only wire headroom.
- **Philips Hue is fully supported.** Enter the bridge address, press the bridge's link button and pair. Lights can then be switched, dimmed and coloured (sRGB to CIE xy, written to `color.xy`). Every write is read back and restored if the bridge rejects it; the bridge's self-signed certificate is trusted only for the address you entered.
- **BLE devices have a page.** Bluetooth-only Razer mice such as the Viper V4 Pro now appear in the device list with firmware, serial number, battery, charging state, polling rate and DPI, and accept polling-rate and DPI writes over the middleware's GATT protocol 30.
- **THX controls now cover every V3 and V4 model.** Spatial audio, presets, 10-band EQ, volume levelling and voice clarity are exposed, with sidetone on V3. Model-specific native DLLs are checked before download, and V4 offers GameProfiles and Patcher entries. The BlackShark V2 Pro (2020 and 2023) audio page also gains a THX status card that checks the service and control component and offers Razer's official installer when either is missing.
- **Integrated-graphics-only mode is a real display switch.** A machine-wide MSI install registers a protected GPU service, asks for administrator permission once during installation and then switches without UAC; per-user and portable installs still fall back to one UAC prompt per change. The machine-wide install migrates an older per-user install, keeps settings and updates the startup entry. User data now lives in `%LOCALAPPDATA%\A1mAssist\OpenSynapse`, outside the directories removed by MSI upgrades.
- **HyperBoost no longer requires a Razer cooling pad.** On supported models (Blade 14 2023 and Blade 16 2023/2024/2025) it is selectable with the full-power adapter connected. Choosing it without a pad shows a disclaimer first and leaves third-party cooling to the user; battery and low-power adapters still cannot use it, and pad-present behaviour is unchanged.
- **Blade 14 (2022) is now a fully supported device.** It exposes the performance modes Synapse lists (Balanced, Silent, Custom and Performance), a fixed fan target of 3500-5000 RPM that only acts in Custom mode, the CPU Boost level and firmware-rendered keyboard lighting.
- **Kraken V4 gets wired lighting.** It is no longer matched as a BlackShark, and its effect and brightness writes are applied and verified.
- **HID command scans now work on mice.** The sweep only sends device-to-host reads, so the old mouse guard protected nothing; a wireless mouse now gets its 2.4 GHz command space probed too.
- **Seiren Emote is a real product now, not an unnamed PID.** `0F1B` is the Razer Seiren Emote microphone (it shares the Camyt3 middleware bundle with `0524`). It has an audio page with 10-band EQ and microphone volume under the microphone category, plus an 8x8 LED emote matrix card over class `0x10` commands `10:03` and `10:05`.
- **The minimum window width drops from 1080 to 700 logical pixels.** Small windows are usable again; page content keeps a 640-pixel floor plus the 48-pixel navigation pane.

### Added

- The Blade 14 (2022) is now a supported device: it exposes the performance modes Synapse lists (Balanced, Silent, Custom and Performance), a fixed fan target of 3500-5000 RPM that only acts in Custom mode, the CPU Boost level, and firmware-rendered keyboard lighting.
- The macro editor can import the XML macros Synapse 3 and Synapse 4 export.
- The Kraken V4 headset now gets wired lighting: it is no longer matched as a BlackShark, and the lighting effect and brightness are written and verified.
- Mice can now run the HID command scan. The sweep only sends device-to-host reads since the write half was removed, so the old mouse guard protected nothing; a wireless mouse now gets its 2.4 GHz command space probed too.
- HyperBoost no longer requires the Razer Laptop Cooling Pad. On models that support it (Blade 14 2023, Blade 16 2023/2024/2025) it is selectable from the performance-mode list whenever the full-power adapter is connected. Selecting it without a pad now shows a disclaimer first and leaves third-party cooling to the user; battery and low-power adapters still cannot use it. The pad-present behaviour is unchanged.
- Middleware-derived mice now expose onboard button mapping: read the button list and current assignments over the bundle's `Set/Get Single Button Assignment` commands (class 0x02, 0x0C/0x8C), then remap a button on the normal or HyperShift layer to off, another mouse button, a keyboard key with Ctrl/Shift/Alt/Win, a DPI action, profile navigation, a media key, a power key, double click, a scroll-wheel mode, a Razer key, a Windows shortcut or HyperShift. Every write is verified by reading it back, and the card stays hidden when the device refuses the command.
- When the 0F1F ARGB controller is missing Razer's filter driver, the lighting page now offers Razer's driver download instead of only showing an error.
- The BlackShark V2 Pro (2020 and 2023) audio page now shows THX status: it checks the THX service (VSSrv) and the control component, links to Razer's THX installer when the service is missing, and downloads plus verifies the control component when only that is missing.
- THX headset controls now cover V3 and V4 models: spatial audio, presets, 10-band EQ, volume leveling, voice clarity, and sidetone on V3. The app downloads and verifies the model-specific native DLL and offers V4 GameProfiles / Patcher entries. Not hardware-verified.
- Added per-LED control for the Chroma Addressable RGB Controller (0F1F): pick a port and LED count on the lighting page, click cells to paint, then fill, clear or apply the whole strip. The app switches the controller to custom-frame mode first and remembers the LED count per port. Frames hold up to 105 LEDs; the UI follows the device's real 6 ports x 80 LEDs.
- After pairing, the Hue card shows the bridge name, model and software version so it is clear which bridge is connected.
- The lighting page now shows device identity: serial number and firmware version for devices that answer, hidden without an error when they do not.
- The lighting page now shows battery for devices that have one: wireless mice display their percentage and charging state under the name, while lamps and docks hide the line because the probe fails silently.
- Added Philips Hue support. Enter the bridge address in Settings, press the link button on the bridge, then choose Pair to list lights and control on/off and brightness. Every write is read back and the previous state is restored when the bridge keeps a different value; the bridge certificate is trusted only for the address you entered.
- Philips Hue also sets colour: pick a light, choose a colour, and the app converts sRGB to CIE 1931 xy and writes color.xy, with the same read-back check.
- Added a BLE device page: Razer mice that only speak Bluetooth (such as the Viper V4 Pro) now appear in the device list with firmware, serial number, battery, charging state, polling rate and DPI, and accept polling-rate and DPI writes. Every write is read back, and the previous value is restored when the device does not take it. The channel is the middleware's GATT protocol 30.
- The lighting page no longer stops at a single device: every attached Aether lamp, mouse or accessory gets its own row, several HID interfaces of one device collapse into one row, and the previous selection survives a refresh.
- The keyboard page now drives the class 0x0F lighting channel used by 16 keyboards that have no class 0x10 support (Turret, BlackWidow X, Huntsman Essential, V3 X, V3 Pro series, BlackWidow V4 Low-profile, Reclusa, Tartarus V2 Pro and others): off/static/breathing/spectrum with one or two colours. The channel has no brightness command, so the brightness controls hide themselves; writes are read back when the firmware answers 0F:130 and applied without a readback otherwise, matching the middleware.
- The previously unnamed 0F1B is the Razer Seiren Emote microphone (it shares the Camyt3 middleware bundle with 0524). It now has an audio page with the 10-band EQ and microphone volume, filed under the microphone category.
- The Seiren Emote LED matrix now has a lighting card: effect (off/static/breathing/spectrum), brightness and primary/secondary colours over the class 0x10 10:03 and 10:05 commands, with read-before-write, readback verification and restore on failure. Other audio devices do not answer these commands, so the card stays hidden.
- Two edge cases are now covered: Razer Marci (audio) on the audio page and the Razer Wireless Control Pod on the lighting page. Of the 119 devices with identifiable middleware commands, 118 now have a page; only Philips Hue (a network bridge, not HID) is left.
- The keyboard page now covers 12 more models: BlackWidow X, V4 Pro 75%, V4 Low-profile (including TKL), Huntsman Essential, V3 X and V3 Pro Low-profile, Reclusa Mini and Reclusa X Mini, Pro Type Ergo, Tartarus V2 Pro and the Turret keyboard. Features the device answers (firmware, serial, game mode, polling rate, battery) appear automatically; the rest stay hidden.
- The audio page now covers 5 more devices: Seiren V2 Pro, V2 X and V3 Mini microphones, the Leviathan V2 Pro speaker, and the Kraken V4 X headset, plus a new microphone device category.
- Added a shared lighting page for 38 devices that had no page: Viper V4 / Basilisk V4 / DeathAdder V4 / Naga V3 mice, the Key Light and Aether lamp family, Chroma ARGB controllers, and accessories such as Thunderbolt 5 Dock, Core X V2 and monitor stands. It offers effect (off/static/breathing/spectrum), brightness and colour, verified by reading the value back, and skips anything already owned by the OpenRazer catalogue, audio page, keyboard page or the 320-byte ARGB channel.
- Added a lighting card to the keyboard page: effect (off/static/breathing/spectrum), brightness, and one or two colors, each verified by reading the value back. The underlying class 0x10 Chroma commands (effect and brightness) are also used by 51 mice, lamps, ARGB controllers, and docks, so later pages can reuse them directly.
- The audio page now covers 11 more devices that use the same protocol 25 audio commands: Kraken BT (Sanrio / Kitty), Kraken X USB, Kraken V3 X, Kraken Kitty V2 (including BT Quartz), plus Leviathan V2 and Nommo V2 / V2 Pro / V2 X. Speakers are labelled as speakers, and models without ANC hide the noise-cancelling card.
- Added a device page for 11 keyboards the OpenRazer catalogue does not cover (Huntsman V3 series, Pro Type series, BlackWidow V4 75% Xbox edition): firmware, serial number, battery and charging, game mode, and polling rate up to 8000 Hz. Each item probes the device first, and unsupported commands are hidden instead of leaving an empty control.
- Added an audio page for the 18 protocol 25 gaming headsets (Nari, Kraken V3/V4, Barracuda, BlackShark V2 Pro, USB audio controllers): firmware, battery, charging state, EQ presets and 8/10-band curves, bass, microphone mute and volume, audio source, AI noise suppression, and ANC. Every write reads the current value first, verifies the readback, and restores the previous value when the device does not accept the change.
- Added an integrated-graphics-only display switch. The machine-wide MSI install registers a protected GPU service, asks for administrator permission once during installation, and then switches without UAC; per-user and portable installs still fall back to a UAC prompt per change. The machine-wide install migrates an older per-user install, preserving settings and updating its startup entry. User data now lives in `%LOCALAPPDATA%\A1mAssist\OpenSynapse`, outside the directories removed by MSI upgrades.

### Fixed

- Fixed a failing discrete GPU driver being reported as "the discrete GPU is enabled". The switch only looked for problem code 22, which is a device someone turned off, so a driver that failed to start (code 43) counted as healthy and the switch tried to disable an adapter that was already broken. The adapter state is now one of healthy, disabled or faulted; a fault shows its error code, the switch is disabled, and a "Restart GPU device" button restarts the device through the machine-wide service without a UAC prompt, falling back to one administrator request on a per-user install.
- Fixed the discrete GPU not coming back after the integrated-only switch is turned off. The state comparison was inverted: an adapter that was already disabled matched the request to enable it, so the change returned success without running it and the UI claimed the GPU was back while the device stayed disabled.
- Audited the 12 models (16 PIDs) that reuse a sibling protocol against the middleware bundle `DeviceInfo`: restored the missing 125/500/1000 Hz polling rates for the Abyssus Lite and removed the Viper V4 Pro from the lighting catalogue because it has no lighting. See `docs/MIDDLEWARE_MOUSE_AUDIT.md`.
- Kraken Tournament Edition, Ultimate and Kitty V2 (0x0520/0x0527/0x0560) used to appear twice — once on the audio page and once on the 37-byte lighting page. They now keep a single card on the audio page with the lighting merged in; the other Kraken models (7.1, 7.1 Chroma, …) keep their lighting page.
- Fixed battery readings for headsets and keyboards: the middleware reports 0–255, now scaled to percent, and the charging flag is read from the first data byte instead of being off by one.
- The window minimum width is now 700 logical pixels instead of 1080, so the app can be resized small again; page content still has a 640-pixel floor plus the 48-pixel navigation pane, and going smaller would require reflowing the pages.
- Fixed the legacy per-user install directory lingering after a machine-wide install. Installer artifacts (`Update.exe`, `packages\`) are now cleaned on every launch instead of only once during installation; user settings, backups, and logs are always kept.
- Fixed the desktop and Start-menu shortcuts disappearing when upgrading from a per-user install to a machine-wide install. Shortcuts still pointing at the old directory are redirected to the new launcher; shortcuts the user deleted are not recreated.
- Fixed the transitional data root being removed too eagerly. The directory is deleted only once every file in it has been migrated; files still pending are kept for the next launch.
- Fixed the GPU-mode switch falling back to a UAC prompt when the service was still waking up. The client retries once when the pipe connection fails, so the common wake-up race no longer reaches UAC.
- Fixed the device list refresh switching the page away from the current device.
- Fixed Chroma adding a card for a device that already has a native page.
- Fixed two mice without lighting showing up on the lighting page: the Viper V3 HyperSpeed page offered a lighting button that could only produce a read-failure card, and the Viper V4 Pro reports `brightness:false` in its own DeviceInfo. Both are out of the lighting catalogue.
- Fixed Hue discovery running in the background all the time. It now runs only while the devices page is visible, reuses its HTTP client, and slows polling on battery power.
- Fixed the missing restart offer after a display-mode (MUX) switch. Writing only stores the firmware value, so the app now offers a restart right away (`shutdown /r /t 5`, without `/f`, so unsaved applications can still prompt).
- Fixed the macro editor trimming the shortcut summary in the playback bar with no way to read it in full (it now carries a tooltip). The properties card is always open, so its chevron and the click-to-collapse handler are gone.

### Improved

- Closed the last two gaps in the middleware bundle list: Razer Cobra (`00A2`, reuse the `00A3` Cobra definition) and the Chroma Wireless ARGB Controller (`0F2E`). All 278 products with a middleware bundle now have a page.
- Removed the System audio card from Settings. Default playback/recording devices, volume and mute stay in Windows Sound settings, and the app no longer maintains the undocumented default-endpoint switch.
- The OpenRazer catalogue is upgraded to upstream `477f4d6`, adding Razer Joro, Cobra HyperSpeed (wired/wireless), Blade 16 (2026), and Blade 18 (2026), for 259 entries in total.
- Added the middleware device protocol reference, listing the HID commands 119 Synapse products actually call from their own middleware, grouped into 33 command profiles. It shows what is implementable for devices the OpenRazer catalogue does not cover.
- The cooling-pad protocol now uses the generic `rzDevice25` thermal layout that 0F43 actually uses: an 8-bit speed byte, a 12-byte fan-information payload, and a 125–2600 RPM range taken from the firmware. The previous implementation applied the 16-bit big-endian layout of the PWM fan controller (0F3C).
- CPU telemetry now reads the HWiNFO shared memory: the clock is `Processor Frequency × % Processor Performance`, the temperature prefers the HWiNFO package sensor, a flat ACPI reading hides itself, and the GPU temperature falls back to HWiNFO when IGCL is empty.
- Added Intel IGCL for the integrated GPU clock and power, without AIDA64 or HWiNFO64.
- OpenRazer diagnostics now log requests before they reach the HID layer and record the expected command, transaction, length and actual response fields on failure. Writes also show the value the device actually holds, distinguishing a successful write from one the device accepts and immediately reverts.
- The telemetry bars animate again and show the session minimum and maximum alongside the current value.
- Added six reverse-engineering documents: the Razer 0F1F filter driver and ARGB channel, the native Razer THX layers, the middleware mouse audit, Philips Hue bridge discovery, BLE protocol 30, and the Leviathan V2X.
- Icon fonts now use the `Segoe Fluent Icons,Segoe MDL2 Assets` fallback chain instead of swapping application resources at runtime, and `Cascadia Mono` and the Chinese UI text fall back to `Consolas` and `Segoe UI`.
- Windows 10 now falls back for Mica, dark title bars and tray corners instead of showing light title bars or a transparent background.
- The window minimum scales with DPI, so the device page is no longer clipped at 200% scaling.
- The tray menu can hide its battery row and remembers the window size.

### Notes

OpenSynapse v1.6.0 is unsigned and targets Windows 11 x64. The THX V3/V4 headset path is built from the native clients and middleware protocol but was not verified on physical hardware; the app still checks the model-specific DLL and verifies downloads.

## [1.5.5] - 2026-10-23

### Added

- Added a macro page and full-size editor with the Synapse-style macro list, action sequence, trigger binding, recording, once/repeat/continuous playback, and per-step delay and hold settings. The editor includes keyboard and mouse pickers; a captured key or mouse button becomes a complete action.
- Manual macro steps can include a mouse-move path: add, drag, and delete points on the plot, or enter exact X/Y and time values. The existing macro is unchanged until the edit is applied.
- The tray menu now reads battery levels for connected peripherals when opened, shows mouse/keyboard icons, and opens the clicked device page; the computer itself is not listed.
- Fan control is now a three-way picker for automatic, fixed RPM, and manual curve. The RPM editor appears only in fixed mode; the manual curve remains in its own window.
- Added Razer Bluetooth HID discovery and control-interface fallback for `BTHLE`, `BTHENUM`, and GATT HID paths.
- Added baseline device-page admission and CPU performance levels for the Blade 14 (2022) `1532:028C`; maximum fan and the fan curve remain closed.
- Added read-only recognition for the DeathAdder 2000 `1532:005F` and `1532:004D`; controls remain read-only until hardware evidence is available.

### Fixed

- Fixed the entire CPU performance column disappearing in Blade 16 (2025) Custom mode; visibility now follows the model capability contract instead of the last firmware readback.
- Fixed fan fixed-RPM writes, target readback, and the switch into manual mode, including the fixed-RPM gate on the Blade 15 (Mid 2019) `0245`.
- Fixed Bluetooth HID devices failing to resolve a control interface; devices such as the Naga Pro can now enter the discovery flow.
- Fixed the tray battery row icons, click targets, and keyboard/mouse glyphs.
- Fixed macro recording being committed when the editor closed mid-recording, editor clicks entering the macro, localized button content replacing step icons, and the keyboard layout becoming unusable in narrow windows.
- Fixed firmware update links opening an unstable Razer download page or a 401 answer page.
- Fixed macro-page and mouse-picker layout, wheel position, region highlighting, and button separators.

### Improved

- OpenRazer diagnostics now log requests before they reach the HID layer and record the expected command, transaction, length, and actual response fields on failure.
- OpenRazer writes now show the value the device actually holds, distinguishing a successful write from one the device accepts and immediately reverts.
- The macro editor uses a full-size window, so the keyboard, mouse, and properties panels remain usable when the main window is narrow.
- Device query and recovery state in diagnostics now refresh with the device, so recovered errors stop appearing as permanent failures.

This release is unsigned and targets Windows 11 x64. CPU core-voltage offset, GPU clock/memory offset, and HyperBoost are not implemented yet (#6); the manual fan curve remains limited to the admitted Blade 16 (2025); the 1.5.4 fan-speed crash report in #14 is still awaiting the reporter's log, and a reproduction should include `%LOCALAPPDATA%\OpenSynapse\logs\opensynapse.log`. DeathAdder 2000 `005F`/`004D` currently have read-only recognition.

## [1.5.4] - 2026-10-21

### Added

- Blade 16 (2025) fan control now offers automatic, fixed-RPM, and manual-curve modes. The curve editor opens in a separate window with draggable CPU/GPU points, exact temperature and RPM inputs, tier add/remove, and reset-to-default.
- Added Razer BlackShark V3 Pro support (2.4 GHz `1532:101B`, USB `1532:101C`): battery, charging state, sidetone, THX Spatial Audio, ANC, and game/chat balance.
- The CPU and GPU temperature, power, and frequency readings in the system telemetry card now show the lowest and highest values seen while OpenSynapse was running when hovered.
- Fixed fan targets are now admitted for the Blade 15 (Mid 2019) `1532:0245`, with both fan-target channels read back.

### Fixed

- Fixed the target-RPM input appearing while automatic fan mode was selected. The RPM input and Apply button now appear only in fixed mode, while manual curve mode shows only Edit.
- Fixed the panel-mode card appearing on machines without that capability. It now appears only when the SKU hardware configuration explicitly reports a dual-resolution mini-LED panel.
- Fixed polling-rate reads for high-rate OpenRazer mice so the displayed rate no longer lands on the wrong step.
- Fixed missing battery and DPI controls on the DeathAdder V2 X HyperSpeed by using the transaction IDs OpenRazer sends.
- Fixed the DeathAdder V4 Pro not restoring driver mode after its 2.4 GHz link went down.
- An unanswered OpenRazer command no longer disables the whole device, and recovered errors are cleared from diagnostics.
- Fixed BlackShark V3 Pro control-interface selection and saves that could stall; both USB and 2.4 GHz channels are now recognized.
- Fixed an external-display-only laptop being reported as a display error.
- Fixed repeated retries when the Blade Fn filter endpoint is absent.
- Fixed Viper button-mapping selection being lost and added the DPI-cycle action.
- Fixed excessive tray-menu side padding, bottom clipping, and empty space on the right.
- Blade controls without model evidence are now hidden instead of showing switches that cannot work.

### Improved

- The fan-curve chart now includes a temperature X axis, RPM Y axis, ticks, grid, unit labels, and a point marker for every tier.
- The fan-speed and fan-control cards use equal widths again, aligned with the three-column performance, fan, and charge-limit row below.
- OpenRazer now reports the value the device actually holds after a write, with command, expected-response, and actual-response arguments recorded in diagnostics.
- Added a device-adaptation guide and device-support issue template so users can collect evidence and help add new models.

This release is unsigned and targets Windows 11 x64. BlackShark V3 Pro does not yet include EQ, microphone gain, firmware updates, or indicator-LED modes; the manual fan curve is available only on the admitted Blade 16 (2025).

## [1.5.3] - 2026-10-19

### Added

- Added support for the Razer Blade 16 (2023) `1532:029F`: keyboard brightness, firmware-rendered effects (Off, Breathing, Spectrum, Wave), performance modes, Custom CPU/GPU levels with CPU Boost and Max Fan, fixed fan targets from 2000 to 5000 RPM, charge limit, one-time full charge, and Gaming mode.
- Added THX Spatial Audio controls for supported Blade 16 and Blade 14 models: speaker/headphone selection, spatial audio, EQ presets and bands, voice clarity, and speaker volume normalization. The vendor native component is not bundled; if missing, the app offers a verified download from Razer's official server.
- Added panel mode and local dimming cards, hidden when the device does not expose the corresponding capability.

### Fixed

- Fixed the Blade 16 (2023) keyboard lighting reports, which used the brightness query command for writes and omitted the store and region selector, so every brightness write failed.
- Fixed the lighting effect selector picking the wrong effect on the Blade 16 (2023): the selected index is now resolved against the effect list that model actually renders.
- Fixed the Blade 16 (2023) lighting switch, brightness, and saved profiles having no effect, because they were routed through the per-key matrix lighting path that generation does not have. Screen-off and wake now restore its lighting as well.
- Saved lighting profiles on the Blade 16 (2023) no longer store an effect key that the profile reader rejects on the next launch.
- The .chroma import button and the wave direction selector are now hidden on the Blade 16 (2023), which can neither play a .chroma file nor change the wave direction.
- Blade capabilities are now admitted per exact model instead of per successful readback. Blade 16 (2025) `1532:02C6` no longer offers Max Fan, CPU Boost, or the undervolt level.
- Leaving Custom performance mode on a Max Fan model now turns Max Fan off and confirms it first, so a preset change cannot leave the fans pinned at maximum.
- Enabling Max Fan is refused while other Power Mode Control settings are on, because the write replaces the whole shared control byte.
- Fixed the tray menu being too wide on the sides and clipped at the bottom: item spacing now matches AskKey and the window height follows the real content.
- Fixed the tray menu leaving excess space on the right: the width now follows the widest localized label, so Chinese and English both sit flush.
- Fixed the tray menu closing when the pointer left the taskbar. It renders in its own window that never takes focus and closes only on an outside click, Esc, a menu selection, or app exit.

### Improved

- THX controls now live behind an Audio card in the Blade device view and open in a reusable popout window instead of occupying a separate navigation page.
- Blade game mode, startup animation, and one-time full charge are shown as three separate cards. Raw SKU flags are no longer shown in the device UI; the value is used only for capability detection.
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
- Power-source wording is consistent between the Chinese and English UI.

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
