using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class CoreRuntimeAuditRegressionTests
{
    [Theory]
    [InlineData(false, 0x120u)]
    [InlineData(true, 0x21u)]
    public void RawInputRegistrationTargetsHostExceptOnRemoval(bool removing, uint flags)
    {
        var devices = RawInputRouter.CreateRegistration((nint)1234, removing);
        Assert.Equal(new ushort[] { 1, 12 }, devices.Select(item => item.UsagePage));
        Assert.All(devices, item =>
        {
            Assert.Equal((ushort)0, item.Usage);
            Assert.Equal(flags, item.Flags);
            Assert.Equal(removing ? 0 : (nint)1234, item.Target);
        });
    }

    [Fact]
    public void GroupMovesAcrossOneNeighborAndPreservesInternalOrder()
    {
        var before = new MacroStep();
        var first = new MacroStep { GroupId = "combo" };
        var second = new MacroStep { GroupId = "combo" };
        var after = new MacroStep();
        var steps = new List<MacroStep> { before, first, second, after };
        Assert.True(MacroStepTree.MoveGroup(steps, "combo", -1));
        Assert.Equal(new[] { first, second, before, after }, steps);
        Assert.False(MacroStepTree.MoveGroup(steps, "combo", -1));
        Assert.True(MacroStepTree.MoveGroup(steps, "combo", 1));
        Assert.Equal(new[] { before, first, second, after }, steps);
        Assert.True(MacroStepTree.MoveGroup(steps, "combo", 1));
        Assert.Equal(new[] { before, after, first, second }, steps);
        Assert.False(MacroStepTree.MoveGroup(steps, "combo", 1));
        Assert.False(MacroStepTree.MoveGroup(steps, "combo", 0));
        Assert.False(MacroStepTree.MoveGroup(steps, "missing", -1));
    }

    [Fact]
    public void DraftRequiresOriginalOwnerAndOriginalLiveStepAndCopiesPoints()
    {
        var step = new MacroStep { Kind = MacroStepKind.MouseMove };
        var owner = new MacroDefinition { Steps = [new() { Kind = MacroStepKind.Loop, Children = [step] }] };
        var other = new MacroDefinition { Id = owner.Id };
        var point = new MacroMousePoint { X = 17, Y = 23, OffsetMilliseconds = 50 };
        Assert.False(MacroStepTree.TryApplyMousePath(owner, other, step, [point]));
        Assert.False(MacroStepTree.TryApplyMousePath(owner, null, step, [point]));
        Assert.False(MacroStepTree.TryApplyMousePath(owner, owner, new() { Kind = MacroStepKind.MouseMove }, [point]));
        Assert.Empty(step.MousePath);
        Assert.True(MacroStepTree.TryApplyMousePath(owner, owner, step, [point]));
        Assert.NotSame(point, Assert.Single(step.MousePath));
        point.X = 999;
        Assert.Equal(17, step.MousePath[0].X);
        owner.Steps.Clear();
        Assert.False(MacroStepTree.TryApplyMousePath(owner, owner, step, []));
        Assert.Single(step.MousePath);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(1000)]
    public void MouseTurboRoundTripsEverySupportedButton(int rate)
    {
        var index = ViperMappingActions.Definitions.ToList().FindIndex(item => item.Function == ViperButtonMappingFunction.MouseTurbo);
        var source = new ViperButtonAssignment(1, 1, default, ViperButtonMappingFunction.Off, []);
        for (byte button = 1; button <= 10; button++)
        {
            Assert.True(ViperMappingActions.TryApply(source, index, 0, 4, 800, 800, rate, button, out var mapped));
            Assert.Equal(index, ViperMappingActions.IndexOf(mapped));
            Assert.Equal(button, mapped.FunctionData[0]);
            Assert.Equal(1000 / Math.Clamp(rate, 1, 50), (mapped.FunctionData[1] << 8) | mapped.FunctionData[2]);
        }
        foreach (byte[] invalid in new byte[][] { [], [0, 0, 1], [11, 0, 1], [1, 0, 0], [1, 0], [1, 0, 1, 2] })
            Assert.Equal(-1, ViperMappingActions.IndexOf(source with { Function = ViperButtonMappingFunction.MouseTurbo, FunctionData = invalid }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WheelRepeatsAcrossManyPositiveAndNegativeTurns(byte speed)
    {
        foreach (var direction in new[] { QuickLightingDirection.Clockwise, QuickLightingDirection.CounterClockwise })
        foreach (var palette in new LightingPalette?[] { null, LightingPalette.Create(new(255, 0, 0), new(0, 255, 0), new(0, 0, 255)) })
        {
            var first = QuickLightingEngine.RenderWheel(TimeSpan.FromSeconds(1), direction, 1, 3, palette, speed);
            // 24 seconds is an integral number of turns at all supported speeds.
            Assert.Equal(first, QuickLightingEngine.RenderWheel(TimeSpan.FromSeconds(25), direction, 1, 3, palette, speed));
            Assert.Equal(first, QuickLightingEngine.RenderWheel(TimeSpan.FromSeconds(2401), direction, 1, 3, palette, speed));
        }
    }

    [Fact]
    public void BehaviorFailedCommitPreservesOldFileAndCleansTemporaryFile()
    {
        WithTemporarySettings(path =>
        {
            var settings = new AppBehaviorSettings { WindowWidth = 640 };
            settings.Save(path);
            var original = File.ReadAllText(path);
            settings.WindowWidth = 900;
            Assert.Throws<IOException>(() => settings.Save(path, _ => throw new IOException("commit interrupted")));
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp-*"));
            settings.Save(path);
            Assert.Equal(900, AppBehaviorSettings.Load(path).WindowWidth);
        });
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public void BehaviorCorruptionIsPreservedBeforeRecoveryCanSave(string corrupt)
    {
        WithTemporarySettings(path =>
        {
            File.WriteAllText(path, corrupt);
            var recovered = AppBehaviorSettings.Load(path);
            Assert.NotNull(recovered.LoadError);
            Assert.NotNull(recovered.RecoveryFilePath);
            Assert.Equal(corrupt, File.ReadAllText(path));
            Assert.Equal(corrupt, File.ReadAllText(recovered.RecoveryFilePath));
            recovered.Save(path);
            Assert.Equal(corrupt, File.ReadAllText(recovered.RecoveryFilePath));
            Assert.Null(AppBehaviorSettings.Load(path).LoadError);
            Assert.DoesNotContain("LoadError", File.ReadAllText(path));
        });
    }

    [Fact]
    public void BehaviorUnreadableFileCannotBeOverwrittenUntilReloaded()
    {
        WithTemporarySettings(path =>
        {
            new AppBehaviorSettings { WindowWidth = 123 }.Save(path);
            AppBehaviorSettings fallback;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                fallback = AppBehaviorSettings.Load(path);
            Assert.NotNull(fallback.LoadError);
            Assert.Null(fallback.RecoveryFilePath);
            Assert.Throws<IOException>(() => fallback.Save(path));
            var reloaded = AppBehaviorSettings.Load(path);
            Assert.Equal(123, reloaded.WindowWidth);
            Assert.Null(reloaded.LoadError);
            reloaded.Save(path);
        });
    }

    [Fact]
    public void BehaviorConcurrentSavesRemainCompleteAndNormalizationIsPreserved()
    {
        WithTemporarySettings(path =>
        {
            Parallel.For(0, 12, index => new AppBehaviorSettings { WindowWidth = index + 1 }.Save(path));
            Assert.InRange(AppBehaviorSettings.Load(path).WindowWidth, 1, 12);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp-*"));
            File.WriteAllText(path, "{\"ChromaBlockedSources\":[\" Game \",\"game\",\" \"],\"RefreshRateCycleHertz\":[0,-1,144],\"PerformanceCycleModes\":[]}");
            var normalized = AppBehaviorSettings.Load(path);
            Assert.Null(normalized.LoadError);
            Assert.Single(normalized.ChromaBlockedSources);
            Assert.Contains("GAME", normalized.ChromaBlockedSources);
            Assert.Equal(new[] { 144 }, normalized.RefreshRateCycleHertz!);
            Assert.NotEmpty(normalized.PerformanceCycleModes);
        });
    }

    private static void WithTemporarySettings(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenSynapse-core-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "behavior.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
