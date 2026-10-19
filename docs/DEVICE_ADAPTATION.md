# Device Adaptation Guide

This guide has a short owner checklist and a maintainer reference. You do not need to understand the protocol sections to help.

## For device owners

### 1. Open a device support request

Use the [device support issue form](https://github.com/A1mAssist/OpenSynapse/issues/new?template=device-support.yml). Include:

- the exact model and SKU;
- the USB PID, or `unknown` if you cannot find it;
- your Windows build and Synapse version;
- the controls you care about, in priority order;
- whether you can install a temporary test build.

If the model is already in [SUPPORTED_DEVICES.md](SUPPORTED_DEVICES.md), say which capability is missing instead of treating it as a new device.

### 2. Run the collector when possible

The collector is read-only. It finds the local Synapse product JavaScript and creates a ZIP with a manifest. Download [Collect-RazerProductBundle.ps1](https://raw.githubusercontent.com/A1mAssist/OpenSynapse/main/tools/Collect-RazerProductBundle.ps1), save it to your `Downloads` folder, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$HOME\Downloads\Collect-RazerProductBundle.ps1"
```

If PowerShell shows a `>>` prompt, the trailing line-continuation character was copied too; press Ctrl+C and paste the line again.

If the script path is not found, confirm the file is in `Downloads`; if your browser or OneDrive uses a different folder, replace the path with the actual save location.

No parameters are needed. The script auto-detects connected Razer devices and matches their names and USB PIDs. If detection misses your device, add `-Sku` or `-UsbPid` to the command. The script prints the ZIP path and creates it under `%TEMP%`.

If it finds no files, open the device page in Synapse once, wait for it to finish loading, and run it again.

If you do not want to run PowerShell, skip this step and say so in the issue. The maintainer can guide you.

### 3. Send the result and test a build

Do not attach Razer proprietary files or account data to a public issue. Upload the ZIP to a temporary file host and post the link, or paste only the manifest summary.

The maintainer will extract the protocol and send a temporary build. Test read-only checks first. Test write controls only if you are comfortable doing so; every write test includes readback and restoration.

## What happens next

1. The maintainer checks OpenRazer first.
2. If the capability is not covered there, the local Synapse product files are inspected.
3. The control is added as a read-only candidate.
4. A test build is sent to the owner.
5. The owner checks reading, then a safe write with readback and restore where applicable.
6. The control is enabled only after those checks pass.

## Privacy

- Do not upload Razer JavaScript, DLLs, drivers, firmware, installers, serial numbers, tokens, or account data to a public issue.
- The collector packages only matching JavaScript cache candidates. It does not execute JavaScript.
- Raw product files and USB captures stay outside Git.
- Reviewed protocol constants and tests, not the original proprietary files, are committed.

## For maintainers

### Evidence order

Use the cheapest source that can answer the question:

1. **OpenRazer source or catalog** — reuse the existing implementation and manifest.
2. **Local Synapse product JavaScript** — product main bundle and `rzDevice25` or the shared protocol chunk.
3. **OpenSynapse diagnostics** — if the device is already reachable, use the read action or command scan to record baseline values and check candidate GET responses.
4. **USB capture** — only when source and diagnostics cannot answer the question. Change one visible setting and restore it.
5. **Native DLL or service analysis** — only for functionality not reachable through the product JavaScript, diagnostics, or HID capture.

Diagnostics is a confirmation tool. It does not generate a manifest, discover command semantics, or promote a capability. For a device OpenSynapse cannot open yet, build a minimal read-only connection or use a bounded capture.

### Collector parameters

- No parameters — auto-detect connected Razer devices and match their names and USB PIDs; falls back to files that reference `rzDevice25`.
- `-Sku` — optional SKU or model-number prefix.
- `-UsbPid` — optional PID in `0246` or `1532:0246` form.
- `-ProductId` — optional Synapse product ID.
- `-Root` — optional Razer AppEngine user-data directory.
- `-MaxFiles` — optional package-size limit.

### Promotion workflow

1. **Triage** — confirm model, PID, support state, and requested controls.
2. **Collect** — request the collector manifest or a private temporary link.
3. **Extract** — map product calls to report headers, arguments, responses, and capability boundaries.
4. **Probe and baseline** — use diagnostics when the device is reachable; record current values and candidate GET responses.
5. **Build a candidate** — add a manifest or protocol builder/parser and mark new capabilities `SourceBacked`. Do not expose production writes yet.
6. **Test build** — ask the owner to run read-only checks, then same-value and minimal-change writes with readback and restore.
7. **Promote** — mark `Verified` only after current-device evidence and restoration succeed. Update the device list and changelog.
8. **Retain evidence** — keep reviewed constants, tests, and a short evidence note. Do not commit proprietary files or raw captures.

### Capability states

| State | Meaning |
|---|---|
| `SourceBacked` | Command and layout are known from source or local product files; read-only probes only. |
| `TestBuild` | A candidate build exists; current-device validation is incomplete. |
| `Verified` | Read, write, readback or deterministic behavior, and restoration passed on the current device. |
| `Blocked` | The capability is known but lacks a safe command, response, persistence, or recovery path. |

A successful ACK alone is not `Verified`.

`SourceBacked` is a build-time evidence label. It is not generated by the Diagnostics page. Diagnostics only displays the resulting capability or error state.

### Safety and acceptance

Every write path must have:

- a matching current-device read, or an approved reason why no read exists;
- a same-value or minimal-change test;
- readback or a deterministic behavior check;
- restoration in a `finally` path or equivalent recovery;
- a visual check for lighting and other visible effects;
- no arbitrary raw-HID console, command enumeration, or guessed value range.

Before closing the adaptation, confirm that unsupported controls are absent from the UI, the owner has tested the relevant build, and the documentation describes the support boundary accurately.
