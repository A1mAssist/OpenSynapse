# Blade Product 710 startup-animation protocol

## Result

The current Product 710 bundle does use startup-animation control. It is gated
by the Blade firmware version and is unrelated to the generic OLED mode strings
that were previously used to exclude the feature.

No HID report was sent during this static audit.

## Exact local sources

| Source | SHA-256 | Relevant evidence |
|---|---|---|
| Product 710 `main.2c094ef7118d3f0c24c7.js` cache entry `4520b62b873ad8c3_0` | `AD12E1536ED95E4652EFD38A9E4358CDD202266B531F809030A86E308FDC7258` | Declares `startupAnimationControlFWVersion="1.08.00"` |
| Current shared MW chunk `a2cb83e8876bf761_0` | `8E04D33B17C5C16ACA94D0546F53C47D85E200DF458125DB50C1E77804237153` | Firmware gate, GET on startup, and `ON_SET_STARTUP_ANIMATION` task |
| Current `rzDevice25` chunk `279af1be02badacf_0` | `90180855158483E086D6175E57376BFDD68495FEB80CAF881D5690674A353358` | Exact descriptors, arguments, and response parser |

These are the entries under the current Service Worker cache container
`66cde48a-183d-4c4f-b70d-36dab581523d`. Older Program Files cache entries are
not evidence for this conclusion.

## Product call chain

```text
startupAnimationControlFWVersion = 1.08.00
  -> compare runtimeData.firmwareInfo.currentFWVersion
  -> publish MW_SET_IS_STARTUP_ANIMATION_SUPPORTED when current >= required
  -> StartupAnimationHandler.getStartupAnimationStatus()
  -> rzDevice.getStartupAnimationControl()

ON_SET_STARTUP_ANIMATION
  -> enabled ? disableAnimation=0 : disableAnimation=1
  -> rzDevice.setStartupAnimationControl(profileId=0, disableAnimation)
```

The version gate is part of the capability contract. Product identity alone is
not enough to expose the control.

## Wire contract

All reports use the Protocol 2.5 91-byte feature-report envelope.

| Operation | Data size | Class | Command | Arguments |
|---|---:|---:|---:|---|
| GET | `01` | `0F` | `98` | `[00]` |
| SET | `02` | `0F` | `18` | `[profileId=00, disableAnimation=00|01]` |

The official response parser accepts either:

```text
[disableAnimation]
[profileId, disableAnimation]
```

`disableAnimation=0` means enabled and `1` means disabled. OpenSynapse now has
strict builders and a parser for both response forms, but no production sender.

## Admission decision

- Status: `SourceBacked`.
- GET may be added to an opt-in validation tool after the firmware version is
  confirmed to be at least `1.08.00`.
- Production SET remains disabled until a current-device GET, same-value SET,
  changed-value readback, visual confirmation, and exact restore all succeed.
- OLED remains excluded; this finding proves startup animation only.

## Adversarial review

1. The current Product selector and the consumer call site both exist; this is
   not a generic-enum inference.
2. The firmware gate is retained instead of treating every Product 710 as
   writable.
3. A successful SET ACK alone will not promote the feature; state and visual
   behavior must be restored.
4. The one-byte and two-byte response forms are both handled without weakening
   transaction, command, status, or CRC validation.
