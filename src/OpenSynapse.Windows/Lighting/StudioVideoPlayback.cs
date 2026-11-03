using OpenSynapse.Core.Lighting;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Caches a video as 32 x 32 BGRA frames sampled every 250 ms (at most 240
/// frames, covering 60 seconds) and samples the frame nearest a requested
/// time. The cache is immutable after loading, so reads are thread-safe.
/// </summary>
public sealed class StudioVideoPlayback : IStudioMediaSampler
{
    private const int FrameIntervalMilliseconds = 250;
    private const int MaxFrameCount = 240;
    private const int FrameSize = StudioMediaPalette.GridSize;
    private const int FrameLength = FrameSize * FrameSize * 4;
    private static readonly TimeSpan FrameDecodeTimeout = TimeSpan.FromSeconds(5);

    private readonly byte[][] _frames;
    private readonly TimeSpan _interval;

    private StudioVideoPlayback(byte[][] frames, TimeSpan interval)
    {
        _frames = frames;
        _interval = interval;
    }

    /// <summary>
    /// Decodes evenly spaced frames from a video. When Media Foundation cannot
    /// open the clip, falls back to the shell video thumbnail as one frame, and
    /// returns null only when no frame can be decoded at all.
    /// </summary>
    public static async Task<StudioVideoPlayback?> TryCreateAsync(StorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            var clip = await MediaClip.CreateFromFileAsync(file);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var frameCount = (int)Math.Clamp(
                Math.Ceiling(clip.OriginalDuration.TotalMilliseconds / FrameIntervalMilliseconds),
                1,
                MaxFrameCount);
            var frames = new List<byte[]>(frameCount);
            for (var index = 0; index < frameCount; index++)
            {
                var pixels = await TryDecodeFrameAsync(
                    composition, TimeSpan.FromMilliseconds(index * FrameIntervalMilliseconds));
                if (pixels is null) break;
                frames.Add(pixels);
            }

            if (frames.Count > 0)
            {
                return new StudioVideoPlayback(
                    [.. frames], TimeSpan.FromMilliseconds(FrameIntervalMilliseconds));
            }
        }
        catch (Exception)
        {
            // Missing codecs and unsupported containers surface as WinRT
            // exceptions; the shell thumbnail below is the fallback frame.
        }

        try
        {
            using var timeout = new CancellationTokenSource(FrameDecodeTimeout);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.VideosView,
                FrameSize,
                ThumbnailOptions.UseCurrentScale).AsTask(timeout.Token);
            if (thumbnail is null) return null;
            return new StudioVideoPlayback(
                [await StudioMediaSource.DecodePixelsAsync(thumbnail)], TimeSpan.Zero);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Samples the first cached frame; static playback ignores time.</summary>
    public bool TrySample(double u, double v, out StudioRgb color) =>
        TrySampleAt(u, v, 0, out color);

    /// <summary>Samples the frame nearest <paramref name="seconds"/>; time outside the cache clamps to the ends.</summary>
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

    private int NearestFrameIndex(double seconds)
    {
        if (_frames.Length == 1 || double.IsNaN(seconds) || seconds <= 0) return 0;

        var last = _frames.Length - 1;
        if (double.IsPositiveInfinity(seconds) || seconds >= _interval.TotalSeconds * last) return last;

        var index = (int)Math.Round(seconds / _interval.TotalSeconds, MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, last);
    }

    private static async Task<byte[]?> TryDecodeFrameAsync(MediaComposition composition, TimeSpan time)
    {
        try
        {
            using var timeout = new CancellationTokenSource(FrameDecodeTimeout);
            using var stream = await composition.GetThumbnailAsync(
                time, FrameSize, FrameSize, VideoFramePrecision.NearestFrame).AsTask(timeout.Token);
            return await StudioMediaSource.DecodePixelsAsync(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
