using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAuditFanOwnershipTests
{
    private static BladeProfileSettings Fixed(int rpm = 3200) =>
        new() { FanMode = (byte)BladeFanMode.Manual, FanTargetRpm = rpm };

    private static string Fingerprint(BladeProfileSettings profile,
        PowerSourceKind power = PowerSourceKind.FullPowerAdapter, string name = "Default", string device = "blade-one") =>
        BladeFanProfileFingerprint.Create(profile, device, power, name);

    [Fact]
    public void SameOwnerRefreshPreservesExplicitManualControl()
    {
        var running = Fingerprint(Fixed());
        Assert.True(BladeFanProfileFingerprint.MatchesRunning(running, Fingerprint(Fixed()), true));
        Assert.False(BladeFanProfileFingerprint.MatchesRunning(running, Fingerprint(Fixed()), false));
    }

    [Fact]
    public void OwnershipIncludesPowerNamedProfileDeviceAndFanSettings()
    {
        var running = Fingerprint(Fixed());
        var targets = new[]
        {
            Fingerprint(Fixed(), PowerSourceKind.Battery),
            Fingerprint(Fixed(), name: "Other"),
            Fingerprint(Fixed(), device: "blade-two"),
            Fingerprint(Fixed(3300)),
            Fingerprint(new() { FanMode = (byte)BladeFanMode.Automatic }),
            Fingerprint(new()),
        };
        Assert.All(targets, target => Assert.False(BladeFanProfileFingerprint.MatchesRunning(running, target, true)));
    }

    [Fact]
    public void ExplicitBatteryApplyBeforeItsFirstRefreshIsNotMistakenForTheOldAcOwner()
    {
        var oldAc = Fingerprint(Fixed());
        var appliedBattery = Fingerprint(Fixed(3500), PowerSourceKind.Battery);
        var refreshedBattery = Fingerprint(Fixed(3500), PowerSourceKind.Battery);
        Assert.NotEqual(oldAc, appliedBattery);
        Assert.True(BladeFanProfileFingerprint.MatchesRunning(appliedBattery, refreshedBattery, true));
    }

    [Fact]
    public void CurveOwnerIncludesAllControlPointsAndBaselines()
    {
        var curve = BladeFanCurve.CreateDefault();
        var settings = new BladeProfileSettings { FanCurve = BladeFanCurveProfile.FromCurve(curve) };
        var running = Fingerprint(settings);
        var edited = BladeFanCurveProfile.FromCurve(curve);
        edited.MinimumCpuTemperatureCelsius++;
        Assert.NotEqual(running, Fingerprint(new() { FanCurve = edited }));
        Assert.NotEqual(running, Fingerprint(settings, PowerSourceKind.Battery));
        Assert.NotEqual(running, Fingerprint(settings, name: "Other"));
        edited = BladeFanCurveProfile.FromCurve(new BladeFanCurve(BladeFanCurveTemperatureMode.Both,
            [new(75, 4000, 4000)], [new(80, 4100, 4100)]));
        Assert.NotEqual(running, Fingerprint(new() { FanCurve = edited }));
    }

    [Fact]
    public void MissingConfigurationCannotBeAnActiveOwner() =>
        Assert.False(BladeFanProfileFingerprint.MatchesRunning(string.Empty, Fingerprint(new()), true));

    [Fact]
    public void RefreshReleasesDifferentOwnerBeforeFixedEarlyReturnWithoutStartingStoredTarget()
    {
        // Source wiring only: no App or hardware is started by this test.
        var source = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/ViewModels/FanWorkspace.cs"));
        var method = source[source.IndexOf("internal async Task<BladeFanProfileApplyResult> ApplyProfileAsync", StringComparison.Ordinal)..];
        var early = method.IndexOf("if (!writeStoredTarget && curve is null)", StringComparison.Ordinal);
        Assert.True(method.IndexOf("StopAsync(\"profile-ownership-change\")", StringComparison.Ordinal) < early);
        var start = method.IndexOf("await _bladeFanRuntime.StartFixedAsync", StringComparison.Ordinal);
        var release = method.IndexOf("StopAsync(\"profile-refresh-release\")", StringComparison.Ordinal);
        Assert.True(early < release && release < start);
        Assert.Contains("BladeFanProfileFingerprint.MatchesRunning", method[early..release]);
        Assert.Contains("document.ActiveProfileName", method[..early]);
    }
}
