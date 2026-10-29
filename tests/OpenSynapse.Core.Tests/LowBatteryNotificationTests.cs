using System.Text.Json;
using Xunit;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Core.Tests;

public sealed class LowBatteryNotificationTests
{
    [Fact]
    public void NotifiesOncePerLevelAndKeepsCriticalLevelAcrossNoiseAndReconnect()
    {
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tracker = new LowBatteryNotificationTracker(levels);
        Assert.Equal(20, Observe(tracker, 20));
        Assert.Null(Observe(tracker, 18));
        Assert.Null(Observe(tracker, null));
        Assert.Equal(10, Observe(tracker, 10));
        Assert.Null(Observe(tracker, 15));
        Assert.Null(Observe(tracker, 9));
        Assert.Null(Observe(tracker, 24));
        Assert.Null(Observe(tracker, 20));
        Assert.Null(Observe(tracker, 25));
        Assert.Equal(20, Observe(tracker, 19));
    }

    [Fact]
    public void FirstCriticalReadingOnlyNotifiesCriticalThreshold()
    {
        var tracker = new LowBatteryNotificationTracker([]);
        Assert.Equal(10, Observe(tracker, 5));
        Assert.Null(Observe(tracker, 12));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidOrMissingReadingsCannotNotifyOrReset(int? percent)
    {
        var levels = new Dictionary<string, int> { ["device"] = 10 };
        var tracker = new LowBatteryNotificationTracker(levels);
        Assert.Null(Observe(tracker, percent));
        Assert.Equal(10, levels["device"]);
    }

    [Fact]
    public void ChargingAndDisabledNotificationsDoNotConsumeAlerts()
    {
        var levels = new Dictionary<string, int>();
        var tracker = new LowBatteryNotificationTracker(levels);
        Assert.Null(tracker.Observe(new("device", "Mouse", 15, true), true, out _));
        Assert.Null(tracker.Observe(new("device", "Mouse", 15, false), false, out _));
        Assert.Empty(levels);
        Assert.Equal(20, Observe(tracker, 15));
        Assert.Null(tracker.Observe(new("device", "Mouse", 30, true), false, out var changed));
        Assert.True(changed);
        Assert.Equal(20, Observe(tracker, 15));
    }

    [Fact]
    public void DevicesHaveIndependentEpisodesAndPersistAcrossRestart()
    {
        var settings = new AppBehaviorSettings();
        var tracker = new LowBatteryNotificationTracker(settings.LowBatteryNotifiedLevels);
        Assert.Equal(20, Observe(tracker, 17));
        Assert.Equal(10, tracker.Observe(new("keyboard", "Keyboard", 8, null), true, out _));
        var restored = JsonSerializer.Deserialize<AppBehaviorSettings>(JsonSerializer.Serialize(settings))!;
        var restarted = new LowBatteryNotificationTracker(restored.LowBatteryNotifiedLevels);
        Assert.Null(Observe(restarted, 17));
        Assert.Null(restarted.Observe(new("keyboard", "Keyboard", 8, null), true, out _));
        Assert.Equal(10, Observe(restarted, 9));
    }

    [Fact]
    public void OldBehaviorSettingsKeepBothFeatureDefaultsAndSourceBlocksRoundTrip()
    {
        var old = JsonSerializer.Deserialize<AppBehaviorSettings>("{\"ModeChangeNotificationsEnabled\":false}")!;
        Assert.True(old.LowBatteryNotificationsEnabled);
        Assert.Empty(old.LowBatteryNotifiedLevels);
        Assert.Empty(old.ChromaBlockedSources);
        old.ChromaBlockedSources.Add("Example game");
        var restored = JsonSerializer.Deserialize<AppBehaviorSettings>(JsonSerializer.Serialize(old))!;
        Assert.Contains("Example game", restored.ChromaBlockedSources);
    }

    [Fact]
    public void WindowSizeDefaultsToZeroAndRoundTrips()
    {
        var settings = new AppBehaviorSettings();
        Assert.Equal(0, settings.WindowWidth);
        Assert.Equal(0, settings.WindowHeight);
        settings.WindowWidth = 1440;
        settings.WindowHeight = 900;
        var restored = JsonSerializer.Deserialize<AppBehaviorSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(1440, restored.WindowWidth);
        Assert.Equal(900, restored.WindowHeight);
    }

    [Fact]
    public void TrayBatterySettingDefaultsOnAndRoundTrips()
    {
        var old = JsonSerializer.Deserialize<AppBehaviorSettings>("{\"ModeChangeNotificationsEnabled\":false}")!;
        Assert.True(old.TrayBatteryLevelsEnabled);
        old.TrayBatteryLevelsEnabled = false;
        var restored = JsonSerializer.Deserialize<AppBehaviorSettings>(JsonSerializer.Serialize(old))!;
        Assert.False(restored.TrayBatteryLevelsEnabled);
    }

    private static int? Observe(LowBatteryNotificationTracker tracker, int? percent) =>
        tracker.Observe(new("device", "Mouse", percent, null), true, out _);
}
