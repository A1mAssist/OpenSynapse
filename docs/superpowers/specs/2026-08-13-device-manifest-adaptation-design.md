# OpenSynapse Device Manifest Adaptation Design

## Goal

Allow a new Razer device that reuses an admitted Blade or Viper protocol family to be supported by adding a strict built-in JSON manifest and hardware evidence, without rewriting HID discovery or packet headers.

## Scope

- Keep the existing 91-byte `RazerFeatureTransport`, strong Core telemetry types, profile model, and WinUI pages.
- Configure device identity, HID collection matching, protocol family, wait time, and capability request descriptors.
- Migrate Blade 16 2025 `02C6` and Viper V3 HyperSpeed `00B8` to the manifest registry.
- Keep multi-command parsing, readback comparison, cancellation, and restoration in reviewed C# handlers.
- Do not add dynamic UI generation, arbitrary raw reports, scripts, plugins, or external production-write manifests.

## Manifest Boundary

Every manifest contains a stable ID, display name, VID/PID list, usage page, usage, feature-report length, protocol family, transport timing, and a map of known capability IDs to request descriptors. Hex values are JSON strings containing exactly two or four uppercase hexadecimal digits as appropriate.

Built-in manifests are embedded at build time and may reference capabilities already marked `Verified` in the repository ledger. Optional external manifests, when introduced, must be restricted to discovery and read-only probes until separately promoted in source; this implementation does not load an external directory.

Capability IDs and protocol families are closed sets. A manifest cannot invent a handler name, mark itself verified, execute a raw report, override another PID, or change the report length beyond the Razer 91-byte transport currently supported.

## Runtime Architecture

`RazerDeviceRegistry` loads and validates the embedded manifests once. `WindowsHidDiscovery` asks the registry whether a VID/PID is supported and uses its collection constraints and display name. `RazerDeviceTelemetryReader` resolves the current device manifest and passes configured request descriptors into existing protocol parsers and safety workflows.

Product-specific C# handlers remain responsible for dynamic arguments and invariants. For example, the manifest owns the `04/86` and `04/06` DPI-stage headers, while `ViperProduct184Protocol` still owns zero-based SET records, one-based GET normalization, DPI bounds, complete-table comparison, and recovery.

## Configuration Model

The initial schema uses:

```json
{
  "schemaVersion": 1,
  "id": "viper-184",
  "displayName": "Razer Viper V3 HyperSpeed",
  "vendorId": "1532",
  "productIds": ["00B8"],
  "collection": {
    "usagePage": "0001",
    "usage": "0002",
    "featureReportLength": 91
  },
  "protocolFamily": "viper-184",
  "transport": {
    "waitMilliseconds": 60
  },
  "capabilities": {
    "current-dpi.get": {
      "transactionId": "1F",
      "dataSize": "07",
      "commandClass": "04",
      "commandId": "85",
      "arguments": "00"
    }
  }
}
```

The loader rejects unknown JSON properties, missing required fields, malformed hex, duplicate IDs/PIDs/capabilities, unknown protocol families, invalid data sizes, argument bytes beyond data size, non-91-byte collections, and capability sets that do not meet the protocol family's required contract.

## Blade And Viper Migration

The Blade manifest declares Product 710 battery/status/sleep reads and the admitted thermal, Boost, charge, Max Fan, brightness, and logo command descriptors used by the backend. The Viper manifest declares battery, polling, current DPI, idle, low-battery threshold, and DPI-stage GET/SET descriptors.

Protocol builders accept a configured request descriptor where a header varies by device. Existing no-argument methods remain as compatibility wrappers over the built-in family definition only where tests and tools need them. The telemetry reader resolves the manifest by the connected device PID, so an additional PID using the same family can reuse the same handlers through a new manifest.

## Safety And Failure Handling

- Discovery fails closed when the registry is invalid.
- A write is available only through an existing strongly typed backend method.
- Every production write still requires a successful current-path read.
- SET acknowledgement never replaces GET readback.
- Existing failure/cancellation restoration remains mandatory.
- Unknown capabilities stay unavailable instead of falling back to a similar command.
- No manifest field grants evidence status or UI access.

## Verification

Unit tests cover valid built-in loading; malformed hex; unknown properties/families; duplicate PID/capability definitions; oversized arguments; missing required family capabilities; registry-based discovery matching; and exact Blade/Viper request bytes. Existing protocol, reader recovery, profile, probe, hardware-safety, and WinUI builds must remain green.

No new hardware write is required for this refactor because the migrated byte reports must be identical to the reports already validated. A byte-for-byte regression test is the acceptance condition for configuration migration.

## Self-Review

- No dynamic UI or dynamic profile rewrite is included.
- No arbitrary report sender or external writable configuration is included.
- Manifest validation is strict and fails closed.
- Complex device safety remains in C# handlers.
- Blade `02C6` and Viper `00B8` remain behaviorally and byte-for-byte compatible.
