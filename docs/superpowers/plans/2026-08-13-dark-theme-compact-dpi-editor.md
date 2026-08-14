# Dark Theme and Compact DPI Editor Implementation Plan

> **For agentic workers:** Execute each checkbox in order and verify before continuing.

**Goal:** Make OpenSynapse dark-only and replace the stretched Viper DPI-stage controls with a compact toolbar and five-row table.

**Architecture:** Keep the existing semantic resource keys and ViewModel bindings. Set their application defaults to the approved dark palette, delete the theme-switch UI and branches, and constrain the existing native WinUI `NumberBox` controls directly in XAML.

**Tech Stack:** C# 14, .NET 10, WinUI 3, Windows App SDK 1.8.

## Global Constraints

- Do not change device protocols, setters, validation, rollback, Profile behavior, or `MainViewModel`.
- Do not add dependencies, custom controls, or theme persistence.
- Do not set `OPENSYNAPSE_HARDWARE_TEST` during tests.
- Do not click any hardware write button during visual verification.
- The workspace is not a Git repository, so there are no commit steps.

---

### Task 1: Fix the application to the dark palette

**Files:**
- Modify: `src/OpenSynapse.App/App.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: existing semantic resource keys and `AppWindow.TitleBar` customization.
- Produces: a fixed dark resource palette with no user-facing theme selector.

- [ ] Change `Application.RequestedTheme` from `Light` to `Dark` and replace the initial light brush values with the existing dark values from `ApplyThemeColors(true)`.
- [ ] Delete `ThemeButtonStyle` because no controls consume it after removing the selector.
- [ ] Delete the complete `NavigationView.PaneFooter` containing “外观”, “浅色”, and “深色”.
- [ ] Delete `LightThemeClick` and `DarkThemeClick`.
- [ ] Replace `SetTheme(ElementTheme.Light)` with a fixed `ApplyDarkTheme()` call.
- [ ] Make `ApplyDarkTheme()` request `ElementTheme.Dark`, apply fixed dark title-bar colors, and refresh the selected device button.
- [ ] Remove light/dark branches and the obsolete `ApplyThemeColors`, leaving no runtime theme-switch path.

### Task 2: Compact the Viper DPI-stage editor

**Files:**
- Modify: `src/OpenSynapse.App/MainWindow.xaml`

**Interfaces:**
- Consumes: existing `ViperDpiStagesText`, `ViperDpiStageCount`, `ViperActiveDpiStage`, `ViperDpiStages`, `CanSetViperDpiStages`, and `ApplyDpiStagesClick`.
- Produces: the same bindings and action in a stable-height layout.

- [ ] Put the title and readback summary in their own `StackPanel` above the toolbar.
- [ ] Build a compact toolbar with separate labels above two `100`-pixel NumberBoxes at explicit `Height="34"`, followed by the existing bottom-aligned apply button.
- [ ] Keep a three-column header using `44,128,128` column widths.
- [ ] Render each stage in a `44,128,128` grid with `Height="34"`; make both X/Y NumberBoxes fill that row and keep native compact spin buttons.
- [ ] Reduce row spacing to `6` and retain the existing full-table scope note.

### Task 3: Verify and adversarially review

**Files:**
- Verify: `src/OpenSynapse.App`
- Verify: `tests/OpenSynapse.Core.Tests`

- [ ] Search source XAML/C# for `LightTheme`, `DarkTheme`, `ThemeButtonStyle`, `RequestedTheme="Light"`, and visible “外观”; expect no matches.
- [ ] Run `dotnet build '.\src\OpenSynapse.App\OpenSynapse.App.csproj' -p:Platform=x64 --no-restore`; require 0 warnings and 0 errors.
- [ ] Run `dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore`; expect 325 passing, 9 hardware tests skipped, and 0 failing.
- [ ] Restart the built application and inspect the sidebar and Viper DPI-stage region without clicking any apply button.
- [ ] Attack the likely failures: leftover light resources, invisible dark text, stretched toolbar fields, clipped fifth stage, and accidental binding or hardware logic changes. Fix any observed issue before delivery.
