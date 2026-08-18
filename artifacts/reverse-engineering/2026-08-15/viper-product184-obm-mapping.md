# Viper Product 184 onboard mapping protocol

## Result

Product 184 does use the Razer onboard-memory mouse engine. Button assignments
and the Normal/HyperShift layers are stored in the device through Protocol 2.5,
not only in Synapse AppEngine JSON.

The exact single-assignment commands are recovered. OpenSynapse has strict GET
and SET builders plus a validation-only transaction. Production wiring remains
absent even though the current-device same-value write, minimal change,
readback, layer-isolation check, and exact restore have succeeded.

## Sources

| Source | Role | SHA-256 |
| --- | --- | --- |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000081` | Generic `rzDevice25` command builders, response decoders, function and mode enums | `14BC86FAE481354C3F9593973C636103027E29F9AC76B7813B8E5E6AF5559C48` |
| `products_184_mw *.log` under the local AppEngine logs directory | Product 184 `obmEngineMouse` startup and actual device-derived Profile/button assignments | Local evidence; the container identifier is deliberately omitted |

The dependency parses a single button response into:

```text
classId/profileId, buttonId, mode, functionId, dataSize, data[0..4]
```

Its setter constructs the same ten logical bytes and pads the remainder of the
declared 80-byte argument area with zeroes.

## Profile and button metadata

All reports use Product 184 transaction ID `0x1F` and the 91-byte Razer feature
report envelope.

| Operation | dataSize | class/id | Arguments or response |
| --- | ---: | --- | --- |
| Get maximum Profiles | `0x01` | `05/8A` | response byte `maxProfilesSupported` |
| Get Profile count | `0x01` | `05/80` | response byte `numOfProfiles` |
| Get Profile ID list | `0x50` | `05/81` | response `[count,id0,...]` |
| Get active Profile | `0x01` | `05/84` | response byte `profileId` |
| Get button ID list | `0x50` | `02/84` | response `[count,buttonId0,...]` |

The actual Product 184 logs repeatedly show:

```text
maxProfilesSupported = 1
numOfProfiles         = 1
profileIdList         = [1]
buttonIdList          = [1,2,3,4,5,9,10,96]
```

Therefore this product has one fixed onboard Profile. The generic dependency
also contains create/delete/select/name/reset/color Profile commands, but their
presence is not Product 184 capability evidence. A multi-slot UI would be
incorrect for this device.

Product 184 initializes `obmEngineMouse` with these relevant options:

```json
{
  "macros": false,
  "dpi": true,
  "pollingRate": true,
  "resetProfile": false,
  "killSwitch": false
}
```

This also means generic macro-memory and Profile-reset commands must not be
attributed to this product.

## Single button assignment

### Read

```text
transaction = 0x1F
dataSize    = 0x50
class/id    = 02/8C
arguments   = [profileId, buttonId, mode]
```

### Write

```text
transaction = 0x1F
dataSize    = 0x50
class/id    = 02/0C
arguments   = [
  profileId,
  buttonId,
  mode,
  functionId,
  functionDataSize,
  functionData0,
  functionData1,
  functionData2,
  functionData3,
  functionData4,
  00 ... padding to 0x50
]
```

`functionDataSize` must be `0..5`. The decoder always exposes five physical
data bytes but only the declared prefix is meaningful. `mode=0` is Normal and
`mode=1` is HyperShift. This is the layer selector inside board storage; it is
not a second Profile.

The current Product 184 baseline read by Synapse shows both layers for all
eight buttons. Buttons `1,2,3,4,5,9,10` use `functionId=1` (`ButtonCode`) with
one data byte equal to the original button code. Button `96` uses
`functionId=6` (`DPI`) with one data byte `6` (`DPI_CycleUp`).

## Function ID boundary

The shared dependency defines these IDs:

| ID | Shared name | ID | Shared name |
| ---: | --- | ---: | --- |
| `0` | Off | `10` | MediaKeys |
| `1` | ButtonCode | `11` | DoubleClick |
| `2` | KeyCode | `12` | ModeButtonkey |
| `3` | MacroTypeI | `13` | TurboModeKey |
| `4` | MacroTypeII | `14` | TurboModeButton |
| `5` | MacroTypeIII | `15` | MacroTypeIV |
| `6` | DPI | `16` | Controller |
| `7` | Profile | `17` | RazerKey |
| `8` | Lighting | `18` | Win8ShortcutsKey |
| `9` | PowerKeys |  |  |

This table proves how the common decoder labels bytes. It does not prove that
Product 184 accepts every function. The current Product 184 bundle now closes
that product-level boundary: it exposes Off, ButtonCode, KeyCode, DPI,
MediaKeys, DoubleClick, ModeButtonkey, TurboModeKey, and TurboModeButton.
Macro, Profile, Lighting, PowerKeys, Controller, RazerKey, and Windows shortcut
functions are excluded by the product specification. HyperShift uses
ModeButtonkey `12/[1]`, not the shared RazerKey `17/[89]` representation. See
`artifacts/reverse-engineering/2026-08-16/viper-product184-product-scope-and-obm-capabilities.md`
for the exact online bundle version and `OBMSpecs` evidence.

## OpenSynapse implementation

The strict protocol and validation implementation is in:

```text
src/OpenSynapse.Windows/Protocols/ViperObmProtocol.cs
tools/OpenSynapse.HardwareValidation/ViperObmReadValidation.cs
tools/OpenSynapse.HardwareValidation/ViperObmWriteValidation.cs
```

It validates the Razer envelope, transaction, command, CRC, echoed Profile and
button, unique non-zero ID lists, the Product 184 function whitelist, and the
maximum five-byte function-data layout. The response mode byte is not trusted:
this device returns `1` even for a Normal request, so the validated request
context determines the layer. The production telemetry reader now enumerates
all 16 assignments and exposes a single-assignment transaction with separate
GET readback, sibling-layer verification, and non-cancelable restoration.

## Physical validation result

On 2026-08-15, the validation-only transaction targeted Profile `1`, button
`5`, HyperShift only. It wrote the original `ButtonCode [5]` unchanged and read
it back, wrote `Off []` and read it back while Normal remained `ButtonCode [5]`,
then restored HyperShift to `ButtonCode [5]` and read both layers again. A new
process subsequently read all 16 assignments and found both button-5 layers at
their original values.

Evidence:

```text
artifacts/protocol/2026-08-15/viper-obm-write-01.json
artifacts/protocol/2026-08-15/viper-obm-read-08-after-write.json
```

## Physical validation sequence

### Phase A: read-only baseline

1. Fully exit the Synapse UI, wake the Viper, and leave the dongle connected.
2. Run `--viper-obm-read` to a new JSON artifact path.
3. Require Profile metadata `max=1`, `active=1`, `ids=[1]` and exactly the eight
   known button IDs.
4. Require 16 assignments, one Normal and one HyperShift record per button.
5. Preserve the complete JSON as the restore source before enabling any SET.

### Phase B: reversible write (completed)

1. Back up button `5` Normal and HyperShift and require the known default
   `ButtonCode [5]` baseline before the first write.
2. Write HyperShift unchanged and require exact `028C` readback.
3. Write HyperShift `Off`, require exact readback, and require Normal to remain
   `ButtonCode [5]`.
4. Restore HyperShift in a non-cancelable `finally` and read both layers again.
5. Exit the writer process and run the full reader in a new process; require all
   16 assignments and the original button-5 values.

Disconnect and sleep/resume persistence remain separate lifecycle checks for
the eventual production integration; they are not needed to establish the
wire format or restoration behavior.

Any failed restore keeps production SET disabled and must leave the original
baseline artifact intact.
