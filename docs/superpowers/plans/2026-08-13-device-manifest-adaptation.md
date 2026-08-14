# Device Manifest Adaptation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move OpenSynapse device matching and stable Blade/Viper request descriptors into strict built-in manifests while preserving strong handlers and hardware safety.

**Architecture:** An embedded `RazerDeviceRegistry` loads strict JSON manifests. Discovery and the telemetry reader resolve a device definition by VID/PID; existing protocol classes continue to encode dynamic values, parse responses, compare readback, and restore state.

**Tech Stack:** .NET 10, C#, `System.Text.Json`, Windows HID, xUnit.

## Global Constraints

- Preserve the current Blade `1532:02C6` and Viper `1532:00B8` packet bytes and behavior.
- No arbitrary raw report sender, runtime scripts, plugins, external writable manifests, dynamic Profile model, or generated WinUI.
- Only existing strongly typed backend methods may write hardware.
- Existing current-path, readback, cancellation, and restoration gates remain mandatory.
- Unknown or malformed configuration fails closed.
- The workspace has no Git metadata; do not add commit steps.

---

### Task 1: Strict Manifest Registry

**Files:**
- Create: `src/OpenSynapse.Windows/Devices/RazerDeviceManifest.cs`
- Create: `src/OpenSynapse.Windows/Devices/RazerDeviceRegistry.cs`
- Create: `src/OpenSynapse.Windows/Devices/Manifests/blade-710.json`
- Create: `src/OpenSynapse.Windows/Devices/Manifests/viper-184.json`
- Modify: `src/OpenSynapse.Windows/OpenSynapse.Windows.csproj`
- Create: `tests/OpenSynapse.Core.Tests/RazerDeviceRegistryTests.cs`

**Interfaces:**
- Produces: `RazerDeviceRegistry.BuiltIn`, `Find(ushort vendorId, ushort productId)`, and typed request descriptors.

- [ ] Add tests that load both built-ins and reject malformed hex, unknown members/families, duplicate PIDs, arguments beyond data size, and missing required family capabilities.
- [ ] Implement strict `System.Text.Json` loading with `UnmappedMemberHandling.Disallow` and explicit semantic validation.
- [ ] Embed both manifests as resources and verify exact Blade/Viper descriptor bytes in tests.

### Task 2: Registry-Backed HID Discovery

**Files:**
- Modify: `src/OpenSynapse.Windows/Devices/WindowsHidDiscovery.cs`
- Modify: `tests/OpenSynapse.Core.Tests/DeviceIdParserTests.cs`

**Interfaces:**
- Consumes: `RazerDeviceRegistry`.
- Produces: descriptors only for registered VID/PID/collection matches.

- [ ] Replace the `02C6/00B8` branch with registry lookup and manifest display names/collection constraints.
- [ ] Keep unsupported collections blocked and preserve current product collapsing behavior.
- [ ] Test registered, unregistered, and wrong-collection matching through an internal pure matching helper.

### Task 3: Configured Blade And Viper Requests

**Files:**
- Modify: `src/OpenSynapse.Windows/Protocols/RazerFeatureReport.cs`
- Modify: `src/OpenSynapse.Windows/Protocols/BladeProduct710Protocol.cs`
- Modify: `src/OpenSynapse.Windows/Protocols/ViperProduct184Protocol.cs`
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Modify: `tests/OpenSynapse.Core.Tests/BladeProduct710ProtocolTests.cs`
- Modify: `tests/OpenSynapse.Core.Tests/ViperProduct184ProtocolTests.cs`
- Modify: `tests/OpenSynapse.Core.Tests/RazerDeviceTelemetryReaderTests.cs`

**Interfaces:**
- Consumes: manifest request descriptors resolved by connected PID.
- Produces: byte-identical GET/SET reports and existing strong parser/readback/recovery behavior.

- [ ] Add byte-for-byte regression tests for all migrated fixed requests and dynamic Viper polling/DPI/idle/DPI-stage SET headers.
- [ ] Add the smallest request-descriptor overloads needed by existing product handlers.
- [ ] Resolve the family manifest once per reader operation and reject a PID whose family does not match the handler.
- [ ] Keep Blade multi-zone/cluster/mask and Viper table recovery in C#.

### Task 4: Verification And Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/device-capability-matrix.md`

**Interfaces:**
- Produces: verified configuration workflow and unchanged production behavior.

- [ ] Run registry, protocol, discovery, and telemetry-reader tests.
- [ ] Run the complete non-hardware test suite.
- [ ] Build `OpenSynapse.slnx` with zero warnings and errors.
- [ ] Attack malformed manifests, PID collisions, protocol-family mismatch, accidental raw writes, and lost restoration paths; fix every finding.
- [ ] Document that a same-family device needs a built-in manifest plus current-device evidence, while a new protocol structure still needs a reviewed handler.

## Self-Review

- The plan configures stable device/report differences without moving safety logic into JSON.
- Existing strong backend and WinUI contracts remain intact.
- No external manifest can enable production writes.
- Every migrated request has a byte-regression test.
