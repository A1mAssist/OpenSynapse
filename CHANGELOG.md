# Changelog

All notable user-visible changes to OpenSynapse are documented here.

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
