using OpenSynapse.Core.Lighting;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Caches a video as 32 x 32 BGRA frames and samples the frame nearest a
/// requested time. Short videos keep the preferred 250 ms cadence; longer
/// videos spread at most 240 frames over their full duration, so playback
/// covers the whole clip within a fixed memory budget instead of truncating at
/// 60 seconds. Progressive loading publishes the first frame immediately and
/// fills the remaining slots on one background worker.
/// </summary>
public sealed class StudioVideoPlayback : IStudioMediaSampler
{
    private const int PreferredFrameIntervalMilliseconds = 250;
    private const int MaxFrameCount = 240;
    private const int FrameSize = StudioMediaPalette.GridSize;
    private const int FrameLength = FrameSize * FrameSize * 4;
    private static readonly TimeSpan FrameDecodeTimeout = TimeSpan.FromSeconds(5);

    private readonly byte[][] _frames;
    private readonly double _intervalSeconds;
    private readonly double _loopSeconds;
    private int _loadedFrameCount;
    private string? _decodeWarning;
    private Task _backgroundLoad = Task.CompletedTask;

    private StudioVideoPlayback(byte[][] frames, TimeSpan interval)
    {
        _frames = frames;
        _intervalSeconds = interval.TotalSeconds;
        _loopSeconds = _intervalSeconds * frames.Length;
        _loadedFrameCount = frames.Length;
    }

    private StudioVideoPlayback(
        byte[][] frames,
        TimeSpan interval,
        bool thumbnailFallback,
        string? decodeWarning)
        : this(frames, interval)
    {
        IsThumbnailFallback = thumbnailFallback;
        _decodeWarning = decodeWarning;
    }

    private StudioVideoPlayback(TimeSpan interval, int frameCount, byte[] firstFrame)
    {
        _frames = new byte[frameCount][];
        Array.Fill(_frames, firstFrame);
        _intervalSeconds = interval.TotalSeconds;
        _loopSeconds = _intervalSeconds * frameCount;
        _loadedFrameCount = 1;
    }

    /// <summary>Number of cached frames; never more than 240 (about 0.94 MiB).</summary>
    public int FrameCount => _frames.Length;

    /// <summary>Number of slots that hold a decoded frame; 1 while the rest load.</summary>
    public int LoadedFrameCount => Volatile.Read(ref _loadedFrameCount);

    /// <summary>Completes when the background decode worker stops or finishes.</summary>
    public Task BackgroundLoad => _backgroundLoad;

    /// <summary>Time between cached frames; grows for videos longer than 60 seconds.</summary>
    public TimeSpan SampleInterval => TimeSpan.FromSeconds(_intervalSeconds);

    /// <summary>Full media duration covered by the cache.</summary>
    public TimeSpan CachedDuration => TimeSpan.FromSeconds(_loopSeconds);

    /// <summary>True when the shell thumbnail was the only decodable frame.</summary>
    public bool IsThumbnailFallback { get; }

    /// <summary>First frame or background decode error, or null when every frame decoded.</summary>
    public string? DecodeWarning => _decodeWarning;

    /// <summary>
    /// Decodes every frame before returning. Use
    /// <see cref="TryCreateProgressiveAsync"/> for the studio path, which
    /// publishes a first frame without waiting for the whole clip.
    /// </summary>
    public static async Task<StudioVideoPlayback?> TryCreateAsync(StorageFile file) =>
        (await TryCreateWithDiagnosticsAsync(file)).Playback;

    /// <summary>
    /// Decodes the first frame, returns a usable playback, and fills the
    /// remaining frame slots on one background worker. Cancellation stops that
    /// worker before it publishes another frame. The cache stays bounded by the
    /// existing 240-frame plan.
    /// </summary>
    public static Task<StudioVideoPlayback?> TryCreateProgressiveAsync(
        StorageFile file,
        CancellationToken cancellationToken = default) =>
        TryCreateProgressiveAsync(file, MaxFrameCount, cancellationToken);

    internal static async Task<StudioVideoPlayback?> TryCreateProgressiveAsync(
        StorageFile file,
        int maxFrames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (maxFrames < 1) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(
            () => CreateProgressiveCoreAsync(file, maxFrames, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Decodes frames and returns the first decode error alongside the result.
    /// A non-null error with a non-null playback means the cache is incomplete
    /// or is the single-frame thumbnail fallback.
    /// </summary>
    internal static async Task<(StudioVideoPlayback? Playback, string? Error)> TryCreateWithDiagnosticsAsync(
        StorageFile file,
        int maxFrames = MaxFrameCount)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (maxFrames < 1) throw new ArgumentOutOfRangeException(nameof(maxFrames));

        string? decodeError = null;
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(file);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var (frameCount, interval) = PlanFrames(clip.OriginalDuration, maxFrames);
            var frames = new List<byte[]>(frameCount);
            for (var index = 0; index < frameCount; index++)
            {
                var time = TimeSpan.FromSeconds(interval.TotalSeconds * index);
                var (pixels, error) = await TryDecodeFrameAsync(composition, time);
                if (pixels is null)
                {
                    decodeError ??= $"Frame {index} at {time.TotalSeconds:F2}s: {error}";
                    if (frames.Count == 0) break;

                    // Keep the time axis aligned when one seek fails; the
                    // affected slice holds the last good frame.
                    frames.Add(frames[^1]);
                    continue;
                }

                frames.Add(pixels);
            }

            if (frames.Count > 0)
            {
                return (new StudioVideoPlayback([.. frames], interval, false, decodeError), decodeError);
            }

            decodeError ??= "Media Foundation returned no frames.";
        }
        catch (Exception ex)
        {
            decodeError = DescribeException(ex);
        }

        return await TryCreateThumbnailAsync(file, decodeError, CancellationToken.None);
    }

    /// <summary>
    /// Splits a media duration into at most <paramref name="maxFrames"/> evenly
    /// spaced samples. Videos up to 60 seconds keep the 250 ms cadence; longer
    /// videos trade temporal resolution for full-duration coverage.
    /// </summary>
    internal static (int FrameCount, TimeSpan Interval) PlanFrames(
        TimeSpan duration,
        int maxFrames = MaxFrameCount)
    {
        if (maxFrames < 1) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        if (!double.IsFinite(duration.TotalSeconds) || duration <= TimeSpan.Zero)
        {
            return (1, TimeSpan.Zero);
        }

        var frameCount = (int)Math.Clamp(
            Math.Ceiling(duration.TotalMilliseconds / PreferredFrameIntervalMilliseconds),
            1,
            maxFrames);
        return (frameCount, TimeSpan.FromSeconds(duration.TotalSeconds / frameCount));
    }

    /// <summary>Samples the first cached frame; static playback ignores time.</summary>
    public bool TrySample(double u, double v, out StudioRgb color) =>
        TrySampleAt(u, v, 0, out color);

    /// <summary>Samples the frame nearest <paramref name="seconds"/>; playback wraps after the cached span.</summary>
    public bool TrySampleAt(double u, double v, double seconds, out StudioRgb color)
    {
        var pixels = _frames[NearestFrameIndex(seconds)];
        return TrySampleFrame(pixels, u, v, out color);
    }

    internal static bool TrySampleFrame(byte[] bgra, double u, double v, out StudioRgb color)
    {
        if (!double.IsFinite(u) || !double.IsFinite(v))
        {
            color = default;
            return false;
        }

        var column = Math.Clamp((int)((u - Math.Floor(u)) * FrameSize), 0, FrameSize - 1);
        var row = Math.Clamp((int)((v - Math.Floor(v)) * FrameSize), 0, FrameSize - 1);
        var index = (row * FrameSize + column) * 4;
        color = new StudioRgb(bgra[index + 2], bgra[index + 1], bgra[index]);
        return true;
    }

    /// <summary>Builds a playback from evenly spaced BGRA frames; used by tests.</summary>
    internal static StudioVideoPlayback FromFrames(TimeSpan interval, IReadOnlyList<byte[]> frames)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        if (frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));

        var snapshot = new byte[frames.Count][];
        for (var index = 0; index < frames.Count; index++)
        {
            var pixels = frames[index];
            if (pixels.Length != FrameLength)
            {
                throw new ArgumentException("Frames must be 32 x 32 BGRA buffers.", nameof(frames));
            }

            snapshot[index] = pixels;
        }

        return new StudioVideoPlayback(snapshot, interval);
    }

    /// <summary>Creates the same progressive state machine with a deterministic decoder.</summary>
    internal static StudioVideoPlayback CreateProgressiveForTesting(
        TimeSpan interval,
        int frameCount,
        byte[] firstFrame,
        Func<int, CancellationToken, Task<byte[]?>> decodeFrame,
        CancellationToken cancellationToken = default)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (firstFrame.Length != FrameLength) throw new ArgumentException("Frames must be 32 x 32 BGRA buffers.", nameof(firstFrame));
        ArgumentNullException.ThrowIfNull(decodeFrame);

        var playback = new StudioVideoPlayback(interval, frameCount, firstFrame);
        playback._backgroundLoad = Task.Run(
            () => playback.DecodeRemainingAsync(decodeFrame, cancellationToken),
            CancellationToken.None);
        return playback;
    }

    private static async Task<StudioVideoPlayback?> CreateProgressiveCoreAsync(
        StorageFile file,
        int maxFrames,
        CancellationToken cancellationToken = default)
    {
        string? decodeError = null;
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(file).AsTask(cancellationToken);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var (frameCount, interval) = PlanFrames(clip.OriginalDuration, maxFrames);
            var (pixels, error) = await TryDecodeFrameAsync(
                composition,
                TimeSpan.Zero,
                cancellationToken).ConfigureAwait(false);
            if (pixels is not null)
            {
                var playback = new StudioVideoPlayback(interval, frameCount, pixels);
                playback.StartBackgroundDecode(composition, cancellationToken);
                return playback;
            }

            decodeError = $"Frame 0 at 0.00s: {error}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            decodeError = DescribeException(ex);
        }

        return (await TryCreateThumbnailAsync(file, decodeError, cancellationToken).ConfigureAwait(false)).Playback;
    }

    private void StartBackgroundDecode(
        MediaComposition composition,
        CancellationToken cancellationToken)
    {
        // One worker per media source: avoids a thread per frame and keeps the
        // decode loop off the UI thread while the first frame is already live.
        _backgroundLoad = Task.Run(
            () => DecodeRemainingAsync(composition, cancellationToken),
            CancellationToken.None);
    }

    private async Task DecodeRemainingAsync(
        MediaComposition composition,
        CancellationToken cancellationToken)
    {
        await DecodeRemainingAsync(
            async (index, token) =>
            {
                var time = TimeSpan.FromSeconds(_intervalSeconds * index);
                var (pixels, error) = await TryDecodeFrameAsync(composition, time, token)
                    .ConfigureAwait(false);
                if (pixels is null)
                {
                    RecordWarning($"Frame {index} at {time.TotalSeconds:F2}s: {error}");
                }

                return pixels;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DecodeRemainingAsync(
        Func<int, CancellationToken, Task<byte[]?>> decodeFrame,
        CancellationToken cancellationToken)
    {
        try
        {
            for (var index = 1; index < _frames.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pixels = await decodeFrame(index, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _frames[index] = pixels ?? _frames[index - 1];
                Interlocked.Increment(ref _loadedFrameCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller switched media or disposed the view model.
        }
        catch (Exception ex)
        {
            RecordWarning(DescribeException(ex));
        }
    }

    private void RecordWarning(string? warning)
    {
        if (!string.IsNullOrWhiteSpace(warning))
        {
            Interlocked.CompareExchange(ref _decodeWarning, warning, null);
        }
    }

    private int NearestFrameIndex(double seconds)
    {
        if (_frames.Length == 1 || double.IsNaN(seconds) || seconds <= 0) return 0;

        var last = _frames.Length - 1;
        if (double.IsPositiveInfinity(seconds)) return last;

        // Playback wraps after the full cached span, not after a fixed 60 s.
        var time = seconds % _loopSeconds;
        var index = (int)Math.Round(time / _intervalSeconds, MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, last);
    }

    private static async Task<(byte[]? Pixels, string? Error)> TryDecodeFrameAsync(
        MediaComposition composition,
        TimeSpan time,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CreateTimeout(cancellationToken);
            using var stream = await composition.GetThumbnailAsync(
                time, FrameSize, FrameSize, VideoFramePrecision.NearestFrame).AsTask(timeout.Token);
            return (await StudioMediaSource.DecodePixelsAsync(stream), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, DescribeException(ex));
        }
    }

    private static async Task<(StudioVideoPlayback? Playback, string? Error)> TryCreateThumbnailAsync(
        StorageFile file,
        string? decodeError,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CreateTimeout(cancellationToken);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.VideosView,
                FrameSize,
                ThumbnailOptions.UseCurrentScale).AsTask(timeout.Token);
            if (thumbnail is null) return (null, decodeError);

            var pixels = await StudioMediaSource.DecodePixelsAsync(thumbnail);
            return (
                new StudioVideoPlayback([pixels], TimeSpan.Zero, true, decodeError),
                decodeError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var thumbnailError = DescribeException(ex);
            return (
                null,
                decodeError is null ? thumbnailError : $"{decodeError} | thumbnail: {thumbnailError}");
        }
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FrameDecodeTimeout);
        return timeout;
    }

    private static string DescribeException(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException com
            ? $"{com.GetType().Name} 0x{com.HResult:X8}: {com.Message}"
            : $"{ex.GetType().Name}: {ex.Message}";
}
