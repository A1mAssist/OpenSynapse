# Integrated Title Bar Implementation Plan

> **For agentic workers:** Execute each checkbox in order and verify before continuing.

**Goal:** Merge the Windows title bar and OpenSynapse page header into one 48px dark title bar.

**Architecture:** Promote `AppTitleBar` from inside `NavigationView.Content` to the first row of the root grid. Put the `NavigationView` in the second row, register the promoted grid as the WinUI drag region, and retain native Windows caption buttons.

**Tech Stack:** C# 14, .NET 10, WinUI 3, Windows App SDK 1.8.

## Global Constraints

- Keep the title bar exactly 48px high and the expanded navigation pane exactly 224px wide.
- Keep native Windows minimize, maximize, and close buttons.
- Do not add search, back navigation, custom caption buttons, dependencies, or state.
- Do not modify device, telemetry, Profile, tray, or hardware-control behavior.
- Do not set `OPENSYNAPSE_HARDWARE_TEST` during tests.
- The workspace is not a Git repository, so there are no commit steps.

---

### Task 1: Promote and register the application title bar

**Files:**
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: existing `AppTitleBar`, `BreadcrumbText`, fixed dark brushes, and `ApplyTitleBarColors()`.
- Produces: one custom drag region with native Windows caption buttons.

- [ ] Give the root grid `48,*` rows and move `AppTitleBar` into row 0 with `224,*` columns.
- [ ] Move the existing logo, product name, and subtitle from `NavigationView.PaneHeader` into title-bar column 0.
- [ ] Move the existing `OpenSynapse / BreadcrumbText` content into title-bar column 1.
- [ ] Put `RootNavigationView` in root row 1, remove its PaneHeader, and set `IsTitleBarAutoPaddingEnabled="False"`.
- [ ] Remove the nested `48,*` content grid and its duplicate internal `AppTitleBar`; page scroll viewers become direct children of `NavigationView.Content`.
- [ ] After `InitializeComponent()`, set `ExtendsContentIntoTitleBar = true` and call `SetTitleBar(AppTitleBar)` before applying title-bar colors.

### Task 2: Verify structure, behavior, and regressions

**Files:**
- Verify: `src/OpenSynapse.App`
- Verify: `tests/OpenSynapse.Core.Tests`

- [ ] Search source for exactly one `x:Name="AppTitleBar"`, no `NavigationView.PaneHeader`, and both `ExtendsContentIntoTitleBar` and `SetTitleBar(AppTitleBar)`.
- [ ] Stop the running app before building so the executable is not locked.
- [ ] Run `dotnet build '.\src\OpenSynapse.App\OpenSynapse.App.csproj' -p:Platform=x64 --no-restore`; require 0 warnings and 0 errors.
- [ ] Run `dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore`; expect 325 passing, 9 hardware tests skipped, and 0 failing.
- [ ] Start the latest x64 build and inspect all four pages for a single title row, aligned branding and breadcrumb, visible caption buttons, and unobstructed page content.
- [ ] Verify the title-bar blank area drags the window and double-click toggles maximize without clicking any hardware write button.
- [ ] Attack the likely failures: duplicate top padding, repeated branding, caption-button overlap, inactive drag region, and page content shifted or clipped.
