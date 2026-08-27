# OpenRazer frontend handoff

Updated: 2026-08-27

This document covers the new generic OpenRazer backend only. Existing Blade 16
(`1532:02C6`) and Viper V3 HyperSpeed (`1532:00B8`) pages keep using their current
product-specific ViewModels and setters.

## Entry point

Create one application-scoped `OpenRazerDeviceService` and call:

```csharp
IReadOnlyList<OpenRazerDeviceConnection> devices =
    await service.DiscoverAsync(cancellationToken);
```

Do not create tabs from the 254-entry catalog. Create a tab only for a connection
returned by `DiscoverAsync`; those entries correspond to currently present 91-byte
HID devices. Use `connection.Definition.DisplayName` and
`connection.Definition.Category` for the label and icon. Use `connection.InstanceId`
as the item key; two connected devices with the same PID are returned separately.

## Endpoint states

| `OpenRazerEndpointState` | UI behavior |
|---|---|
| `Resolved` | Show sections allowed by `connection.Capabilities`. |
| `RecognizedButUnresolved` | Show the device and one unavailable-state message. Do not render write controls. |
| `BusyOrUnavailable` | Show the device as busy/unavailable and provide a rescan command. Do not render write controls. |

`connection.Error` is diagnostic text. It may be placed in an expandable details
area, but it is not suitable as the primary localized user message.

After an I/O failure the backend invalidates the cached endpoint. Rescan with
`DiscoverAsync` before allowing another write.

## Device metadata

`OpenRazerDeviceDefinition` exposes:

- `ProductId`, `DisplayName`, `Category`
- `MaximumDpi`
- `PollingRates`
- optional `MatrixDimensions`

Frontend visibility must use `OpenRazerDeviceConnection.Capabilities`, not the raw
OpenRazer source method names, which are intentionally internal. The normalized enum
also requires a resolved Windows transaction and endpoint, so it is the production gate.

## Sections and controls

| Section | Required backend capability | Service methods / model |
|---|---|---|
| Device information | `FirmwareRead`, `SerialRead` | `GetFirmwareAsync`, `GetSerialAsync` |
| Device mode | `DeviceModeRead` | `GetSoftwareModeAsync` |
| Battery | `BatteryRead`, optionally `ChargingRead` | `GetBatteryPercentAsync`, `GetChargingAsync` |
| Polling rate | `PollingRateRead` / `PollingRateWrite` | `GetPollingRateAsync`, `SetPollingRateAsync`; options from `Definition.PollingRates` |
| DPI | `DpiRead` / `DpiWrite` | `GetDpiAsync`, `SetDpiAsync`; maximum from `Definition.MaximumDpi` |
| DPI stages | `DpiStagesRead` / `DpiStagesWrite` | `GetDpiStagesAsync`, `SetDpiStagesAsync`, `OpenRazerDpiStages` |
| Power saving | `IdleTimeoutRead` / `IdleTimeoutWrite` | `GetIdleTimeoutAsync`, `SetIdleTimeoutAsync` (60..900 seconds) |
| Low battery warning | `LowBatteryThresholdRead` / `LowBatteryThresholdWrite` | `GetLowBatteryThresholdAsync`, `SetLowBatteryThresholdAsync` (5..25%, step 5) |
| Lighting brightness | `BrightnessRead` / `BrightnessWrite` | `GetBrightnessAsync`, `SetBrightnessAsync` |
| Lighting effect | `LightingEffectWrite` | `SetLightingAsync`, `OpenRazerLightingSettings` |
| Per-zone LED state | `LedStateWrite` | `SetLedStateAsync`; show only where the zone reports `CanWriteState` |
| Matrix custom frame | `MatrixFrameWrite` | `SetCustomRowAsync`; dimensions from `Definition.MatrixDimensions` |
| Reactive trigger | `ReactiveTriggerWrite` | `TriggerReactiveAsync` |
| Scroll wheel mode | `ScrollModeRead` / `ScrollModeWrite` | `GetScrollModeAsync`, `SetScrollModeAsync` |
| Scroll acceleration | corresponding `ScrollAcceleration*` | getter/setter methods |
| Smart Reel | corresponding `SmartReel*` | getter/setter methods |
| Fn primary behavior | `FnPrimaryWrite` | `SetFnPrimaryAsync` |
| Keyswitch optimization | corresponding `KeyswitchOptimization*` | `OpenRazerKeyswitchOptimization`, getter/setter methods |
| HyperPolling indicator | `HyperPollingIndicatorWrite` | `SetHyperPollingIndicatorAsync` (modes 1..3) |
| HyperPolling pairing | `HyperPollingPairWrite` / `HyperPollingUnpairWrite` | pairing methods; require an explicit confirmation dialog |

For a read-only capability, show the value without an enabled editor. Hide a
section when none of its capabilities are present. Never infer support from device
category, product name, maximum DPI, or matrix dimensions.

## Initial state load

`ReadBasicStateAsync` is the compact initial load for firmware, serial, device mode,
battery, charging, polling rate, DPI, idle timeout, low-battery threshold and
brightness. Individual failures are returned in `OpenRazerBasicState.Errors`; one
failed field must not replace all other values with `--`.

Load DPI stages, scroll settings and lighting details only when their section is
opened. These require separate calls and should not slow initial device discovery.

## Lighting UI

`connection.LightingZones` is the authoritative lighting contract. Its key is an
`OpenRazerLedZone`; each `OpenRazerLightingZoneCapabilities` value independently
publishes `CanReadBrightness`, `CanWriteBrightness`, `CanWriteState`, and
`LightingEffects`. Build a zone selector only from these entries, then build the
controls for the selected zone from that entry. Do not form a Cartesian product of
all zones and all effects.

`SupportedLedZones` and `SupportedLightingEffects` are aggregate convenience views.
They are useful for a summary, but they are not sufficient to enable a write control.
For example, one device can expose brightness on `All` and matrix effects on
`Backlight`. The backend resolves a missing zone separately for brightness GET,
brightness SET, and each default effect; the frontend may omit `Zone` when applying
the backend-selected default.

Use color pickers for `Primary` and `Secondary`, a small speed selector for reactive,
breathing and starlight, and a direction selector for wave/wheel. Do not expose
transaction IDs, builder names, storage bytes or LED IDs as free-form inputs.

## Refresh and errors

- Cancel outstanding reads when a device tab closes or a rescan starts.
- Disable only the control whose write is in progress.
- After a write, call its typed getter when available and display the read-back value.
- On `NotSupportedException`, remove/disable that control for the current connection.
- On `InvalidOperationException`, `IOException`, or `Win32Exception`, rescan because
  the endpoint cache may have been invalidated.
- Do not fall back to sending a SET through another HID collection.

## Not included in this frontend pass

- The standard 91-byte catalog does not include ARGB, Kraken, or legacy fixed-report devices.
  Kraken is exposed by `OpenRazerSpecialLightingService` only after an `MI_03` 37-byte output
  endpoint resolves. ARGB and DeathAdder 3.5G setters remain hidden because their payload
  codecs exist but Windows arbitrary control-transfer transport is not implemented.
- No generic macro editor or onboard mapping editor is exposed by this backend.
- Existing Blade/Viper pages must not be converted to the generic page.
- Device-mode writes, LED state reads, and the legacy `standard_set_led_effect`
  function are not exposed because this source snapshot does not preserve an
  unambiguous capability-to-builder binding for them.
