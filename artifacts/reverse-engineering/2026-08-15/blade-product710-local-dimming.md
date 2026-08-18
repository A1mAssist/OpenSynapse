# Product 710 local dimming path

## Result

Product 710 is configured as Blade series 10 with `hasLocalDimming:true`.
For this product, Synapse does not use the generic
`setBladeMonitorWindowsHDRMode` native path. It reads and writes bit 3 of the
same Power Mode Control mask used by maximum fan mode and the one-time charge
override:

```text
GET  transaction=1F size=01 class=07 command=8F args=[00]
SET  transaction=1F size=01 class=07 command=0F args=[mask]

local dimming disabled = mask & 08 == 0
local dimming enabled  = mask & 08 != 0
```

The Product 710 task runner branches on `bladeSeries === 10` and calls the
shared read-modify-write helper with field `localDimming` and value `8` or `0`.
Only other Blade series call `setBladeMonitorWindowsHDRMode`. The helper first
reads all four fields, changes the named field, and writes their combined mask,
so sibling bits 0..2 must be preserved.

## Sources

| Local source | SHA-256 | Evidence |
| --- | --- | --- |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_000233` | `A0F0AF07C6CFBD1C6F03516ED63C93858F5CD0FF2E17B6A3B6D0DAFB4608A79B` | Product 710 declares `hasLocalDimming:true` and `bladeSeries:10` |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00022a` | `00E18FD4D8C4F5C20E31308160C6D8599997FE26BF3C41CEF4A076440708D749` | Series-specific local-dimming task and read-modify-write helper |
| `C:\Program Files\Razer\RazerAppEngine\User Data\Default\Cache\Cache_Data\f_00023d` | `0E386F87468BC19CBCB52E434CF0C3737A74D5483D6BC7201EA40B5A5096B985` | Power Mode Control bit parser and GET/SET wrappers |

## Admission boundary

This removes the protocol uncertainty for Product 710 but does not make the
setting hardware-verified. OpenSynapse may add a strict source-backed parser
or validation probe using the existing Power Mode Control report. Production
SET remains disabled until a current-device read, minimal reversible change,
readback and exact mask restore all succeed.
