# Device Type Icons Implementation Plan

> **For agentic workers:** Execute each checkbox in order and verify before continuing.

**Goal:** Make Blade laptops and Viper mice visually distinct in the three existing device-selection surfaces.

**Architecture:** Map the existing protocol family once in `DeviceRowViewModel` to a Fluent icon glyph and accessible label. Bind those values in repeated device rows, while the two fixed selector buttons use the same known glyph constants directly in XAML.

**Tech Stack:** C# 14, .NET 10, WinUI 3, Windows App SDK 1.8, Segoe Fluent Icons.

## Global Constraints

- Use `U+EC76` (`LaptopSelected`) for `blade-710` and `U+E962` (`Mouse`) for `viper-184`.
- Unknown protocol families keep the generic `U+E772` (`Devices`) glyph.
- Do not infer type from device name, VID/PID, or list order.
- Do not add image assets, SVG files, packages, dependencies, animation, or a new classification layer.
- Do not modify device discovery, protocols, hardware operations, or Profile behavior.
- Do not set `OPENSYNAPSE_HARDWARE_TEST` during tests.
- The workspace is not a Git repository, so there are no commit steps.

---

### Task 1: Expose device glyph metadata

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `DeviceDescriptor.ProtocolFamily`.
- Produces: `DeviceRowViewModel.IconGlyph` and `DeviceRowViewModel.IconAutomationName`.

- [ ] In the existing `DeviceRowViewModel` constructor, map `blade-710` to `"\uEC76"` and `"笔记本设备"`, `viper-184` to `"\uE962"` and `"鼠标设备"`, and other values to `"\uE772"` and `"设备"`.
- [ ] Expose both mapped values as immutable string properties next to the existing display properties.

### Task 2: Display the icons in all device-selection surfaces

**Files:**
- Modify: `src/OpenSynapse.App/MainWindow.xaml`

**Interfaces:**
- Consumes: Task 1 glyph and automation-name properties, existing Blade/Viper names, and existing selector click handlers.
- Produces: distinct device icons in overview rows, device-page rows, and selector buttons.

- [ ] Replace the overview row's generic `SymbolIcon` with a 20px `FontIcon` bound to `IconGlyph`, using `Segoe Fluent Icons`, `AccentBrush`, and the bound accessible name.
- [ ] Add a 42px icon block to each device-page summary row and shift name/identity to the next column while preserving right-aligned status.
- [ ] Replace plain selector-button content with horizontal icon-and-name stacks: `&#xEC76;` for Blade and `&#xE962;` for Viper at 18px.
- [ ] Keep all button tags, names, styles, click handlers, and selection-background logic unchanged.

### Task 3: Verify and adversarially review

**Files:**
- Verify: `src/OpenSynapse.App`
- Verify: `tests/OpenSynapse.Core.Tests`

- [ ] Search source to confirm the protocol-family mapping exists once and repeated rows bind to it rather than parsing names.
- [ ] Stop the running app before building to avoid an executable lock.
- [ ] Run `dotnet build '.\src\OpenSynapse.App\OpenSynapse.App.csproj' -p:Platform=x64 --no-restore`; require 0 warnings and 0 errors.
- [ ] Run `dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore`; expect 325 passing, 9 hardware tests skipped, and 0 failing.
- [ ] Start the latest build and visually inspect overview rows, device-page rows, and both selector buttons without clicking hardware write buttons.
- [ ] Attack the likely failures: missing-glyph squares, indistinguishable silhouettes, clipped device names, displaced status text, and lost button click/selection behavior.
