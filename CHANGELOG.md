# Changelog

All notable user-visible changes to OpenSynapse are documented here.

## [Unreleased]

### Added

- Added THX Spatial Audio controls for supported Blade 16 and Blade 14 models, including speaker/headphone selection, spatial audio, EQ presets and bands, voice clarity, and speaker volume normalization. The vendor native component is not bundled; if missing, the app offers a verified download from Razer's official server.
- Added panel mode and local dimming cards on supported Blade models. Both cards are hidden when the device does not expose the corresponding capability.
- Added support for the Razer Blade 16 (2023) `1532:029F`: keyboard brightness and firmware-rendered effects (Off, Breathing, Spectrum, Wave), performance modes, Custom CPU/GPU levels with CPU Boost, Max Fan, fixed fan targets from 2000 to 5000 RPM, charge limit, one-time full charge, and Gaming mode.

### Fixed

- Fixed the Blade 16 (2023) keyboard lighting reports, which used the brightness query command for writes and omitted the store and region selector, so every brightness write failed.
- Fixed the lighting effect selector picking the wrong effect on the Blade 16 (2023): the selected index is now resolved against the effect list that model actually renders.
- Fixed the Blade 16 (2023) lighting switch, brightness, and saved profiles having no effect, because they were routed through the per-key matrix lighting path that generation does not have. Screen-off and wake now restore its lighting as well.
- Saved lighting profiles on the Blade 16 (2023) no longer store an effect key that the profile reader rejects on the next launch.
- The .chroma import button and the wave direction selector are now hidden on the Blade 16 (2023), which can neither play a .chroma file nor change the wave direction.
- Blade capabilities are now admitted per exact model instead of per successful readback. Blade 16 (2025) `1532:02C6` no longer offers Max Fan, CPU Boost, or the undervolt level, because its maximum fan is a separate fixed 5400 RPM fan target and no undervolt evidence exists for it.
- Leaving Custom performance mode on a Max Fan model now turns Max Fan off and confirms it first, so a preset change cannot leave the fans pinned at maximum.
- Enabling Max Fan is refused while other Power Mode Control settings are on, because the write replaces the whole shared control byte.

### Improved

- THX controls now live behind an Audio card in the Blade device view and open in a reusable popout window instead of occupying a separate navigation page.
- The tray menu now stays open when the pointer leaves the taskbar. It renders in its own window that never takes focus, and closes only on an outside click, Esc, a menu selection, or app exit.

- Blade game mode, startup animation, and one-time full charge are shown as three separate cards.
- Raw SKU flags are no longer shown in the device UI; the value is used only for capability detection. Native panel mode is kept in diagnostics instead of crowding the Blade platform status card.

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
