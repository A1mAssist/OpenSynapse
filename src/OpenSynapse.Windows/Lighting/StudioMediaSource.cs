using OpenSynapse.Core.Lighting;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Decodes one Chroma Generate media file into cached 32 x 32 BGRA frames and
/// samples them with normalized coordinates. Images use a single frame; videos
/// use <see cref="StudioVideoPlayback"/> for timed sampling. The progressive
/// path publishes the first video frame before the rest of the cache is loaded.
/// </summary>
public sealed class StudioMediaSource : IStudioMediaSampler
{
    private const int FrameSize = StudioMediaPalette.GridSize;
    private readonly byte[]? _bgra;
    private readonly StudioVideoPlayback? _video;

    private StudioMediaSource(byte[] bgra) => _bgra = bgra;

    private StudioMediaSource(StudioVideoPlayback video) => _video = video;

    /// <summary>
    /// Loads an image or every video frame. Returns null for a missing,
    /// unreadable or undecodable file so the renderer keeps its procedural
    /// fallback.
    /// </summary>
    public static Task<StudioMediaSource?> TryLoadAsync(string? path) =>
        TryLoadCoreAsync(path, progressive: false, CancellationToken.None);

    /// <summary>
    /// Loads the first video frame, then fills the rest on a background worker.
    /// Cancellation stops the worker before it publishes another frame.
    /// </summary>
    public static Task<StudioMediaSource?> TryLoadProgressiveAsync(
        string? path,
        CancellationToken cancellationToken = default) =>
        TryLoadCoreAsync(path, progressive: true, cancellationToken);

    private static async Task<StudioMediaSource?> TryLoadCoreAsync(
        string? path,
        bool progressive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path)) return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return null;
        }

        if (!File.Exists(fullPath)) return null;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(fullPath);
            if (IsVideo(fullPath))
            {
                var video = progressive
                    ? await StudioVideoPlayback.TryCreateProgressiveAsync(file, cancellationToken)
                    : await StudioVideoPlayback.TryCreateAsync(file);
                return video is null ? null : new StudioMediaSource(video);
            }

            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var pixels = await DecodePixelsAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return new StudioMediaSource(pixels);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Missing codecs, locked files and truncated media all surface as
            // WinRT exceptions; the caller treats them as "no media".
            return null;
        }
    }

    /// <summary>Completes when the video's background cache worker stops; images complete immediately.</summary>
    public Task BackgroundLoad => _video?.BackgroundLoad ?? Task.CompletedTask;

    /// <summary>True when only the shell thumbnail could be decoded.</summary>
    public bool IsThumbnailFallback => _video?.IsThumbnailFallback ?? false;

    /// <summary>First frame decode error, or null when the media decoded cleanly.</summary>
    public string? DecodeWarning => _video?.DecodeWarning;

    /// <summary>Samples the first cached frame; coordinates outside 0..1 wrap.</summary>
    public bool TrySample(double u, double v, out StudioRgb color) =>
        _video is not null
            ? _video.TrySample(u, v, out color)
            : StudioVideoPlayback.TrySampleFrame(_bgra!, u, v, out color);

    /// <summary>Samples the frame nearest <paramref name="seconds"/>; images ignore time.</summary>
    public bool TrySampleAt(double u, double v, double seconds, out StudioRgb color) =>
        _video is not null
            ? _video.TrySampleAt(u, v, seconds, out color)
            : StudioVideoPlayback.TrySampleFrame(_bgra!, u, v, out color);

    internal static async Task<byte[]> DecodePixelsAsync(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform
        {
            ScaledWidth = FrameSize,
            ScaledHeight = FrameSize,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var length = checked(FrameSize * FrameSize * 4);
        var buffer = new global::Windows.Storage.Streams.Buffer((uint)length);
        bitmap.CopyToBuffer(buffer);
        buffer.Length = (uint)length;
        using var reader = DataReader.FromBuffer(buffer);
        var pixels = new byte[length];
        reader.ReadBytes(pixels);
        return pixels;
    }

    private static bool IsVideo(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mkv" or ".avi";
}
