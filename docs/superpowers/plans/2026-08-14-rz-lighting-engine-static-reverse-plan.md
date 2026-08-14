# RzLightingEngine Static Reverse Engineering Plan

> **For agentic workers:** This is a read-only reverse-engineering pass. Do not send HID reports or change device state until a separate evidence review approves it.

**Goal:** Recover the local Razer Basic Lighting Engine's effect dispatch and frame-generation boundaries so OpenSynapse can replace its approximate quick-effect renderers with evidence-backed implementations.

**Architecture:** Treat `RzLightingEngineApi_v4.0.55.0.dll` as the primary target. First record PE exports/imports/strings and Ghidra function references, then map the JSON effect schema and native effect objects to frame-buffer writes. Preserve the existing OpenSynapse matrix transport; only renderer formulas may be changed after evidence review.

**Tech Stack:** Ghidra 12.1.2 headless analysis, LLVM `llvm-readobj`/`llvm-objdump`, PowerShell 7, .NET 10, existing OpenSynapse lighting tests.

## Global Constraints

- Read-only analysis until a human-approved dynamic validation step.
- Do not launch Synapse or send HID reports during static analysis.
- Do not copy Razer binaries into the repository; record path, version, SHA-256, and redacted findings only.
- Do not claim visual parity from ACKs, strings, or decompiler output alone.

### Task 1: Freeze Binary Evidence

**Files:**
- Create: `artifacts/reverse-engineering/2026-08-14/rz-lighting-engine-binary.json`
- Create: `artifacts/reverse-engineering/2026-08-14/rz-lighting-engine-exports.txt`

- [x] Record the DLL path, version `4.0.55.0`, SHA-256, PE architecture, exports, imports, and effect-related strings.
- [x] Verify the artifact contains no device paths, serial numbers, or user identifiers.

### Task 2: Headless Ghidra Function Map

**Files:**
- Create: `artifacts/reverse-engineering/2026-08-14/ghidra/`
- Create: `artifacts/reverse-engineering/2026-08-14/rz-lighting-engine-function-map.md`

- [x] Run `support\analyzeHeadless.bat` against the original DLL into a disposable Ghidra project.
- [x] Export function and string references for `RzLightingApi`, `CreateLightingEngine`, `BreathingEffect`, `SpectrumEffect`, `Wave`, `Fire`, `Reactive`, `Ripple`, and `Starlight`.
- [x] Record calling-convention uncertainty explicitly; do not infer parameter types from names alone.

### Task 3: Renderer Boundary Decision

**Files:**
- Modify only after review: `src/OpenSynapse.Windows/Lighting/QuickLightingEngine.cs`
- Test only after review: `tests/OpenSynapse.Core.Tests/QuickLightingEngineTests.cs`

- [x] Decide whether each effect's native code writes a 6 x 17 RGB frame directly or emits normalized parameters consumed by another renderer.
- [x] Keep unproven Fire geometry and Wave timing unchanged where static evidence is insufficient.
- [x] For promoted Spectrum/Breathing formulas, add deterministic fixture tests before changing production code.

### Task 4: Self-Review and Handoff

- [x] Attack the findings for false symbol matches, compiler-generated names, RGB/BGR confusion, timing-unit errors, and unproven randomness.
- [x] Report which effects are statically recoverable, which require a one-time dynamic frame capture, and which remain blocked.
