# RzLightingEngineApi v4.0.55.0 static triage

Read-only inspection of:

`%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Apps\Common\RzLightingEngineApi_v4.0.55.0.dll`

SHA-256: `CD9FC2A61FF920B9B73E1F5E27632020E2F8586F443E426A875AD3B042B714FA`

No process was started and no HID/API call was made.

## PE and exports

- AMD64 PE DLL, image base `0x180000000`, timestamp `2025-12-01 02:39:12`.
- `.text` RVA `0x1000`; `.rdata` RVA `0x199000`, raw pointer `0x198200`.
- `CreateLightingEngine`: RVA `0x45230` (VA `0x180045230`).
- `RzLightingApi`: RVA `0x33490` (VA `0x180033490`).
- `SetOperatingMode`: RVA `0x32D00` (VA `0x180032D00`).
- `UsePollingMode`: RVA `0x32D80`, a two-instruction tail jump to `SetOperatingMode(1)`.

## Export behavior from disassembly

### `SetOperatingMode`

At `0x180032D00`, the first `uint32` argument is copied to a global at `0x1801F7BF4` (low byte plus the upper 16 bits are packed into a 64-bit value). It then calls an internal event/dispatch routine with category `4`, mode-dependent string descriptors, and the original mode. For low byte `2` it takes a special path using a descriptor at `0x18019D2E8`; all other values use the normal path. `UsePollingMode` is therefore equivalent to `SetOperatingMode(1)`.

### `CreateLightingEngine`

At `0x180045230`, the wrapper allocates `0x160` bytes, initializes the object through internal routines at `0x18004B2E0` and `0x18004B6C0`, passing the second argument (`EDX`) as the mode/config value, and writes the resulting object to the third-argument output pointer (`R8`). On failure it invokes the object's release vtable entry and stores null in `*R8`; the wrapper returns the internal status code. The first argument (`RCX`) is not consumed by this wrapper before allocation/initialization.

### `RzLightingApi`

At `0x180033490`, `RCX` is treated as an opaque request object. The wrapper obtains a global service/interface at `0x1801F7B08`, checks readiness through vtable slot `0`, obtains a service through slot `8`, and requires flag `0x5` in the returned object. It converts the request through helper `0x180033600`, invokes the service vtable slot `0x10` with message/category byte `0x58`, then delivers the response through vtable slot `0x18`. The wrapper itself does not contain effect formulas or direct HID writes; those are below the service/parser layer.

## Effect/schema strings and references

The strings are ANSI data in `.rdata`; file offset to RVA is `offset + 0xE00` for this image.

| String | File offset | VA | Static reference evidence |
|---|---:|---:|---|
| `effect` | `0x19BDFF` | `0x18019CCFF` | `0x180021D4E`, `0x180021D6A`, `0x180021D86`: member lookup, type check, and value extraction/dispatch |
| `ColorStops` | `0x19BE18` | `0x18019CC18` | Repeated member lookups around `0x180021BC2`, `0x180021CB7`, `0x180021E13`; returned tagged values are converted/validated |
| `Cycles` | `0x19BE28` | `0x18019CD28` | `0x180024DB3`: member lookup followed by integer conversion (`0x18000B730`) and passed to an effect operation |
| `Pause` | `0x19BE2F` | `0x18019CD2F` | Present in the contiguous key table; no direct RIP-relative use found in the sampled text (likely table/indirect lookup) |
| `Width` | `0x19BE3A` | `0x18019CD3A` | Same contiguous key table; no direct RIP-relative use found in sampled text |
| `Speed` | `0x19BE40` | `0x18019CD40` | Same contiguous key table; no direct RIP-relative use found in sampled text |
| `Duration` | `0x19BE91` | `0x18019CD91` | Same contiguous key table; no direct RIP-relative use found in sampled text |
| `ripple`, `speed`, `effectConfig` | `0x19BFB2`, `0x19BFB9`, `0x19C06F` | `0x18019CFB2`, `0x18019CFB9`, `0x18019D06F` | Contiguous parser key table; no direct RIP-relative use found in sampled text |

The parser code around `0x180021900`-`0x180023E9F` dispatches on a JSON-like tagged value, calls a common member lookup helper (`0x180002DF0`), and converts scalar values through `0x18000B730`. This is direct evidence that `effect`/`ColorStops`/`Cycles` are consumed as structured effect configuration, not merely log text.

Separate effect names occur in log format strings, with direct references from the effect logger at `0x180051043`-`0x18005116C`:

`Breath` VA `0x1801A96DD`, `Spectrum` `0x1801A9706`, `Starlight` `0x1801A972F`, `Reactive` `0x1801A9763`, `Ripple` `0x1801A978C`, `Wave` `0x1801A97C7`, `Fire` `0x1801A9814`.

Those logger branches read fields at object offsets such as `0x54`, `0x58`, `0x5C`, `0x60`, `0x64`, `0x6C`, `0x70`, and `0x74` and format duration/width/speed/pause/rate values. This is useful for field-layout hypotheses, but it is not proof of the public JSON-to-object ABI.

## Feasibility judgment

Static evidence is sufficient to reproduce the native export boundary and to continue mapping the JSON effect schema. It is **not** sufficient to safely call the DLL standalone: the wrappers depend on a process-installed global service/event interface and opaque allocator/vtable state. The lowest-risk next step is offline parser/object mapping (including the helper functions and the existing Ghidra project), while treating direct `RzLightingApi` invocation as unsafe until the required interface initialization and ownership contract are proven.
