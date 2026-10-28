namespace OpenSynapse.Windows.Protocols;

public sealed record RazerProximitySensorConfiguration(
    byte ClassId,
    byte SensorId,
    IReadOnlyList<byte> Parameters);

public readonly record struct RazerProximitySensorThreshold(
    byte ClassId,
    byte SensorId,
    ushort MaxThreshold,
    ushort MinThreshold,
    ushort OverspeedThreshold);

public readonly record struct RazerProximitySensorState(
    byte ClassId,
    byte SensorId,
    byte State);

public readonly record struct RazerProximitySensorLiftSetting(
    byte ClassId,
    byte SensorId,
    byte LiftMode,
    byte LiftHeight);

public readonly record struct RazerProximitySensorControl(
    byte ClassId,
    byte SensorId,
    byte Type,
    byte Stage);

/// <summary>
/// Protocol 25 class 0x0B proximity and lift-off commands from the mouse middleware.
/// The command tuples are get configuration [10,11,133], set configuration [10,11,5],
/// set state [3,11,3], set threshold [8,11,2], set lift setting [4,11,11],
/// get control [4,11,137] and set control [4,11,9].
/// </summary>
public static class RazerProximitySensorProtocol
{
    public const int MaximumConfigurationBytes = 8;

    private const byte CommandClass = 0x0B;
    private const byte GetConfigurationCommand = 0x85;
    private const byte SetConfigurationCommand = 0x05;
    private const byte SetStateCommand = 0x03;
    private const byte SetThresholdCommand = 0x02;
    private const byte SetLiftSettingCommand = 0x0B;
    private const byte GetControlCommand = 0x89;
    private const byte SetControlCommand = 0x09;
    private const int ConfigurationSize = 10;
    private const int StateSize = 3;
    private const int ThresholdSize = 8;
    private const int LiftSettingSize = 4;
    private const int ControlSize = 4;

    public static byte[] CreateGetProximitySensorConfigurationRequest(
        byte transactionId,
        byte classId,
        byte sensorId)
    {
        Span<byte> arguments = stackalloc byte[ConfigurationSize];
        arguments[0] = classId;
        arguments[1] = sensorId;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ConfigurationSize,
            CommandClass,
            GetConfigurationCommand,
            arguments);
    }

    public static byte[] CreateSetProximitySensorConfigurationRequest(
        byte transactionId,
        byte classId,
        byte sensorId,
        ReadOnlySpan<byte> parameters)
    {
        if (parameters.Length > MaximumConfigurationBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                parameters.Length,
                $"Proximity configuration carries at most {MaximumConfigurationBytes} bytes.");
        }

        // The middleware allocates [10,11,5] and copies from offset 2, leaving eight bytes.
        Span<byte> arguments = stackalloc byte[ConfigurationSize];
        arguments[0] = classId;
        arguments[1] = sensorId;
        parameters.CopyTo(arguments[2..]);
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ConfigurationSize,
            CommandClass,
            SetConfigurationCommand,
            arguments);
    }

    public static byte[] CreateSetProximitySensorStateRequest(
        byte transactionId,
        byte classId,
        byte sensorId,
        byte state) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            StateSize,
            CommandClass,
            SetStateCommand,
            new byte[] { classId, sensorId, state });

    public static byte[] CreateSetProximitySensorThresholdRequest(
        byte transactionId,
        byte classId,
        byte sensorId,
        ushort maxThreshold,
        ushort minThreshold,
        ushort overspeedThreshold) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ThresholdSize,
            CommandClass,
            SetThresholdCommand,
            new byte[]
            {
                classId,
                sensorId,
                (byte)(maxThreshold >> 8),
                (byte)maxThreshold,
                (byte)(minThreshold >> 8),
                (byte)minThreshold,
                (byte)(overspeedThreshold >> 8),
                (byte)overspeedThreshold,
            });

    public static byte[] CreateSetProximitySensorLiftSettingRequest(
        byte transactionId,
        byte classId,
        byte sensorId,
        byte liftMode,
        byte liftHeight) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            LiftSettingSize,
            CommandClass,
            SetLiftSettingCommand,
            new byte[] { classId, sensorId, liftMode, liftHeight });

    public static byte[] CreateGetProximitySensorControlRequest(
        byte transactionId,
        byte classId,
        byte sensorId)
    {
        Span<byte> arguments = stackalloc byte[ControlSize];
        arguments[0] = classId;
        arguments[1] = sensorId;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ControlSize,
            CommandClass,
            GetControlCommand,
            arguments);
    }

    public static byte[] CreateSetProximitySensorControlRequest(
        byte transactionId,
        byte classId,
        byte sensorId,
        byte type,
        byte stage) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ControlSize,
            CommandClass,
            SetControlCommand,
            new byte[] { classId, sensorId, type, stage });

    public static RazerProximitySensorConfiguration ParseConfiguration(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Payload(response, request, 11);
        return new RazerProximitySensorConfiguration(
            arguments[0],
            arguments[1],
            arguments[2..11].ToArray());
    }

    public static RazerProximitySensorState ParseState(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Payload(response, request, StateSize);
        return new RazerProximitySensorState(arguments[0], arguments[1], arguments[2]);
    }

    public static RazerProximitySensorThreshold ParseThreshold(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Payload(response, request, ThresholdSize);
        return new RazerProximitySensorThreshold(
            arguments[0],
            arguments[1],
            ReadBigEndianUInt16(arguments, 2),
            ReadBigEndianUInt16(arguments, 4),
            ReadBigEndianUInt16(arguments, 6));
    }

    public static RazerProximitySensorLiftSetting ParseLiftSetting(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Payload(response, request, LiftSettingSize);
        return new RazerProximitySensorLiftSetting(
            arguments[0],
            arguments[1],
            arguments[2],
            arguments[3]);
    }

    public static RazerProximitySensorControl ParseControl(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Payload(response, request, ControlSize);
        return new RazerProximitySensorControl(
            arguments[0],
            arguments[1],
            arguments[2],
            arguments[3]);
    }

    private static ushort ReadBigEndianUInt16(ReadOnlySpan<byte> source, int offset) =>
        (ushort)((source[offset] << 8) | source[offset + 1]);

    private static ReadOnlySpan<byte> Payload(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments)
    {
        if (!RazerFeatureReport.Matches(request, response) ||
            !RazerFeatureReport.IsAcceptedStatus(response[1]) ||
            response[6] < minimumArguments ||
            response[6] > 80)
        {
            throw new InvalidOperationException("Razer proximity sensor returned an invalid or unsuccessful feature report.");
        }

        return response.Slice(RazerFeatureReport.ArgumentsOffset, response[6]);
    }
}
