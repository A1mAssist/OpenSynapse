# OpenSynapse Device Workspace Design

Date: 2026-08-12
Status: Approved design direction; pending implementation-plan review

## Goal

Reshape the WinUI 3 application into a quiet hardware-control workspace. The
overview answers whether the system and connected devices are usable. Device
capabilities live under the device they belong to, rather than appearing as
global navigation destinations.

The work preserves the existing single-process WinUI 3 architecture,
`MainViewModel` bindings, and hardware evidence gates. It does not add a web
surface, a third-party UI library, or unverified hardware writes.

## Information Architecture

The application-level sidebar contains only:

1. Overview
2. Devices
3. Profiles
4. Diagnostics

`Lighting` and `Mouse` are removed as top-level destinations. They describe
device-specific capabilities, not application-level workspaces.

The device workspace lists supported connected devices. Opening a device
selects that device in the content area:

| Device | Detail sections | Currently verified write controls |
| --- | --- | --- |
| Blade 16 2025 | Status, Lighting, Performance, Battery | Keyboard brightness |
| Viper V3 HyperSpeed | Status, DPI and polling rate, Power | DPI, polling rate, idle timeout |

Sections without verified writes remain read-only or explicitly blocked. The
interface never renders them as active controls.

## Overview

The overview uses this order:

1. Header: `Overview`, last refresh time, and refresh action.
2. Connected devices: one compact summary per device, containing the actual
   device name, control-channel state, concise capability summary, and a
   state badge.
3. System telemetry: CPU, GPU, memory, and storage.

There is no persistent global "all devices are normal" card. Healthy state is
communicated by the device rows. A compact `InfoBar` appears below the page
header only for an actionable condition: Synapse channel contention, access
denied, timeout, disconnect, query failure, a write failure, or restoration.

Device summary rows navigate to the corresponding selected device in the
Devices workspace. They do not expose hardware writes directly.

## Device Workspace

The Devices workspace has a device list or selector in its content surface;
it does not put device names in the application sidebar. The selected device
shows its own internal navigation using native WinUI controls such as a
`NavigationView` detail pane, `TabView`, or a compact segmented selector. The
implementation selects the smallest native control that fits the current
`MainWindow.xaml` structure.

The first version needs no new backend API. Existing values and write actions
are routed into their relevant device sections:

- Blade keyboard brightness moves to `Blade 16 > Lighting`.
- Blade performance, fans, battery limit, display telemetry, and existing
  write gates appear in the matching read-only/verified sections.
- Viper DPI and polling-rate controls move to `Viper V3 > DPI and polling
  rate`.
- Viper idle timeout moves to `Viper V3 > Power`.

Capabilities that are SourceBacked, Blocked, Unavailable, Busy, Failed,
ReadOnly, or Restoring are represented with existing state data and disabled
controls where applicable. Only diagnostic surfaces use protocol vocabulary.

## Profiles

Profiles remain a top-level workspace because they can apply across devices
and conditions. This change only gives the existing profile controls a
consistent shell and does not broaden profile behavior.

## Visual and Interaction Rules

- Keep WinUI 3, Mica, dark/light support, `Segoe UI Variable`, and `Cascadia
  Mono` for hardware values.
- Keep the existing compact, instrument-like surface and 4px control/card
  corners.
- Healthy state uses a concise `READY` state badge inside the device row; it
  does not add a dashboard status card.
- Green means verified/ready, amber means blocked/busy/read-only, red means
  failure/danger, and cyan remains reserved for GPU telemetry.
- Stable actions use explicit text. Compact utility actions can use familiar
  WinUI icons with accessible names and tooltips.
- Device capabilities do not become sidebar nodes. The sidebar represents
  application-wide tasks only.
- Page headers retain a refresh action, recent update time, and contextual
  error surface without moving the native caption buttons.

## Implementation Scope

Primary files:

- `src/OpenSynapse.App/MainWindow.xaml`
- `src/OpenSynapse.App/MainWindow.xaml.cs`
- `src/OpenSynapse.App/ViewModels/MainViewModel.cs` only if UI-only selection
  or state labels cannot be held in the window layer.
- `src/OpenSynapse.App/App.xaml` for narrowly required shared styles.

No HID, protocol, device-discovery, telemetry, or profile persistence changes
are part of this redesign unless a pre-existing UI binding is missing.

## Verification

1. Build the app project for x64 with no warnings/errors.
2. Run the existing core test project; UI restructuring must not change
   protocol or profile behavior.
3. Launch the application and inspect Overview, Devices, Profiles, and
   Diagnostics in both themes.
4. Confirm all verified write controls are still callable only after their
   existing backend state enables them.
5. Check narrow-window navigation, keyboard focus, disabled states, and the
   following runtime conditions where observable: no device, blocked channel,
   busy write, failure, and recovery.

## Deliberate Exclusions

- No top-level Lighting or Mouse navigation.
- No device names in the application sidebar.
- No new capability control for unverified hardware functionality.
- No new UI framework, DI container, page framework, or backend service.
