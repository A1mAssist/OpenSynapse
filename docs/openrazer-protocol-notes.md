# OpenRazer Protocol Porting Notes

Source snapshot: OpenRazer commit `6820f9da169d354bc7e6e93a0aa8683a6bb75792`.

These notes define how OpenRazer's Linux-side protocol facts can be used to add future Windows devices. Blade 16 2025 (`1532:02C6`) and Viper V3 HyperSpeed (`1532:00B8`) are comparison oracles only. Their existing `blade-710` and `viper-184` production implementations remain authoritative and are not replaced by OpenRazer-derived manifests.

Generated research catalogs live under the ignored `artifacts/reverse-engineering/openrazer/`
directory. The typed backend uses a reviewed, reduced snapshot embedded under
`OpenSynapse.Windows/Devices/OpenRazer`; it contains only common-report devices and
the source facts needed to construct their requests.

## Upstream Inventory

The catalog was generated from the current OpenRazer `master` revision shown above. It contains:

| Item | Count |
|---|---:|
| Devices | 267 |
| Common report builders | 93 |
| Driver call sites using those builders | 376 |
| Call sites with one statically resolved transaction ID | 375 |
| Daemon capability names | 175 |
| Common 90-byte logical report devices | 256 |
| ARGB 320-byte devices | 1 |
| Kraken 37-byte devices | 8 |
| Legacy fixed-report devices | 2 |

After excluding the existing Blade 16 2025 and Viper V3 HyperSpeed implementations, 254 devices remain candidates for the common report family. Their daemon capability sets form 103 distinct cohorts. This is why support should be added by shared capability cohort, not by copying 254 nearly identical protocol implementations.

The complete source-derived inventory is stored in:

- `openrazer-report-builders.json`: all builder headers, arguments, wrappers and source locations;
- `openrazer-effective-command-usages.json`: driver call sites, PID cases, effective transaction IDs and post-builder mutations;
- `openrazer-device-catalog.json`: VID/PID, category, capabilities and Linux transport metadata;
- `openrazer-capability-catalog.json`: capability-to-device index;
- `openrazer-transport-rules.json`: Linux report/response indexes and waits;
- `openrazer-summary.json`: counts and source revision.

The public builder function is not the complete command contract. OpenRazer initializes its common report transaction to `00`, then many device handlers override it to `1F`, `3F`, `9F`, `FF`, `80` or `08`. The catalog records whether each effective value comes from the builder default or a handler override. One wave-effect call site remains explicitly conditional (`1F` normally, `FF` for Mouse Dock Pro) rather than being collapsed to a guessed value. A production command must therefore combine the builder payload, the handler's PID branch and mutations, and the target's separately verified Windows transport profile.

## Standard Report Framing

OpenRazer represents the common Razer payload as 90 bytes. Windows HID APIs expose the report ID as the first byte of the Feature Report buffer, so the equivalent OpenSynapse buffer is 91 bytes:

| Field | OpenRazer logical offset | Windows Feature Report offset |
|---|---:|---:|
| Report ID | USB `wValue=0x0300` | `0` |
| Status | `0` | `1` |
| Transaction | `1` | `2` |
| Remaining packets | `2..3` | `3..4` |
| Protocol type | `4` | `5` |
| Data size | `5` | `6` |
| Command class | `6` | `7` |
| Command ID | `7` | `8` |
| Arguments | `8..87` | `9..88` |
| CRC | `88` | `89` |
| Reserved | `89` | `90` |

The logical command is unchanged. Only the Windows report-ID prefix shifts every logical field by one byte. OpenRazer's CRC XOR over logical offsets `2..87` is therefore the same XOR over Windows offsets `3..88`.

OpenSynapse uses the existing 91-byte `RazerFeatureReport` codec for this family. A Windows device manifest must describe the enumerated Windows Feature Report length (`91`), never OpenRazer's 90-byte logical structure length.

The implemented conversion entry point is `RazerFeatureReport.CreateRequestFromOpenRazerLogical`. It validates the 90-byte request header and OpenRazer CRC, then prefixes the Windows Report ID without changing any logical field. Linux report/response indexes are intentionally not accepted as inputs; they remain Linux transport metadata. The Windows Report ID is supplied separately by the selected Windows endpoint.

## Two-Device Comparison

### Viper V3 HyperSpeed (`1532:00B8`)

The following command headers match after adding the Windows report-ID byte:

| Capability | Transaction | Size | Class | GET/SET ID |
|---|---:|---:|---:|---:|
| Battery | `1F` | `02` | `07` | `80` |
| Polling rate | `1F` | `01` | `00` | `85` / `05` |
| Current DPI GET | `1F` | `07` | `04` | `85` |
| DPI stages | `1F` | `26` | `04` | `86` / `06` |
| Idle timeout | `1F` | `02` | `07` | `83` / `03` |
| Low-battery threshold | `1F` | `01` | `07` | `81` / `01` |

OpenRazer uses report/response index `0` and waits `59900 us`; the validated OpenSynapse transport waits `60 ms`. This is transport policy, not a payload conversion.

OpenRazer's current-DPI SET uses persistent storage byte `01`, while the validated OpenSynapse instant-DPI SET uses `00`. Preserve the target device's verified storage semantics. Battery chemistry and onboard mapping commands are Synapse-derived OpenSynapse capabilities and have no corresponding exposed `00B8` OpenRazer capability.

### Blade 16 2025 (`1532:02C6`)

The standard effect command (`transaction FF`, class `03`, ID `0A`) matches for Off, Wave, Spectrum, Reactive, Static, Breathing and Starlight. Keyboard brightness matches class `0E`, GET/SET IDs `84` / `04`, with arguments beginning at `01`. Logo power matches class `03`, GET/SET IDs `80` / `00`, with arguments `01 04 state`.

OpenRazer defaults to report/response index `1` and waits `600 us`; the validated OpenSynapse transport waits `2 ms`. Again, these values belong to the transport profile, not the command payload.

Known wire quirks must be preserved rather than normalized:

- OpenRazer's custom-row request declares size `0x46`; OpenSynapse currently declares the populated argument length.
- Starlight declares size `01` while carrying nine argument bytes in both implementations.
- Fan, performance, charge, display and Fn commands are Synapse-derived and outside OpenRazer's Blade lighting coverage.

The byte-level oracle tests cover Viper battery, polling, DPI, DPI stages, idle timeout and low-battery commands, plus Blade standard effects, all Starlight variants, keyboard brightness, logo power and the custom-row declared-size quirk. The Viper oracle proves the field shift and `1F` transaction family. The Blade oracle proves the `FF` lighting transaction family and that Linux report index `1` does not become the Windows Report ID.

## Transport Is Not Payload

The class, ID, declared size, arguments and CRC describe the device command. The following are host/endpoint transport properties and remain device-specific:

- HID collection and enumerated Feature Report length;
- report and response IDs/indexes;
- wait and retry timing;
- endpoint discovery and access permissions;
- response matching exceptions.

Do not infer these values from Linux device category, another PID, or the 90-byte logical structure. Every new Windows PID needs endpoint discovery and real-device evidence.

## Porting Procedure

1. Classify the upstream request. Continue only if it uses OpenRazer's common 90-byte logical report.
2. Record the upstream transaction, declared size, class, ID, arguments, CRC rule, report/response index and wait.
3. Enumerate the Windows HID collections and confirm a 91-byte Feature Report endpoint. Prefix report ID `00` and use `RazerFeatureReport`; do not create another 90-byte production codec.
4. Preserve command-specific quirks such as storage selector, declared-size mismatch and response matching behavior.
5. Add a reviewed entry to the embedded catalog. External manifests cannot activate `openrazer-standard` devices.
6. Resolve the Windows endpoint with a side-effect-free GET before any write is allowed.
7. Keep source-derived support marked `PendingValidation` until a reversible SET, read-back, restore and timeout/disconnect gate passes on that PID.

## OpenSynapse Integration

The minimum additive path reuses the current implementation rather than introducing a second protocol stack:

1. Keep `blade-710` and `viper-184` authoritative and unchanged. They are comparison oracles, not migration targets.
2. Reuse `RazerFeatureReport` for the common 90-byte logical family and `RazerFeatureTransport` for Windows HID I/O, retries, matching and serialization.
3. Add a small strongly typed protocol helper only when a new admitted cohort needs argument construction that is not already present.
4. Keep per-PID command facts in the embedded OpenRazer catalog. Group collections by Windows container ID and resolve the actual 91-byte endpoint at runtime with known GET commands; never guess a collection or probe with SET.
5. Extend telemetry and UI only from the typed capabilities published by the resolved connection. Do not create a universal property-bag ViewModel.
6. Treat each source-derived SET as pending physical validation until its PID passes GET, reversible SET, read-back, restore and failure recovery testing.

The generic backend covers the common 91-byte paths: identity, battery, polling, DPI, idle settings, low-battery threshold, standard/classic/extended lighting, matrix rows, scroll controls, selected Fn behavior, keyswitch optimization and HyperPolling sequences. The separate 37-byte Kraken payload and Windows output-report path are also implemented, with endpoint selection restricted to `MI_03`. The 320-byte ARGB payload and four-byte DeathAdder 3.5G state codec are implemented, but their arbitrary USB control transfers are not exposed as ready Windows setters.

The generated catalog is a source-derived command inventory, not a static Windows endpoint manifest. OpenRazer supplies the device command and Linux transport behavior; runtime GET probing supplies the Windows collection match. A specific device should be described as hardware-validated only after its relevant GET/SET/read-back path has been tested on Windows.

## Separate Report Families

The standard porting rule does not apply to:

- addressable RGB controller reports using 320-byte buffers and report IDs `04` / `84`;
- Kraken's separate 37-byte request path;
- legacy fixed reports and device-specific control transfers;
- devices whose Windows endpoint does not enumerate a 91-byte Feature Report.

These must not be padded or truncated to fit `RazerFeatureReport`. Kraken uses its own 37-byte output-report service. ARGB requires `SET_REPORT` with `wValue=0x0300`, `wIndex=1`; DeathAdder 3.5G requires `bmRequestType=0x21`, `bRequest=0x09`, `wValue=0x0010`, `wIndex=0`. Their payload codecs are retained, while production setters remain unavailable until OpenSynapse has a control-transfer-capable Windows transport and endpoint validation.

## Source and License Boundary

OpenRazer is GPL-2.0-or-later. OpenSynapse does not copy OpenRazer C/Python implementation code into its MIT source tree. The pinned source is used to identify wire-level facts; production code is an independent implementation backed by field comparisons and device validation.

The candidate catalogs summarize upstream metadata. They are ignored by Git and never loaded at runtime. No production manifest is generated until its target hardware passes discovery, GET, SET, read-back, restore and disconnect gates.
