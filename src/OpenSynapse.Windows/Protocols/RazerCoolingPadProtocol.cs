namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer Laptop Cooling Pad (1532:0F43) fan control using the generic
/// <c>rzDevice25</c> thermal class.
///
/// This is deliberately separate from the 0F3C Razer PWM PC Fan Controller,
/// which uses the <c>rzDevice25PWMPCFanController</c> 16-bit layout.
/// <list type="bullet">
/// <item>set speed: class 0x0D, command 0x01, dataSize 3,
/// arguments <c>[classId, thermalId, speed]</c></item>
/// <item>get speed: class 0x0D, command 0x81, dataSize 3,
/// arguments <c>[classId, thermalId]</c></item>
/// <item>fan information: class 0x0D, command 0x83, dataSize 12,
/// arguments <c>[classId]</c></item>
/// </list>
/// The speed byte is an 8-bit protocol unit; one unit is 50 RPM.
/// The fan-information pairs are big-endian 16-bit values.
/// </summary>
public static partial class RazerCoolingPadProtocol
{
    public const ushort VendorId = 0x1532;
    public const ushort ProductId = 0x0F43;

    /// <summary>Thermal class used by the generic rzDevice25 fan API.</summary>
    public const byte ClassId = 0x01;

    /// <summary>Thermal id for Fan1.</summary>
    public const byte ThermalId = 0x05;

    /// <summary>RPM represented by one protocol unit (middleware FACTOR=50).</summary>
    public const int RpmPerUnit = 50;

    /// <summary>
    /// Highest speed the pad's own firmware runs. The middleware hard-codes 125 and
    /// 2600 RPM as the fan's min and max, below the 12750 RPM the 8-bit protocol
    /// field could express.
    /// </summary>
    public const int MaximumRpm = 2600;

    /// <summary>
    /// Lowest speed the protocol can express for a moving fan. The middleware's own
    /// lower bound is 125 RPM, which the 50 RPM unit cannot represent exactly, so the
    /// first addressable running speed is 150 RPM.
    /// </summary>
    public const int MinimumRpm = 150;

    /// <summary>Lowest speed in the bundle's smart-curve tables (quiet, 30 C).</summary>
    public const int MinimumCurveRpm = 500;

    /// <summary>Highest speed in the bundle's smart-curve tables (performance).</summary>
    public const int MaximumCurveRpm = 2700;

    private const byte TransactionId = 0x1F;
    private const byte CommandClass = 0x0D;
    private const byte SetSpeedCommandId = 0x01;
    private const byte GetSpeedCommandId = 0x81;
    private const byte GetFanInformationCommandId = 0x83;
    private const byte SetCoolingModeCommandId = 0x10;
    private const byte GetCoolingModeCommandId = 0x90;
    private const byte CoolingModeSourceFirmware = 0x00;
    private const byte CoolingModeSourceSynapse = 0x01;
    private const byte SetSpeedDataSize = 0x03;
    private const byte GetSpeedDataSize = 0x03;
    private const byte GetFanInformationDataSize = 0x0C;
    private const byte CoolingModeDataSize = 0x03;

    public static byte[] CreateGetSpeedRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            GetSpeedDataSize,
            CommandClass,
            GetSpeedCommandId,
            new[] { ClassId, ThermalId });

    public static byte[] CreateSetSpeedRequest(int rpm)
    {
        var units = ToUnits(rpm);
        return RazerFeatureReport.CreateRequest(
            TransactionId,
            SetSpeedDataSize,
            CommandClass,
            SetSpeedCommandId,
            new[] { ClassId, ThermalId, (byte)units });
    }

    /// <summary>
    /// Writes a smart-curve target. The curve tables top out at 2700 RPM, above the
    /// manual slider's 2600 ceiling, so this path validates against the table range
    /// instead of <see cref="ToUnits"/>.
    /// </summary>
    public static byte[] CreateSetCurveSpeedRequest(int rpm)
    {
        if (rpm is < MinimumCurveRpm or > MaximumCurveRpm || rpm % RpmPerUnit != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                rpm,
                $"Cooling pad curve speed must be {MinimumCurveRpm}..{MaximumCurveRpm} RPM in {RpmPerUnit} RPM steps.");
        }

        return RazerFeatureReport.CreateRequest(
            TransactionId,
            SetSpeedDataSize,
            CommandClass,
            SetSpeedCommandId,
            new[] { ClassId, ThermalId, (byte)(rpm / RpmPerUnit) });
    }

    public static byte[] CreateGetFanInformationRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            GetFanInformationDataSize,
            CommandClass,
            GetFanInformationCommandId,
            new[] { ClassId });

    /// <summary>Gets the fixed cooling-mode state the 0F43 bundle writes with 0D:10.</summary>
    public static byte[] CreateGetCoolingModeRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            CoolingModeDataSize,
            CommandClass,
            GetCoolingModeCommandId,
            new[] { ClassId });

    /// <summary>
    /// Selects one of the bundle's fixed presets. The first byte is the bundle's
    /// source flag (1 = Synapse/UI), not the thermal class; the pad stores speed
    /// as 50-RPM units. High is the bundle's 2700 RPM, above the slider's
    /// 2600 RPM ceiling, which stays unchanged until hardware confirms it.
    /// </summary>
    public static byte[] CreateSetCoolingModeRequest(RazerCoolingPadFixedMode mode)
    {
        var source = mode == RazerCoolingPadFixedMode.Off
            ? CoolingModeSourceFirmware
            : CoolingModeSourceSynapse;
        return CreateSetCoolingModeRequest((byte)mode, source, UnitsFor(mode));
    }

    /// <summary>Selects one of the bundle's smart-curve modes without a target speed.</summary>
    public static byte[] CreateSetSmartModeRequest(RazerCoolingPadSmartMode mode) =>
        CreateSetCoolingModeRequest((byte)mode, CoolingModeSourceFirmware, 0);

    public static byte[] CreateSetCoolingModeRequest(byte mode, byte source, byte units)
    {
        if (mode > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Cooling mode must be 0..6.");
        }
        if (source > CoolingModeSourceSynapse)
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Cooling mode source must be 0 or 1.");
        }

        return RazerFeatureReport.CreateRequest(
            TransactionId,
            CoolingModeDataSize,
            CommandClass,
            SetCoolingModeCommandId,
            new[] { source, mode, units });
    }

    public static int RpmFor(RazerCoolingPadFixedMode mode) => mode switch
    {
        RazerCoolingPadFixedMode.Low => 1600,
        RazerCoolingPadFixedMode.Mid => 2200,
        RazerCoolingPadFixedMode.High => 2700,
        RazerCoolingPadFixedMode.Off => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static byte UnitsFor(RazerCoolingPadFixedMode mode) => mode switch
    {
        RazerCoolingPadFixedMode.Low => 32,
        RazerCoolingPadFixedMode.Mid => 44,
        RazerCoolingPadFixedMode.High => 54,
        RazerCoolingPadFixedMode.Off => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Reads the 0D:90 reply: [firmware/synapse source, mode, raw 50-RPM units].</summary>
    public static RazerCoolingPadCoolingMode ParseCoolingMode(ReadOnlySpan<byte> response)
    {
        ValidateResponse(response, CreateGetCoolingModeRequest(), expectedArguments: 3);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        return new RazerCoolingPadCoolingMode(
            arguments[0],
            arguments[1],
            arguments[2],
            arguments[2] * RpmPerUnit);
    }

    /// <summary>
    /// Reads the fan speed byte at data[2]. Unlike 0F3C, this is one byte,
    /// not a big-endian 16-bit value.
    /// </summary>
    public static int ParseSpeedRpm(ReadOnlySpan<byte> response)
    {
        ValidateResponse(response, CreateGetSpeedRequest(), expectedArguments: 3);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != ClassId || arguments[1] != ThermalId)
        {
            throw new InvalidOperationException(
                "Cooling pad returned an unexpected thermal object; expected class 1, Fan1 (thermal id 5).");
        }

        return FromUnits(arguments[2]);
    }

    /// <summary>
    /// Reads the fan-curve fields: thermal sampling, hysteresis, fan update
    /// interval, and maximum delta. The three pairs are big-endian 16-bit values.
    /// </summary>
    public static RazerCoolingPadFanInformation ParseFanInformation(ReadOnlySpan<byte> response)
    {
        ValidateResponse(response, CreateGetFanInformationRequest(), expectedArguments: 8);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != ClassId)
        {
            throw new InvalidOperationException("Cooling pad returned an unexpected thermal class.");
        }

        return new RazerCoolingPadFanInformation(
            ReadBigEndianUInt16(arguments, 1),
            arguments[3],
            ReadBigEndianUInt16(arguments, 4),
            ReadBigEndianUInt16(arguments, 6));
    }

    /// <summary>Converts an RPM target into the protocol's 8-bit 50 RPM units.</summary>
    public static int ToUnits(int rpm)
    {
        if (rpm is < MinimumRpm or > MaximumRpm)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                rpm,
                $"Cooling pad speed must be {MinimumRpm}..{MaximumRpm} RPM.");
        }

        var units = rpm / RpmPerUnit;
        if (units * RpmPerUnit != rpm)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                rpm,
                $"Cooling pad speed must be a multiple of {RpmPerUnit} RPM.");
        }

        return units;
    }

    private static int FromUnits(int units)
    {
        if (units is < 0 or > byte.MaxValue)
        {
            throw new InvalidOperationException(
                $"Cooling pad reported an invalid 8-bit speed unit ({units}).");
        }

        return units * RpmPerUnit;
    }

    private static int ReadBigEndianUInt16(ReadOnlySpan<byte> source, int offset) =>
        (source[offset] << 8) | source[offset + 1];

    private static void ValidateResponse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte expectedArguments)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, expectedArguments))
        {
            throw new InvalidOperationException(
                "Cooling pad returned an invalid or out-of-order feature report.");
        }
    }
}

/// <summary>Fixed cooling-mode presets from the 0F43 bundle.</summary>
public enum RazerCoolingPadFixedMode : byte
{
    Low = 0,
    Mid = 1,
    High = 2,
    Off = 6,
}

/// <summary>Smart-curve modes the 0F43 bundle selects with 0D:10 source 0.</summary>
public enum RazerCoolingPadSmartMode : byte
{
    Quiet = 3,
    Balanced = 4,
    Performance = 5,
}

/// <summary>State returned by 0D:90; ModeId also carries quiet/balanced/performance/off.</summary>
public readonly record struct RazerCoolingPadCoolingMode(
    byte Source,
    byte ModeId,
    byte SpeedUnits,
    int Rpm);

/// <summary>Fan-curve description the cooling pad reports for its thermal fan.</summary>
public readonly record struct RazerCoolingPadFanInformation(
    int ThermalSamplingInterval,
    byte Hysteresis,
    int FanUpdateInterval,
    int FanMaximumDelta);
