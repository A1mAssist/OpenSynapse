using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAuditFanPersistenceTests
{
    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    public async Task SaveFailureRestoresOnlyTheCapturedFanFields(bool activePower, bool curve, int failure)
    {
        var document = ProfileDocument.CreateDefault();
        var profile = document.Profiles[document.ActiveProfileName];
        var target = activePower ? profile.PluggedIn.Blade : profile.OnBattery.Blade;
        var untouched = activePower ? profile.OnBattery.Blade : profile.PluggedIn.Blade;
        var originalCurve = BladeFanCurveProfile.FromCurve(BladeFanCurve.CreateDefault());
        target.FanCurve = originalCurve;
        target.PerformanceMode = (byte)BladePerformanceMode.Balanced;
        var edit = new BladeFanProfileEdit(target, restorePerformance: !curve);
        if (curve) target.FanCurve = BladeFanCurveProfile.FromCurve(BladeFanCurve.CreateDefault());
        else
        {
            target.FanCurve = null; target.FanMode = (byte)BladeFanMode.Manual;
            target.FanTargetRpm = 3500; target.PerformanceMode = (byte)BladePerformanceMode.Custom;
        }
        document.Profiles["Other"] = new ProfileDefinition();
        await Assert.ThrowsAnyAsync<Exception>(() => edit.SaveAsync(_ =>
        {
            target.ChargeLimitPercent = 85;
            untouched.KeyboardBrightness = 70;
            document.ActiveProfileName = "Other";
            return failure switch
            {
                0 => Task.FromResult(false),
                1 => Task.FromException<bool>(new IOException("disk failure")),
                _ => Task.FromCanceled<bool>(new CancellationToken(canceled: true)),
            };
        }, default, "save failed"));
        Assert.Same(originalCurve, target.FanCurve);
        Assert.Null(target.FanMode); Assert.Null(target.FanTargetRpm);
        Assert.Equal((byte)BladePerformanceMode.Balanced, target.PerformanceMode);
        Assert.Equal(85, target.ChargeLimitPercent);
        Assert.Equal((byte)70, untouched.KeyboardBrightness);
        Assert.Equal("Other", document.ActiveProfileName);
        Assert.Same(profile, document.Profiles.First(item => ReferenceEquals(item.Value, profile)).Value);
    }

    [Fact]
    public async Task SuccessfulSaveKeepsTheNewFanTarget()
    {
        var target = new BladeProfileSettings { FanMode = (byte)BladeFanMode.Automatic };
        var edit = new BladeFanProfileEdit(target);
        target.FanMode = (byte)BladeFanMode.Manual; target.FanTargetRpm = 3500;
        await edit.SaveAsync(_ => Task.FromResult(true), default, "save failed");
        Assert.Equal((byte)BladeFanMode.Manual, target.FanMode); Assert.Equal(3500, target.FanTargetRpm);
    }

    [Fact]
    public async Task ACurveRollbackDoesNotOverwriteAnUnrelatedPerformanceEdit()
    {
        var target = new BladeProfileSettings { PerformanceMode = (byte)BladePerformanceMode.Balanced };
        var edit = new BladeFanProfileEdit(target);
        target.FanCurve = BladeFanCurveProfile.FromCurve(BladeFanCurve.CreateDefault());
        target.PerformanceMode = (byte)BladePerformanceMode.Silent;
        await Assert.ThrowsAsync<InvalidOperationException>(() => edit.SaveAsync(
            _ => Task.FromResult(false), default, "save failed"));
        Assert.Null(target.FanCurve); Assert.Equal((byte)BladePerformanceMode.Silent, target.PerformanceMode);
    }

    [Fact]
    public void BothFanCallersUseTheTestedTransactionAndRecoveryDoesNotUseTheCancelledToken()
    {
        // App wiring contract only, not a WinUI or hardware integration run.
        var source = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/ViewModels/FanWorkspace.cs"));
        Assert.Equal(2, source.Split("await PersistBladeFanEditAsync(", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("_setProfile(previous)", source);
        var persistence = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/ViewModels/FanWorkspace.Persistence.cs"));
        Assert.Contains("if (active)", persistence);
        Assert.Contains("await edit.SaveAsync(_saveProfileAsync", persistence);
        Assert.Contains("catch (Exception failure)", persistence);
        Assert.Contains("edit.Restore()", persistence);
        Assert.Contains("StopAsync(\"fan-edit-failed\")", persistence);
        Assert.Contains("hadRunningControl", persistence);
        Assert.Contains("== previousOwner", persistence);
        Assert.Contains("ApplyProfileAsync(blade, power, CancellationToken.None)", persistence);
    }
}
