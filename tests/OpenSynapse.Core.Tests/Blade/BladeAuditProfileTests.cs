using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAuditProfileTests
{
    [Theory]
    [InlineData(PowerSourceKind.Battery, PowerSourceKind.FullPowerAdapter, true, true, false)]
    [InlineData(PowerSourceKind.Battery, PowerSourceKind.Battery, true, false, false)]
    [InlineData(PowerSourceKind.Battery, PowerSourceKind.Battery, false, true, false)]
    [InlineData(PowerSourceKind.Battery, PowerSourceKind.Battery, true, true, true)]
    public void FirmwareEffectOnlyWritesTheActiveEnabledLightingProfile(
        PowerSourceKind selected, PowerSourceKind current, bool display, bool enabled, bool expected)
    {
        var document = ProfileDocument.CreateDefault();
        var edit = new BladeLightingProfileEdit(document, selected);
        Assert.Equal(expected, edit.CanWriteEffect(document, current, display, enabled));
        edit.Blade.LightingEnabled = false;
        Assert.False(edit.CanWriteEffect(document, current, display, lightingEnabled: true));
    }

    [Fact]
    public async Task DelayedBrightnessSaveKeepsTheCapturedPowerAndNamedProfile()
    {
        var document = ProfileDocument.CreateDefault();
        var first = document.Profiles[document.ActiveProfileName];
        first.Global.Blade.KeyboardBrightness = 10;
        var battery = new BladeLightingProfileEdit(document, PowerSourceKind.Battery);
        var ac = new BladeLightingProfileEdit(document, PowerSourceKind.FullPowerAdapter);
        document.Profiles["Other"] = new ProfileDefinition();
        document.ActiveProfileName = "Other";
        Assert.False(battery.CanWriteHardware(document, PowerSourceKind.Battery, true));
        await battery.SaveBrightnessAsync(document, 80, _ => Task.FromResult(true), default, "save");
        await ac.SaveBrightnessAsync(document, 160, _ => Task.FromResult(true), default, "save");
        Assert.Equal((byte)80, first.OnBattery.Blade.KeyboardBrightness);
        Assert.Equal((byte)160, first.PluggedIn.Blade.KeyboardBrightness);
        Assert.Equal((byte)10, first.Global.Blade.KeyboardBrightness);
        Assert.Null(document.Profiles["Other"].OnBattery.Blade.KeyboardBrightness);
    }

    [Fact]
    public async Task AReplacedDocumentCannotReceiveAnOldBrightnessReadback()
    {
        var original = ProfileDocument.CreateDefault();
        var edit = new BladeLightingProfileEdit(original, PowerSourceKind.Battery);
        var replacement = original.Clone();
        var saves = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => edit.SaveBrightnessAsync(
            replacement, 200, _ => { saves++; return Task.FromResult(true); }, default, "save"));
        Assert.Equal(0, saves);
        Assert.Null(original.OnBattery.Blade.KeyboardBrightness);
        Assert.Null(replacement.OnBattery.Blade.KeyboardBrightness);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FailedBrightnessSaveRollsBackOnlyTheCapturedField(int failure)
    {
        var document = ProfileDocument.CreateDefault();
        var edit = new BladeLightingProfileEdit(document, PowerSourceKind.Battery);
        edit.Blade.KeyboardBrightness = 70;
        await Assert.ThrowsAnyAsync<Exception>(() => edit.SaveBrightnessAsync(document, 190, _ =>
        {
            edit.Blade.ChargeLimitPercent = 80;
            return Failure(failure);
        }, default, "save failed"));
        Assert.Equal((byte)70, edit.Blade.KeyboardBrightness);
        Assert.Equal(80, edit.Blade.ChargeLimitPercent);
        Assert.Null(document.Global.Blade.KeyboardBrightness);
    }

    [Fact]
    public async Task FailedFirmwareEffectSaveRestoresTheOldCodecPayload()
    {
        var document = ProfileDocument.CreateDefault();
        var edit = new BladeLightingProfileEdit(document, PowerSourceKind.Battery);
        edit.Lighting.Effect = "off";
        var previous = edit.Lighting.Parameters;
        await Assert.ThrowsAsync<InvalidOperationException>(() => edit.SaveEffectAsync(document,
            new LightingProfile { Effect = "static", Parameters = new() { ["color"] = "#123456" } },
            _ => Task.FromResult(false), default, "save failed"));
        Assert.Equal("off", edit.Lighting.Effect);
        Assert.Same(previous, edit.Lighting.Parameters);
        Assert.Equal(string.Empty, document.Global.Lighting.Effect);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task InactivePerformanceEditsRollbackOnFalseThrowAndCancellation(bool maxFan, int failure)
    {
        var target = new BladeProfileSettings { PerformanceMode = 5, MaxFanMode = 0 };
        Func<CancellationToken, Task<bool>> save = _ => { target.ChargeLimitPercent = 85; return Failure(failure); };
        await Assert.ThrowsAnyAsync<Exception>(() => maxFan
            ? BladePerformanceProfileEdit.SaveMaxFanAsync(target, 1, save, default, "save")
            : BladePerformanceProfileEdit.SaveModeAsync(target, 4, save, default, "save"));
        Assert.Equal((byte)5, target.PerformanceMode);
        Assert.Equal((byte)0, target.MaxFanMode);
        Assert.Equal(85, target.ChargeLimitPercent);
    }

    [Fact]
    public async Task SuccessfulPerformanceEditsPersistOnlyTheirOwnField()
    {
        var target = new BladeProfileSettings();
        await BladePerformanceProfileEdit.SaveModeAsync(target, 4, _ => Task.FromResult(true), default, "save");
        await BladePerformanceProfileEdit.SaveMaxFanAsync(target, 1, _ => Task.FromResult(true), default, "save");
        Assert.Equal((byte)4, target.PerformanceMode);
        Assert.Equal((byte)1, target.MaxFanMode);
    }

    private static Task<bool> Failure(int failure) => failure switch
    {
        0 => Task.FromResult(false),
        1 => Task.FromException<bool>(new IOException("disk failure")),
        _ => Task.FromCanceled<bool>(new CancellationToken(canceled: true)),
    };

    [Fact]
    public void WorkspaceConnectsTheTestedTransactionsAndNeverConfirmsInactiveMaxFan()
    {
        var workspace = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/ViewModels/BladeWorkspace.cs"));
        Assert.Contains("BladePerformanceProfileEdit.SaveModeAsync", workspace);
        Assert.Contains("BladePerformanceProfileEdit.SaveMaxFanAsync", workspace);
        Assert.Contains("SetMaxFan(requested, confirm: false)", workspace);
        Assert.Contains("SetMaxFan((BladeMaxFanMode)rawMaxFan, confirm: false)", workspace);
        Assert.Contains("edit.CanWriteEffect(Profile, CurrentPowerSource, IsDisplayAvailable, enabled)", workspace);
        var model = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/ViewModels/BladeViewModel.cs"));
        Assert.Contains("if (confirm) _confirmedBladeMaxFanEnabled = _bladeMaxFanEnabled", model);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task ChargeAndRefreshSaveFailuresRestoreOnlyTheirOwnPreference(bool refresh, int failure)
    {
        var target = new BladeProfileSettings
        { ChargeLimitPercent = 80, RefreshRateHertz = 120, FanMode = (byte)BladeFanMode.Manual, FanTargetRpm = 3200 };
        Func<CancellationToken, Task<bool>> save = _ =>
        { target.KeyboardBrightness = 170; return Failure(failure); };
        await Assert.ThrowsAnyAsync<Exception>(() => refresh
            ? BladePerformanceProfileEdit.SaveRefreshRateAsync(target, 60, save, default, "save failed")
            : BladePerformanceProfileEdit.SaveChargeLimitAsync(target, 75, save, default, "save failed"));
        Assert.Equal(80, target.ChargeLimitPercent); Assert.Equal(120, target.RefreshRateHertz);
        Assert.Equal((byte)170, target.KeyboardBrightness);
        Assert.Equal((byte)BladeFanMode.Manual, target.FanMode); Assert.Equal(3200, target.FanTargetRpm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullablePreferencesAndChangedNamedProfilesRetainTheirOriginalTargets(bool refresh)
    {
        var document = ProfileDocument.CreateDefault();
        var profile = document.Profiles[document.ActiveProfileName];
        var target = refresh ? profile.PluggedIn.Blade : profile.Global.Blade;
        var save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = refresh
            ? BladePerformanceProfileEdit.SaveRefreshRateAsync(target, 60, _ => save.Task, default, "save failed")
            : BladePerformanceProfileEdit.SaveChargeLimitAsync(target, 75, _ => save.Task, default, "save failed");
        document.Profiles["Other"] = new ProfileDefinition();
        document.ActiveProfileName = "Other";
        document.Profiles["Other"].Global.Blade.ChargeLimitPercent = 85;
        document.Profiles["Other"].PluggedIn.Blade.RefreshRateHertz = 144;
        save.SetResult(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.Null(target.ChargeLimitPercent); Assert.Null(target.RefreshRateHertz);
        Assert.Equal(85, document.Profiles["Other"].Global.Blade.ChargeLimitPercent);
        Assert.Equal(144, document.Profiles["Other"].PluggedIn.Blade.RefreshRateHertz);
    }

    [Fact]
    public async Task SuccessfulChargeAndRefreshEditsKeepTheirConfirmedValues()
    {
        var target = new BladeProfileSettings();
        await BladePerformanceProfileEdit.SaveChargeLimitAsync(target, 75, _ => Task.FromResult(true), default, "save");
        await BladePerformanceProfileEdit.SaveRefreshRateAsync(target, 60, _ => Task.FromResult(true), default, "save");
        Assert.Equal(75, target.ChargeLimitPercent); Assert.Equal(60, target.RefreshRateHertz);
    }

    [Fact]
    public void RemainingBladeCallersCaptureBeforeHardwareAndNotifyOnlyAfterAwaitedSave()
    {
        // Source contract for App wiring; production transactions above run without UI/hardware.
        var source = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/ViewModels/BladeWorkspace.cs"));
        static string Method(string text, string start, string end) =>
            text[text.IndexOf(start, StringComparison.Ordinal)..text.IndexOf(end, StringComparison.Ordinal)];
        var charge = Method(source, "public async Task ApplyChargeLimitAsync", "public async Task ApplyCpuBoostAsync");
        Assert.True(charge.IndexOf("var target = Profile.Global.Blade", StringComparison.Ordinal) <
            charge.IndexOf("await _deviceTelemetryReader.SetBladeChargeLimitAsync", StringComparison.Ordinal));
        Assert.Contains("await BladePerformanceProfileEdit.SaveChargeLimitAsync(target, actual", charge);
        Assert.Contains("SetChargeLimit(actual)", charge);
        Assert.Equal(1, charge.Split("SetBladeChargeLimitAsync(", StringSplitOptions.None).Length - 1);
        var cycle = Method(source, "internal async Task CyclePerformanceModeAsync", "internal async Task ToggleGamingModeAsync");
        Assert.True(cycle.IndexOf("var target = CurrentPowerOverrides?.Blade", StringComparison.Ordinal) <
            cycle.IndexOf("await _deviceTelemetryReader.SetBladePerformanceModeAsync", StringComparison.Ordinal));
        Assert.Contains("ClearFixedFanWhenLeavingCustom(actual, target)", cycle);
        Assert.Contains("(target.FanMode, target.FanTargetRpm, target.FanCurve) = previousFan", cycle);
        Assert.True(cycle.IndexOf("await BladePerformanceProfileEdit.SaveModeAsync", StringComparison.Ordinal) <
            cycle.IndexOf("PerformanceModeChangedByUser?.Invoke", StringComparison.Ordinal));
        var rate = Method(source, "internal async Task CycleInternalDisplayRefreshRateAsync", "private static byte ToBladeBrightness");
        Assert.True(rate.IndexOf("var target = CurrentPowerOverrides?.Blade", StringComparison.Ordinal) <
            rate.IndexOf("display.Controller.SetRefreshRate(", StringComparison.Ordinal));
        Assert.True(rate.IndexOf("await BladePerformanceProfileEdit.SaveRefreshRateAsync", StringComparison.Ordinal) <
            rate.IndexOf("_internalDisplayRefreshRateChangedByUser(", StringComparison.Ordinal));
        Assert.Contains("display.ApplyInternalDisplaySnapshot(snapshot)", rate);
        Assert.Equal(1, rate.Split("display.Controller.SetRefreshRate(", StringSplitOptions.None).Length - 1);
        var clear = Method(source, "private bool ClearFixedFanWhenLeavingCustom", "public async Task ApplyChargeLimitAsync");
        Assert.DoesNotContain("EditablePerformanceProfile", clear);
        Assert.DoesNotContain("ClearPendingMode", clear);
    }

    [Fact]
    public void PerformanceModeSelectionUpdatesCardsInsteadOfRebuildingTheList()
    {
        // The mode picker lives inside the scrolling device page. Replacing its ItemsSource
        // on every selection recreates every card and snaps the page back to the top.
        var source = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/ViewModels/BladeWorkspace.cs"));
        static string Method(string text, string start, string end) =>
            text[text.IndexOf(start, StringComparison.Ordinal)..text.IndexOf(end, StringComparison.Ordinal)];
        var setter = Method(source, "public int PerformanceModeIndex", "public bool CanSetPerformanceMode");
        Assert.Contains("RefreshPerformanceModeCardSelection()", setter);
        Assert.DoesNotContain("OnPropertyChanged(nameof(PerformanceModeCards))", setter);
        var cards = Method(source, "public IReadOnlyList<BladePerformanceModeCardViewModel> PerformanceModeCards",
            "private void RefreshPerformanceModeCardSelection");
        Assert.Contains("_performanceModeCards ??=", cards);
        Assert.Contains("card.SetSelected(card.Index == PerformanceModeIndex)", source);
        Assert.DoesNotContain("OnPropertyChanged(nameof(PerformanceModeOptions))", source);
    }
}
