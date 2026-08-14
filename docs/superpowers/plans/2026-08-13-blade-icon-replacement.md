# Blade Icon Replacement Implementation Plan

> **For agentic workers:** Execute each checkbox in order and verify before continuing.

**Goal:** Replace the Blade laptop glyph with the user-selected B option while leaving every other icon and behavior unchanged.

**Architecture:** Change the same glyph constant in the protocol-family mapping and the fixed Blade selector button. Repeated device rows continue consuming `IconGlyph`, so no additional UI branches are needed.

**Tech Stack:** C# 14, .NET 10, WinUI 3, Segoe Fluent Icons.

## Global Constraints

- Replace only Blade `U+EC76` with `U+E7F8`.
- Keep Viper `U+E962`, unknown-device `U+E772`, sizes, colors, containers, bindings, and button behavior unchanged.
- Do not modify device discovery, protocols, hardware operations, or Profile behavior.
- Do not set `OPENSYNAPSE_HARDWARE_TEST` during tests.
- The workspace is not a Git repository, so there are no commit steps.

---

### Task 1: Replace and verify the Blade glyph

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Verify: `src/OpenSynapse.App`
- Verify: `tests/OpenSynapse.Core.Tests`

- [ ] Change `blade-710` from `"\uEC76"` to `"\uE7F8"` in `DeviceRowViewModel`.
- [ ] Change the fixed `BladeDeviceButton` glyph from `&#xEC76;` to `&#xE7F8;`.
- [ ] Search application source for `EC76`; expect no matches, and confirm `E962` remains present for Viper.
- [ ] Stop the running app before building to avoid an executable lock.
- [ ] Run `dotnet build '.\src\OpenSynapse.App\OpenSynapse.App.csproj' -p:Platform=x64 --no-restore`; require 0 warnings and 0 errors.
- [ ] Run `dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore`; require 0 failures with hardware tests skipped.
- [ ] Start the latest build and inspect Blade icons in the overview list, device-page summary, and selector button; verify Viper remains unchanged and do not click hardware write buttons.
