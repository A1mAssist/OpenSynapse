# WinUI Global Alignment and OpenSynapse Logo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align the OpenSynapse WinUI 3 shell and page surfaces with native Fluent layout conventions, then add one scalable SVG logo used by the brand surface and app resources.

**Architecture:** Keep the native Windows caption buttons and Mica window backdrop. Use `NavigationView` as the only application shell, flatten the context toolbar into the content surface, centralize spacing/color/control tokens in `App.xaml`, and use a single SVG source asset for brand rendering. Existing bindings and verified device capability boundaries remain unchanged.

**Tech Stack:** WinUI 3, Windows App SDK 1.8, .NET 10, XAML resource dictionaries, SVG via `SvgImageSource`, existing `MainViewModel` bindings.

## Global Constraints

- No React, WebView, Tailwind, or third-party UI library.
- Preserve existing real-device bindings and capability gating.
- Do not expose unverified hardware controls as enabled actions.
- Support light and dark themes through existing resource brushes.
- Keep `Segoe UI Variable` for UI text and `Cascadia Mono` for identifiers and telemetry values.
- Logo SVG must have a transparent background, a `0 0 32 32` viewBox, and remain legible at 20px and 32px.

---

### Task 1: Global Fluent shell tokens and layout

**Files:**
- Modify: `src/OpenSynapse.App/App.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: existing `MainViewModel` bindings and theme methods.
- Produces: a flat, native-feeling `NavigationView` shell with a 48px context bar, shared surface background, and no floating top card.

- [x] **Step 1: Define the shell token adjustments**

  Keep the existing semantic brush keys and adjust only their values/styles: use 4/8/12/16/24/32 spacing, reduce data-card corner radius to 4px, keep action buttons at 4px, and remove any style that visually nests a card inside another card.

- [x] **Step 2: Flatten the context toolbar**

  Change `AppTitleBar` to use the same surface as the content, remove its standalone card treatment, set the row to 48px, keep a single bottom divider, and retain the existing breadcrumb, refresh, and theme actions.

- [x] **Step 3: Align navigation and page spacing**

  Preserve `NavigationView` and its existing selection handlers. Adjust page padding to `24,24,32,48`, normalize section spacing to 16px, and keep one surface boundary per repeated data group.

- [x] **Step 4: Keep native caption buttons isolated**

  Do not restore `ExtendsContentIntoTitleBar` or `SetTitleBar`. Keep `ApplyTitleBarColors` synchronized with the selected theme and avoid any content element overlapping system buttons.

- [x] **Step 5: Build the app shell**

  Run:

  ```powershell
  dotnet build .\src\OpenSynapse.App\OpenSynapse.App.csproj -p:Platform=x64 --no-restore
  ```

  Expected: `0` warnings and `0` errors.

### Task 2: Unified SVG logo resource

**Files:**
- Create: `src/OpenSynapse.App/Assets/OpenSynapseLogo.svg`
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/OpenSynapse.App.csproj` only if the SDK does not include the asset automatically.

**Interfaces:**
- Consumes: the existing `AccentBrush` semantic color and the brand name `OpenSynapse`.
- Produces: a transparent, scalable `OpenSynapseLogo.svg` rendered in the sidebar and available as the single vector source for later package icon exports.

- [x] **Step 1: Draw the vector mark**

  Create a 32x32 SVG with no text and no background rectangle. Use a geometric open `O`/synapse aperture with two short signal terminals, `fill="#99DD72"`, `stroke="none"`, and shapes that remain distinct at 20px.

- [x] **Step 2: Replace the sidebar text mark**

  Replace the current single-letter `O` `TextBlock` in `NavigationView.PaneHeader` with an `Image` using `SvgImageSource` and `ms-appx:///Assets/OpenSynapseLogo.svg`; keep `OpenSynapse` as adjacent accessible text.

- [x] **Step 3: Validate asset loading**

  Build the app and launch it. Confirm the logo appears at 20–24px without a green square background, and that both light and dark themes preserve contrast.

### Task 3: Verification and visual review

**Files:**
- Test: existing `tests/OpenSynapse.Core.Tests` project (no new test required for static XAML-only styling).

**Interfaces:**
- Consumes: Tasks 1 and 2 outputs.
- Produces: verified build, test output, and runtime screenshots for light and dark themes.

- [x] **Step 1: Run the core tests**

  ```powershell
  dotnet test .\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore
  ```

  Expected: `35 passed`, `3 skipped`, `0 failed` unless the existing test set has changed independently.

- [x] **Step 2: Run the full solution build**

  ```powershell
  dotnet build .\OpenSynapse.slnx --no-restore
  ```

  Expected: `0` warnings and `0` errors.

- [x] **Step 3: Perform runtime checks**

  Launch the x64 app and verify: the context bar is flat and aligned with content, the system caption buttons are separate, the sidebar logo is visible, the light theme is readable, the dark theme changes title-bar colors, and no page binding or capability state changes.
