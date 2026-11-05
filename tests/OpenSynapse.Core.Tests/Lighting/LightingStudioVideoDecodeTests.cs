using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using Windows.Storage;
using Xunit;
using Xunit.Abstractions;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioVideoDecodeTests
{
    private const string ReproCommand =
        "dotnet test tests\\OpenSynapse.Core.Tests\\OpenSynapse.Core.Tests.csproj " +
        "--filter FullyQualifiedName~LightingStudioVideoDecode";

    private readonly ITestOutputHelper _output;

    public LightingStudioVideoDecodeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RealVideoDecodesMultipleFramesAndSamplesBothHalves()
    {
        // 64x64, not 32x32: GetThumbnailAsync never completes for a 32x32
        // H.264 clip on this machine; 64x64 decodes in about 600 ms per frame.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "studio-two-tone.mp4");
        Assert.True(File.Exists(path), $"Video fixture was not copied to the test output: {path}");

        var source = await LoadAsync(path);
        Assert.True(
            source is not null,
            $"Media Foundation could not decode '{path}'. Reproduce with: {ReproCommand}");

        Assert.True(source.TrySampleAt(0.5, 0.5, 0.1, out var early), "Early sample failed.");
        Assert.True(source.TrySampleAt(0.5, 0.5, 0.85, out var late), "Late sample failed.");

        // A failed video decode falls back to one shell thumbnail frame, so two
        // different colours prove the source decoded at least two real frames.
        Assert.True(IsRed(early), $"Expected the first 0.5 s to be red, got {early}.");
        Assert.True(IsBlue(late), $"Expected the last 0.5 s to be blue, got {late}.");
    }

    [Fact]
    public async Task RealVideoLoopsAfterItsDurationInsteadOfFreezingOnTheLastFrame()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "studio-two-tone.mp4");
        Assert.True(File.Exists(path), $"Video fixture was not copied to the test output: {path}");

        var source = await LoadAsync(path);
        Assert.True(
            source is not null,
            $"Media Foundation could not decode '{path}'. Reproduce with: {ReproCommand}");

        // The 1 s fixture caches four 250 ms frames. At 1.3 s playback wraps to
        // 0.3 s (red); the old clamp returned the final blue frame forever.
        Assert.True(source.TrySampleAt(0.5, 0.5, 1.3, out var wrappedEarly), "Wrapped early sample failed.");
        Assert.True(IsRed(wrappedEarly), $"Expected 1.3 s to wrap to the red half, got {wrappedEarly}.");

        Assert.True(source.TrySampleAt(0.5, 0.5, 1.8, out var wrappedLate), "Wrapped late sample failed.");
        Assert.True(IsBlue(wrappedLate), $"Expected 1.8 s to wrap to the blue half, got {wrappedLate}.");
    }

    [Fact]
    public void PlanFramesKeepsThePreferredCadenceForShortVideos()
    {
        var (oneSecondCount, oneSecondInterval) = StudioVideoPlayback.PlanFrames(TimeSpan.FromSeconds(1));
        Assert.Equal(4, oneSecondCount);
        Assert.Equal(TimeSpan.FromMilliseconds(250), oneSecondInterval);

        var (sixtyCount, sixtyInterval) = StudioVideoPlayback.PlanFrames(TimeSpan.FromSeconds(60));
        Assert.Equal(240, sixtyCount);
        Assert.Equal(TimeSpan.FromMilliseconds(250), sixtyInterval);
    }

    [Fact]
    public void PlanFramesSpreadsTheSameBudgetOverTheFullLongVideo()
    {
        var (fiveMinuteCount, fiveMinuteInterval) = StudioVideoPlayback.PlanFrames(TimeSpan.FromMinutes(5));
        Assert.Equal(240, fiveMinuteCount);
        Assert.Equal(TimeSpan.FromSeconds(1.25), fiveMinuteInterval);
        Assert.True(fiveMinuteInterval * (fiveMinuteCount - 1) < TimeSpan.FromMinutes(5));

        var (twoHourCount, twoHourInterval) = StudioVideoPlayback.PlanFrames(TimeSpan.FromHours(2));
        Assert.Equal(240, twoHourCount);
        Assert.Equal(TimeSpan.FromSeconds(30), twoHourInterval);
        Assert.True(twoHourInterval * (twoHourCount - 1) < TimeSpan.FromHours(2));
    }

    [Fact]
    public void PlanFramesSpreadsThirtyMinutesIntoSevenPointFiveSecondSamples()
    {
        var (count, interval) = StudioVideoPlayback.PlanFrames(TimeSpan.FromMinutes(30));

        Assert.Equal(240, count);
        Assert.Equal(TimeSpan.FromSeconds(7.5), interval);
        Assert.Equal(TimeSpan.FromMinutes(30), interval * count);
    }

    [Fact]
    public void LongCacheWrapsAfterItsFullDurationInsteadOfSixtySeconds()
    {
        var frames = new byte[300][]; // 300 * 250 ms = 75 s
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index] = SolidFrame((byte)index, 0, 0);
        }

        var playback = StudioVideoPlayback.FromFrames(TimeSpan.FromMilliseconds(250), frames);

        Assert.Equal(TimeSpan.FromSeconds(75), playback.CachedDuration);
        Assert.True(playback.TrySampleAt(0.5, 0.5, 74.9, out var last));
        Assert.Equal(new StudioRgb(43, 0, 0), last); // (byte)299 wraps to 43.

        Assert.True(playback.TrySampleAt(0.5, 0.5, 75.1, out var wrapped));
        Assert.Equal(new StudioRgb(0, 0, 0), wrapped);

        Assert.True(playback.TrySampleAt(0.5, 0.5, 150.6, out var secondLoop));
        Assert.Equal(new StudioRgb(2, 0, 0), secondLoop);
    }

    [Fact]
    public async Task VideoBeyondSixtySecondsDecodesFramesFromTheTail()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "studio-long-65s.mp4");
        Assert.True(File.Exists(path), $"Video fixture was not copied to the test output: {path}");

        // Sixteen frames keep this test to roughly ten seconds of decoding
        // while still proving that seeks past the old 60 s cut-off work.
        var (playback, error) = await LoadPlaybackAsync(path, maxFrames: 16);
        Assert.True(
            playback is not null,
            $"Media Foundation could not decode '{path}': {error}. Reproduce with: {ReproCommand}");

        Assert.Equal(16, playback.FrameCount);
        Assert.InRange(playback.CachedDuration.TotalSeconds, 64.9, 65.1);
        Assert.True(playback.SampleInterval > TimeSpan.FromSeconds(4));

        Assert.True(playback.TrySampleAt(0.5, 0.5, 30, out var middle), "Middle sample failed.");
        Assert.True(IsGreen(middle), $"Expected 30 s to be green, got {middle}.");

        Assert.True(playback.TrySampleAt(0.5, 0.5, 62, out var tail), "Tail sample failed.");
        Assert.True(IsRed(tail), $"Expected 62 s to reach the red tail, got {tail}.");

        Assert.True(playback.TrySampleAt(0.5, 0.5, 65.5, out var wrapped), "Wrapped sample failed.");
        Assert.True(IsGreen(wrapped), $"Expected 65.5 s to wrap to green, got {wrapped}.");
    }

    [Fact]
    public async Task HevcFixtureDecodesOrReportsAnObservableFallback()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "studio-hevc-two-tone.mp4");
        Assert.True(File.Exists(path), $"Video fixture was not copied to the test output: {path}");

        var (playback, error) = await LoadPlaybackAsync(path);
        if (playback is null)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(error),
                "HEVC failed without a diagnostic; the caller cannot tell why it fell back.");
            _output.WriteLine(
                $"HEVC unavailable on this machine; sampler is null and the renderer uses its " +
                $"procedural fallback instead of black. Reason: {error}");
            return;
        }

        if (playback.IsThumbnailFallback)
        {
            Assert.Equal(1, playback.FrameCount);
            Assert.False(
                string.IsNullOrWhiteSpace(playback.DecodeWarning),
                "Thumbnail fallback did not record why frame decoding failed.");
            Assert.True(playback.TrySampleAt(0.5, 0.5, 0.1, out var early));
            Assert.True(playback.TrySampleAt(0.5, 0.5, 0.85, out var late));
            Assert.Equal(early, late);
            _output.WriteLine(
                $"HEVC frame decoding unavailable; shell thumbnail is the single fallback frame " +
                $"(center RGB {early.R},{early.G},{early.B}). Reason: {playback.DecodeWarning}");
            return;
        }

        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.1, out var red), "HEVC early sample failed.");
        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.85, out var blue), "HEVC late sample failed.");
        Assert.True(IsRed(red), $"Expected HEVC first half to be red, got {red}.");
        Assert.True(IsBlue(blue), $"Expected HEVC second half to be blue, got {blue}.");
        _output.WriteLine(
            $"HEVC decoded {playback.FrameCount} frames at {playback.SampleInterval} per frame.");
    }

    [Fact]
    public async Task HevcMediaSourceSurfacesItsThumbnailFallbackWarning()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "studio-hevc-two-tone.mp4");
        Assert.True(File.Exists(path), $"Video fixture was not copied to the test output: {path}");

        var source = await StudioMediaSource.TryLoadAsync(path).WaitAsync(TimeSpan.FromSeconds(60));
        if (source is null)
        {
            _output.WriteLine("HEVC produced no thumbnail; the caller only sees a null sampler.");
            return;
        }

        Assert.True(source.TrySampleAt(0.5, 0.5, 0.1, out var color), "The fallback frame was unavailable.");
        Assert.True(color.R + color.G + color.B > 0, "The thumbnail fallback is black.");
        if (source.IsThumbnailFallback)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(source.DecodeWarning),
                "The thumbnail fallback did not expose why frame decoding failed.");
        }

        _output.WriteLine(
            $"thumbnailFallback={source.IsThumbnailFallback} warning={source.DecodeWarning ?? "<none>"} " +
            $"center=RGB {color.R},{color.G},{color.B}");
    }

    [Fact]
    public async Task CorruptVideoReportsAnErrorInsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-corrupt-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(path, "this is not a video");
        try
        {
            var (playback, error) = await LoadPlaybackAsync(path);
            Assert.False(
                playback is not null && string.IsNullOrWhiteSpace(error),
                "A fallback playback was created without recording the decode error.");
            if (playback is null)
            {
                Assert.False(string.IsNullOrWhiteSpace(error), "A null playback had no diagnostic.");
            }
            else
            {
                Assert.True(playback.IsThumbnailFallback);
            }

            // This is the caller contract: no exception, null sampler, and the
            // renderer keeps its procedural fallback.
            Assert.Null(await StudioMediaSource.TryLoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FiveMinuteCacheLoopsAcrossTenMinutesWithoutDrift()
    {
        var frames = Enumerable.Range(0, 240)
            .Select(index => SolidFrame((byte)index, 0, 0))
            .ToArray();
        var playback = StudioVideoPlayback.FromFrames(TimeSpan.FromSeconds(1.25), frames);

        Assert.Equal(TimeSpan.FromMinutes(5), playback.CachedDuration);
        for (var second = 0; second < 600; second++)
        {
            var expectedIndex = (int)Math.Round(
                second % 300 / 1.25,
                MidpointRounding.AwayFromZero);
            Assert.True(
                playback.TrySampleAt(0.5, 0.5, second, out var color),
                $"Second {second} was unavailable.");
            Assert.Equal(new StudioRgb((byte)expectedIndex, 0, 0), color);
        }
    }

    [Fact]
    public void ThirtyMinuteCacheLoopsAcrossSixtyMinutesWithoutDrift()
    {
        var frames = Enumerable.Range(0, 240)
            .Select(index => SolidFrame((byte)index, 0, 0))
            .ToArray();
        var playback = StudioVideoPlayback.FromFrames(TimeSpan.FromSeconds(7.5), frames);

        Assert.Equal(TimeSpan.FromMinutes(30), playback.CachedDuration);
        for (var second = 0; second < 3600; second++)
        {
            var expectedIndex = Math.Clamp(
                (int)Math.Round(
                    second % 1800 / 7.5,
                    MidpointRounding.AwayFromZero),
                0,
                239);
            Assert.True(
                playback.TrySampleAt(0.5, 0.5, second, out var color),
                $"Second {second} was unavailable.");
            Assert.Equal(new StudioRgb((byte)expectedIndex, 0, 0), color);
        }
    }

    [Fact]
    public void SingleFrameFallbackSamplesTheSamePixelsAtAnyTime()
    {
        var playback = StudioVideoPlayback.FromFrames(
            TimeSpan.FromSeconds(1),
            [SolidFrame(0, 0, 254)]);

        foreach (var seconds in new[] { 0d, 0.1, 30, 300, 1800, 7200 })
        {
            Assert.True(
                playback.TrySampleAt(0.5, 0.5, seconds, out var color),
                $"Single-frame sample at {seconds} s failed.");
            Assert.True(IsBlue(color), $"Single-frame sample at {seconds} s changed: {color}.");
        }
    }

    [Fact]
    public async Task ProgressivePlaybackServesFirstFrameWhileLaterFramesLoad()
    {
        var first = SolidFrame(255, 0, 0);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var playback = StudioVideoPlayback.CreateProgressiveForTesting(
            TimeSpan.FromMilliseconds(250),
            4,
            first,
            async (_, token) =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(token);
                return SolidFrame(0, 0, 255);
            },
            cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, playback.LoadedFrameCount);
        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.8, out var before), "First frame was unavailable.");
        Assert.True(IsRed(before), $"Expected the first frame while loading, got {before}.");

        release.SetResult(true);
        await playback.BackgroundLoad.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, playback.LoadedFrameCount);
        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.8, out var after), "Loaded frame was unavailable.");
        Assert.True(IsBlue(after), $"Expected the loaded frame, got {after}.");
    }

    [Fact]
    public async Task ProgressivePlaybackCancellationStopsTheWorkerBeforeItWrites()
    {
        var first = SolidFrame(255, 0, 0);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var playback = StudioVideoPlayback.CreateProgressiveForTesting(
            TimeSpan.FromMilliseconds(250),
            4,
            first,
            async (_, token) =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(token);
                return SolidFrame(0, 0, 255);
            },
            cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        release.TrySetResult(true);
        await playback.BackgroundLoad.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, playback.LoadedFrameCount);
        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.8, out var color), "Cached frame was unavailable.");
        Assert.True(IsRed(color), $"Cancellation published a late frame: {color}.");
    }

    [Fact]
    public async Task ProgressivePlaybackFillsEverySlotInOrder()
    {
        var frames = Enumerable.Range(0, 8)
            .Select(index => SolidFrame((byte)(index * 20), 0, 0))
            .ToArray();
        var playback = StudioVideoPlayback.CreateProgressiveForTesting(
            TimeSpan.FromSeconds(1),
            frames.Length,
            frames[0],
            (index, _) => Task.FromResult<byte[]?>(frames[index]));

        await playback.BackgroundLoad.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(frames.Length, playback.LoadedFrameCount);
        for (var index = 0; index < frames.Length; index++)
        {
            Assert.True(
                playback.TrySampleAt(0.5, 0.5, index + 0.1, out var color),
                $"Frame {index} was unavailable.");
            Assert.Equal(new StudioRgb((byte)(index * 20), 0, 0), color);
        }

        Assert.Equal(TimeSpan.FromSeconds(frames.Length), playback.CachedDuration);
    }

    [Fact]
    public async Task RealVideoProgressiveLoadPublishesAUsableFirstFrame()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "studio-two-tone.mp4");
        Assert.True(File.Exists(path), $"Video fixture was not copied to the test output: {path}");

        var file = await StorageFile.GetFileFromPathAsync(path);
        var playback = await StudioVideoPlayback.TryCreateProgressiveAsync(file, maxFrames: 4)
            .WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(playback is not null, $"Media Foundation could not decode '{path}'.");

        Assert.InRange(playback.LoadedFrameCount, 1, 4);
        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.1, out var first), "The first frame was unavailable.");
        Assert.True(IsRed(first), $"Expected the first frame to be red, got {first}.");

        await playback.BackgroundLoad.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(4, playback.LoadedFrameCount);
        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.85, out var last), "The final frame was unavailable.");
        Assert.True(IsBlue(last), $"Expected the final frame to be blue, got {last}.");
    }

    [Fact]
    public async Task ProgressivePlaybackCancelsALongRealVideoLoad()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "studio-long-65s.mp4");
        Assert.True(File.Exists(path), $"Video fixture was not copied to the test output: {path}");

        var file = await StorageFile.GetFileFromPathAsync(path);
        using var cancellation = new CancellationTokenSource();
        var playback = await StudioVideoPlayback.TryCreateProgressiveAsync(
                file,
                maxFrames: 240,
                cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(playback is not null, $"Media Foundation could not decode '{path}'.");
        Assert.InRange(playback.LoadedFrameCount, 1, 239);

        cancellation.Cancel();
        await playback.BackgroundLoad.WaitAsync(TimeSpan.FromSeconds(10));
        var loadedAtCancel = playback.LoadedFrameCount;
        await Task.Delay(300);
        Assert.Equal(loadedAtCancel, playback.LoadedFrameCount);
        Assert.True(loadedAtCancel < 240, "Cancellation did not stop the long video worker.");
    }

    private static async Task<StudioMediaSource?> LoadAsync(string path)
    {
        try
        {
            return await StudioMediaSource.TryLoadAsync(path).WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"Media Foundation did not decode '{path}' within 60 s. Reproduce with: {ReproCommand}", ex);
        }
    }

    private static async Task<(StudioVideoPlayback? Playback, string? Error)> LoadPlaybackAsync(
        string path,
        int maxFrames = 240)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        try
        {
            return await StudioVideoPlayback.TryCreateWithDiagnosticsAsync(file, maxFrames)
                .WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"Media Foundation did not decode '{path}' within 120 s. Reproduce with: {ReproCommand}", ex);
        }
    }

    private static byte[] SolidFrame(byte red, byte green, byte blue)
    {
        var pixels = new byte[32 * 32 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = 255;
        }

        return pixels;
    }

    private static bool IsRed(StudioRgb color) => color.R > 200 && color.G < 60 && color.B < 60;

    private static bool IsGreen(StudioRgb color) => color.G > 200 && color.R < 60 && color.B < 60;

    private static bool IsBlue(StudioRgb color) => color.B > 200 && color.R < 60 && color.G < 60;
}
