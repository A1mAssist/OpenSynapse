# Changelog

English · [Chinese](CHANGELOG.zh-CN.md)

All notable user-visible changes to OpenSynapse are documented here.

## [Unreleased]

### Added

- **Kraken V3 Pro**: connect the 052C receiver and 052D wired identity through the bundle-declared NXP serial channel, with battery, sidetone, silence detection and haptics controls and profile restore.
- **Ripsaw HD audio**: add the capture card's speaker, PC, HDMI and microphone monitoring volume/mute controls, with saved settings restored on connection.
- **Clio audio**: add Clio and Clio X controls through their dedicated Carol protocol, including product-specific EQ and power settings.
- **Nommo audio**: expose volume, mute and EQ presets on the native Nommo Chroma and Nommo Pro device pages; Pro also supports eight-band EQ, bass, Dolby, input selection and profile restore.
- **Dock volume**: Thunderbolt 5 Dock, Monitor Stand, Laptop Monitor Stand and Handheld Dock can adjust the current Windows default playback device's volume and mute state.
- **Wireless Control Pod**: connect through its dedicated Bluetooth protocol, map eight button/rotation gestures to media actions or saved macros, and restore device mode when the session ends.
- **Update source**: automatic updates can use the Gitee mirror; Settings offers Automatic, Gitee and GitHub, and Automatic prefers Gitee while falling back when it is unreachable or behind.
- **Keyboard animations**: RazerHid keyboards can import a `.chroma` animation on the device page and play it through the software frame runtime; the file stays with the lighting profile.
- **Naga side plates**: the 12-key and 7-key side plates report their buttons as firmware ids 64–75 and 80–86; mapping, macros and Turbo now resolve them, so every plate works.
- **Naga scroll wheel**: Naga V3 Pro exposes its three-way tactile, free-spin and micro-tactile wheel mode, including profile apply and Smart Reel.

### Fixed

- **Mouse mapping layout**: align both button lists at the top so unequal button counts no longer stagger the two columns.
- **Macro execution**: persist trigger and playback choices even before a trigger key is assigned, so restarting or switching macros keeps the selection; changing only the trigger mode also saves automatically.
- **Audio discovery**: keep separate audio devices visible together and track each wireless receiver independently; a USB sound card no longer hides a second headset.
- **Kitty Bluetooth audio**: preserve the six supported models' product identities, accept both documented response types and empty acknowledgements, and release failed GATT connections before retrying.
- **Nommo V2 and Soma Pro audio**: use their own report IDs and supported commands; restore Nommo's seven bass levels and preserve Soma's 0.1 dB EQ values without sending unrelated headset or lighting commands.
- **Bluetooth mice**: mapping and scroll settings read and write the active onboard profile instead of the first slot, and the profile list loads before the mapping table, so switching profiles no longer leaves stale mappings.
- **Naga identities**: Naga Pro, Naga V2 Pro and their Bluetooth identities are matched, and the missing private keys for 0053, 008D, 0096 and the cycle-scroll action are included.
- **Service install**: a clean MSI install now creates OpenSynapseService in the same elevation, resolving the installing user from the active console session when the MSI action runs as SYSTEM; registration is optional and no longer fails the install with 1721, and the app retries on first launch when the SID is unavailable.
- **Blade lighting**: launch and window restore reapply the saved keyboard effect; an unrelated Blade fan or thermal error no longer suppresses the lighting write. Reactive and ripple effects also light the arrow keys correctly because right Ctrl no longer shares the left-arrow LED cell.
- **Blade fan curve**: the first curve write re-declares manual fan mode, so a Blade that already reports Manual no longer keeps following its old curve or sticks at one RPM.
- **Touchpad indicator**: the lamp is re-sent after the software-mode takeover and retried until the EC confirms it, covering the autostart case.
- **02CF and 02A6 extra keys**: the single media key exposes its real Play/Pause row, the Xbox Game Bar and Cycle Apps side keys get their own names, and the 02A6 side keys are complete.
- **Tartarus Pro**: OBM reads select the keymap the device actually reports.
- **Mouse Dock Pro**: short profile-id replies from linked devices are accepted, so the onboard profile list loads and writes are no longer rejected.
- **About**: the brand credit stays untranslated.

## [1.7.3] - 2026-11-22

### Added

- **Fullscreen polling**: mice and keyboards can switch to a separate polling rate while a borderless or fullscreen app runs; the write is verified and the normal rate returns when the app exits fullscreen.
- **Synapse EQ import**: load a Synapse EQ curve into the headset equalizer and apply it to the device.
- **Keypad mapping**: Tartarus V2 and V2 Pro use the official key tables, artwork and hotspots; Synapse 2 keypads map through the P2 profile.
- **Macros and shortcuts**: macro wait steps are edited in seconds, and the mapping catalog includes a minimize-all-windows preset.
- **Seiren V2 Pro / V2 X**: microphone volume, mute, high-pass filter and limiter over the 8-byte HID report.
- **Mapping coverage**: keyboard extra keys (macro, media and side keys) and private keys now use their real names; Huntsman V3 Pro 8KHz media keys, Viper V4 Pro artwork, keypad D-pad/thumb rows, and the Tartarus Pro and Tartarus wheel layouts are drawn from the official assets.
- **Combined shortcuts**: mappings can send modifier combinations and Windows shortcut presets; Win+Tab uses the native shortcut, and shortcut sequences play as software input instead of leaving a modifier stuck.
- **Lighting Studio**: class-0x10 Chroma devices (mice, stands and docks) can be driven from the canvas; every lit RazerHid device exposes brightness; added Kraken V4 Pro and Kitty V3 Pro lighting, Kraken V4 Chroma brightness, and the remaining Kraken and Hammerhead gaps.
- **Display-off lighting**: the optional “turn off peripheral lighting while the display is off” setting covers RazerHid, Chroma and headset paths, and restores the saved lighting when the display returns.
- **Device cards**: accessory body pages, active onboard-profile reset, serial copy, and a firmware updater check and launch entry.
- **App profiles**: per-device application binding and a customizable overview; the home-layout entry moved into Settings.
- **Macros**: record actions inside OpenSynapse, bind private device keys from the macro page, and let one macro own multiple device triggers.
- **Taskbar and tray**: vertical and secondary taskbar support, a performance panel in the tray, and a continuous-macro notification toggle.
- **THX and firmware**: THX APO and V3 preset packages resolve from the product manifest; Blade 18 2025, BlackShark V3 console headsets and per-model repair packages are admitted.
- **Languages**: Japanese, Korean, German, French, Russian and Traditional Chinese were added.
- **Headset**: spatial audio switches immediately instead of waiting for the page to reload.

### Improvements

- Inactive pages, Studio resources and card art are released or decoded at display size; the THX helper idles out, lowering background memory and CPU.
- HID writes are serialized per device, retry busy replies, and bound feature-report calls on wedged endpoints; one optional card can no longer fail a whole device read.
- Mapping and macro triggers survive rescans, sleep, page teardown and re-saving; turbo stops on the physical release and no longer sticks.
- Driver installation verifies the filter INF, handles DPInst 259 and reboot-pending states, and distinguishes a dead package from a network failure.
- The taskbar strip holds through device rescans, Explorer and display changes and fullscreen transitions; Lite mode follows its own toggle.
- The autostart choice survives MSI upgrades, and the launch overlay no longer exposes scanning text to accessibility.

### Fixed

- **Command dial**: reads and writes the active onboard profile instead of a fixed profile 1; rotation is no longer emitted as a mapping key, so the dial no longer claims the F/G rows.
- **OBM keyboards**: mappings are addressed by the firmware key id, so edits land on the row the device reports.
- **Mapping and shortcuts**: BlackWidow V4 75% reads all 81 keys through the 0x9E table and writes through the key table; hotspots and names follow the firmware key id; Basilisk V3 X boss key; Razer's 10-byte single-assignment payload; Win modifier and keyboard shortcut combinations; Win+Tab and per-key macros no longer interfere.
- **Macros and turbo**: explicit down steps no longer re-enable auto release; turbo repeats on an absolute clock; driver mode is armed before the first key; each side key keeps its own trigger and a shared private key keeps its macro trigger.
- **Blade state and brightness**: game mode reads across middleware models; the touchpad indicator is verified against the EC and reasserted; keyboard brightness uses the live readout, and the 597/710-chroma families no longer scale the raw 0–255 value twice, which made 39% the maximum; the lighting path is held through boot; selecting the same CPU/GPU Boost level re-applies it so the EC budget changes; 029F, 0233, 023A, 0240, 0245, 0255, 02E0 and 02E1 declare their Logo power/mode/effect reports.
- **Dual-dongle receivers**: keyboards paired to a 00AB receiver are discovered, each device gets its own transaction bank, and the receiver no longer lists itself as a paired device.
- **Lighting writes**: Kraken V4 writes through the headset's hid.write path; class-0x10 devices write the selected profile and region; devices that cannot read back still receive lighting; the authored LED map survives a same-product import in Lighting Studio.
- **Cold boot**: saved lighting is restored per endpoint, including a dock-linked mouse that enumerates later, and a failed write is retried on the next device refresh.
- **Device refresh**: an invalid device-mode reply no longer aborts the whole Razer HID refresh, so the device list and Blade telemetry still load; the Devices and Diagnostics refresh buttons no longer crash on a failed probe.
- **DPI**: the mouse page shows the device's current DPI on first open instead of stale defaults.
- **THX manual EQ**: applies through the custom preset; leftover service registrations are repaired; service health is read from the image path.
- **Taskbar layout**: the status strip stays clear of a left-aligned Start button.
- Lighting Studio replays the last applied effect on startup and keeps it through automatic profile applies, page eviction and device takeovers; it hands the devices back when another Chroma source takes over.
- Blade: matrix frames are paced and drained so fan replies are not consumed; 60 fps matrix output is restored; fan floors follow the model; custom power levels survive mode and fan writes; HyperBoost requires verified power and pad readiness; GPU mode appears on 028C and hasGPUmode models; the Fn marker and touchpad indicator recover; Blade 18 2025 exposes the native display switch.
- Mapping: Basilisk V3 Pro writes through protocol-25 driver mode; Mouse Dock Pro paired mice expose their full command set; Viper side buttons, DPI stages and OBM data, Tartarus Pro key tables, Huntsman V3 Pro extra keys and 0098 anchors are read correctly; firmware rejecting device mode no longer blanks the page.
- Headsets: Barracuda X Chroma EQ, lighting, presets and dongle direction; Barracuda V2 X battery; Hammerhead V3 Chroma protocol40 and feature collection; Kraken V3 HyperSense 62-byte channel; Kitty V3 Pro wireless direction; Leviathan effect writes retry while busy.
- THX: no installer loop when native init fails; stopped services wake before install; newer presets are not downgraded; exit 3 means installed; reboot-pending and per-model packages are handled; the panel stays when a write is refused.
- Onboard profiles: the active profile is followed and verified, short id replies are accepted, names are read from the device, id 0 is never cached, and the selector stays on the active slot.
- Taskbar: the strip returns after Show Desktop, stays above the taskbar content bridge, hides only for real fullscreen, accepts a top taskbar, and draws its battery text with the real semibold face.
- App: stopped the .NET 10 COM wrapper registry from growing, guarded device handlers during page teardown, kept a page alive while its cache entry is created, and stopped foreground changes from rescanning every device.
- CPU and sensors: the AMD wall reads through SMU when the Razer path has no PPT; Intel temperatures fall back to the core therm status.
- Devices: the mouse low-power threshold covers the full range; Orochi V2 battery chemistry is exposed; THX installer failures are described; the onboard profile selector follows the active profile.

## [1.7.2] - 2026-11-15

### Added

- **Lite mode** keeps only the peripheral pages, skips Blade, performance and Lighting Studio features, and marks the title bar with a Lite badge.
- **Smart tracking and sensor rotation for mice**: adaptive or adjustable lift-off, independent lift-off/landing distances and rotation calibration; a failed probe no longer hides the card for known models.
- **Mapping and macros**: special keys, Viper private keys and keyboard keys can all bind software macros; HyperShift gates their triggers; the key page owns execution settings and the macro trigger card moved into the left inspector; changing a key action clears its old macro trigger.
- **Blade**: Fn row primary switch and live Fn profile (including 023A and 02C6), logo effects decoded from the Synapse bundle (including 02B6 and 029D), a redrawn fixed-fan slider with Synapse fan icons, and batched matrix frame writes for lower lighting latency.
- **Kraken V4 lighting**: completed the Billie T2 Starlight, Fire, AudioMeter and Wheel effects; the V4 static frame now follows the device ring size.
- **Mako X lighting** (Synapse parity): the strip now behaves like Synapse 4 on the same unit - effects crossfade over 1.5 s, Static/Breathing/Spectrum/Starlight/Fire/Audio Meter are rendered as per-LED frames with the same palettes and durations, rotation replays the measured 25-frame cycle, and the audio meter colours the whole strip green-yellow-red by level and goes dark in silence. The quick-effect list and its wording now follow Synapse (no Wave, no screen-sampled Ambient on the device page; Static/Breathing/Starlight/Fire read as "... effect", Audio Meter as "audio meter", Wheel as "rotation").
- **Mako X firmware and controls**: EQ presets and the 10-band curve, the six firmware lighting effects (Off, Static, Breathing, Spectrum, Wave and the per-LED CustomFrame channel), Chroma SDK frames and auto-sleep; the 65-byte Jolie frame and 64-byte feature report are handled separately.
- **Protocol-25 headsets**: EQ, microphone, input source and ANC now follow the Synapse bundles; MXIC headsets use Synapse's HID write and input-report path.
- **Lighting Studio**: gradient, ripple, tidal and radiate effects, native 25 fps device output, and faster zoom, canvas rebuild and mask caching.
- **Diagnostics**: crash reports, a log bundle export, and reports stamped with date and app version.
- **Install and update**: startup updates ask first; MSI installs update through the machine service without UAC; per-device driver installs also run through the service.
- **Onboard profiles** read the device's real id list and create the first free id; delete and reset ask for confirmation, and the active profile cannot be deleted.
- **Huntsman V3 Pro TKL / Mini / 8KHz**: analog keys are back, with TKL, Mini and full-size layouts, drawn on the same SVG as the mapping page.
- **Pair a new device**: the devices page can scan and bind a new device to a multi-device receiver, backed by the official dual-dongle compatibility table that filters candidates for the current receiver.
- **Blade software matrix lighting**: 15 Blades from 2019-2023 gain software matrix effects (Static, Spectrum, Wave and others); a rejected matrix write falls back to the existing firmware effects.
- **Headset wireless state and powered-off hiding**: the BlackShark V2 Pro (0555) reads its wireless link and hides its card when powered off; the Barracuda 2.4 GHz card hides when the link drops. Added the Barracuda V2 X and Kraken Kitty V2.
- **More battery sources in the tray**: headset, standalone keyboard and BLE peripheral battery now feed the tray menu and taskbar strip; the menu lists all of them and the strip still shows only the lowest.
- **Analog-key tooltips**: hovering any analog key shows that key's own actuation and Rapid Trigger sensitivity without selecting it first.
- **MXIC headsets**: added a do-not-disturb switch.
- **Game mode**: RazerHid devices that support the command gain a game-mode switch on their device card.
- **Macro playback**: the editor now chooses how a macro runs, once per press or repeat while the trigger is held; the choice applies to software macros on keys and on the primary mouse buttons.
- **Blade fan curve**: the curve window draws live fan RPM and CPU/GPU temperature, and point limits follow the model and the current performance mode; every fixed-fan Blade now offers the curve.
- **HyperPolling receivers**: the mouse paired to a HyperPolling dongle is discovered as its own device.
- **Performance overlay**: added an always-on-top switch, on by default; turn it off for borderless games that lose DirectFlip.
- **Shutdown lighting**: optional “Turn off peripheral lighting on shutdown” sends Off to RazerHid lighting devices and the Mako X audio-card lighting path before Windows shuts down or signs out, and restores the saved lighting after the next launch.

### Improvements

- Pages are built on first navigation, shortening startup.
- The first device scan now shows “Scanning devices...” instead of appearing stuck.
- Device cards are one compact line; four stay open before the list collapses, and a wired/wireless pair shares one card.
- NVIDIA telemetry reads through NVML instead of launching `nvidia-smi`, and GPU memory matches what `nvidia-smi` reports.
- Chroma SDK client discovery now follows the official SDK registration instead of scanning every process, removing the broker's idle CPU load.

### Fixed

- Ambient Awareness now works on desktops and clamshell laptops: when Windows reports no internal panel the capture follows the primary monitor, and when Graphics Capture is denied (`E_ACCESSDENIED`) it falls back to a 25 fps desktop copy. The copy leaves out layered windows so the screen cursor no longer flickers while the effect runs.
- Mako X Starlight, Fire and Audio Meter no longer blank the light strip: the Joliet firmware answers status `0x05` for every effect id outside `0`/`1`/`2`/`3`/`4`/`8` and leaves the LEDs dark, so those effects are now streamed as per-LED frames instead of unsupported ids.
- Mako X no longer reports a failed state query while a frame effect is running: every service opens its own HID handle, so feature-report writes and reads for the same device now share one gate instead of overwriting each other's replies.
- Mako X restores frame-rendered effects (including rotation) on launch and profile replay; the frame pump is not persistent, so it is started again from the saved effect instead of leaving the strip dark until Apply.
- Mako X frame effects keep animating through a transient HID failure instead of freezing on the first missed frame; the pump retries and only stops after repeated failures.
- Mako X keeps the startup probe view model alive until the device page takes it over; disposing that temporary probe stopped the frame pump after the first frame, which made rotation light up but stay frozen until the page was opened.
- Shutdown lighting now covers Mako X, whose Chroma channel lives on the audio HID collection rather than the RazerHid catalogue.
- BlackShark V2 Pro (0555) no longer stalls on “reading”: timed-out HID control requests are actually cancelled instead of piling up in the driver queue; USB-only headsets stay visible before HID is ready.
- Kiyo, sensor rotation, smart tracking and software-lighting cleanup no longer raise `PropertyChanged` on the thread pool, fixing the `0x8001010E` settings-page failure.
- Partial reads are informational instead of a whole-device error (Leviathan V2 X, Mako X and others), and the bass control is a slider.
- BlackShark V3 is no longer mislabelled as V2 Pro; missing headset identities were filled in.
- Razer driver setup now matches device, catalog key and installer PID, binds the staged INF by hardware ID, verifies the filter is really attached, treats 3010/1641 as success, stops rejecting republished packages by size, reinstalls the common driver during repair, and installs the DKM lower filter that carries Blade Fn input.
- The taskbar status strip returns after Show Desktop and passes clicks through to the taskbar.
- The taskbar strip keeps a headset, BlackShark or standalone keyboard battery when the tray read misses; it now falls back to the value the device page already read.
- The tray menu no longer leaves a black strip below the battery rows after the display-off item was removed.
- The launch overlay waits for the first device scan, so the device list is no longer empty during startup.
- CPU temperature now reads on AMD Ryzen as well as Intel through PawnIO, and low ACPI idle temperatures are no longer discarded.
- THX service, native DLL and startup now complete in one click.
- Fixed Blade logo effect ids, 02B6/029D reads, stale Fn state, control state and ChromaBroker startup.
- Fixed several mouse/keyboard button ids (009C DPI keys, Keypad, Basilisk Ultimate DKM, legacy mouse private keys) and mapping-page refreshes.
- Fixed the missing GPU level, CPU/GPU Boost and max-fan controls after switching to Custom performance mode; sensor rotation, analog-key layout and THX install visibility now refresh with state changes.
- The Blade 16 (2023) `029F` fixed-fan slider and fan curve reach its own 2000 RPM floor in every performance mode; every other Blade keeps the per-mode minimum its firmware publishes.
- Fixed update routing for non-MSI installs, MSI update permissions and same-version beta MSI upgrades.
- Fixed macro editor rename labels, playback controls and import status text.
- Fixed the mouse-record offsets in driver-level macros: relative movement, absolute coordinates and vertical scrolling no longer land in the wrong fields.
- The MSI now closes a running OpenSynapse before replacing files, so an overwrite install cannot leave old DLLs behind; update-task failures surface the real error.
- Fixed the keyboard Keypad mapping table, the wireless RazerHid keyboard transaction bank, and the DeathAdder V4 receiver indicator readout.
- THX enhancements stay available when the native export is missing or a read fails instead of disappearing.
- Device cards reuse the Lighting Studio product artwork.
- Keyboard lighting is saved to the app profile and restored on the next launch.
- Multiple connected Razer gamepads are all listed, and the previously selected one stays selected.
- Blade lighting remembers the colour chosen for each effect, so switching back no longer resets it.
- The Blade microphone mute light is seeded at startup and re-read after a missed notification, so F1 mute matches the real state.
- Macro steps can be moved or deleted after saving, and a recorded tap shows its release instead of only the press.
- Hold and hold-repeat macros on Razer filter mice no longer stop after the first 100 ms physical-state check; the trigger's release event ends them.
- Key bindings: bare modifier keys are written in the byte form each keyboard family expects, so Left Shift no longer fires a four-modifier chord; rows already rewritten by an earlier beta are repaired, and the full keyboard key table is read instead of stopping short.
- Onboard profiles: create and delete wait for the firmware to commit, the device's active profile is followed, id 0 is never cached, and a keyboard that rejects reset now points to create-and-delete.
- Blade Wave follows the device's LED axis, so left and right are no longer reversed on 2023 models.
- A sleeping wireless mouse is reported as a link state instead of a run of read failures.
- Analog keys verify their writes and only send changed settings; the dead zone shows 0.3 / 0.5 / 0.8 mm, and the report-mode control is hidden.
- Lighting Studio downloads and flattens multi-part SVG art, keeps static effects running on idle devices, and gives id-less media keys a selectable cell.
- Sidetone and ANC now cover more headset families; KrakenBase frames use the declared report length and stay out of the ANC path.
- Game mode uses the standard Game LED command, so the state persists on keyboards that store it.
- The driver check only judges the interfaces the package covers, so an installed receiver no longer prompts for a driver; downloaded driver packages must carry a Razer publisher signature.
- The MSI upgrade fails with the real reason instead of leaving a half-installed app when the running process cannot be closed.
- ProgramData service caches now use a SYSTEM/Administrators-only ACL, so a standard user cannot plant a junction that the service follows.

## [1.7.1] - 2026-11-09

### Fixed

- Blade diagnostics now list only the capabilities the connected model actually declares. Blade 15 (2019) Advanced (023A) no longer shows unsupported Boost, charge-limit, performance-mode and adapter rows, and a device that answers “does not support this command” is treated as unsupported instead of a failed read.
- Diagnostics no longer expose internal backend identifiers such as `HeadsetAudio`, `AudioAWKittyBLE` and `Keyboard`; accessory rows use the same localized device labels as the rest of the app.
- The onboarding guide is wider and better spaced, points at the device card the next step actually opens, and highlights the polling/DPI controls instead of the device-information card.
- Lighting effect names no longer append `Firmware` or `Software frames`; the separate render-mode selector carries that choice. The Chinese label now reads `软件渲染` instead of `软件帧泵`.

## [1.7.0] - 2026-11-08

### Added

- Chroma now covers every channel in Razer's own Chroma device manifest (327 PIDs). The REST API accepts effects on `/keyboard`, `/mouse`, `/headset`, `/mousepad`, `/keypad` and `/chromalink`, and a session may declare any of those channels instead of requiring `keyboard`.
- The Chroma SDK shim now routes ChromaLink and Keypad effects. Every connected light joins the channel Razer assigns to its PID: matrix devices receive the resized frame; single-zone devices, Aether lamps, docks, stands and ARGB controllers receive the frame's average colour; Kraken headsets receive a static colour.
- Keyboards driven only by the protocol-25 page (Pro Type, Pro Type Ultra, Pikachu, Cynosapro, BlackWidow X Tenkeyless, Huntsman Essential, Tartarus V2 Pro) now accept Chroma frames through their whole-device effect channel.
- The router snapshots each whole-device target before the first Chroma frame and restores its previous effect, colour and brightness when the session ends.
- Older mice, headsets and audio devices in the Chroma manifest now fall back to the class 0x10 whole-device static path whenever their 91-byte control interface is present, instead of requiring a hand-maintained allowlist.
- The Chroma SDK broker now reads the official Chroma SDK 4.x shared-memory protocol and is started by `OpenSynapseService`; the retired self-developed shim is archived and no longer starts with the app.
- The mapping page now covers every HID/BLE device: device art, button hotspots, software macros and onboard macros share one Macro action, with keyboard key tables, Naga side buttons, Snap Tap, analog keys and scroll-wheel modes.
- Driver-level macros are complete: the official Razer filter driver and RzCommon can be installed, and mouse and keyboard macros use the same driver injection path as Synapse; missing drivers are shown on the device page.
- Settings can download and install Razer's official `RzChromaSDK64.dll` without a browser or a separate Chroma SDK install.
- DeathAdder V4 Pro now uses its class 0x03 onboard profile dialect, fixing profile reads; BlackWidow V4 Pro wrist-rest lighting and Blade 2019-2023 logo controls are also covered.
- The taskbar status strip and performance overlay were reworked: the strip shows only peripheral battery on a transparent background and disappears while the taskbar is hidden or covered; the overlay has compact and detailed cards, and borderless games no longer inherit a topmost window.
- Kraken V4 wired lighting now keeps its HID session open, so changing colours does not require a second click.
- The project is licensed under AGPL-3.0-only, and the uninstaller can optionally remove local settings.

### Fixed

- Fixed onboard profile rejection, mappings that saved but did not work, Naga side-button software macros, unpaired Hue bridges appearing in the list, a flickering taskbar battery readout, and the performance page jumping back to the top.
- Fixed capability counts, keyboard and headset battery scaling, discrete-GPU fault states, and restoration of the previous lighting effect after a Chroma session.
- The uninstaller now removes the desktop and Start-menu shortcuts instead of leaving them behind.
- Blade Studio now uses the official per-model LED layout and frame cadence. Blade 16 2024 (7 x 15), Blade 18 2025 (8 x 18 logical / 7 x 19 device), the 2026 Blades and older 2019-2023 Blades no longer apply the wrong matrix, and static effects stay applied after the first frame.

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
- **HyperBoost no longer requires a Razer cooling pad.** On supported models (Blade 14 2023 and Blade 16 2023/2024/2025) it is selectable with a verified full-power adapter. Choosing it without a pad shows a disclaimer first and leaves third-party cooling to the user; battery, low-power adapters and an unreadable adapter wattage refuse it. With the pad attached, HyperBoost takes the pad away from its own curve and drives Razer's eight-step temperature table; unplugging the pad rolls the mode back to the previous one.
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
- HyperBoost no longer requires the Razer Laptop Cooling Pad. On models that support it (Blade 14 2023, Blade 16 2023/2024/2025) it is selectable from the performance-mode list whenever the adapter wattage was read and matches the full-power unit. Selecting it without a pad shows a disclaimer first and leaves third-party cooling to the user; battery, a lower-wattage adapter or a failed wattage read refuse it. With the pad attached, HyperBoost drives the pad from the bundle's eight-step temperature/hysteresis table and restores the pad's own mode on exit; losing the pad falls back to the previous performance mode unless the pad-less disclaimer was accepted.
- Middleware-derived mice now expose onboard button mapping: read the button list and current assignments over the bundle's `Set/Get Single Button Assignment` commands (class 0x02, 0x0C/0x8C), then remap a button on the normal or HyperShift layer to off, another mouse button, a keyboard key with Ctrl/Shift/Alt/Win, a DPI action, profile navigation, a media key, a power key, double click, a scroll-wheel mode, a Razer key, a Windows shortcut or HyperShift. Every write is verified by reading it back, and the card stays hidden when the device refuses the command.
- When the 0F1F ARGB controller is missing Razer's filter driver, the lighting page now offers Razer's driver download instead of only showing an error.
- The BlackShark V2 Pro (2020 and 2023) audio page now shows THX status: it checks the THX service (VSSrv) and the control component, installs the official THX package silently when the service is missing, and downloads plus verifies the control component when only that is missing. The package is THX-only; Synapse is not installed.
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
- Audited the 12 models (16 PIDs) that reuse a sibling protocol against the middleware bundle `DeviceInfo`: restored the missing 125/500/1000 Hz polling rates for the Abyssus Lite and removed the Viper V4 Pro from the lighting catalogue because it has no lighting.
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
