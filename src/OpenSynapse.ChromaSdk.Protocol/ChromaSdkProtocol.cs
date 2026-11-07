using System.Buffers.Binary;
using System.Text;

namespace OpenSynapse.ChromaSdk.Protocol;

public enum ChromaSdkDeviceType : byte
{
    Keyboard = 1,
    Mouse = 2,
    Headset = 3,
    Mousepad = 4,
    Keypad = 5,
    ChromaLink = 6,
}

public enum ChromaSdkEffectKind : byte
{
    None = 0,
    Static = 1,
    Custom = 2,
    CustomKey = 3,
    Custom2 = 4,
}

public enum ChromaSdkMessageType : byte
{
    Hello = 1,
    HelloAck = 2,
    SetEffect = 3,
    Clear = 4,
    Bye = 5,
    Query = 6,
    QueryAck = 7,
}

public static class ChromaSdkDeviceMask
{
    public const uint Keyboard = 1u << 0;
    public const uint Mouse = 1u << 1;
    public const uint Headset = 1u << 2;
    public const uint Mousepad = 1u << 3;
    public const uint Keypad = 1u << 4;
    public const uint ChromaLink = 1u << 5;

    public static uint From(ChromaSdkDeviceType type) => type switch
    {
        ChromaSdkDeviceType.Keyboard => Keyboard,
        ChromaSdkDeviceType.Mouse => Mouse,
        ChromaSdkDeviceType.Headset => Headset,
        ChromaSdkDeviceType.Mousepad => Mousepad,
        ChromaSdkDeviceType.Keypad => Keypad,
        ChromaSdkDeviceType.ChromaLink => ChromaLink,
        _ => 0,
    };
}

public sealed record ChromaSdkEffect(
    ChromaSdkDeviceType DeviceType,
    ChromaSdkEffectKind Kind,
    int Width,
    int Height,
    uint[] Colors,
    uint[]? Keys = null)
{
    public const int MaxPixels = 30 * 30;

    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || Width > 30 || Height > 30 ||
            Colors.Length != Width * Height || Colors.Length > MaxPixels)
        {
            throw new InvalidDataException("Invalid Chroma SDK effect dimensions.");
        }
        if (Kind == ChromaSdkEffectKind.CustomKey &&
            (Keys is null || Keys.Length != Colors.Length))
        {
            throw new InvalidDataException("A custom-key effect needs one key mask per pixel.");
        }
        if (Kind != ChromaSdkEffectKind.CustomKey && Keys is not null)
        {
            throw new InvalidDataException("Only custom-key effects carry key masks.");
        }
    }
}

public sealed record ChromaSdkHello(int ProtocolVersion, int ProcessId, string Title, uint SupportedDevices);
public sealed record ChromaSdkHelloAck(int Result, uint SupportedDevices);
public sealed record ChromaSdkQueryAck(uint SupportedDevices);

public abstract record ChromaSdkMessage;
public sealed record ChromaSdkHelloMessage(ChromaSdkHello Value) : ChromaSdkMessage;
public sealed record ChromaSdkHelloAckMessage(ChromaSdkHelloAck Value) : ChromaSdkMessage;
public sealed record ChromaSdkSetEffectMessage(ChromaSdkEffect Value) : ChromaSdkMessage;
public sealed record ChromaSdkClearMessage(ChromaSdkDeviceType DeviceType) : ChromaSdkMessage;
public sealed record ChromaSdkByeMessage : ChromaSdkMessage;
public sealed record ChromaSdkQueryMessage : ChromaSdkMessage;
public sealed record ChromaSdkQueryAckMessage(ChromaSdkQueryAck Value) : ChromaSdkMessage;

public static class ChromaSdkPipe
{
    public const string Name = "OpenSynapseChromaSdk";
    public const int ProtocolVersion = 1;
    public const int MaxPayload = 64 * 1024;

    public static void Write(Stream stream, ChromaSdkMessage message)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            switch (message)
            {
                case ChromaSdkHelloMessage hello:
                    writer.Write((byte)ChromaSdkMessageType.Hello);
                    writer.Write(hello.Value.ProtocolVersion);
                    writer.Write(hello.Value.ProcessId);
                    writer.Write(hello.Value.Title);
                    writer.Write(hello.Value.SupportedDevices);
                    break;
                case ChromaSdkHelloAckMessage ack:
                    writer.Write((byte)ChromaSdkMessageType.HelloAck);
                    writer.Write(ack.Value.Result);
                    writer.Write(ack.Value.SupportedDevices);
                    break;
                case ChromaSdkSetEffectMessage set:
                    set.Value.Validate();
                    writer.Write((byte)ChromaSdkMessageType.SetEffect);
                    writer.Write((byte)set.Value.DeviceType);
                    writer.Write((byte)set.Value.Kind);
                    writer.Write((ushort)set.Value.Width);
                    writer.Write((ushort)set.Value.Height);
                    writer.Write(set.Value.Colors.Length);
                    foreach (var color in set.Value.Colors) writer.Write(color);
                    writer.Write(set.Value.Keys?.Length ?? 0);
                    if (set.Value.Keys is not null)
                    {
                        foreach (var key in set.Value.Keys) writer.Write(key);
                    }
                    break;
                case ChromaSdkClearMessage clear:
                    writer.Write((byte)ChromaSdkMessageType.Clear);
                    writer.Write((byte)clear.DeviceType);
                    break;
                case ChromaSdkByeMessage:
                    writer.Write((byte)ChromaSdkMessageType.Bye);
                    break;
                case ChromaSdkQueryMessage:
                    writer.Write((byte)ChromaSdkMessageType.Query);
                    break;
                case ChromaSdkQueryAckMessage query:
                    writer.Write((byte)ChromaSdkMessageType.QueryAck);
                    writer.Write(query.Value.SupportedDevices);
                    break;
                default:
                    throw new InvalidDataException("Unknown Chroma SDK message.");
            }
        }

        if (payload.Length > MaxPayload)
        {
            throw new InvalidDataException("Chroma SDK message is too large.");
        }

        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, (int)payload.Length);
        stream.Write(length);
        payload.Position = 0;
        payload.CopyTo(stream);
        stream.Flush();
    }

    public static ChromaSdkMessage Read(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> lengthBytes = stackalloc byte[4];
        ReadExactly(stream, lengthBytes, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > MaxPayload)
        {
            throw new InvalidDataException("Invalid Chroma SDK frame length.");
        }

        var payload = new byte[length];
        ReadExactly(stream, payload, cancellationToken);
        using var reader = new BinaryReader(new MemoryStream(payload, writable: false), Encoding.UTF8, leaveOpen: false);
        var type = (ChromaSdkMessageType)reader.ReadByte();
        return type switch
        {
            ChromaSdkMessageType.Hello => new ChromaSdkHelloMessage(new ChromaSdkHello(
                reader.ReadInt32(), reader.ReadInt32(), ReadString(reader, 256), reader.ReadUInt32())),
            ChromaSdkMessageType.HelloAck => new ChromaSdkHelloAckMessage(new ChromaSdkHelloAck(
                reader.ReadInt32(), reader.ReadUInt32())),
            ChromaSdkMessageType.SetEffect => ReadSetEffect(reader),
            ChromaSdkMessageType.Clear => new ChromaSdkClearMessage((ChromaSdkDeviceType)reader.ReadByte()),
            ChromaSdkMessageType.Bye => new ChromaSdkByeMessage(),
            ChromaSdkMessageType.Query => new ChromaSdkQueryMessage(),
            ChromaSdkMessageType.QueryAck => new ChromaSdkQueryAckMessage(
                new ChromaSdkQueryAck(reader.ReadUInt32())),
            _ => throw new InvalidDataException("Unknown Chroma SDK message type."),
        };
    }

    private static ChromaSdkSetEffectMessage ReadSetEffect(BinaryReader reader)
    {
        var deviceType = (ChromaSdkDeviceType)reader.ReadByte();
        var kind = (ChromaSdkEffectKind)reader.ReadByte();
        var width = reader.ReadUInt16();
        var height = reader.ReadUInt16();
        var colorCount = reader.ReadInt32();
        if (colorCount < 0 || colorCount > ChromaSdkEffect.MaxPixels) throw new InvalidDataException();
        var colors = new uint[colorCount];
        for (var index = 0; index < colorCount; index++) colors[index] = reader.ReadUInt32();
        var keyCount = reader.ReadInt32();
        if (keyCount < 0 || keyCount > ChromaSdkEffect.MaxPixels) throw new InvalidDataException();
        var keys = keyCount == 0 ? null : new uint[keyCount];
        if (keys is not null)
        {
            for (var index = 0; index < keyCount; index++) keys[index] = reader.ReadUInt32();
        }
        var effect = new ChromaSdkEffect(deviceType, kind, width, height, colors, keys);
        effect.Validate();
        return new ChromaSdkSetEffectMessage(effect);
    }

    private static string ReadString(BinaryReader reader, int maxLength)
    {
        var value = reader.ReadString();
        if (value.Length > maxLength) throw new InvalidDataException("String is too long.");
        return value;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer[offset..]);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        if (offset != buffer.Length) throw new EndOfStreamException();
    }
}

public static class ChromaBrokerProtocol
{
    public const string PipeName = "OpenSynapseChromaBroker";
}
