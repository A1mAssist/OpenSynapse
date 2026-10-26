namespace OpenSynapse.Windows.Macros;

public enum OnboardMacroItemKind
{
    Key,
    MouseButton,
    Wheel,
    Delay,
    Cursor,
}

/// <summary>
/// One decoded entry of a firmware macro stream. Key items keep the raw HID usage
/// in Scancode so an unsupported key can still be written back unchanged.
/// </summary>
public sealed record OnboardMacroItem(OnboardMacroItemKind Kind)
{
    public int Scancode { get; init; }
    public bool Released { get; init; }
    public int MouseButton { get; init; }
    public bool MousePressed { get; init; }
    public int WheelDelta { get; init; }
    public int DelayMilliseconds { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
}

/// <summary>
/// Encodes and decodes the firmware macro stream built by Synapse's
/// macroItemToFWData/fwDataToMacroItem pair.
/// </summary>
public static class RazerOnboardMacroCodec
{
    private const byte KeyMake = 1;
    private const byte KeyBreak = 2;
    private const byte Button = 8;
    private const byte ScrollWheel = 10;
    private const byte Delay1 = 17;
    private const byte Delay2 = 18;
    private const byte Delay3 = 19;
    private const byte Delay4 = 20;
    private const byte Cursor = 21;

    private const int WheelNotch = 120;

    public static IReadOnlyList<OnboardMacroItem> Decode(ReadOnlySpan<byte> data)
    {
        var items = new List<OnboardMacroItem>();
        var held = 0;
        var offset = 0;
        while (offset < data.Length)
        {
            var opcode = data[offset];
            switch (opcode)
            {
                case KeyMake:
                case KeyBreak:
                {
                    EnsureAvailable(data, offset, 2);
                    items.Add(DecodeKey(data[offset + 1], opcode == KeyBreak));
                    offset += 2;
                    break;
                }

                case Button:
                {
                    EnsureAvailable(data, offset, 2);
                    var mask = data[offset + 1];
                    AppendButtonChanges(items, ref held, mask);
                    offset += 2;
                    break;
                }

                case ScrollWheel:
                {
                    EnsureAvailable(data, offset, 2);
                    var delta = data[offset + 1] switch
                    {
                        1 => WheelNotch,
                        255 => -WheelNotch,
                        _ => throw new InvalidDataException(
                            $"Onboard macro wheel byte 0x{data[offset + 1]:X2} is not a supported notch."),
                    };
                    items.Add(new OnboardMacroItem(OnboardMacroItemKind.Wheel) { WheelDelta = delta });
                    offset += 2;
                    break;
                }

                case Delay1:
                case Delay2:
                case Delay3:
                case Delay4:
                {
                    var size = opcode - Delay1 + 2;
                    EnsureAvailable(data, offset, size);
                    var milliseconds = 0;
                    for (var index = 1; index < size; index++)
                    {
                        milliseconds = (milliseconds << 8) | data[offset + index];
                    }

                    items.Add(new OnboardMacroItem(OnboardMacroItemKind.Delay)
                    {
                        DelayMilliseconds = milliseconds,
                    });
                    offset += size;
                    break;
                }

                case Cursor:
                {
                    EnsureAvailable(data, offset, 5);
                    items.Add(new OnboardMacroItem(OnboardMacroItemKind.Cursor)
                    {
                        X = (data[offset + 1] << 8) | data[offset + 2],
                        Y = (data[offset + 3] << 8) | data[offset + 4],
                    });
                    offset += 5;
                    break;
                }

                default:
                    // Power and media frames are skipped by Synapse without a size,
                    // so the stream cannot be walked past them.
                    throw new InvalidDataException(
                        $"Onboard macro opcode 0x{opcode:X2} at offset {offset} is not supported.");
            }
        }

        return items;
    }

    public static byte[] Encode(IReadOnlyList<OnboardMacroItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var bytes = new List<byte>(items.Count * 2);
        var held = 0;
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case OnboardMacroItemKind.Key:
                    bytes.Add(item.Released ? KeyBreak : KeyMake);
                    bytes.Add(ResolveUsage(item.Scancode));
                    break;

                case OnboardMacroItemKind.MouseButton:
                    AppendButtonMask(bytes, ref held, item);
                    break;

                case OnboardMacroItemKind.Wheel:
                    bytes.Add(ScrollWheel);
                    bytes.Add(item.WheelDelta switch
                    {
                        WheelNotch => 1,
                        -WheelNotch => 255,
                        _ => throw new InvalidOperationException(
                            $"Onboard macros carry one wheel notch per item, not {item.WheelDelta}."),
                    });
                    break;

                case OnboardMacroItemKind.Delay:
                    AppendDelay(bytes, item.DelayMilliseconds);
                    break;

                case OnboardMacroItemKind.Cursor:
                    bytes.Add(Cursor);
                    bytes.Add((byte)(item.X >> 8));
                    bytes.Add((byte)item.X);
                    bytes.Add((byte)(item.Y >> 8));
                    bytes.Add((byte)item.Y);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown onboard macro item kind {item.Kind}.");
            }
        }

        return [.. bytes];
    }

    private static OnboardMacroItem DecodeKey(byte usage, bool released)
    {
        var scancode = RazerHidUsageMapping.TryGetScancode(usage, out var value) ? value : usage;
        return new OnboardMacroItem(OnboardMacroItemKind.Key)
        {
            Scancode = scancode,
            Released = released,
        };
    }

    private static void AppendButtonChanges(List<OnboardMacroItem> items, ref int held, byte mask)
    {
        for (var bit = 0; bit < 5; bit++)
        {
            var set = (mask & (1 << bit)) != 0;
            var was = (held & (1 << bit)) != 0;
            if (was && !set)
            {
                items.Add(new OnboardMacroItem(OnboardMacroItemKind.MouseButton)
                {
                    MouseButton = bit + 1,
                    MousePressed = false,
                });
            }
        }

        for (var bit = 0; bit < 5; bit++)
        {
            var set = (mask & (1 << bit)) != 0;
            var was = (held & (1 << bit)) != 0;
            if (set && !was)
            {
                items.Add(new OnboardMacroItem(OnboardMacroItemKind.MouseButton)
                {
                    MouseButton = bit + 1,
                    MousePressed = true,
                });
            }
        }

        held = mask;
    }

    private static void AppendButtonMask(List<byte> bytes, ref int held, OnboardMacroItem item)
    {
        if (item.MouseButton is < 1 or > 5)
        {
            throw new InvalidOperationException(
                $"Onboard macros address five mouse buttons, not button {item.MouseButton}.");
        }

        var bit = 1 << (item.MouseButton - 1);
        held = item.MousePressed ? held | bit : held & ~bit;
        bytes.Add(Button);
        bytes.Add((byte)held);
    }

    private static void AppendDelay(List<byte> bytes, int milliseconds)
    {
        if (milliseconds is < 0 or > 0xFFFFFF)
        {
            throw new InvalidOperationException(
                $"Onboard macro delays stop at 16777215 ms, not {milliseconds}.");
        }

        if (milliseconds < 0x100)
        {
            bytes.Add(Delay1);
            bytes.Add((byte)milliseconds);
        }
        else if (milliseconds < 0x10000)
        {
            bytes.Add(Delay2);
            bytes.Add((byte)(milliseconds >> 8));
            bytes.Add((byte)milliseconds);
        }
        else
        {
            bytes.Add(Delay3);
            bytes.Add((byte)(milliseconds >> 16));
            bytes.Add((byte)(milliseconds >> 8));
            bytes.Add((byte)milliseconds);
        }
    }

    private static byte ResolveUsage(int scancode) =>
        RazerHidUsageMapping.TryGetUsage(scancode, out var usage)
            ? usage
            : throw new InvalidOperationException(
                $"Scancode 0x{scancode:X} has no HID usage in the onboard macro table.");

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int size)
    {
        if (offset + size > data.Length)
        {
            throw new InvalidDataException(
                $"Onboard macro item at offset {offset} needs {size} bytes, only {data.Length - offset} remain.");
        }
    }
}
