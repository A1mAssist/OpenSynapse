# Product 710 native and service contracts

## Scope and result

This is a read-only static audit of the Product 710 paths selected by its own
feature manifest. No hardware command or native setter was executed.

Product 710 selects:

```text
productId                    710 (1532:02C6)
rzDeviceType                 rzDevice25LedMatrixBlade2
thxVersion                   thxv4
deviceUI.performance         hasNativeDisplayMode, hasLocalDimming
deviceUI.power               hasCustomColorProfile
deviceUI.deviceSetting       bladeSeries = 10
systemHelper                 RzSystemCommon / RzSystemService
```

The important transport correction is that `Native Display Mode` is not one
closed native call. The panel-mode GET/SET is HID. Only the following reboot
uses the Razer system-helper channel. HDR state and ICC/CTR operations use that
native channel. THX uses a separate `ThxV4Native` FFI channel.

On 2026-08-16 the current `1532:02C6` control collection returned success for
both display GETs: native mode `0` (`UHD`) and SKU flags `0x00`. Both responses
set the remaining-packets field to `0x0001`; OpenSynapse admits that quirk only
for `0D8E` and `0D8F` while still checking transaction, class, command,
declared size, status, and CRC. The formal capture is
`artifacts/protocol/2026-08-16/source-backed-read-04.json`.

## Evidence

| File | SHA-256 | Role |
| --- | --- | --- |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000233` | `A0F0AF07C6CFBD1C6F03516ED63C93858F5CD0FF2E17B6A3B6D0DAFB4608A79B` | Product 710 manifest and defaults |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00022a` | `00E18FD4D8C4F5C20E31308160C6D8599997FE26BF3C41CEF4A076440708D749` | Product-selected task runners, THX v4 caller and wrapper |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00016f` | `C99877121C3D00B26146A8062031C5BDE73803CF15CA3001FE08F2FEC7436D44` | MiniLED/SKU HID report implementation |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000241` | `D51B8FDBF254679FCB1390417D124EA97C3F9670445C02C12A3395A48B1EC475` | Product 710 `rzDevice25LedMatrixBlade2` native-helper actions |
| `C:\Program Files\Razer\Blade10Tools\CTR.dll` | `255DC26B0F2A5512D2EF96790DE504633EB075F948F35D34517D9445E4CF032E` | Razer/Portrait display-profile engine |
| `%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Apps\Common\bladeCommon\Razer_GetColorFilesAdmin.exe` | `B228EA95F92533850507163BD5261C65FEFE98D732E4411141C5A422C51600BC` | Color-file recovery helper |
| `%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Apps\Common\thxv4common\ThxV4Native_v1.1.1.0.dll` | `8E8F6923624762935D70B72F58BEC00808DB84DF740340047919B53331130A0A` | THX v4 native implementation |

The two files checked by the color restore gate are present:
`C:\ProgramData\Portrait Displays\Razer\cvt_DisplayP3.csv` and
`C:\ProgramData\Portrait Displays\Razer\Native.bin`.

## THX v4 actual Product 710 path

### Lifetime and endpoint selection

Product 710 constructs `AudioEffectsTHXV4(710,
"SOME_DEVICE_CONTAINER_ID")`, resolves `THXNativeDLLV4Common`, then calls:

```text
initElectron(dllPath)
  -> ConfigureFFI(dllPath, exported API signature map)
  -> ConfigureFFI_APIToCallWhenExit("Terminate")
  -> GetDLLVersion()
  -> SetNodeFFIEvent()
getVersion()
```

It recognizes only these exact Windows endpoint names:

```text
Speakers (Realtek(R) Audio Codec with THX Spatial Audio)
Headphones (Realtek(R) Audio Codec with THX Spatial Audio)
```

It obtains them through `simpleEnumerateAudioDevices()` and
`simpleGetDefaultSpeaker()`. A matching default endpoint causes:

```text
addDevice("rznb11", "speakers" | "headphones")
  -> AddDevice([JSON.stringify({ productId: "rznb11",
                                endpointId: "speakers" | "headphones" })])
checkStatus()
  -> CheckStatus([deviceInfoJson])
setRenderEQEnabled(true)
  -> SetProperty([deviceInfoJson, "eq_enabled", "true", undefined])
```

On teardown it calls `setRenderEQEnabled(false)` and `removeDevice()`, the
latter becoming `RemoveDevice([deviceInfoJson])`.

### Product-used settings

| UI operation | Product 710 wrapper calls | Native FFI arguments |
| --- | --- | --- |
| THX spatial | `getRenderSpatialProcessing()` / `setRenderSpatialProcessing(enabled)` | `GetProperty([deviceInfo,"spatial_enabled"])`; `SetProperty([deviceInfo,"spatial_enabled",JSON.stringify(enabled),undefined])` |
| Equalizer | `selectPreset("eq_presets", preset)` then `setRenderEQGains(gains)` | Presets map `game->game`, `movie->movie`, `music->music`, `voice->podcast`, `custom->custom`; ten Product 710 gains become `SetProperty` for `eq_band_1_gain_db` through `eq_band_10_gain_db`, JSON number, type `"Float"` |
| Active application preset | `selectPreset("standard", presetName)` when no explicit gain array is supplied | `SelectPreset([deviceInfo,"standard",presetName])` |
| Sound normalization enable | `getRenderNormalization()` / `setRenderNormalization(enabled)` | property `volume_leveling_enabled` |
| Sound normalization level | intended read plus `setRenderNormalizationLevel(level)` | property `volume_leveling_level`; SET uses type `"Float"`, accepted UI range is 0..100 |
| Voice clarity | `get/setRenderVoiceClarity(enabled)` and `get/setRenderVoiceClarityLevel(level)` | properties `voice_clarity_enabled` and `voice_clarity_level`; level SET uses `"Float"`, accepted UI range is 0..100 |
| Speaker volume/mute | `simpleGetDefaultSpeaker()`, `simpleGetSpeakerVolume(deviceId)`, `simpleSetSpeakerVolume(deviceId, muted, level)` | This path does not call `ThxV4Native`; it is a Windows endpoint-volume helper |

Static defect: the Product 710 THX v4 normalization task calls
`getRendererNormalizationLevel()` (with `Renderer`), but the v4 class exposes
`getRenderNormalizationLevel()`. No definition of the former exists in the
audited bundles. The task catches the resulting error, so the current bundle
does not establish a working normalization-level read. This must not be
silently corrected in OpenSynapse without runtime evidence.

Generic v3/capture methods, microphone controls, head tracking, bass boost,
speaker gain and the rest of the broad `ThxV4Native` wrapper are not Product
710 admissions: no selected Product 710 caller was found for them.

## Native Display Mode is a split transport

Initialization calls both:

```text
getMiniLEDPanelResolution()
  GET size=01 class=0D command=8E args=[00]
  result jsonData.panelResolution = data[0]

getSKUHardwareConfiguration()
  GET size=01 class=0D command=8F args=[00]
  result masks: dds=01, miniLedResolution=02, illegalBatterySupport=04
```

The UI write task accepts only the declared enum `UHD=0` or `FHD=1`:

```text
setMiniLEDPanelResolution(mode)
  SET size=01 class=0D command=0E args=[mode]

restartSystem()
  -> RzSystemCommon/RzSystemService action "RestartSystem"
```

Windows display APIs can select an already exposed resolution, but they do not
replace this panel firmware mode. OpenSynapse can use the source-backed HID
GET/SET after separate physical validation. It does not need the proprietary
service merely to reboot: a user-directed reboot or documented Windows reboot
API covers that final step.

## HDR and local dimming boundary

Because Product 710 declares `hasLocalDimming`, startup executes:

```text
getBladeMonitorWindowsHDRMode()
  -> native action "GetBladeMonitorWindowsHDRMode" with no arguments
```

That is an actual Product 710 read. The corresponding generic
`setBladeMonitorWindowsHDRMode(enabled)` exists in the shared class but is not
the Product 710 write path. `bladeSeries === 10` instead writes local dimming as
Power Mode Control bit 3, documented separately in
`blade-product710-local-dimming.md`.

The HDR read can be replaced by documented Windows display configuration APIs,
for example `QueryDisplayConfig` plus
`DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO` (or WinRT `AdvancedColorInfo`). No
Razer service is required to display whether Windows HDR is active. This audit
does not admit an HDR setter because Product 710 does not call the generic one.

## ICC and Razer CTR path

Product 710's `hasCustomColorProfile` branch actually performs this lifecycle:

```text
colorProfileSetCTRDLLPath("C:\\Program Files\\Razer\\Blade10Tools\\CTR.dll")
  -> ColorProfileSetCTRDLLPath([path])

colorProfileStart()
  -> ColorProfileStart
  retries at most three times with exponential 500 ms base delay

colorProfileGetNameList()
  -> ColorProfileGetNameList
  -> JSON.ColorProfileNameList

getColorManagementICCProfiles()
  -> GetColorManagementICCProfiles
  -> JSON { profiles, defaultProfile }

colorProfileStop()
  -> ColorProfileStop
```

If a returned profile filename belongs to `ColorProfileNameList`, selecting it
calls `colorProfileSetDisplayMode(index)` ->
`ColorProfileSetDisplayMode([index])`. Other installed filenames call
`setColorManagementDefaultICCProfile(fileName)` ->
`SetColorManagementDefaultICCProfile([fileName])`.

The product defaults mention `Blade.icm` and `Blade2.icm`, but the bundle does
not define what colorspace each name means. Missing Razer calibration files are
handled by `Razer_GetColorFilesAdmin.exe`; the presence check specifically uses
`cvt_DisplayP3.csv` and `Native.bin`.

Windows Color System APIs can enumerate installed profiles and get/set a
display's default ICC association (`WcsGetDefaultColorProfile`,
`WcsSetDefaultColorProfile`, and related association APIs). That is sufficient
for switching already-installed ordinary ICC profiles. It cannot reproduce
CTR's calibrated preset generation/recovery or infer the semantics of Razer's
index-based modes. Those remain dependent on the proprietary native assets.

## System detail and firmware-update reads

The remaining Product 710 Blade-helper reads are native service actions, not
hidden `02C6` feature reports:

| Product wrapper | Service action | Product use |
| --- | --- | --- |
| `getBladeScreenResolution()` | `GetBladeScreenResolution` | Parses JSON `ScreenWidth` / `ScreenHeight`; Windows display APIs are equivalent for the active panel |
| `getBladeDisplayData()` | `GetBladeDisplayData` | Parses read-only display inventory JSON |
| `getBladeFirmwareInfo()` | `GetBladeFirmwareInfo` | Vendor firmware inventory/update presentation |
| `getBladeDriverInfo()` | `GetBladeDriverInfo` | Vendor driver inventory |
| `getAllBladeInfo()` | `GetAllBladeInfo` | Saves returned JSON as a user-selected diagnostic `.synapse4` file |
| `getBladeLEDPanelProducerShortName()` | `GetBladeLEDPanelProducerShortName` | BOE-only firmware-update eligibility gate |
| `getBladeUSB4RootRouterFWVersion(vid,pid)` | `GetBladeUSB4RootRouterFWVersion`, args `[vid,pid]` | Thunderbolt/USB4 updater version comparison |

The last three updater checks do not control hardware and are excluded with the
vendor firmware updater. `GetAllBladeInfo` is a diagnostic export, not a device
capability. Screen resolution has a documented Windows equivalent. The exact
internal collection logic for Razer's aggregate firmware/display JSON remains
inside the proprietary helper, but there is no missing HID packet to reverse or
admit into OpenSynapse.

## Admission boundary

| Capability | Static status | OpenSynapse admission |
| --- | --- | --- |
| THX endpoint enumeration and speaker volume | Windows-equivalent path identified | Use Core Audio APIs; no Razer dependency needed |
| THX spatial/EQ/normalization/voice clarity | Exact Product 710 properties and arguments identified | Still proprietary; do not expose without a compatible THX APO/native contract and runtime validation |
| MiniLED native display mode | Exact HID reports identified | Source-backed only; physical read/change/readback/restore and reboot behavior still required |
| Windows HDR state | Product 710 native GET identified; documented Windows equivalent exists | Implement read through Windows API, not the Razer helper |
| Generic HDR SET | Shared method only; Product 710 does not call it | Rejected |
| Installed ICC enumeration/default selection | Exact native calls identified; Windows equivalent exists | Implement with Windows Color System after display-target validation |
| CTR calibration/preset restore | Exact Razer lifecycle identified | Proprietary and not reproducible from these bundles alone |
| System detail aggregate / driver inventory | Exact service action names and Product consumers identified | Read-only proprietary diagnostics; no HID admission |
| Panel-vendor and USB4 firmware checks | Exact action names and USB4 `[vid,pid]` arguments identified | Excluded with vendor firmware updates |

No production code was changed. No HID SET, native setter, service action or
hardware validation was performed.
