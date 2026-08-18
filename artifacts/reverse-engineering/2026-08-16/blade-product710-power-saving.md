# Blade Product 710 power-saving boundary audit

## Result

Product 710 does not use the shared `rzDevice25` battery and automatic-sleep
reports as a Blade device capability. No HID report was sent in this static
audit.

The product runtime explicitly advertises `readSystemBattery=true`, keeps the
device `battery` state machine at `powerOff`, and obtains laptop power state
through the Windows/system battery path. Its lighting power-saving controls are
host policy stored under `switchOffLighting`; display-off, system-idle, and
battery conditions eventually call the already verified keyboard-brightness
operation.

The reports below exist in the shared dependency but are therefore excluded
from Product 710:

| Shared operation | Header `[size,class,command]` | Layout |
| --- | --- | --- |
| Battery level | `02/07/80` | battery object and raw level |
| Charging status | `02/07/84` | battery object and raw status |
| Get / set auto sleep | `02/07/88`, `02/07/08` | battery object and enabled flag |
| Get / set sleep time | `02/07/83`, `02/07/03` | big-endian seconds |

## Evidence

The current Product 710 runtime logs repeatedly expose:

```text
readSystemBattery: true
battery: powerOff
switchOffLighting: {
  isIdleEnabled: false,
  idleMinutes: 30,
  activeMode: plugged
}
```

The default Product 710 profile has `switchOffLighting` and separate plugged /
battery brightness policy. It does not contain a device `powerSaving` object.
The retained physical GET artifacts returned successful reports whose decoded
values were all zero:

```text
artifacts/protocol/2026-08-14/blade-battery-sleep-before.json
artifacts/protocol/2026-08-14/blade-battery-sleep-before-retry.json
artifacts/protocol/2026-08-14/blade-battery-sleep-after.json
```

A success ACK with zero data proves only that the shared command was accepted;
it does not prove that this Blade owns or implements the feature.

## Admission decision

- Remove the four reports from the Product 710 manifest, probe catalog,
  production telemetry, frontend contract, and UI.
- Do not retain SET builders for auto sleep or time-to-sleep.
- Use Windows system battery data for laptop battery display.
- Implement lighting idle/display/battery behavior as App-owned host policy on
  top of the verified keyboard-brightness command.
- Do not re-admit these reports without a future Product 710 call chain or
  device-specific non-zero behavioral evidence.

## Adversarial review

1. A method in `rzDevice25` is shared surface, not Product 710 evidence.
2. A successful HID status is not proof of feature ownership.
3. Calling the zero byte a valid `0%` battery value would create a false UI.
4. System sleep and lighting switch-off are different behaviors; neither should
   be inferred from the shared automatic-sleep report.
