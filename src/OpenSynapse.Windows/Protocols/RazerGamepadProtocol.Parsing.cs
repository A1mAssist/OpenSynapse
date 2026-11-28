namespace OpenSynapse.Windows.Protocols;

public static partial class RazerGamepadProtocol
{
    public static IReadOnlyList<byte> ReadButtonIdList(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response, 1);
        var total = data[0];
        if (total > 80 || data.Length < total + 1)
        {
            throw new InvalidDataException("The gamepad button-id reply is truncated.");
        }

        return data.Slice(1, total).ToArray();
    }

    public static RazerGamepadButtonAssignment ReadButtonAssignment(ReadOnlySpan<byte> response,
        byte expectedProfileId, byte expectedButtonId, RazerGamepadButtonMode expectedMode)
    {
        var data = ReadData(response);
        if (data.Length < 10 || data[0] != expectedProfileId || data[1] != expectedButtonId || data[2] != (byte)expectedMode)
        {
            throw new InvalidDataException(
                "The gamepad button assignment does not match the requested profile, button and mode.");
        }

        var size = data[4];
        if (size > 5)
        {
            throw new InvalidDataException("The gamepad button-assignment data size is out of range.");
        }

        return new(
            data[1],
            (RazerGamepadButtonMode)data[2],
            (RazerGamepadFunction)data[3],
            size,
            data.Slice(5, 5).ToArray());
    }

    public static byte ReadSensitivity(ReadOnlySpan<byte> response, byte controlId, byte expectedProfileId) =>
        ReadValue(response, controlId, expectedProfileId)[2];

    public static byte ReadVibrationScale(ReadOnlySpan<byte> response, byte controlId, byte expectedProfileId) =>
        ReadValue(response, controlId, expectedProfileId)[2];

    public static byte ReadTriggerMode(ReadOnlySpan<byte> response, byte controlId, byte expectedProfileId) =>
        ReadValue(response, controlId, expectedProfileId)[2];

    public static (byte Start, byte End) ReadTriggerRange(ReadOnlySpan<byte> response, byte controlId, byte expectedProfileId)
    {
        var value = ReadValue(response, controlId, expectedProfileId, 4);
        return (value[2], value[3]);
    }

    public static IReadOnlyList<byte> ReadControlIdList(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response, 1);
        var total = data[0];
        if (total > 80 || data.Length < total + 1)
        {
            throw new InvalidDataException("The gamepad control-id reply is truncated.");
        }

        return data.Slice(1, total).ToArray();
    }

    public static RazerGamepadAnalogControl ReadAnalogControl(
        ReadOnlySpan<byte> response,
        byte controlId, byte expectedProfileId)
    {
        var value = ReadValue(response, controlId, expectedProfileId);
        if (value.Length < 4)
        {
            throw new InvalidDataException("The gamepad analog-control reply is truncated.");
        }

        return new(
            value[0],
            value[1],
            (RazerGamepadAnalogType)value[2],
            value[3]);
    }

    public static ushort ReadAdcMask(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        if (data.Length < 2)
        {
            throw new InvalidDataException("The gamepad ADC-mask reply is truncated.");
        }

        return ReadUInt16(data, 0);
    }

    public static byte ReadCircularity(ReadOnlySpan<byte> response, byte controlId, byte expectedProfileId) =>
        ReadValue(response, controlId, expectedProfileId)[2];

    public static RazerGamepadDigitalTrigger ReadDigitalTrigger(
        ReadOnlySpan<byte> response,
        byte controlId, byte expectedProfileId)
    {
        var value = ReadValue(response, controlId, expectedProfileId);
        if (value.Length < 4)
        {
            throw new InvalidDataException("The gamepad digital-trigger reply is truncated.");
        }

        return new(value[2], value[3] != 0);
    }

    public static byte ReadIgnoreDPadDiagonals(ReadOnlySpan<byte> response, byte expectedProfileId) => ReadOption(response, expectedProfileId);

    public static RazerGamepadSocdMode ReadSocdMode(ReadOnlySpan<byte> response, byte expectedProfileId) =>
        (RazerGamepadSocdMode)ReadOption(response, expectedProfileId);

    public static byte ReadControllerModeSwitch(ReadOnlySpan<byte> response, byte expectedProfileId) => ReadOption(response, expectedProfileId);

    public static byte ReadActiveProfileId(ReadOnlySpan<byte> response) => ReadData(response, 1)[0];

    public static RazerGamepadRawInput ReadRawInput(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        if (data.Length < 12)
        {
            throw new InvalidDataException("The gamepad raw-input reply is truncated.");
        }

        return new(
            ReadUInt16(data, 0),
            ReadUInt16(data, 2),
            ReadUInt16(data, 4),
            ReadUInt16(data, 6),
            ReadUInt16(data, 8),
            ReadUInt16(data, 10));
    }

    public static RazerGamepadCalibration ReadCalibration(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        if (data.Length < 33)
        {
            throw new InvalidDataException("The gamepad calibration reply is truncated.");
        }

        var values = new ushort[16];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ReadUInt16(data, 1 + index * 2);
        }

        return new(data[0], values);
    }

    public static RazerGamepadZones ReadZones(ReadOnlySpan<byte> response, byte controlId, byte expectedProfileId)
    {
        var value = ReadValue(response, controlId, expectedProfileId, 9);
        return new RazerGamepadZones(
            ReadUInt16(value, 2),
            ReadUInt16(value, 4),
            ReadUInt16(value, 6),
            value[8] != 0);
    }

    private static ReadOnlySpan<byte> ReadValue(ReadOnlySpan<byte> response, byte controlId,
        byte expectedProfileId, int minimumLength = 3)
    {
        var value = ReadData(response);
        if (value.Length < minimumLength || value[0] != expectedProfileId || value[1] != controlId)
        {
            throw new InvalidDataException(
                $"The gamepad reply is truncated or does not match profile 0x{expectedProfileId:X2} control 0x{controlId:X2}.");
        }

        return value;
    }

    private static ReadOnlySpan<byte> ReadData(ReadOnlySpan<byte> response, int minimumLength = 0)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new ArgumentException(
                $"Razer feature reports must be {RazerFeatureReport.Length} bytes.",
                nameof(response));
        }

        var length = response[RazerFeatureReport.DataSizeOffset];
        if (length < minimumLength || length > 80)
            throw new InvalidDataException("The gamepad reply has an invalid payload length.");
        return response.Slice(RazerFeatureReport.ArgumentsOffset, length);
    }

    private static byte ReadOption(ReadOnlySpan<byte> response, byte expectedProfileId)
    {
        var value = ReadData(response);
        if (value.Length < 2 || value[0] != expectedProfileId)
        {
            throw new InvalidDataException(
                $"The gamepad option reply is truncated or does not match profile 0x{expectedProfileId:X2}.");
        }

        return value[1];
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);
}
