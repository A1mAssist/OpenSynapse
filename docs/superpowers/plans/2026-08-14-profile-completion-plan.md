# Profile Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist, resolve, and automatically apply every existing verified Blade/Viper control and App-owned software-lighting selection.

**Architecture:** Extend the existing optional Profile fields and deep-copy path. Keep hardware writes in `VerifiedProfileApplier`; keep software-lighting ownership in `MainViewModel` and map validated `LightingProfile` values through one Windows-layer codec.

**Tech Stack:** .NET 10, C#, System.Text.Json, xUnit, WinUI 3.

## Global Constraints

- Keep Profile JSON version `1`; new properties are optional.
- A current-path GET remains mandatory before every hardware SET.
- Do not persist fan curves, Logo Breathing, or unverified effect parameters in this plan.
- Preserve complete in-memory rollback when Profile persistence fails.

---

### Task 1: Deep-copy the expanded Profile model

**Files:**
- Modify: `src/OpenSynapse.Core/Profiles/ProfileModels.cs`
- Test: `tests/OpenSynapse.Core.Tests/ProfileCatalogTests.cs`
- Test: `tests/OpenSynapse.Core.Tests/ProfileStoreTests.cs`

**Interfaces:**
- Produces: `BladeProfileSettings.CpuBoostMode`, `GpuBoostMode`, `LogoMode`.
- Produces: `ViperDpiStagesProfile`, `ViperDpiStageProfile`, and `ViperProfileSettings.DpiStages`.
- Produces: `DeviceProfileSettings.Lighting`.

- [ ] **Step 1: Add a failing deep-clone test**

```csharp
[Fact]
public void DocumentCloneDeepCopiesExpandedDeviceSettings()
{
    var document = ProfileDocument.CreateDefault();
    document.Global.Blade.CpuBoostMode = (byte)BladeCpuBoostMode.Boost;
    document.Global.Blade.GpuBoostMode = (byte)BladeGpuBoostMode.High;
    document.Global.Blade.LogoMode = (byte)BladeLogoMode.Static;
    document.Global.Viper.DpiStages = new ViperDpiStagesProfile
    {
        ActiveStage = 1,
        Stages = [new() { Number = 1, X = 800, Y = 800 }],
    };
    document.Devices["1532:02C6"] = new DeviceProfileSettings
    {
        Lighting = new LightingProfile
        {
            Effect = "static",
            Parameters = new(StringComparer.OrdinalIgnoreCase) { ["color"] = "99DD72" },
        },
    };

    var clone = document.Clone();
    clone.Global.Viper.DpiStages!.Stages[0].X = 1600;
    clone.Devices["1532:02C6"].Lighting.Parameters["color"] = "FFFFFF";

    Assert.Equal(800, document.Global.Viper.DpiStages.Stages[0].X);
    Assert.Equal("99DD72", document.Devices["1532:02C6"].Lighting.Parameters["color"]);
}
```

- [ ] **Step 2: Run the test and confirm it fails to compile**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~DocumentCloneDeepCopiesExpandedDeviceSettings'
```

Expected: compile failure because the new Profile properties do not exist.

- [ ] **Step 3: Add the minimal model and deep-copy implementation**

```csharp
public sealed class ViperDpiStagesProfile
{
    public byte ActiveStage { get; set; }
    public List<ViperDpiStageProfile> Stages { get; set; } = [];

    internal ViperDpiStagesProfile Clone() => new()
    {
        ActiveStage = ActiveStage,
        Stages = Stages.Select(stage => stage.Clone()).ToList(),
    };
}

public sealed class ViperDpiStageProfile
{
    public byte Number { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    internal ViperDpiStageProfile Clone() => new() { Number = Number, X = X, Y = Y };
}
```

Add the three nullable Blade bytes, nullable `DpiStages`, and `Lighting` on device settings. Update `ApplySafeDefaults`, `CloneBlade`, `CloneViper`, and the device-settings clone loop.

- [ ] **Step 4: Add an import/export round-trip test for the new fields**

Serialize a Profile with Boost, Logo, one DPI stage, and device lighting; import it and assert every value and parameter.

- [ ] **Step 5: Run focused tests**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~ProfileCatalogTests|FullyQualifiedName~ProfileStoreTests'
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add 'src\OpenSynapse.Core\Profiles\ProfileModels.cs' 'tests\OpenSynapse.Core.Tests\ProfileCatalogTests.cs' 'tests\OpenSynapse.Core.Tests\ProfileStoreTests.cs'
git commit -m 'feat: persist expanded device profile settings'
```

### Task 2: Resolve expanded settings with existing precedence

**Files:**
- Modify: `src/OpenSynapse.Core/Profiles/ProfileResolver.cs`
- Test: `tests/OpenSynapse.Core.Tests/ProfileResolverTests.cs`

**Interfaces:**
- Consumes: expanded settings from Task 1.
- Produces: resolved Boost, Logo, DPI stages, and global/device/power lighting.

- [ ] **Step 1: Add failing precedence tests**

Create global, `1532:02C6` device, and plugged-in overrides. Assert power wins for Boost/Logo, device wins when power is null, and a cloned DPI-stage table is returned. Add lighting parameters at all three levels and assert later levels overwrite earlier keys.

- [ ] **Step 2: Run the focused test and confirm failure**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~ProfileResolverTests'
```

- [ ] **Step 3: Extend the existing resolver**

Use `First(power, device, global)` for nullable scalar bytes, clone the winning DPI-stage value, and change `ResolveLighting` to accept global, device, and power inputs:

```csharp
private static LightingProfile ResolveLighting(
    LightingProfile? global,
    LightingProfile? device,
    LightingProfile? power)
{
    var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    CopyParameters(parameters, global?.Parameters);
    CopyParameters(parameters, device?.Parameters);
    CopyParameters(parameters, power?.Parameters);
    return new LightingProfile
    {
        Effect = ResolveEffect(global, device, power),
        Parameters = parameters,
    };
}
```

- [ ] **Step 4: Run resolver tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add 'src\OpenSynapse.Core\Profiles\ProfileResolver.cs' 'tests\OpenSynapse.Core.Tests\ProfileResolverTests.cs'
git commit -m 'feat: resolve expanded profile settings'
```

### Task 3: Apply verified Boost, Logo, and DPI stages

**Files:**
- Modify: `src/OpenSynapse.Core/Profiles/VerifiedProfileApplier.cs`
- Test: `tests/OpenSynapse.Core.Tests/VerifiedProfileApplierTests.cs`

**Interfaces:**
- Consumes: resolved values from Task 2 and existing `IRazerDeviceTelemetryReader` setters.
- Produces: per-device isolated errors and complete-table equality for DPI stages.

- [ ] **Step 1: Add failing tests for all new apply paths**

Tests must cover CPU/GPU Boost in Custom mode, Logo Off/Static, DPI-stage mismatch, no-op on equal complete table, invalid enum bytes, and a Blade failure that does not suppress an independent Viper apply.

- [ ] **Step 2: Run the applier tests and confirm failure**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~VerifiedProfileApplierTests'
```

- [ ] **Step 3: Add complete-table conversion and equality**

```csharp
private static ViperDpiStagesTelemetry ToTelemetry(ViperDpiStagesProfile profile) =>
    new(profile.ActiveStage, profile.Stages.Select(stage =>
        new ViperDpiStageTelemetry(stage.Number, stage.X, stage.Y)).ToArray());

private static bool DpiStagesEqual(ViperDpiStagesTelemetry left, ViperDpiStagesTelemetry right) =>
    left.ActiveStage == right.ActiveStage && left.Stages.SequenceEqual(right.Stages);
```

- [ ] **Step 4: Apply settings in dependency order**

Keep performance first. Apply Boost and Max Fan only when the requested/current mode is Custom; use the existing strongly typed setters. Apply brightness, charge, and Logo next. Start Viper application with its own error list so Blade-only failure does not suppress it. Convert invalid Profile data into visible errors rather than exceptions escaping the watcher.

- [ ] **Step 5: Run applier and full Profile tests**

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add 'src\OpenSynapse.Core\Profiles\VerifiedProfileApplier.cs' 'tests\OpenSynapse.Core.Tests\VerifiedProfileApplierTests.cs'
git commit -m 'feat: apply all verified profile controls'
```

### Task 4: Persist UI writes and apply software-lighting profiles

**Files:**
- Create: `src/OpenSynapse.Windows/Lighting/BladeLightingProfileCodec.cs`
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Test: `tests/OpenSynapse.Core.Tests/BladeLightingProfileCodecTests.cs`

**Interfaces:**
- Produces: `BladeLightingProfileCodec.Parse(LightingProfile)` and `Create(BladeLightingEffect)`.
- Produces: one lighting shadow fingerprint owned by `MainViewModel`.

- [ ] **Step 1: Add failing codec tests**

Cover the six current modes, `RRGGBB` color, Wave direction, canonical serialization, unknown mode, malformed color, and irrelevant parameters.

- [ ] **Step 2: Implement the strict codec**

```csharp
internal static class BladeLightingProfileCodec
{
    internal static BladeLightingEffect Parse(LightingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var parameters = profile.Parameters ?? new Dictionary<string, string>();
        var mode = profile.Effect.Trim().ToLowerInvariant();
        var allowed = mode switch
        {
            "static" or "breathing" => new[] { "color" },
            "wave" => new[] { "direction" },
            "off" or "spectrum" or "fire" => Array.Empty<string>(),
            _ => throw new InvalidOperationException($"不支持的键盘灯效：{profile.Effect}。"),
        };
        if (parameters.Keys.Any(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"灯效 {profile.Effect} 包含不支持的参数。");
        }

        var color = parameters.TryGetValue("color", out var hex) ? ParseColor(hex) : default;
        var direction = parameters.TryGetValue("direction", out var value) &&
                        value.Equals("left", StringComparison.OrdinalIgnoreCase)
            ? BladeWaveDirection.Left
            : BladeWaveDirection.Right;
        return new BladeLightingEffect(mode switch
        {
            "off" => BladeLightingMode.Off,
            "static" => BladeLightingMode.Static,
            "breathing" => BladeLightingMode.Breathing,
            "spectrum" => BladeLightingMode.Spectrum,
            "wave" => BladeLightingMode.Wave,
            _ => BladeLightingMode.Fire,
        }, color, direction);
    }

    internal static LightingProfile Create(BladeLightingEffect effect)
    {
        var profile = new LightingProfile { Effect = effect.Mode.ToString().ToLowerInvariant() };
        if (effect.Mode is BladeLightingMode.Static or BladeLightingMode.Breathing)
        {
            profile.Parameters["color"] = $"{effect.Color.Red:X2}{effect.Color.Green:X2}{effect.Color.Blue:X2}";
        }
        else if (effect.Mode == BladeLightingMode.Wave)
        {
            profile.Parameters["direction"] = effect.Direction == BladeWaveDirection.Left ? "left" : "right";
        }
        return profile;
    }

    internal static string Fingerprint(LightingProfile profile, string devicePath)
    {
        var effect = Parse(profile);
        return $"{devicePath}\n{effect.Mode}\n{effect.Color.Red:X2}{effect.Color.Green:X2}{effect.Color.Blue:X2}\n{effect.Direction}";
    }

    private static RazerRgb ParseColor(string value)
    {
        try
        {
            var bytes = Convert.FromHexString(value);
            return bytes.Length == 3
                ? new RazerRgb(bytes[0], bytes[1], bytes[2])
                : throw new FormatException();
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("灯效颜色必须是六位 RRGGBB。", exception);
        }
    }
}
```

Use `Convert.FromHexString` and enum switches; do not add a second JSON serializer.

- [ ] **Step 3: Save read-back/current values from existing setters**

After successful CPU Boost, GPU Boost, Logo, and DPI-stage writes, assign the canonical Profile fields and call the existing `SaveProfileAsync`. After software-lighting first-frame success, store `BladeLightingProfileCodec.Create(effect)` and save.

- [ ] **Step 4: Apply resolved lighting only when its shadow changes**

After `VerifiedProfileApplier.ApplyAsync`, resolve the Blade Profile, parse it, compare its fingerprint, and invoke `_bladeLightingController.ApplyAsync`. Clear the shadow on disconnect, resume, device-path change, and observed runtime fault.

- [ ] **Step 5: Run codec tests and Release build**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~BladeLightingProfileCodecTests'
dotnet build 'OpenSynapse.slnx' -c Release --no-restore
```

Expected: tests PASS; build has zero warnings/errors.

- [ ] **Step 6: Commit**

```powershell
git add 'src\OpenSynapse.Windows\Lighting\BladeLightingProfileCodec.cs' 'src\OpenSynapse.App\ViewModels\MainViewModel.cs' 'tests\OpenSynapse.Core.Tests\BladeLightingProfileCodecTests.cs'
git commit -m 'feat: persist and apply software lighting profiles'
```
