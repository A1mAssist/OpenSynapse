# OpenSynapse Device Workspace Implementation Plan

> **For agentic workers:** Implement task-by-task and review after every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current capability-based shell with `概览 / 设备 / 配置 / 诊断`, move Blade and Viper controls into a device workspace, and simplify the overview into device status followed by system telemetry.

**Architecture:** Keep the existing single `MainWindow` and `MainViewModel`. Reorganize the current XAML surfaces and use small code-behind navigation helpers; do not introduce page classes, routing infrastructure, DI, or backend changes. Add only the minimum profile-status binding needed to make the new configuration workspace truthful.

**Tech Stack:** WinUI 3, Windows App SDK 1.8, .NET 10, XAML, existing `INotifyPropertyChanged` ViewModel.

## Global Constraints

- Application navigation contains only `概览 / 设备 / 配置 / 诊断`.
- Device names and device-specific capabilities do not appear as application-level navigation entries.
- Preserve all existing verified write gates and event handlers.
- Do not expose SourceBacked or Blocked hardware capabilities as enabled controls.
- Keep Mica, native caption buttons, light/dark themes, current typefaces, and 4px surface corners.
- Add no packages, page framework, DI container, protocol API, or device service.
- The workspace is not a Git repository, so no commit step can be executed.

---

### Task 1: Global Shell and Overview

**Files:**
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: existing `MainViewModel.Devices`, status fields, telemetry fields, `RefreshClick`, and `NavigationChanged`.
- Produces: top-level page tags `overview`, `devices`, `profiles`, and `diagnostics`; named surfaces `OverviewPage`, `DevicesPage`, `ProfilesPage`, and `DiagnosticsPage`.

- [ ] **Step 1: Replace the top-level menu**

Use four `NavigationViewItem` elements only:

```xml
<NavigationViewItem Content="概览" Tag="overview" IsSelected="True">
  <NavigationViewItem.Icon><SymbolIcon Symbol="Home" /></NavigationViewItem.Icon>
</NavigationViewItem>
<NavigationViewItem Content="设备" Tag="devices">
  <NavigationViewItem.Icon><SymbolIcon Symbol="AllApps" /></NavigationViewItem.Icon>
</NavigationViewItem>
<NavigationViewItem Content="配置" Tag="profiles">
  <NavigationViewItem.Icon><SymbolIcon Symbol="Library" /></NavigationViewItem.Icon>
</NavigationViewItem>
<NavigationViewItem Content="诊断" Tag="diagnostics">
  <NavigationViewItem.Icon><SymbolIcon Symbol="Document" /></NavigationViewItem.Icon>
</NavigationViewItem>
```

- [ ] **Step 2: Simplify the overview header and device summaries**

Rename `PerformancePage` to `OverviewPage`. Remove the three metric cards that repeat connected-device count and Blade/Viper status. Remove the `快速操作` card. Keep one connected-device section using the existing `Devices` collection, followed immediately by the existing CPU/GPU/memory/storage telemetry grid.

The page order must be:

```text
Header + refresh
Conditional InfoBar
Connected devices
CPU / GPU / memory / storage
Telemetry timestamp
```

Do not add a healthy-state banner. Keep `InfoBar IsOpen="{Binding HasError}"` so only actual errors occupy that position.

- [ ] **Step 3: Update top-level navigation code**

Replace the current visibility mapping with exactly four pages:

```csharp
private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
{
    var page = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
    OverviewPage.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
    DevicesPage.Visibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
    ProfilesPage.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
    DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
    BreadcrumbText.Text = page switch
    {
        "devices" => "设备",
        "profiles" => "配置",
        "diagnostics" => "诊断",
        _ => "概览",
    };
}
```

Update all `QuickNavigateClick` tags to one of these four values or remove the obsolete shortcut.

- [ ] **Step 4: Build the app shell**

Run:

```powershell
dotnet build .\src\OpenSynapse.App\OpenSynapse.App.csproj -p:Platform=x64 --no-restore
```

Expected: `0` warnings and `0` errors.

---

### Task 2: Device Workspace

**Files:**
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: existing Blade/Viper bindings and `ApplyBrightnessClick`, `ApplyPollingRateClick`, `ApplyDpiClick`, `ApplyIdleClick`, and `ApplyInternalDisplayRefreshRateClick` handlers.
- Produces: one `DevicesPage` containing `DeviceSelector`, `BladeDevicePanel`, and `ViperDevicePanel`; helper `SelectDevice(string device)`.

- [ ] **Step 1: Add the device selector to `DevicesPage`**

Keep the current discovered-device list at the start of the page. Add a compact selector below it using native buttons with tags:

```xml
<StackPanel Orientation="Horizontal" Spacing="8">
  <Button Content="Blade 16 2025" Tag="blade" Click="DeviceSelectorClick" Style="{StaticResource SecondaryButtonStyle}" />
  <Button Content="Viper V3 HyperSpeed" Tag="viper" Click="DeviceSelectorClick" Style="{StaticResource SecondaryButtonStyle}" />
</StackPanel>
```

Use buttons rather than a nested `NavigationView`; there are exactly two fixed target devices and no need for another navigation system.

- [ ] **Step 2: Move the Blade controls into `BladeDevicePanel`**

Move, without changing bindings or write handlers:

- the full existing keyboard-brightness control from `LightingPage`;
- Blade performance mode, fan, charge-limit, GPU, and internal-display readouts already present in the XAML;
- the internal display refresh-rate write control and its existing gating;
- the informational advanced-lighting blocked state.

The panel heading is `Blade 16 2025`. Its section order is `状态 / 灯光 / 性能 / 显示 / 电池`. Read-only and blocked items remain visibly non-interactive.

- [ ] **Step 3: Move the Viper controls into `ViperDevicePanel`**

Move the existing Viper battery, polling rate, DPI, and idle timeout controls from `MousePage` without changing bindings, ranges, or event handlers. The panel heading is `Viper V3 HyperSpeed`; its section order is `状态 / DPI 与轮询率 / 电源`.

- [ ] **Step 4: Remove obsolete page surfaces**

Delete `LightingPage` and `MousePage` only after all their bindings and click handlers occur inside the two device panels. Verify with:

```powershell
rg -n 'LightingPage|MousePage|Tag="lighting"|Tag="mouse"' .\src\OpenSynapse.App
```

Expected: no matches.

- [ ] **Step 5: Add minimal device-panel switching**

Add code-behind only:

```csharp
private void DeviceSelectorClick(object sender, RoutedEventArgs e)
{
    if (sender is Button { Tag: string device })
    {
        SelectDevice(device);
    }
}

private void SelectDevice(string device)
{
    BladeDevicePanel.Visibility = device == "blade" ? Visibility.Visible : Visibility.Collapsed;
    ViperDevicePanel.Visibility = device == "viper" ? Visibility.Visible : Visibility.Collapsed;
}
```

Default to Blade in XAML and do not add selection state to `MainViewModel`; this is transient UI state.

- [ ] **Step 6: Build after moving controls**

Run:

```powershell
dotnet build .\src\OpenSynapse.App\OpenSynapse.App.csproj -p:Platform=x64 --no-restore
```

Expected: `0` warnings and `0` errors, including no missing XAML event handler or binding compile errors.

---

### Task 3: Truthful Configuration Workspace and Visual Verification

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify only if needed: `src/OpenSynapse.App/App.xaml`

**Interfaces:**
- Consumes: existing private `_profile`, `LoadProfileAsync`, power-source state, and application-profile switching behavior.
- Produces: read-only `ProfileStatusText` binding and `ProfilesPage`.

- [ ] **Step 1: Expose one truthful profile status string**

Add a backing field and property:

```csharp
private string _profileStatusText = "等待加载本地配置";

public string ProfileStatusText
{
    get => _profileStatusText;
    private set => SetField(ref _profileStatusText, value);
}
```

In `LoadProfileAsync`, set `ProfileStatusText` to `"本地配置已加载"` after a successful load and `"已使用默认本地配置"` when falling back to `ProfileDocument.CreateDefault()`. Do not expose the profile path, application paths, or private identifiers.

- [ ] **Step 2: Add `ProfilesPage`**

Create a read-only page that states only behavior the backend already performs:

```xml
<ScrollViewer x:Name="ProfilesPage" Visibility="Collapsed">
  <StackPanel Padding="24,24,32,48" Spacing="16">
    <TextBlock Text="配置" Style="{StaticResource PageTitleStyle}" />
    <TextBlock Text="本地配置、应用绑定以及插电和电池策略。" Foreground="{StaticResource MutedBrush}" />
    <Border Style="{StaticResource CardStyle}" Padding="20">
      <StackPanel Spacing="8">
        <TextBlock Text="本地配置" FontSize="18" FontWeight="SemiBold" />
        <TextBlock Text="{Binding ProfileStatusText}" Foreground="{StaticResource AccentBrush}" />
        <TextBlock Text="配置保存在本机；前台应用或电源状态变化时自动应用已验证的设置。" TextWrapping="Wrap" Foreground="{StaticResource MutedBrush}" />
      </StackPanel>
    </Border>
  </StackPanel>
</ScrollViewer>
```

Do not add edit, import, export, application-picker, or policy controls in this pass because no corresponding UI contract exists.

- [ ] **Step 3: Run the core tests**

Run:

```powershell
dotnet test .\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore
```

Expected: `0` failures. Existing hardware-dependent skips may remain.

- [ ] **Step 4: Run the full solution build**

Run:

```powershell
dotnet build .\OpenSynapse.slnx --no-restore
```

Expected: `0` warnings and `0` errors.

- [ ] **Step 5: Launch and perform runtime review**

Launch:

```powershell
.\src\OpenSynapse.App\bin\x64\Debug\net10.0-windows10.0.19041.0\OpenSynapse.App.exe
```

Check:

1. Sidebar contains only `概览 / 设备 / 配置 / 诊断`.
2. Overview has no persistent healthy-state summary card and no quick-action card.
3. Device selector switches between Blade and Viper without changing backend state.
4. Blade brightness and Viper DPI/polling/idle controls retain their existing enabled gates.
5. Blocked/read-only capabilities are not clickable.
6. Light and dark themes remain readable.
7. At narrow width, `NavigationView` collapses without content overlap.

- [ ] **Step 6: Adversarial review**

Attack the implementation against these likely failures and fix any observed issue:

1. A moved write control lost its original `IsEnabled` binding or click handler.
2. `LightingPage`/`MousePage` references remain in code-behind and cause a runtime or XAML compile failure.
3. Profile copy claims editability or application behavior the backend does not provide.
4. Device detail content overflows or nests cards inside cards at the default `1180 x 800` window.
5. Theme brushes or selected device styling become unreadable in dark mode.
