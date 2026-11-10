# Headset transport matrix

`OK` means the path matches the product bundle and has current-device evidence. `CODE_ONLY`
means the path matches the bundle but this checkout has no device evidence. `PENDING` means
the bundle suggests a different report type and the path still needs a probe. `NO_EVIDENCE`
means there is no bundle or hardware to decide.

| PID | Product | Bundle class | JS send / read | Our transport | Report length / id | Status | Evidence |
|---|---|---|---|---|---|---|---|
| 0555 | BlackShark V2 Pro 2023 | AudioMxicDevice | `hid.write` / `hid.read` | `RazerMxicHeadsetTransport` | 64 / 2 | CODE_ONLY | `bundles/0555/AudioMxicDevice.*.js` |
| 0539, 053B | Barracuda Pro / 2.4 | AudioMxicMabelT1 | `hid.write` / `hid.read` | `RazerMxicHeadsetTransport` | 64 / 1 | CODE_ONLY | `bundles/0539/AudioMxicMabelT1.*.js` |
| 0583, 0584 | Barracuda Pro ECR | AudioMxicMabelT1 | `hid.write` / `hid.read` | `RazerMxicHeadsetTransport` | 64 / 1 | CODE_ONLY | `bundles/0583/AudioMxicMabelT1.*.js` |
| 0593 | Barracuda V2 Pro | rzDevice40Headset / Airoha | `hid.write` / `hid.read` | `RazerMxicHeadsetTransport` | 64 / 2 | CODE_ONLY | `bundles/0593/rzDevice40Headset.*.js` |
| 0A61, 0A64, 0A68, 0A6C, 0A81 | Hammerhead V3 / BlackShark V3 X | rzDevice40 | `hid.sendFeatureReport` / `hid.getFeatureReport` | `RazerMxicHeadsetTransport` | 64 / 2 feature | PENDING | `bundles/0A61/rzDevice40.*.js` |
| 0579, 0A4E, 0A51, 101E, 101F | AudioValkyrie | AudioValkyrie | `hid.write` / `hid.read` | `RazerHeadsetHidTransport` | 64 / 7 | CODE_ONLY | shared AudioValkyrie chunk |
| 0567, 056B, 0587 | AudioBillie | AudioBillieT2 | `hid.write` / `hid.read` | `RazerHeadsetHidTransport` | 64 / 2 | CODE_ONLY | `bundles/0567/AudioBillieT2.*.js` |
| 056E, 0565 | AudioOdessa T2 Wireless | AudioOdessaT2Wireless | `hid.write` / `hid.read` | `RazerHeadsetHidTransport` | 64 / 2 | CODE_ONLY | `bundles/056E/AudioOdessaT2Wireless.*.js` |
| 0576, 0577, 101B, 101C | BlackShark V3 Pro | AudioValkyrie | `hid.write` / `hid.read` | `BlackSharkHeadsetTransport` | 64 / 2 | CODE_ONLY | `bundles/0576/AudioValkyrie.*.js` |
| 0520, 0521, 0526, 0527, 0529, 0554, 0560 | KrakenBase | AudioKrakenBase | `hid.write` / `hid.read` | `RazerHidHidReportTransport` | 64 / varies | CODE_ONLY | shared Kraken chunk |
| 053E | Audio Mixer | AudioMixer | `hid.write` + feature report | `RazerMixer053ETransport` | 66 / varies | CODE_ONLY | `bundles/053E/AudioMixer.*.js` |
| 0A54, 1022 | Valkyrie T3 | rzDevice25ValkyrieT3 | feature report | `RazerValkyrieT3Transport` | 64 / 7 | CODE_ONLY | `bundles/0A54/rzDevice25ValkyrieT3.*.js` |
| 056B lighting | Kraken V4 lighting | AudioKrakenBase | `HidD_SetOutputReport` | `RazerMxicHeadsetTransport(useSetOutputReport: true)` | 64 / 2 | CODE_ONLY | shared Kraken chunk |
| 0532, 0548 | Leviathan V2 / V2 Pro | rzDevice25Noa | feature report | `RazerProtocol25FeatureTransport` | 65 / 8 | CODE_ONLY | `bundles/0532/rzDevice25Noa.*.js` |
| 05A3 | Leviathan V2 ECR | rzDevice25NoaT2ECR | feature report | `RazerProtocol25FeatureTransport` | 64 / 8 | CODE_ONLY | `bundles/05A3/rzDevice25NoaT2ECR.*.js` |
| 056A, 056F, 05A6 | Seiren V3 Mini / Chroma / DSP | rzDevice25AudioCamy* | feature report | `RazerProtocol25FeatureTransport` | 65 / 7 | CODE_ONLY | `bundles/056F/rzDevice25AudioCamy.*.js` |
| 058E | Seiren V3 Pro | rzDevice25AudioMarieT1 | feature report | `RazerProtocol25FeatureTransport` | 65 / 7 | CODE_ONLY | `bundles/058E/rzDevice25AudioMarieT1.*.js` |
| 058C | Hammerhead V3 | AudioLindaT3 | feature report | `RazerProtocol25FeatureTransport` | 65 / 7 | CODE_ONLY | `bundles/058C/AudioLindaT3.*.js` |
| 0574 | Barracuda X Chroma | AudioMabelT3C | `hid.write` / `hid.read` | `RazerHeadsetHidTransport` audio, Kraken V4 64B lighting | 64 / 2 | CODE_ONLY | `bundles/0574/AudioMabelT3C.*.js` |
| 0582 | Kraken V3 X | AudioKrystalT3 | `hid.write` / `hid.read` | `RazerHeadsetAudioService.KrakenBase37` audio | 37 | CODE_ONLY | `bundles/0582/AudioKrystalT3.*.js` |
| 0542, 0543 | Seiren V2 Pro / X | AudioCamyT1 / T2 | official `RzNative_*.dll` only | `NativeDll` status row | n/a | COMPONENT_REQUIRED | `bundles/0542`, `bundles/0543` |
| 052D | Kraken V3 Pro | AudioNXPDevice | `serial.write` + notification | blocked until a real serial endpoint is captured | 28 | PENDING | `bundles/052D/AudioNXPDevice.*.js` |

The only family where the bundle names a different report type than the current code is
`rzDevice40` (0A61 / 0A64 / 0A68 / 0A6C / 0A81). Do not switch it to feature reports until a
device probe shows a feature reply and an output write failure.

HID handles are not seekable. Never use `RandomAccess` with an offset on a HID handle; use a
separate write handle with `WriteFile`, or an `OVERLAPPED` write on the existing handle.
