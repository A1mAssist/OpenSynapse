using OpenSynapse.Windows.Displays;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsInternalDisplayControllerTests
{
    private static readonly DisplayAdapterId Adapter = new(1, 2);

    [Theory]
    [InlineData(6u)]
    [InlineData(11u)]
    [InlineData(13u)]
    [InlineData(0x80000000u)]
    public void ReadsOnlyKnownInternalTechnologies(uint technology)
    {
        var api = FakeApi.SingleInternal(technology);

        var snapshot = new WindowsInternalDisplayController(api).Read();

        Assert.Equal(2560, snapshot.Width);
        Assert.Equal(1600, snapshot.Height);
        Assert.Equal(240, snapshot.RefreshRateHertz);
        Assert.Equal(new[] { 60, 240 }, snapshot.SupportedRefreshRates);
    }

    [Theory]
    [InlineData(5u)]
    [InlineData(10u)]
    public void RejectsExternalTechnologies(uint technology)
    {
        var api = FakeApi.SingleInternal(technology);

        Assert.Throws<InvalidOperationException>(() =>
            new WindowsInternalDisplayController(api).Read());
    }

    [Fact]
    public void RejectsMultipleInternalPathsAndCloneSources()
    {
        var first = Path(1, 1, 0x80000000);
        var secondInternal = Path(2, 2, 11);
        var cloneExternal = Path(1, 3, 5);

        Assert.Throws<InvalidOperationException>(() =>
            new WindowsInternalDisplayController(new FakeApi([first, secondInternal])).Read());
        Assert.Throws<InvalidOperationException>(() =>
            new WindowsInternalDisplayController(new FakeApi([first, cloneExternal])).Read());
    }

    [Fact]
    public void KeepsCurrentResolutionAndDeduplicatesRates()
    {
        var api = FakeApi.SingleInternal();
        api.Modes =
        [
            new(2560, 1600, 240, 32),
            new(1920, 1080, 144, 32),
            new(2560, 1600, 60, 32),
            new(2560, 1600, 60, 24),
        ];

        var snapshot = new WindowsInternalDisplayController(api).Read();

        Assert.Equal(new[] { 60, 240 }, snapshot.SupportedRefreshRates);
    }

    [Fact]
    public void UnsupportedRateDoesNotCallNativeWrite()
    {
        var api = FakeApi.SingleInternal();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WindowsInternalDisplayController(api).SetRefreshRate(144));
        Assert.Empty(api.Changes);
    }

    [Fact]
    public void AppliesAfterTestAndReturnsReadback()
    {
        var api = FakeApi.SingleInternal();
        api.AfterApply = mode => api.CurrentMode = mode;

        var snapshot = new WindowsInternalDisplayController(api).SetRefreshRate(60);

        Assert.Equal(60, snapshot.RefreshRateHertz);
        Assert.Equal([(60, true), (60, false)], api.Changes);
    }

    [Fact]
    public void ValidationFailureDoesNotApplyOrRestore()
    {
        var api = FakeApi.SingleInternal();
        api.TestResult = -2;

        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            new WindowsInternalDisplayController(api).SetRefreshRate(60));
        Assert.Equal([(60, true)], api.Changes);
    }

    [Fact]
    public void ReadbackMismatchRestoresOriginalMode()
    {
        var api = FakeApi.SingleInternal();

        Assert.Throws<InvalidOperationException>(() =>
            new WindowsInternalDisplayController(api).SetRefreshRate(60));
        Assert.Equal([(60, true), (60, false), (240, false)], api.Changes);
    }

    [HardwareFact]
    public void ChangesInternalPanelRateAndRestoresIt()
    {
        var controller = new WindowsInternalDisplayController();
        var original = controller.Read();
        var alternative = original.SupportedRefreshRates
            .Where(rate => rate != original.RefreshRateHertz)
            .OrderBy(rate => Math.Abs(rate - original.RefreshRateHertz))
            .FirstOrDefault();
        if (alternative == 0)
        {
            return;
        }

        try
        {
            Assert.Equal(alternative, controller.SetRefreshRate(alternative).RefreshRateHertz);
        }
        finally
        {
            Assert.Equal(
                original.RefreshRateHertz,
                controller.SetRefreshRate(original.RefreshRateHertz).RefreshRateHertz);
        }
    }

    private static DisplayPath Path(uint sourceId, uint targetId, uint technology) => new(
        new DisplayPathIdentity(Adapter, sourceId, Adapter, targetId),
        technology,
        IsActive: true,
        TargetAvailable: true);

    private sealed class FakeApi : IWindowsDisplayApi
    {
        public FakeApi(IReadOnlyList<DisplayPath> paths)
        {
            Paths = paths;
        }

        public static FakeApi SingleInternal(uint technology = 0x80000000) =>
            new([Path(1, 1, technology)]);

        public IReadOnlyList<DisplayPath> Paths { get; set; }
        public string SourceName { get; set; } = @"\.\DISPLAY1";
        public DisplayMode CurrentMode { get; set; } = new(2560, 1600, 240, 32);
        public IReadOnlyList<DisplayMode> Modes { get; set; } =
        [
            new(2560, 1600, 240, 32),
            new(2560, 1600, 60, 32),
        ];
        public int TestResult { get; set; }
        public int ApplyResult { get; set; }
        public Action<DisplayMode>? AfterApply { get; set; }
        public List<(int Hertz, bool TestOnly)> Changes { get; } = [];

        public IReadOnlyList<DisplayPath> QueryActivePaths() => Paths;

        public string GetSourceName(DisplayAdapterId adapterId, uint sourceId) => SourceName;

        public DisplayMode GetCurrentMode(string sourceName) => CurrentMode;

        public IReadOnlyList<DisplayMode> EnumerateModes(string sourceName) => Modes;

        public int ChangeMode(string sourceName, DisplayMode mode, bool testOnly)
        {
            Changes.Add((mode.RefreshRateHertz, testOnly));
            if (testOnly)
            {
                return TestResult;
            }

            if (ApplyResult == 0)
            {
                AfterApply?.Invoke(mode);
            }

            return ApplyResult;
        }
    }
}
