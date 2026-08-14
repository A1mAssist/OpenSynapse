# Hardware Evidence and Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish manual fan, fan-curve, Logo Breathing, Blade battery/sleep, and real-device lifecycle evidence without guessing packets or leaving changed hardware state behind.

**Architecture:** Implement the already source-backed fixed-target transaction behind tests and a validation-only entry point first. Use isolated Synapse captures to derive the unknown fan-curve and Logo sequences before writing protocol code. Promote only after readback/restore and physical gates pass.

**Tech Stack:** .NET 10, C#, xUnit, PowerShell 7, Wireshark/tshark, existing HID transport and validation tools.

## Global Constraints

- Never expose arbitrary SET input.
- Never overwrite evidence files.
- Every changing validation restores original state in `finally` and verifies restoration.
- Captures remain under `%LocalAppData%\OpenSynapse\reverse-engineering\raw` and are not committed.
- Do not terminate Synapse automatically.

---

### Task 1: Add a transactional fixed-fan backend setter

**Files:**
- Modify: `src/OpenSynapse.Core/Devices/RazerDeviceTelemetry.cs`
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Modify: `src/OpenSynapse.Windows/Protocols/BladeFanProtocol.cs`
- Test: `tests/OpenSynapse.Core.Tests/RazerDeviceTelemetryReaderTests.cs`

**Interfaces:**
- Produces: `SetBladeFanAsync(devices, BladeFanMode mode, int? targetRpm, cancellationToken)` returning read-back `(Mode, TargetRpm)`.

- [ ] **Step 1: Add failing transaction tests**

Cover Manual `3200`, Automatic, invalid bounds/step, current-path GET failure, CPU/GPU mismatch, ignored zone-2 write, cancellation after zone 1, restore mismatch, and an aggregate containing both operation and restoration failures.

- [ ] **Step 2: Run the focused tests and confirm failure**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~RazerDeviceTelemetryReaderTests'
```

- [ ] **Step 3: Implement write helpers using existing builders**

```csharp
private Task WriteBladeFanTargetAsync(
    DeviceDescriptor device,
    byte zone,
    int rpm,
    CancellationToken cancellationToken) =>
    QueryAsync(device, BladeFanProtocol.CreateSetTargetRequest(zone, rpm), cancellationToken);
```

Use `WriteBladeThermalZoneAsync` for mode, write both targets before Manual mode, read both targets and zones after the operation, and restore using `CancellationToken.None`.

- [ ] **Step 4: Keep it validation-only**

Add the method to the interface and reader, but do not add a ViewModel/XAML caller or `VerifiedProfileApplier` use until Task 3 passes on hardware.

- [ ] **Step 5: Run reader and protocol tests**

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add 'src\OpenSynapse.Core\Devices\RazerDeviceTelemetry.cs' 'src\OpenSynapse.Windows\Devices\RazerDeviceTelemetryReader.cs' 'src\OpenSynapse.Windows\Protocols\BladeFanProtocol.cs' 'tests\OpenSynapse.Core.Tests\RazerDeviceTelemetryReaderTests.cs'
git commit -m 'feat: add transactional Blade fixed fan setter'
```

### Task 2: Add fixed-fan hardware validation

**Files:**
- Create: `tools/OpenSynapse.HardwareValidation/BladeFanValidation.cs`
- Modify: `tools/OpenSynapse.HardwareValidation/Program.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeFanValidationSafetyTests.cs`

**Interfaces:**
- Produces: `--blade-fan-fixed --target-rpm <2000..5000> --hold-seconds <5..60> --output <json>`.

- [ ] **Step 1: Add fake-transport safety tests**

Assert same-value pass, minimal `+100 RPM` pass, failure restoration, Ctrl+C restoration, and output refusal when the JSON path exists.

- [ ] **Step 2: Implement the validation action**

Capture original two-zone state, choose a target different by exactly `100 RPM` within bounds, apply/read/hold/restore/read, and serialize original/target/restored values plus errors. JSON must contain VID/PID and collection only, never the HID path.

- [ ] **Step 3: Run safety tests**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~BladeFanValidationSafetyTests'
```

Expected: PASS.

- [ ] **Step 4: Commit**

```powershell
git add 'tools\OpenSynapse.HardwareValidation\BladeFanValidation.cs' 'tools\OpenSynapse.HardwareValidation\Program.cs' 'tests\OpenSynapse.Core.Tests\BladeFanValidationSafetyTests.cs'
git commit -m 'test: add Blade fixed fan hardware validation'
```

### Task 3: Run the fixed-fan lifecycle gate

**Files:**
- Evidence output: `artifacts/protocol/2026-08-14/blade-fixed-fan-write-readback-restore.json`
- Modify after evidence: `docs/device-capability-matrix.md`

**Interfaces:**
- Consumes: validation action from Task 2.
- Produces: current-device promotion evidence or a concrete failed gate.

- [ ] **Step 1: Close Synapse and run the ordinary write/readback/restore action**

```powershell
$env:OPENSYNAPSE_HARDWARE_TEST='1'
dotnet run --project 'tools\OpenSynapse.HardwareValidation\OpenSynapse.HardwareValidation.csproj' -- --blade-fan-fixed --target-rpm 3200 --hold-seconds 30 --output 'artifacts\protocol\2026-08-14\blade-fixed-fan-write-readback-restore.json'
```

Expected: target and original restoration read back exactly.

- [ ] **Step 2: Repeat with USB disconnect/reconnect and sleep/resume at the prompted hold point**

Use new output names for each run. A disconnect may fail the active operation, but after reconnect/resume the tool or App must read a safe state and restore the captured original before promotion.

- [ ] **Step 3: Determine exit ownership**

Run the target in a validation child process, terminate only that child during hold, and immediately GET both zones from the parent. If Manual persists, implement the anonymous-pipe watchdog described in the design and repeat; if firmware reverts, record that evidence and do not add a watchdog.

- [ ] **Step 4: Promote only after all four runs pass**

Add the ViewModel/Profile entry only after ordinary, disconnect, resume, and child-exit evidence passes. Otherwise keep the setter validation-only and record the exact failed gate.

### Task 4: Capture and reconstruct the Product 710 fan-curve contract

**Files:**
- Use: `tools/ProtocolCapture/Capture-RazerProtocol.ps1`
- Use: `tools/ProtocolCapture/Export-RazerTransfers.ps1`
- Create after capture: `docs/reverse-engineering/blade-02c6-fan-curve-contract.md`

**Interfaces:**
- Produces: exact GET/SET headers, node encoding, bounds, zone identity, and restoration sequence.

- [ ] **Step 1: Identify USBPcap interface and Blade address**

```powershell
& 'C:\Program Files\Wireshark\tshark.exe' -D
& 'C:\Program Files\Wireshark\tshark.exe' -i USBPcap1 -a duration:5 -Y 'usb.idVendor == 0x1532 && usb.idProduct == 0x02c6' -T fields -e usb.device_address
```

Select the actual listed USBPcap interface; do not guess the address.

- [ ] **Step 2: Capture page-open with no change**

Run `Capture-RazerProtocol.ps1` with experiment `blade-fan-curve-open`, the observed interface/address, `OriginalValue='unchanged'`, `ChangedValue='unchanged'`, and `RestoredValue='unchanged'`; open the Synapse custom fan page during the capture.

- [ ] **Step 3: Capture one CPU node change and exact restore**

Record original temperature/RPM, change one CPU node by the smallest UI step, apply, then restore it within one 120-second capture named `blade-fan-curve-cpu-node`.

- [ ] **Step 4: Repeat for one GPU node**

Use experiment `blade-fan-curve-gpu-node` with exact original/changed/restored values.

- [ ] **Step 5: Diff only correlated feature reports**

Use the generated `transfers.tsv` to isolate class/command/data-size/arguments that change with exactly one node. Cross-reference the Product 710 call site and dependency; reject generic methods without a Product 710 call.

- [ ] **Step 6: Write the exact contract document**

Document request/response byte offsets, all observed nodes, zone IDs, bounds, readback, and restore order. If no deterministic GET/readback exists, the completion gate fails and no production curve setter is written.

### Task 5: Implement and validate the proven fan-curve contract

**Files:**
- Create after Task 4 evidence: `src/OpenSynapse.Windows/Protocols/BladeFanCurveProtocol.cs`
- Modify: `src/OpenSynapse.Core/Devices/RazerDeviceTelemetry.cs`
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Create: `tools/OpenSynapse.HardwareValidation/BladeFanCurveValidation.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeFanCurveProtocolTests.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeFanCurveValidationSafetyTests.cs`

**Interfaces:**
- Produces only the exact typed curve/node structures documented by Task 4.

- [ ] **Step 1: Freeze captured reports as parser/builder test vectors**

Use sanitized request/response hex from the contract document; include same-value and one-node-change cases for both zones.

- [ ] **Step 2: Implement strict builders/parsers and transactional reader setter**

Validate node count/order, temperature monotonicity, RPM bounds, complete readback equality, cancellation, and non-cancelable restore.

- [ ] **Step 3: Add validation-only action and safety tests**

Mirror fixed-fan validation: original -> one node -> readback -> restore -> readback, refusing existing output files.

- [ ] **Step 4: Run protocol/safety tests, then the four lifecycle hardware runs from Task 3**

Promotion requires all tests plus current-device ordinary/disconnect/resume/exit evidence.

### Task 6: Recover and validate Logo Breathing

**Files:**
- Use: `tools/ProtocolCapture/Capture-RazerProtocol.ps1`
- Modify after evidence: `src/OpenSynapse.Windows/Protocols/BladeLogoProtocol.cs`
- Modify after evidence: `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Modify: `tools/OpenSynapse.HardwareValidation/Program.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeLogoProtocolTests.cs`
- Test: `tests/OpenSynapse.Core.Tests/LogoValidationSafetyTests.cs`

**Interfaces:**
- Produces: exact sustained-Breathing sequence with Off/Static restoration.

- [ ] **Step 1: Capture Synapse Off -> Breathing -> Off and Static -> Breathing -> Static**

Use separate experiments and record every visible state. Keep Synapse running; do not use OpenSynapse SETs in these captures.

- [ ] **Step 2: Correlate Native/service and USB transfers**

Determine profile ID, operating/device mode, mode/power order, repetitions, and any command absent from the current generic packet. A `03/02` ACK alone is insufficient.

- [ ] **Step 3: Add exact test vectors and implementation**

Only after a deterministic sequence is documented, extend the protocol/reader and remove the production rejection for Breathing.

- [ ] **Step 4: Run 30-second visible Breathing and exact restore twice**

One run starts Off and one starts Static. Record visual confirmation and electronic readback; both must pass.

### Task 7: Validate Blade battery/sleep telemetry across a transition

**Files:**
- Create: `tools/OpenSynapse.HardwareValidation/BladeBatterySleepValidation.cs`
- Modify: `tools/OpenSynapse.HardwareValidation/Program.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeProduct710ProtocolTests.cs`
- Evidence: `artifacts/protocol/2026-08-14/blade-battery-sleep-before.json`
- Evidence: `artifacts/protocol/2026-08-14/blade-battery-sleep-after.json`

- [ ] **Step 1: Add strict plausible-range tests**

Battery raw maps to `0..100%`; time-to-sleep is `0..86400` seconds; charging/auto-sleep raw values remain explicitly labeled raw unless exact enums are proven.

- [ ] **Step 2: Add GET-only validation output**

Read each capability independently and serialize sanitized envelopes/errors without changing state.

- [ ] **Step 3: Run before and after unplug/replug or sleep/resume**

Both artifacts must contain successful strict responses. Promote only the fields with two plausible reads; do not invent semantic labels for unknown raw bytes.

- [ ] **Step 4: Commit code, sanitized evidence, and capability documentation**

```powershell
git add 'tools\OpenSynapse.HardwareValidation\BladeBatterySleepValidation.cs' 'tools\OpenSynapse.HardwareValidation\Program.cs' 'tests\OpenSynapse.Core.Tests\BladeProduct710ProtocolTests.cs' 'artifacts\protocol\2026-08-14\blade-battery-sleep-before.json' 'artifacts\protocol\2026-08-14\blade-battery-sleep-after.json' 'docs\device-capability-matrix.md'
git commit -m 'test: verify Blade battery and sleep telemetry'
```
