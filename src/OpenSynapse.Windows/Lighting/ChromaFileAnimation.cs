using System.Buffers.Binary;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public sealed record ChromaFileFrame(TimeSpan Duration, IReadOnlyList<RazerRgb> Colors);

public sealed class ChromaFileAnimation
{
    public ChromaFileAnimation(
        string fileName,
        DateTime lastWriteTimeUtc,
        IReadOnlyList<ChromaFileFrame> frames,
        bool isUniform,
        RazerRgb uniformColor)
    {
        FileName = fileName;
        LastWriteTimeUtc = lastWriteTimeUtc;
        Frames = frames;
        IsUniform = isUniform;
        UniformColor = uniformColor;
        Duration = frames.Aggregate(TimeSpan.Zero, (total, frame) => total + frame.Duration);
    }

    public string FileName { get; }
    public DateTime LastWriteTimeUtc { get; }
    public IReadOnlyList<ChromaFileFrame> Frames { get; }
    public TimeSpan Duration { get; }
    public bool IsUniform { get; }
    public RazerRgb UniformColor { get; }
    public string Fingerprint => $"{FileName}\n{LastWriteTimeUtc.Ticks}\n{Frames.Count}\n{Duration.Ticks}";
}

public static class ChromaFileAnimationParser
{
    public const int MaximumFileSizeBytes = 5 * 1024 * 1024;
    public const int MaximumFrameCount = 1800;
    private const int HeaderSize = sizeof(int) + sizeof(byte) + sizeof(byte) + sizeof(uint);
    private const byte TwoDimensionalDeviceType = 1;
    private const byte KeyboardDevice = 0;
    private const byte KeyboardExtendedDevice = 3;
    private const int MinimumFrameDurationMilliseconds = 1;
    private const int MaximumFrameDurationMilliseconds = 60_000;

    public static ChromaFileAnimation ParseFile(string path, string? fileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The Chroma animation file was not found.", path);
        }
        if (info.Length > MaximumFileSizeBytes)
        {
            throw new InvalidDataException(
                $"The .chroma file cannot exceed {MaximumFileSizeBytes / (1024 * 1024)} MiB.");
        }

        return Parse(
            File.ReadAllBytes(path),
            fileName ?? info.Name,
            info.LastWriteTimeUtc);
    }

    public static ChromaFileAnimation Parse(
        ReadOnlySpan<byte> content,
        string fileName,
        DateTime lastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (content.Length == 0)
        {
            throw new InvalidDataException("The .chroma file is empty.");
        }
        if (content.Length > MaximumFileSizeBytes)
        {
            throw new InvalidDataException(
                $"The .chroma file cannot exceed {MaximumFileSizeBytes / (1024 * 1024)} MiB.");
        }
        if (content.Length < HeaderSize)
        {
            throw InvalidDocument();
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(content);
        if (version != 1)
        {
            throw new InvalidDataException(
                $"The .chroma file uses version {version}; only version 1 is supported.");
        }

        var deviceType = content[sizeof(int)];
        if (deviceType != TwoDimensionalDeviceType)
        {
            throw new InvalidDataException("Only two-dimensional keyboard .chroma files are supported.");
        }

        var device = content[sizeof(int) + sizeof(byte)];
        var (rows, columns) = device switch
        {
            KeyboardDevice => (6, 22),
            KeyboardExtendedDevice => (8, 24),
            _ => throw new InvalidDataException(
                "Only Keyboard and KeyboardExtended .chroma files are supported."),
        };

        var frameCount = BinaryPrimitives.ReadUInt32LittleEndian(
            content.Slice(sizeof(int) + sizeof(byte) + sizeof(byte), sizeof(uint)));
        if (frameCount is 0 or > MaximumFrameCount)
        {
            throw new InvalidDataException(
                $"The .chroma file must contain 1 to {MaximumFrameCount} frames.");
        }

        var pixelCount = checked(rows * columns);
        var frameSize = checked(sizeof(float) + (pixelCount * sizeof(uint)));
        var expectedSize = HeaderSize + ((long)frameCount * frameSize);
        if (content.Length != expectedSize)
        {
            throw InvalidDocument();
        }

        var frames = new List<ChromaFileFrame>((int)frameCount);
        var uniform = true;
        uint firstColor = 0;
        var hasColor = false;
        var offset = HeaderSize;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var duration = BinaryPrimitives.ReadSingleLittleEndian(content.Slice(offset, sizeof(float)));
            offset += sizeof(float);
            if (!float.IsFinite(duration) || duration <= 0)
            {
                throw new InvalidDataException(
                    "The .chroma file contains a non-positive or non-finite frame duration.");
            }

            var durationMilliseconds = duration * 1000d;
            if (durationMilliseconds < MinimumFrameDurationMilliseconds ||
                durationMilliseconds > MaximumFrameDurationMilliseconds)
            {
                throw new InvalidDataException(
                    $"A .chroma frame duration must be between {MinimumFrameDurationMilliseconds} and {MaximumFrameDurationMilliseconds} milliseconds.");
            }
            var roundedDuration = (int)Math.Round(durationMilliseconds, MidpointRounding.AwayFromZero);

            var pixels = new uint[pixelCount];
            for (var pixel = 0; pixel < pixelCount; pixel++)
            {
                var color = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(offset, sizeof(uint)));
                offset += sizeof(uint);
                if ((color & 0xFF000000u) != 0)
                {
                    throw new InvalidDataException(
                        "The .chroma file contains a color value outside COLORREF RGB encoding.");
                }

                pixels[pixel] = color;
                if (!hasColor)
                {
                    firstColor = color;
                    hasColor = true;
                }
                uniform &= color == firstColor;
            }

            var matrix = new uint[rows][];
            for (var row = 0; row < rows; row++)
            {
                matrix[row] = new uint[columns];
                Array.Copy(pixels, row * columns, matrix[row], 0, columns);
            }

            var mapped = device == KeyboardDevice
                ? ChromaKeyboardFrameMapper.Custom(matrix)
                : ChromaKeyboardFrameMapper.Custom2(matrix);
            AddOrCoalesce(frames, roundedDuration, mapped);
        }

        if (uniform)
        {
            var color = ChromaKeyboardFrameMapper.ToRgb(firstColor);
            return new ChromaFileAnimation(
                fileName,
                lastWriteTimeUtc,
                [new ChromaFileFrame(TimeSpan.FromSeconds(1), ChromaKeyboardFrameMapper.Static(color))],
                isUniform: true,
                color);
        }

        return new ChromaFileAnimation(
            fileName,
            lastWriteTimeUtc,
            frames,
            isUniform: false,
            default);
    }

    private static void AddOrCoalesce(
        List<ChromaFileFrame> frames,
        int durationMilliseconds,
        RazerRgb[] mapped)
    {
        if (frames.Count > 0 &&
            frames[^1].Colors.SequenceEqual(mapped) &&
            frames[^1].Duration.TotalMilliseconds + durationMilliseconds <=
                MaximumFrameDurationMilliseconds)
        {
            frames[^1] = frames[^1] with
            {
                Duration = TimeSpan.FromMilliseconds(
                    frames[^1].Duration.TotalMilliseconds + durationMilliseconds),
            };
            return;
        }

        frames.Add(new ChromaFileFrame(
            TimeSpan.FromMilliseconds(durationMilliseconds),
            mapped));
    }

    private static InvalidDataException InvalidDocument() =>
        new("The selected file is not a valid Razer Chroma version 1 animation.");
}

internal static class ChromaFileAnimationStore
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CacheEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static string _rootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapse",
        "lighting");

    internal static string RootDirectory
    {
        get
        {
            lock (Gate)
            {
                return _rootDirectory;
            }
        }
        set
        {
            lock (Gate)
            {
                _rootDirectory = Path.GetFullPath(value);
                Cache.Clear();
            }
        }
    }

    internal static ChromaFileAnimation Import(ReadOnlySpan<byte> content)
    {
        _ = ChromaFileAnimationParser.Parse(content, "pending.chroma", DateTime.MinValue);
        var root = RootDirectory;
        Directory.CreateDirectory(root);
        var fileName = $"{Guid.NewGuid():N}.chroma";
        var path = Path.Combine(root, fileName);
        File.WriteAllBytes(path, content.ToArray());
        var info = new FileInfo(path);
        var animation = ChromaFileAnimationParser.Parse(content, fileName, info.LastWriteTimeUtc);
        lock (Gate)
        {
            Cache[fileName] = new CacheEntry(info.Length, info.LastWriteTimeUtc, animation);
        }
        return animation;
    }

    internal static bool TryLoad(string fileName, out ChromaFileAnimation? animation)
    {
        animation = null;
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = Path.Combine(RootDirectory, fileName);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > ChromaFileAnimationParser.MaximumFileSizeBytes)
            {
                return false;
            }

            lock (Gate)
            {
                if (Cache.TryGetValue(fileName, out var entry) &&
                    entry.Length == info.Length &&
                    entry.LastWriteTimeUtc == info.LastWriteTimeUtc)
                {
                    animation = entry.Animation;
                    return true;
                }
            }

            var parsed = ChromaFileAnimationParser.ParseFile(path, fileName);
            lock (Gate)
            {
                Cache[fileName] = new CacheEntry(info.Length, info.LastWriteTimeUtc, parsed);
            }
            animation = parsed;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record CacheEntry(
        long Length,
        DateTime LastWriteTimeUtc,
        ChromaFileAnimation Animation);
}
