# Device Adaptation Guide

This guide is for device owners who want to help add or complete support for a Razer device. The goal is to produce reviewable protocol evidence without guessing hardware commands, redistributing Razer files, or exposing personal data.

## 1. Check the existing catalog first

Read [SUPPORTED_DEVICES.md](SUPPORTED_DEVICES.md). If the model is already listed, report the missing capability instead of treating the device as unsupported.

An OpenRazer catalog entry proves that upstream has a device implementation. It does not prove that every control has been verified on the current firmware or Windows HID collection.

## 2. What the owner should provide

- Exact model name and SKU, for example `Razer Blade 15 (Mid 2019)` / `RZ09-03006`.
- USB VID/PID, for example `1532:0246`.
- Windows version and build number.
- Synapse version.
- The controls to prioritize, in order.
- Whether temporary test builds can be installed and tested.
- A collector manifest, sanitized protocol inventory, or a temporary link to the collector ZIP.

Do not post any of the following in a public issue:

- Razer proprietary JavaScript, DLLs, drivers, firmware, or installers;
- a complete Razer AppEngine or Synapse installation;
- serial numbers, account identifiers, tokens, HID paths, or machine-local absolute paths;
- raw captures or logs that have not been reviewed.

The collector packages only matching JavaScript cache candidates. Send the ZIP through a temporary link and delete it after the review. Never execute JavaScript received from a contributor.

## 3. Evidence path

Use the cheapest source that can answer the question, in this order.

### 3.1 OpenRazer catalog or source

Use this first when the model already has an upstream implementation. Reuse the existing packet definitions, device manifest, capability boundaries, and tests.

### 3.2 Local Synapse product JavaScript

Use this when the product-specific capability is not covered by OpenRazer. The relevant files are usually the product's main bundle and its `rzDevice25` or shared protocol chunk.

This path can reveal:

- product ID and device metadata;
- `[dataSize, class, command]` headers;
- argument order and endian rules;
- response layouts and enums;
- GET/SET relationships;
- transaction, CRC, batch, and mutex behavior;
- the exact methods the product actually calls.

It cannot by itself prove that the current device accepts a write, that a setting persists, or that a lighting effect is visible. Those still require hardware verification.

### 3.3 USB capture

Use a bounded capture only when the JavaScript and existing source do not expose the required command. Change one visible setting, capture a short window, restore the original setting, and keep raw captures outside the repository.

A captured write is evidence of a transaction, not proof that the same command is safe to send from OpenSynapse. Promotion still requires a readback or deterministic behavior check.

### 3.4 Native DLL or service analysis

Use this only for functionality that is not reachable through the product JavaScript or HID capture, such as mapping-engine input or lighting-effect algorithms. Record the module version, function or offset, and the dynamic observation that justifies each constant.

## 4. Collector script

Prerequisites: Windows, Razer Synapse installed, and the device page opened at least once so the product bundle is cached.

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Collect-RazerProductBundle.ps1 `
  -DeviceName "Blade 15" `
  -Sku "RZ09-030" `
  -UsbPid "0246"
```

Optional parameters:

- `-ProductId 710` when the Synapse product ID is known.
- `-Root <path>` when Razer AppEngine user data is installed outside `%LOCALAPPDATA%`.
- `-MaxFiles 60` to limit the package size.

The script:

- reads the local Razer AppEngine cache without modifying it;
- matches the device name, SKU, USB PID, optional product ID, and `rzDevice25` references;
- copies only matching JavaScript candidates;
- writes `manifest.txt` and `README.txt`;
- creates a ZIP under `%TEMP%`.

If no files are found, open the device page in Synapse once, wait for it to finish loading, and run the script again.

## 5. Maintainer workflow

1. **Triage** — confirm the exact model, PID, current support state, and requested controls.
2. **Collect** — ask for the collector manifest or a private temporary link. Do not ask for a full Synapse installation.
3. **Extract** — map the product calls to report headers, arguments, responses, and capability boundaries. Record the source and date.
4. **Build a candidate** — add a manifest or protocol builder/parser and mark new capabilities `SourceBacked`. Do not expose production writes yet.
5. **Test build** — ask the owner to run read-only checks first, then same-value and minimal-change writes with readback and restore.
6. **Promote** — mark a capability `Verified` only after current-device evidence and restoration succeed. Update the device list and changelog.
7. **Retain evidence** — keep reviewed constants, tests, and a short evidence note. Do not commit the original proprietary files or raw captures.

## 6. Capability states

| State | Meaning |
|---|---|
| `SourceBacked` | Exact command and layout are known from source or local product files; read-only probes only. |
| `TestBuild` | A candidate build is available; current-device validation is incomplete. |
| `Verified` | Read, write, readback or deterministic behavior, and restoration have passed on the current device. |
| `Blocked` | The capability is known but lacks a safe command, response, persistence, or recovery path. |

Do not use `Verified` for a successful ACK alone.

`SourceBacked` is a build-time evidence label. It is not generated by the Diagnostics page. Diagnostics only displays the resulting capability or error state; adding a candidate requires a reviewed manifest or protocol builder/parser in source control.

## 7. Safety and acceptance

Every write path must have:

- a matching current-device read or an approved reason why no read exists;
- a same-value or minimal-change test;
- readback or a deterministic behavior check;
- restoration in a `finally` path or an equivalent recovery mechanism;
- a visual check for lighting and other user-visible effects;
- no arbitrary raw-HID console, command enumeration, or guessed value range.

Before closing the adaptation:

- the device is identified by model, SKU, PID, and HID collection;
- the supported controls match the device's real capabilities;
- unsupported controls are absent, not displayed as fake buttons;
- the owner has tested the relevant build;
- the documentation and changelog describe the support boundary accurately.

## 8. Owner handoff template

Use the repository issue form or include this in a new issue:

```text
Device:
SKU / model number:
USB VID:PID:
Windows build:
Synapse version:
Requested controls (priority order):
Can test temporary builds:
Collector manifest or temporary link:
Current behavior:
```

## 9. Privacy and licensing

- Never commit Razer proprietary files or redistribute them.
- Never execute user-supplied JavaScript; inspect it as data only.
- Keep original files and raw captures outside Git.
- Commit only independently written protocol constants, manifests, tests, and reviewed evidence notes.
- Remove serial numbers, account names, machine names, HID paths, and unrelated USB traffic from published artifacts.