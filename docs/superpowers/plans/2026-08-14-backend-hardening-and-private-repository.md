# OpenSynapse Backend Hardening and Private Repository Plan

> **For agentic workers:** Execute each task against the current workspace and preserve all current-path readback gates.

**Goal:** Further harden the remaining evidence-backed backend paths, complete the existing Profile management surface, and publish the verified workspace to a new private GitHub repository.

**Architecture:** Hardware writes remain limited to `Verified` capabilities. Lighting changes may only use the recovered local Synapse engine evidence and must retain an explicit approximation label where timing or mapping is still unknown. Profile UI continues to call `ProfileCatalog`, `ApplicationProfileBinding`, and `ProfileStore` through `MainViewModel`; Git publication occurs only after tests, build, secret scanning, and ignore rules pass.

**Tech Stack:** .NET 10, WinUI 3, C#, Windows App SDK, xUnit, GitHub CLI.

## Global Constraints

- Do not expose Blade manual fan/curve writes, Logo Breathing, Viper battery chemistry, mapping, HyperShift, calibration, or raw report controls without complete current-device read/write/readback/restore evidence.
- Keep GPU MUX, macros, and the advanced lighting editor excluded.
- Do not commit `bin`, `obj`, IDE state, user-specific paths, logs, captures, temporary Ghidra projects, or credentials.
- Create `A1mAssist/OpenSynapse` as a private repository and verify visibility after push.

### Task 1: Lighting Evidence Hardening

- [x] Recheck recovered Wave and Fire constructor/frame evidence for concrete parameters that can be applied without guessing.
- [x] Implement only proven timing, color-stop, or state-transition improvements in `QuickLightingEngine` (no renderer changes were justified by the recovered evidence).
- [x] Add focused deterministic tests for every changed renderer (not applicable because no renderer was changed).
- [x] Keep unresolved mapping/rate claims marked `SourceBacked` or approximate.

### Task 2: Profile Management Completion

- [x] Add Profile clone and rename commands with transactional rollback.
- [x] Add application binding and unbinding through the existing Core API.
- [x] Add Profile import/export through WinUI file pickers initialized with the current window handle.
- [x] Keep XAML free of JSON and filesystem implementation details.

### Task 3: Backend and UI Contract Audit

- [x] Verify all production setters still require a successful current-path GET.
- [x] Verify blocked capabilities have no App/XAML entry point.
- [x] Update capability and frontend handoff documents to match the actual code.

### Task 4: Repository Hygiene and Verification

- [x] Add minimal `.gitignore` rules for .NET, WinUI, IDE state, local captures, and temporary reverse-engineering projects.
- [x] Scan tracked candidates for secrets, absolute usernames, generated binaries, and oversized files.
- [x] Run the full non-hardware test suite and Release solution build.
- [x] Perform adversarial review and fix all P1/P2 findings.

### Task 5: Private GitHub Publication

- [x] Initialize the local Git repository with branch `main`.
- [x] Commit the verified source tree.
- [x] Create `A1mAssist/OpenSynapse` with private visibility.
- [x] Push `main`, verify remote branch/commit, and verify repository visibility is `PRIVATE`.
