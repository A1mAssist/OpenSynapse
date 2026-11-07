// Chroma shared-memory record layout and effect rendering reference:
// worflor/neuron crates/neuron-host/src/adapters/chroma_shm.rs (GPL-3.0-or-later).
// This C# port is part of OpenSynapse and distributed under AGPL-3.0.
using System.Buffers.Binary;
using System.Reflection;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.ChromaBroker;

internal enum DecodedEffectKind
{
    None,
    Static,
    Custom,
    Wave,
    Spectrum,
    Breathing,
    Blinking,
    Reactive,
    Other,
}

internal readonly record struct ChromaRgb(byte R, byte G, byte B)
{
    public uint ToBgr() => (uint)R | ((uint)G << 8) | ((uint)B << 16);
}

internal sealed record ChromaLayout(
    int Stride,
    int EffectOffset,
    int TickOffset,
    int StaticColorOffset,
    int GridOffset,
    byte Rows,
    byte Cols,
    uint GridCode,
    int? KeyColorOffset = null,
    int? KeyOverrideOffset = null,
    int? WaveDirectionOffset = null,
    int? BreathingTypeOffset = null,
    int? Color1Offset = null,
    int? Color2Offset = null,
    int? BlinkColorOffset = null);

internal sealed record ChromaDeviceFrame(
    ChromaBrokerDeviceClass Class,
    DecodedEffectKind Effect,
    uint EffectCode,
    ushort Device,
    ulong TickMs,
    byte Rows,
    byte Cols,
    ChromaRgb[] Cells,
    bool WaveReverse = false,
    ChromaRgb BreathingColor1 = default,
    ChromaRgb BreathingColor2 = default,
    bool BreathingRandom = false,
    ChromaRgb BlinkColor = default);

internal static class ChromaFrameDecoder
{
    internal const int Record0 = 8;
    internal const int RingDepth = 10;
    internal const int KeyStreamChannelStride = 0x81;

    private static readonly byte[] _keyStream = LoadKeyStream();

    private static readonly ChromaLayout Keyboard = new(
        Stride: 0xB98,
        EffectOffset: 0x04,
        TickOffset: 0xB90,
        StaticColorOffset: 0x48,
        GridOffset: 0x4C,
        Rows: 6,
        Cols: 22,
        GridCode: 7,
        KeyColorOffset: 0x25C,
        KeyOverrideOffset: 0x46C,
        WaveDirectionOffset: 0x0C,
        BreathingTypeOffset: 0x18,
        Color1Offset: 0x1C,
        Color2Offset: 0x20);

    private static readonly ChromaLayout Mouse = new(
        Stride: 0x1C0,
        EffectOffset: 0x08,
        TickOffset: 0x1B8,
        StaticColorOffset: 0x1A8,
        GridOffset: 0x9C,
        Rows: 9,
        Cols: 7,
        GridCode: 8,
        WaveDirectionOffset: 0x1B0,
        BreathingTypeOffset: 0x10,
        Color1Offset: 0x14,
        Color2Offset: 0x18,
        BlinkColorOffset: 0x20);

    private static readonly ChromaLayout Headset = new(
        Stride: 0x40,
        EffectOffset: 0x04,
        TickOffset: 0x38,
        StaticColorOffset: 0x30,
        GridOffset: 0x18,
        Rows: 1,
        Cols: 5,
        GridCode: 7,
        Color1Offset: 0x10);

    private static readonly ChromaLayout Mousepad = new(
        Stride: 0xC0,
        EffectOffset: 0x04,
        TickOffset: 0xB8,
        StaticColorOffset: 0x28,
        GridOffset: 0x2C,
        Rows: 1,
        Cols: 15,
        GridCode: 7,
        WaveDirectionOffset: 0x1C,
        BreathingTypeOffset: 0x0C,
        Color1Offset: 0x10,
        Color2Offset: 0x14);

    private static readonly ChromaLayout Keypad = new(
        Stride: 0x90,
        EffectOffset: 0x04,
        TickOffset: 0x88,
        StaticColorOffset: 0x78,
        GridOffset: 0x18,
        Rows: 4,
        Cols: 5,
        GridCode: 7,
        WaveDirectionOffset: 0x80,
        BreathingTypeOffset: 0x0C,
        Color1Offset: 0x10,
        Color2Offset: 0x14);

    private static readonly ChromaLayout ChromaLink = new(
        Stride: 0xF0,
        EffectOffset: 0x04,
        TickOffset: 0xE8,
        StaticColorOffset: 0xE4,
        GridOffset: 0x18,
        Rows: 1,
        Cols: 5,
        GridCode: 7);

    internal static ReadOnlySpan<byte> Keystream => _keyStream;

    internal static ChromaLayout GetLayout(ChromaBrokerDeviceClass device) => device switch
    {
        ChromaBrokerDeviceClass.Keyboard => Keyboard,
        ChromaBrokerDeviceClass.Mouse => Mouse,
        ChromaBrokerDeviceClass.Headset => Headset,
        ChromaBrokerDeviceClass.Mousepad => Mousepad,
        ChromaBrokerDeviceClass.Keypad => Keypad,
        ChromaBrokerDeviceClass.ChromaLink => ChromaLink,
        _ => throw new ArgumentOutOfRangeException(nameof(device)),
    };

    internal static ChromaDeviceFrame? Decode(ReadOnlySpan<byte> section, ChromaBrokerDeviceClass device)
    {
        var layout = GetLayout(device);
        if (section.Length < Record0 + RingDepth * layout.Stride ||
            !TryReadU32(section, 0, out var head))
        {
            return null;
        }

        var slot = (int)((head + RingDepth - 1) % RingDepth);
        var record = section.Slice(Record0 + slot * layout.Stride, layout.Stride);
        if (!TryReadU32(record, 0, out var deviceMask) ||
            (byte)(deviceMask >> 16) != device.Bit() ||
            !TryReadU32(record, layout.EffectOffset, out var effectCode) ||
            !TryReadU64(record, layout.TickOffset, out var tick))
        {
            return null;
        }

        var phase = FramePhase(tick);
        var cellCount = layout.Rows * layout.Cols;
        var cells = Array.Empty<ChromaRgb>();
        var effect = DecodedEffectKind.Other;
        var waveReverse = false;
        var breathRandom = false;
        var breath1 = default(ChromaRgb);
        var breath2 = default(ChromaRgb);
        var blink = new ChromaRgb(255, 255, 255);

        if (effectCode == 0)
        {
            effect = DecodedEffectKind.None;
        }
        else if (effectCode == 6)
        {
            if (!TryReadXorRgb(record, layout.StaticColorOffset, phase, out var colour))
            {
                return null;
            }
            cells = new ChromaRgb[cellCount];
            Array.Fill(cells, colour);
            effect = DecodedEffectKind.Static;
        }
        else if (effectCode == layout.GridCode)
        {
            cells = new ChromaRgb[cellCount];
            for (var index = 0; index < cellCount; index++)
            {
                if (!TryReadXorRgb(record, layout.GridOffset + 4 * index, phase, out cells[index]))
                {
                    return null;
                }
            }
            effect = DecodedEffectKind.Custom;
        }
        else if (effectCode == 8 && layout.KeyColorOffset is { } colourOffset &&
                 layout.KeyOverrideOffset is { } keyOffset)
        {
            cells = new ChromaRgb[cellCount];
            for (var index = 0; index < cellCount; index++)
            {
                if (!TryReadXorRgb(record, colourOffset + 4 * index, phase, out var baseColour) ||
                    !TryReadXorRgb(record, keyOffset + 4 * index, phase, out var keyColour) ||
                    !TryReadXorByte(record, keyOffset + 4 * index + 3, phase + 3 * KeyStreamChannelStride, out var active))
                {
                    return null;
                }
                cells[index] = (active & 0x01) != 0 ? keyColour : baseColour;
            }
            effect = DecodedEffectKind.Custom;
        }
        else if (effectCode == 1)
        {
            var direction = layout.WaveDirectionOffset is { } offset && TryReadU32(record, offset, out var value)
                ? value
                : 0;
            waveReverse = direction is 2 or 4;
            effect = DecodedEffectKind.Wave;
        }
        else if (effectCode == 2)
        {
            effect = DecodedEffectKind.Spectrum;
        }
        else if (effectCode == 3)
        {
            var breathType = layout.BreathingTypeOffset is { } offset && TryReadU32(record, offset, out var value)
                ? value
                : 1;
            var colour1 = layout.Color1Offset is { } colour1Offset && TryReadRgb(record, colour1Offset, out var c1)
                ? c1
                : (ChromaRgb?)null;
            var colour2 = layout.Color2Offset is { } colour2Offset && TryReadRgb(record, colour2Offset, out var c2)
                ? c2
                : (ChromaRgb?)null;
            if (breathType == 3 || colour1 is null)
            {
                breathRandom = true;
            }
            else if (breathType == 2 && colour2 is { } second)
            {
                breath1 = colour1.Value;
                breath2 = second;
            }
            else
            {
                breath1 = colour1.Value;
                breath2 = colour1.Value;
            }
            effect = DecodedEffectKind.Breathing;
        }
        else if (effectCode == 4)
        {
            if (layout.BlinkColorOffset is { } offset && TryReadRgb(record, offset, out var colour))
            {
                blink = colour;
            }
            effect = DecodedEffectKind.Blinking;
        }
        else if (effectCode == 5)
        {
            effect = DecodedEffectKind.Reactive;
        }

        return new ChromaDeviceFrame(
            device,
            effect,
            effectCode,
            (ushort)(deviceMask & 0xFFFF),
            tick,
            layout.Rows,
            layout.Cols,
            cells,
            waveReverse,
            breath1,
            breath2,
            breathRandom,
            blink);
    }

    internal static ChromaSdkEffect ToEffect(ChromaDeviceFrame frame, ulong nowMs)
    {
        var device = frame.Class.ToSdkType();
        if (frame.Effect is DecodedEffectKind.None or DecodedEffectKind.Other)
        {
            return new ChromaSdkEffect(device, ChromaSdkEffectKind.None, 1, 1, [0]);
        }

        if (frame.Effect is DecodedEffectKind.Static or DecodedEffectKind.Custom)
        {
            return new ChromaSdkEffect(
                device,
                frame.Effect == DecodedEffectKind.Static ? ChromaSdkEffectKind.Static : ChromaSdkEffectKind.Custom,
                frame.Cols,
                frame.Rows,
                frame.Cells.Select(colour => colour.ToBgr()).ToArray());
        }

        var cells = RenderCells(frame, nowMs);
        return new ChromaSdkEffect(
            device,
            ChromaSdkEffectKind.Custom,
            frame.Cols,
            frame.Rows,
            cells.Select(colour => colour.ToBgr()).ToArray());
    }

    internal static bool IsAnimated(DecodedEffectKind effect) =>
        effect is DecodedEffectKind.Wave or DecodedEffectKind.Spectrum or
            DecodedEffectKind.Breathing or DecodedEffectKind.Blinking;

    private static ChromaRgb[] RenderCells(ChromaDeviceFrame frame, ulong nowMs)
    {
        if (frame.Cells.Length > 0)
        {
            return frame.Cells;
        }

        var count = frame.Rows * frame.Cols;
        var cells = new ChromaRgb[count];
        // Keep the float clock small; 240 s is divisible by the preset cycles here.
        var t = (nowMs % 240_000) / 1000f;
        switch (frame.Effect)
        {
            case DecodedEffectKind.Spectrum:
                Array.Fill(cells, HueRgb(t / 6f));
                break;
            case DecodedEffectKind.Wave:
            {
                var columns = Math.Max(1, (int)frame.Cols);
                for (var index = 0; index < count; index++)
                {
                    var x = (index % columns) / (float)columns;
                    if (frame.WaveReverse) x = 1f - x;
                    cells[index] = HueRgb(x - t / 2f);
                }
                break;
            }
            case DecodedEffectKind.Breathing:
            {
                var cycle = (ulong)MathF.Floor(t / 4f);
                var level = 0.5f - 0.5f * MathF.Cos(MathF.PI * 2f * t / 4f);
                var colour = frame.BreathingRandom
                    ? HueRgb(cycle * 0.37f)
                    : (cycle & 1) == 0 ? frame.BreathingColor1 : frame.BreathingColor2;
                Array.Fill(cells, Scale(colour, level));
                break;
            }
            case DecodedEffectKind.Blinking:
                Array.Fill(cells, (nowMs / 500) % 2 == 0 ? frame.BlinkColor : default);
                break;
            default:
                break;
        }
        return cells;
    }

    private static int FramePhase(ulong tick)
    {
        var phase = (int)(tick & 0x7F);
        if (0x80 - phase < 4)
        {
            phase -= 3;
        }
        return phase;
    }

    private static bool TryReadU32(ReadOnlySpan<byte> bytes, int offset, out uint value)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(uint))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
        return true;
    }

    private static bool TryReadU64(ReadOnlySpan<byte> bytes, int offset, out ulong value)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(ulong))
        {
            value = 0;
            return false;
        }
        value = BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
        return true;
    }

    private static bool TryReadRgb(ReadOnlySpan<byte> bytes, int offset, out ChromaRgb value)
    {
        if (offset < 0 || offset > bytes.Length - 3)
        {
            value = default;
            return false;
        }
        value = new ChromaRgb(bytes[offset], bytes[offset + 1], bytes[offset + 2]);
        return true;
    }

    private static bool TryReadXorRgb(ReadOnlySpan<byte> bytes, int offset, int phase, out ChromaRgb value)
    {
        if (offset < 0 || offset > bytes.Length - 3)
        {
            value = default;
            return false;
        }
        value = new ChromaRgb(
            (byte)(bytes[offset] ^ _keyStream[phase]),
            (byte)(bytes[offset + 1] ^ _keyStream[phase + KeyStreamChannelStride]),
            (byte)(bytes[offset + 2] ^ _keyStream[phase + 2 * KeyStreamChannelStride]));
        return true;
    }

    private static bool TryReadXorByte(ReadOnlySpan<byte> bytes, int offset, int keyIndex, out byte value)
    {
        if (offset < 0 || offset >= bytes.Length)
        {
            value = 0;
            return false;
        }
        value = (byte)(bytes[offset] ^ _keyStream[keyIndex]);
        return true;
    }

    private static ChromaRgb Scale(ChromaRgb colour, float level) => new(
        ToByte(colour.R * level),
        ToByte(colour.G * level),
        ToByte(colour.B * level));

    private static ChromaRgb HueRgb(float hue)
    {
        hue -= MathF.Floor(hue);
        hue *= 6f;
        var x = 1f - MathF.Abs(hue % 2f - 1f);
        var (red, green, blue) = (uint)hue switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
        return new ChromaRgb(ToByte(red), ToByte(green), ToByte(blue));
    }

    private static byte ToByte(float value) =>
        (byte)MathF.Round(Math.Clamp(value * 255f, 0f, 255f), MidpointRounding.AwayFromZero);

    private static byte[] LoadKeyStream()
    {
        var assembly = typeof(ChromaFrameDecoder).Assembly;
        using var stream = assembly.GetManifestResourceStream("OpenSynapse.ChromaBroker.keystream.bin") ??
            throw new InvalidOperationException("Embedded keystream.bin is missing.");
        var bytes = new byte[512];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("Embedded keystream.bin must be exactly 512 bytes.");
        }
        return bytes;
    }
}
