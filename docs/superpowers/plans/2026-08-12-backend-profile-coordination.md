# Backend Profile Coordination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing versioned profile store part of the running OpenSynapse lifecycle and safely apply only hardware capabilities already proven on the target devices.

**Architecture:** Keep `ProfileStore` as the file boundary and add a small coordinator in the application layer. The coordinator owns the in-memory active document, resolves global/device/power overrides, and calls the existing `IRazerDeviceTelemetryReader` write methods only after a successful current-device read has enabled those capabilities. Failed writes restore the ViewModel state and leave the profile unchanged.

**Tech Stack:** .NET 10, C#, WinUI 3, `System.Text.Json`, existing HID transport and xUnit test project.

## Global Constraints

- Only `Verified` writes are automatic: Blade keyboard brightness; Viper current X/Y DPI, 125/500/1000 Hz polling, and idle timeout.
- Blade performance, fan, charge, lighting, mapping, display and GPU writes remain blocked until current-device evidence promotes them.
- Macro system, advanced lighting editor and Viper calibration remain outside this slice.
- Profile data remains `%LocalAppData%\\OpenSynapse\\profiles.json` with version `1` and atomic replacement.
- A failed or unavailable capability must not overwrite the profile with an unconfirmed value.
- No new third-party dependency, service, plugin system, database, or guessed HID command.

---

### Task 1: Add deterministic profile resolution and safe apply model

**Files:**
- Create: `src/OpenSynapse.Core/Profiles/ProfileResolver.cs`
- Create: `tests/OpenSynapse.Core.Tests/ProfileResolverTests.cs`
- Modify: `src/OpenSynapse.Core/Profiles/ProfileModels.cs`

**Interfaces:**
- Consumes: `ProfileDocument`, `DeviceDescriptor`, `RazerDeviceTelemetry`.
- Produces: `ResolvedProfile` containing nullable settings for one device and one power state, with precedence `global -> device -> power override`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void DeviceAndPowerOverridesWinOverGlobalValues()
{
    var document = ProfileDocument.CreateDefault();
    document.Global.Viper.DpiX = 800;
    document.Devices["1532:00B8"] = new DeviceProfileSettings { Viper = new() { DpiX = 1600 } };
    document.PluggedIn.Viper.DpiX = 3200;

    var resolved = ProfileResolver.Resolve(
        document,
        new DeviceDescriptor("mouse", "Viper", 0x1532, 0x00B8,
            DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 0x01, 0x02),
        isPluggedIn: true);

    Assert.Equal(3200, resolved.Viper.DpiX);
}

[Fact]
public void MissingOverridesRemainNullInsteadOfInventingWrites()
{
    var resolved = ProfileResolver.Resolve(
        ProfileDocument.CreateDefault(),
        new DeviceDescriptor("mouse", "Viper", 0x1532, 0x00B8,
            DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 0x01, 0x02),
        isPluggedIn: false);

    Assert.Null(resolved.Viper.DpiX);
    Assert.Null(resolved.Blade.KeyboardBrightness);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run: `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ProfileResolver`

Expected: compile failure because `ProfileResolver` and `ResolvedProfile` do not exist.

- [ ] **Step 3: Implement the minimum resolver**

Use case-insensitive device keys in the form `VID:PID` (`1532:00B8`) and copy nullable values field-by-field. Do not merge unsupported fields into a write command; the resolver only returns data.

- [ ] **Step 4: Run the focused tests**

Run the same command. Expected: all resolver tests pass.

### Task 2: Integrate profile loading and verified apply into `MainViewModel`

**Files:**
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Modify: `src/OpenSynapse.App/App.xaml.cs`
- Modify: `src/OpenSynapse.App/MainWindow.xaml.cs`
- Create: `tests/OpenSynapse.Core.Tests/ProfileApplicationTests.cs`

**Interfaces:**
- Consumes: `ProfileStore`, `ProfileResolver`, current `IRazerDeviceTelemetryReader` and device snapshot.
- Produces: startup load, explicit save after confirmed UI writes, and one guarded `ApplyActiveProfileAsync` operation.

- [ ] **Step 1: Add a failing application test around a fake reader**

The fake reader records calls. A profile containing Blade brightness and Viper verified values must call only the four existing verified write methods. A profile containing performance mode, charge limit, fan or lighting values must produce no call for those fields.

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ProfileApplication`

Expected: compile failure for the application operation.

- [ ] **Step 3: Add `ProfileStore` to the application composition root**

Construct one store in `App.OnLaunched` and pass it to `MainViewModel`. Load the document before the first device refresh. A missing or corrupt file uses the store's safe default; a load error is reported without preventing the window from opening.

- [ ] **Step 4: Implement guarded apply**

After `ReadAsync` succeeds, resolve settings by device and power state. For each non-null verified value, require the corresponding `CanSet...` state and call the existing method. Apply one device capability at a time, stop on the first failure, refresh telemetry, and do not save unconfirmed values. Do not call blocked Blade methods from this path.

- [ ] **Step 5: Save only after confirmed user operations**

After brightness, DPI, polling or idle readback succeeds, update the active profile's verified field and call `ProfileStore.SaveAsync`. On cancellation or exception, restore the confirmed UI value and leave the previous profile field unchanged.

- [ ] **Step 6: Run focused and full tests**

Run: `dotnet test tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj --no-restore`

Expected: all tests pass; hardware tests may skip unless `OPENSYNAPSE_HARDWARE_TEST=1` is set.

### Task 3: Add minimal profile lifecycle operations

**Files:**
- Modify: `src/OpenSynapse.Core/Profiles/ProfileStore.cs`
- Modify: `src/OpenSynapse.Core/Profiles/ProfileModels.cs`
- Create: `tests/OpenSynapse.Core.Tests/ProfileLifecycleTests.cs`

**Interfaces:**
- Consumes: `ProfileDocument` and existing atomic `ProfileStore` persistence.
- Produces: create, rename, clone and delete operations with validation for empty/duplicate names.

- [ ] **Step 1: Write failing lifecycle tests**

Verify that names are trimmed, empty names are rejected, duplicate names are rejected case-insensitively, clone produces a deep independent copy, and delete refuses to remove the last profile.

- [ ] **Step 2: Implement the smallest in-memory profile collection**

Add a named profile dictionary to version `1` only if the current schema can preserve backward compatibility. Keep existing `Global`, `Devices`, `PluggedIn`, `OnBattery` as the default profile semantics; do not add a database or migration framework.

- [ ] **Step 3: Persist and test**

Run the focused lifecycle tests, then the full test project. Confirm the temporary file is removed and corrupt source files are untouched.

### Task 4: Verify and document the completed slice

**Files:**
- Modify: `docs/device-capability-matrix.md`
- Modify: `docs/protocol/capability-ledger.md`
- Modify: `README.md`

- [ ] **Step 1: Run build and tests**

```powershell
dotnet build .\OpenSynapse.slnx --no-restore
dotnet test .\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore
$env:OPENSYNAPSE_HARDWARE_TEST='1'
dotnet test .\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore --filter 'Category=Hardware'
```

- [ ] **Step 2: Adversarial review**

Check that startup with corrupt JSON still opens, blocked capabilities never produce a SET, a failed verified write does not save the requested value, a device disconnect stops the remaining apply sequence, and another process cannot cause a stale temporary profile to replace the current one.

- [ ] **Step 3: Update documentation only with verified behavior**

Document profile startup/apply behavior and keep all unverified Blade/Viper capabilities marked `SourceBacked` or `Blocked`.
