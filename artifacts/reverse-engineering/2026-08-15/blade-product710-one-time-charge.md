# Product 710 one-time full-charge override

## Result

The Blade 16 2025 does not use a separate "charge to 100 once" command.
Synapse stores the operation in bit 2 of the existing Power Mode Control mask:

```text
GET  transaction=1F size=01 class=07 command=8F args=[00]
SET  transaction=1F size=01 class=07 command=0F args=[mask]

bit 0 = CPU boost
bit 1 = maximum fan mode
bit 2 = one-time charging override
bit 3 = local dimming
```

Every Product 710 write follows a read-modify-write path. Synapse reads the
current mask, changes only the named field, then ORs all four fields into the
SET byte. OpenSynapse must preserve the three sibling bits as it already does
for maximum fan mode.

## Product policy

Product 710 declares both `hasBatteryOptimizer:true` and
`hasBatteryChargingOverrideEnabled:true`. Its battery optimizer task performs:

```text
setBatteryChargeLimiterControl(isEnabled, threshold)

if isEnabled && batteryChargingOverrideEnabled:
    set PowerModeControl.oneTimeOverride = 4
else:
    set PowerModeControl.oneTimeOverride = 0
```

The separate toggle task uses the same bit and the same read-modify-write
helper. A device-originated toggle is ignored when the charge limiter is not
enabled. The setting is also persisted in Product 710 profile storage as
`batteryOptimizer.batteryChargingOverrideEnabled`.

The bundled notification text defines the behavior as: charge the battery to
full once, then maintain the configured optimizer percentage. No JavaScript
timer or second HID command clears the override after full charge. Completion
and bit-clear timing are therefore firmware-owned and are not statically
specified by the Product 710 bundle.

## Sources

| Local source | SHA-256 | Evidence |
| --- | --- | --- |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000233` | `A0F0AF07C6CFBD1C6F03516ED63C93858F5CD0FF2E17B6A3B6D0DAFB4608A79B` | Product 710 feature flags |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00022a` | `00E18FD4D8C4F5C20E31308160C6D8599997FE26BF3C41CEF4A076440708D749` | Battery optimizer state machine, toggle task, read-modify-write helper and notification keys |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00023d` | `0E386F87468BC19CBCB52E434CF0C3737A74D5483D6BC7201EA40B5A5096B985` | Power Mode Control parser and command wrapper |

## Admission boundary

The wire contract and explicit on/off readback are source-backed. Production
control stays disabled until a current-device test proves that the firmware
clears bit 2 after reaching full charge, restores the configured charge limit,
and remains safe across restart, sleep and cancellation. Do not implement this
as a persistent `100%` charge-limit write.
