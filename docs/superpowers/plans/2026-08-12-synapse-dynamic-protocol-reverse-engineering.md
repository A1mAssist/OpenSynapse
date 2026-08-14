# Synapse Dynamic Protocol Reverse Engineering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a repeatable Windows capture-and-diff workflow that turns controlled Synapse operations into redacted, reviewable Razer feature-report evidence for capabilities still blocked by missing protocol contracts.

**Architecture:** Use Wireshark's USBPcap capture path and `tshark` for USB decoding instead of implementing a capture driver. A small PowerShell wrapper owns capture metadata and raw-file handling; a .NET analyzer reuses `RazerFeatureReport` rules to normalize 91-byte requests/responses and compare one-variable experiments. Raw captures stay outside the workspace in a user-local directory; only reviewed, command-specific redacted evidence may enter the repository.

**Tech Stack:** Windows 11, PowerShell 7, Wireshark/USBPcap, `tshark`, .NET 10, C#, `System.Text.Json`, xUnit.

## Global Constraints

- Target only Blade 16 2025 `1532:02C6` and Viper V3 HyperSpeed `1532:00B8`.
- GPU MUX/Dedicated GPU-only is explicitly excluded for this Optimus-only Blade.
- Advanced lighting editor, macros, and Viper calibration remain deferred.
- Never terminate Synapse; the operator opens/closes it explicitly when a validation phase requires ownership transfer.
- Change exactly one visible Synapse value per capture experiment, record the original value first, and restore it through Synapse before ending capture.
- Never turn a captured SET acknowledgement into a claimed GET/readback contract.
- Raw `pcapng`, `tshark` JSON, and unreviewed full report hex may include unrelated USB traffic, descriptors, or identity values. Store them outside the workspace under `%LocalAppData%\OpenSynapse\reverse-engineering\raw\` and never publish them.
- Redacted artifacts must omit HID paths, USB serials, Windows account names, machine names, Razer account data, tokens, and unrelated-device payloads.
- Production promotion still requires current-device read, same-value/minimal-change write, readback or deterministic behavior check, and restoration with Synapse closed.
- The workspace currently has no Git repository; do not add commit steps or claim commits.

---

## File Map

- Create: `tools/OpenSynapse.ProtocolAnalysis/OpenSynapse.ProtocolAnalysis.csproj` - standalone normalized-report analyzer.
- Create: `tools/OpenSynapse.ProtocolAnalysis/Program.cs` - CLI parsing and JSON output.
- Create: `tools/OpenSynapse.ProtocolAnalysis/CapturedTransfer.cs` - `tshark` TSV input model and strict hex decoding.
- Create: `tools/OpenSynapse.ProtocolAnalysis/RazerReportExchange.cs` - request/response pairing and redaction-safe output.
- Create: `tools/ProtocolCapture/Capture-RazerProtocol.ps1` - bounded USBPcap capture and metadata wrapper.
- Create: `tools/ProtocolCapture/Export-RazerTransfers.ps1` - `tshark` field export with device-address filtering.
- Create: `tests/OpenSynapse.Core.Tests/ProtocolAnalysisTests.cs` - strict parser, pairing, diff, and redaction tests.
- Modify: `OpenSynapse.slnx` - include only the analyzer project; PowerShell scripts remain standalone.
- Modify: `docs/protocol/probe-schema.md` - define raw versus redacted reverse-engineering artifacts.
- Modify after evidence exists: `docs/protocol/capability-ledger.md` and `docs/device-capability-matrix.md`.

### Task 1: Install And Prove The Capture Toolchain

**Files:**
- No repository files changed.

**Interfaces:**
- Consumes: Windows USB stack and the operator's installed Synapse.
- Produces: verified paths for `tshark.exe` and the USBPcap capture interface used by later tasks.

- [ ] **Step 1: Install Wireshark with USBPcap enabled**

Install the current x64 Wireshark package and keep the installer option for USBPcap enabled. Do not install Npcap solely for this workflow; USB HID feature traffic requires USBPcap.

- [ ] **Step 2: Verify executables and capture interfaces**

Run:

```powershell
& 'C:\Program Files\Wireshark\tshark.exe' --version
& 'C:\Program Files\Wireshark\tshark.exe' -D
Get-ChildItem 'C:\Program Files\USBPcap' -File
```

Expected: `tshark` reports a Wireshark version, at least one interface named `USBPcap1` or higher is listed, and `USBPcapCMD.exe` exists.

- [ ] **Step 3: Verify the required decoder fields instead of assuming field names**

Run:

```powershell
$fields = & 'C:\Program Files\Wireshark\tshark.exe' -G fields
$required = 'usb.bus_id','usb.device_address','usb.endpoint_address.direction','usb.transfer_type','usb.setup.bRequest','usb.setup.wValue','usb.capdata'
foreach ($field in $required) {
    if (-not ($fields -match "`t$field`t")) { throw "Missing tshark field: $field" }
}
```

Expected: no exception. If a field is missing, stop and pin the Wireshark version before changing any script; do not guess a replacement field.

### Task 2: Add A Bounded Capture Wrapper

**Files:**
- Create: `tools/ProtocolCapture/Capture-RazerProtocol.ps1`
- Create: `tools/ProtocolCapture/Export-RazerTransfers.ps1`
- Modify: `docs/protocol/probe-schema.md`

**Interfaces:**
- Consumes: `-Interface`, `-DeviceAddress`, `-Experiment`, `-DurationSeconds`, `-ProductId`, and the exact original/changed/restored UI values.
- Produces locally: `%LocalAppData%\OpenSynapse\reverse-engineering\raw\YYYY-MM-DD\<experiment>\capture.pcapng`, `capture-manifest.json`, and `transfers.tsv`.

- [ ] **Step 1: Write the capture wrapper with mandatory bounded duration and metadata**

The script must reject durations outside `5..120` seconds, experiment names outside `[a-z0-9-]+`, and product IDs other than `02C6`/`00B8`. `-Interface` must exactly match an interface returned by `tshark -D` with name `USBPcap<positive integer>`; `-DeviceAddress` must be a parsed integer in `1..127` returned by a preceding enumeration, never an arbitrary filter fragment. It launches `tshark -i <USBPcapN> -a duration:<seconds> -w <local raw/capture.pcapng>` elevated, then records tool version, UTC timestamps, PID, numeric device address, original value, changed value, restored value, and SHA-256 of the raw capture. It must never record a HID path or USB serial.

- [ ] **Step 2: Export only the selected USB device's data-bearing transfers**

Run `tshark` with this fixed field set:

```powershell
& $Tshark -r $CapturePath `
  -Y "usb.device_address == $DeviceAddress && usb.capdata" `
  -T fields -E header=y -E separator='`t' -E quote=d -E occurrence=a `
  -e frame.number -e frame.time_relative -e usb.bus_id -e usb.device_address `
  -e usb.endpoint_address.direction -e usb.transfer_type `
  -e usb.setup.bRequest -e usb.setup.wValue -e usb.capdata
```

Write UTF-8 TSV in the same user-local raw directory. The analyzer, not the PowerShell script, decides whether a payload contains a 91-byte Razer report.

- [ ] **Step 3: Document raw-artifact handling**

Add to `docs/protocol/probe-schema.md`: raw captures and full report hex stay outside the workspace, command-specific redacted JSON is the only artifact eligible for ledger citation, and a successful USB transaction is not by itself a verified device state.

- [ ] **Step 4: Dry-run validation without Synapse state changes**

Capture 10 seconds of idle traffic and confirm the wrapper stops automatically, creates all three files, and stores a valid SHA-256. Do not infer protocol fields from this idle run.

### Task 3: Normalize And Pair Razer Feature Reports

**Files:**
- Create: `tools/OpenSynapse.ProtocolAnalysis/OpenSynapse.ProtocolAnalysis.csproj`
- Create: `tools/OpenSynapse.ProtocolAnalysis/Program.cs`
- Create: `tools/OpenSynapse.ProtocolAnalysis/CapturedTransfer.cs`
- Create: `tools/OpenSynapse.ProtocolAnalysis/RazerReportExchange.cs`
- Create: `tests/OpenSynapse.Core.Tests/ProtocolAnalysisTests.cs`
- Modify: `OpenSynapse.slnx`

**Interfaces:**
- Consumes: `transfers.tsv`, PID, and experiment manifest.
- Produces locally: an unredacted normalized file containing selected-device 91-byte Razer reports and request/response pairs; produces in the workspace only an allowlisted command-specific redacted artifact.

- [ ] **Step 1: Add failing strict-decoder tests**

Cover colon-separated and unseparated hex, multiple `usb.capdata` occurrences, wrong lengths, malformed hex, CRC failures, and a known `RazerFeatureReport.CreateRequest` fixture. A report is accepted only when a contiguous 91-byte candidate has a valid Razer CRC; no padding or truncation is allowed.

- [ ] **Step 2: Implement TSV parsing with `Convert.FromHexString`**

Use `TextFieldParser` only if quoted TSV requires it; otherwise split the known fixed tab columns. Use `Convert.FromHexString` after removing only `:` characters. Reject rather than repair malformed captures.

- [ ] **Step 3: Add request/response pairing tests**

Pair on transaction ID, command class, command ID, and chronological order. Preserve separate unmatched requests/responses. Do not require response remaining-packet bytes to match unless the command-specific ledger already permits that exception.

- [ ] **Step 4: Implement redacted normalized output**

The user-local normalized file may contain frame number, relative time, direction, transaction ID, status, data size, command class, command ID, declared arguments, full report hex, and CRC validity. Repository artifacts must be generated only after the operator allowlists the exact reviewed command class/ID; they omit full report hex and retain only the minimum command-specific argument fields needed to support the ledger conclusion. No interface path, serial, account, process, unrelated command, or unrelated USB payload may enter a repository artifact.

- [ ] **Step 5: Run tests and solution build**

Run:

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore
dotnet build '.\OpenSynapse.slnx' --no-restore
```

Expected: all non-hardware tests pass; hardware tests skip unless explicitly enabled; build has zero errors.

### Task 4: Calibrate Capture Against Already-Verified Controls

**Files:**
- Create locally: `artifacts/reverse-engineering/YYYY-MM-DD/viper-current-dpi-calibration/*`
- Create locally: `artifacts/reverse-engineering/YYYY-MM-DD/viper-polling-calibration/*`
- Modify only after review: `docs/protocol/probe-schema.md`

**Interfaces:**
- Consumes: known commands `0485/0405` for current DPI and `0085/0005` for polling rate.
- Produces: proof that the correct USBPcap root hub/device address and direction mapping are selected.

- [ ] **Step 1: Record the original Synapse values before capture**

For DPI, record current X/Y. For polling, record the current value from `125/500/1000 Hz`. Do not proceed if Synapse cannot display the original value.

- [ ] **Step 2: Capture a one-step DPI change and restore**

Change X and Y by exactly `50`, apply, then restore the original X/Y before the bounded capture ends.

Expected normalized request: class `04`, SET command `05`, arguments containing storage byte plus big-endian X/Y. Expected follow-up GET, if Synapse performs it: class `04`, command `85`.

- [ ] **Step 3: Capture one polling-rate change and restore**

Change only between two supported adjacent choices and restore. Expected normalized SET: class `00`, command `05`; expected GET: class `00`, command `85`.

- [ ] **Step 4: Fail the calibration if known reports are absent**

If neither known command appears, do not analyze missing capabilities. Re-identify the USBPcap root hub/device address. If the known commands still do not appear, continue at Task 7 because Synapse is likely using a service/driver path not visible in the selected transfer stream.

### Task 5: Run Controlled Viper Missing-Protocol Experiments

**Files:**
- Create locally under: `artifacts/reverse-engineering/YYYY-MM-DD/`
- Modify after confirmed evidence: `docs/protocol/capability-ledger.md`
- Modify after confirmed evidence: `docs/device-capability-matrix.md`

**Interfaces:**
- Consumes: calibrated capture pipeline and operator-visible original settings.
- Produces: exact candidate GET/SET exchanges for DPI stages, low-battery threshold, and battery chemistry.

- [ ] **Step 1: Capture DPI-stage page open without changing values**

Start capture, open the DPI-stage page, wait for it to populate, then close the page. Compare against idle. This isolates the real table/current-stage read path and determines whether `0486 [01]` is used by Synapse or whether another collection/service is involved.

- [ ] **Step 2: Capture one DPI-stage value change and restore**

Record all stages and active stage. Change exactly one non-active stage by `50`, apply, wait for any readback, restore it, and wait again. Do not reorder, add, delete, or activate stages in this experiment.

- [ ] **Step 3: Capture current-stage activation separately**

Activate one adjacent existing stage, wait, reactivate the original stage, and wait. Keeping table contents fixed separates the active-stage field from the stage-record payload.

- [ ] **Step 4: Capture low-battery threshold in two directions**

Record the original UI percentage. Change by one available UI step upward, restore, then run a separate capture changing one step downward and restoring. Two directions distinguish value bytes from counters/CRC and reveal the actual step mapping.

- [ ] **Step 5: Capture battery chemistry page open and one reversible change**

Record the original UI chemistry. First capture page open without changes to search for a GET/state source. Only if the original UI value is unambiguous, run a second capture changing to one other chemistry and restoring the original through Synapse. Treat `0714` SET echo as write acknowledgement only.

- [ ] **Step 6: Compare experiments field by field**

For every candidate, list bytes that change with the controlled value, stable envelope bytes, response status, subsequent GET, and restoration evidence. Reject candidates also present unchanged in idle/calibration traffic.

- [ ] **Step 7: Promote only evidence, not production code**

Update the ledger with exact captures and remaining gaps. Production code remains gated until Synapse is closed and OpenSynapse passes same-value write, minimal change, readback/behavior check, `finally` restoration, and for DPI stages an unplug-persistence check.

### Task 6: Run Controlled Blade Missing-Protocol Experiments

**Files:**
- Create locally under: `artifacts/reverse-engineering/YYYY-MM-DD/`
- Modify after confirmed evidence: `docs/protocol/capability-ledger.md`
- Modify after confirmed evidence: `docs/device-capability-matrix.md`

**Interfaces:**
- Consumes: the same calibrated capture pipeline, using Blade PID `02C6`.
- Produces: candidate contracts for one-time full charge, manual fan curve, mappings, Gaming Mode, Snap Tap, and Fn primary.

- [ ] **Step 1: Calibrate Blade capture using charge limit**

Change the verified charge limit by one allowed `5%` step and restore it. Require the known `0712` SET and `0792` GET before analyzing unknown Blade controls.

- [ ] **Step 2: Capture one-time full charge separately from persistent charge limit**

Record the persistent limit, invoke one-time full charge once, wait for any state refresh, then cancel it through Synapse if the UI permits. Identify a distinct command/state; do not relabel persistent `100%` as one-time full charge.

- [ ] **Step 3: Capture manual fan curve page-open and one-node changes**

First capture page open without modification. Then run separate CPU and GPU experiments, changing exactly one existing temperature node's RPM by one UI step and restoring it. Do not change performance mode, node count, or both zones in the same capture.

- [ ] **Step 4: Capture mappings and policies one at a time**

Use separate experiments for one ordinary key mapping, one HyperShift mapping, Gaming Mode, Snap Tap, and Fn primary. Restore each setting before ending its capture. Determine whether traffic reaches USB, a Razer service IPC boundary, or only AppEngine state.

- [ ] **Step 5: Keep safety-critical candidates blocked**

Fan commands remain non-runnable until an exact GET, zone model, value range, readback, and recovery owner survive cancellation, process exit, disconnect, and sleep/wake. Mapping/policy candidates require behavioral verification and restart persistence before production exposure.

### Task 7: Escalate To Targeted Service/Native Reverse Engineering Only When Needed

**Files:**
- Create locally: `artifacts/reverse-engineering/YYYY-MM-DD/service-path-inventory.json`
- Modify after evidence: `docs/protocol/capability-ledger.md`

**Interfaces:**
- Consumes: a calibrated known control that is absent from USB capture, Razer process/module inventory, and Synapse logs.
- Produces: the narrow service, module, named pipe, IOCTL, or alternate HID collection responsible for the operation.

- [ ] **Step 1: Inventory the active Razer processes and signed module paths**

Record process name, executable path, file version, signer, and loaded modules for `RzDeviceManager`, `RzDeviceManagerEx`, and the Synapse AppEngine process. Omit command lines and user-specific paths from redacted artifacts.

- [ ] **Step 2: Use Procmon on one known control first**

Filter to the identified Razer processes and operations `CreateFile`, `DeviceIoControl`, `WriteFile`, and `ReadFile`. Change one already-verified control and restore it. This establishes the actual device/service boundary before investigating an unknown function.

- [ ] **Step 3: Inspect only the module that owns the established boundary**

Use ILSpy for managed assemblies and Ghidra/x64dbg for the one native module observed in the calibrated call path. Search exact known command constants and function names first. Do not recursively decompile the whole Synapse installation.

- [ ] **Step 4: Record provenance for every recovered constant**

For each candidate command, record module SHA-256/file version, function or offset, surrounding validation/value range, and matching dynamic operation. Static constants without a matching current-device dynamic observation remain research notes, not `SourceBacked` production evidence.

### Task 8: Adversarial Review And Promotion Gate

**Files:**
- Modify: `docs/protocol/capability-ledger.md`
- Modify: `docs/device-capability-matrix.md`
- Test only after evidence: relevant protocol and `RazerDeviceTelemetryReader` tests.

**Interfaces:**
- Consumes: normalized captures, manifests, controlled-value notes, and restoration records.
- Produces: accepted or rejected protocol candidates with explicit reasons.

- [ ] **Step 1: Attack the five highest-risk assumptions**

Check that device address did not change, the candidate is absent from idle traffic, counters/CRC were not mistaken for values, SET echo was not mistaken for GET, and the operator actually restored the original UI state.

- [ ] **Step 2: Require three-value evidence for numeric fields**

Use original, one step up, and one step down where the UI permits. Two captures are insufficient when more than one payload byte varies.

- [ ] **Step 3: Require a fresh current-device read before any OpenSynapse write test**

If no successful current state exists, stop. Do not use defaults, Synapse local JSON, or a previous session's cached value as restoration state.

- [ ] **Step 4: Add the smallest parser/builder test before production integration**

One strict parser/builder test must reject wrong PID-envelope form, unknown values, malformed size, CRC error, and cross-command responses. Only then add a same-path-gated production setter following the existing `RazerDeviceTelemetryReader` pattern.

- [ ] **Step 5: Run full verification**

Run:

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore
dotnet build '.\OpenSynapse.slnx' --no-restore
dotnet build '.\src\OpenSynapse.App\OpenSynapse.App.csproj' --no-restore -p:Platform=x64
```

Expected: zero failed tests and zero build errors. Hardware writes remain opt-in and must leave a final restoration read/behavior record.

## Spec Self-Review

- Scope coverage: Viper stages/current stage, low-battery threshold, battery chemistry, Blade one-time full charge, fan curve, mappings, Gaming Mode, Snap Tap, and Fn primary each have an isolated experiment.
- Exclusions: GPU MUX, advanced lighting editor, macros, and Viper calibration are not reintroduced.
- No arbitrary HID console or raw-command option is planned; capture and analysis cannot send device commands.
- The workflow calibrates against known commands before accepting unknown candidates.
- Raw captures and unreviewed report hex remain outside the workspace; redacted evidence boundaries are explicit.
- Every production promotion still requires a separate current-device write/readback/restore gate.
