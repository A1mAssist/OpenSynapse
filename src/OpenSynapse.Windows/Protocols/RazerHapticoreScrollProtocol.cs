using System.Buffers.Binary;

namespace OpenSynapse.Windows.Protocols;

public enum RazerHapticoreScrollTickMode : byte
{
    Mode2 = 1,
    Mode4 = 3,
}

public enum RazerHapticoreScrollTableClass : byte
{
    CwTable = 1,
    CcwTable = 2,
    VelocityFactorTable = 3,
}

public enum RazerHapticoreScrollStageType : byte
{
    Standard = 129,
    Distinct = 130,
    UltraFine = 131,
    Adaptive = 132,
    FreeScroll = 133,
    Custom = 134,
}

public readonly record struct RazerHapticoreScrollProfileStageState(
    byte ProfileId,
    byte Stage);

public readonly record struct RazerHapticoreScrollProfileMaxStageState(
    byte ProfileId,
    byte MaxStage);

public readonly record struct RazerHapticoreScrollTickSettingState(
    byte ProfileId,
    byte Stage,
    bool Enabled,
    RazerHapticoreScrollTickMode Mode,
    float TickAngle,
    float TickCurrent,
    float VelocityFactor,
    float IndexWindowAngle,
    float WindowAngle);

public readonly record struct RazerHapticoreScrollGeneratorSettingState(
    byte ProfileId,
    byte Stage,
    bool Enabled,
    byte AlternateCurrent,
    float ReductionAngleCw,
    float IndexCountAngleCw,
    float ReductionAngleCcw,
    float IndexCountAngleCcw,
    ushort CurrentTableSizeCw,
    ushort CurrentTableSizeCcw,
    ushort VelocityFactorTableSize);

public readonly record struct RazerHapticoreScrollGeneratorPoint(float X, float Y);

public sealed record RazerHapticoreScrollGeneratorTableState(
    byte ProfileId,
    byte Stage,
    RazerHapticoreScrollTableClass TableClass,
    ushort PointOffset,
    ushort PointSize,
    IReadOnlyList<RazerHapticoreScrollGeneratorPoint> Points);

public readonly record struct RazerHapticoreScrollTableMaxPointCountState(
    ushort CwTableMaxPoint,
    ushort CcwTableMaxPoint,
    ushort VelocityFactorTableMaxPoint);

public readonly record struct RazerHapticoreScrollLibraryVersionState(byte Major, byte Minor);

public sealed record RazerHapticoreScrollStageInformationState(
    byte ProfileId,
    byte ActiveStage,
    byte MaxStage,
    IReadOnlyList<RazerHapticoreScrollStageType> StageTypes);

public readonly record struct RazerHapticoreScrollBrowserModeState(bool Enabled);

public readonly record struct RazerHapticoreScrollControlPoint(byte X, byte Y);

public sealed record RazerHapticoreScrollDirection(
    IReadOnlyList<RazerHapticoreScrollControlPoint> ControlPoints);

public sealed record RazerHapticoreScrollGeneratorCustomMetaDataState(
    byte ProfileId,
    byte Size,
    byte Order,
    bool IsActive,
    bool IsLink,
    byte UpScrollSteps,
    byte UpScrollTension,
    byte DownScrollSteps,
    byte DownScrollTension,
    RazerHapticoreScrollDirection UpDirection,
    RazerHapticoreScrollDirection DownDirection);

/// <summary>
/// Protocol 25 class 0x15 Hapticore scroll commands from module 20877 in the
/// shared 9601 bundle. Each tuple in that module is [dataSize, 0x15, commandId].
/// Floats are little-endian, 16-bit table values are big-endian, and every
/// request is zero-filled to its declared data size.
/// </summary>
public static class RazerHapticoreScrollProtocol
{
    public const byte CommandClass = 0x15;
    public const int MaxProfileId = 5;
    public const int MaxStage = 6;
    public const int MaxGeneratorTablePoints = 9;

    private const byte StateSize = 2;
    private const byte GroupSize = 80;
    private const byte MaxPointCountSize = 6;
    private const byte LibraryVersionSize = 2;
    private const byte StageInformationSize = 9;
    private const byte BrowserModeSize = 1;
    private const byte CustomMetaDataSize = 40;
    private const int DirectionControlPointCount = 5;

    private const byte GetActiveStageCommandId = 0x80;
    private const byte SetActiveStageCommandId = 0x00;
    private const byte GetMaxStageCommandId = 0x81;
    private const byte SetMaxStageCommandId = 0x01;
    private const byte GetTickSettingCommandId = 0x82;
    private const byte SetTickSettingCommandId = 0x02;
    private const byte GetGeneratorSettingCommandId = 0x83;
    private const byte SetGeneratorSettingCommandId = 0x03;
    private const byte GetGeneratorTableCommandId = 0x84;
    private const byte SetGeneratorTableCommandId = 0x04;
    private const byte GetGeneratorTableMaxPointCountCommandId = 0x85;
    private const byte GetLibraryVersionCommandId = 0x86;
    private const byte GetStageInformationCommandId = 0x87;
    private const byte SetStageInformationCommandId = 0x07;
    private const byte GetBrowserModeCommandId = 0x88;
    private const byte SetBrowserModeCommandId = 0x08;
    private const byte GetGeneratorCustomMetaDataCommandId = 0x89;
    private const byte SetGeneratorCustomMetaDataCommandId = 0x09;
    private const byte SetStageResetCommandId = 0x0A;

    public static byte[] CreateGetActiveStageRequest(byte transactionId, byte profileId)
    {
        ValidateProfile(profileId);
        return Create(transactionId, StateSize, GetActiveStageCommandId, profileId);
    }

    public static byte[] CreateSetActiveStageRequest(
        byte transactionId,
        byte profileId,
        byte stage)
    {
        ValidateProfile(profileId);
        ValidateStage(stage);
        return Create(transactionId, StateSize, SetActiveStageCommandId, profileId, stage);
    }

    public static byte[] CreateGetMaxStageRequest(byte transactionId, byte profileId)
    {
        ValidateProfile(profileId);
        return Create(transactionId, StateSize, GetMaxStageCommandId, profileId);
    }

    public static byte[] CreateSetMaxStageRequest(
        byte transactionId,
        byte profileId,
        byte maxStage)
    {
        ValidateProfile(profileId);
        return Create(transactionId, StateSize, SetMaxStageCommandId, profileId, maxStage);
    }

    public static byte[] CreateGetTickSettingRequest(
        byte transactionId,
        byte profileId,
        byte stage)
    {
        ValidateProfile(profileId);
        ValidateStage(stage);
        return Create(transactionId, GroupSize, GetTickSettingCommandId, profileId, stage);
    }

    public static byte[] CreateSetTickSettingRequest(
        byte transactionId,
        byte profileId,
        byte stage,
        bool enabled,
        RazerHapticoreScrollTickMode mode,
        float tickAngle,
        float tickCurrent,
        float velocityFactor,
        float indexWindowAngle,
        float windowAngle)
    {
        ValidateProfile(profileId);
        ValidateStage(stage);

        var arguments = new byte[24];
        arguments[0] = profileId;
        arguments[1] = stage;
        arguments[2] = enabled ? (byte)0x01 : (byte)0x00;
        arguments[3] = (byte)mode;
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(4), tickAngle);
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(8), tickCurrent);
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(12), velocityFactor);
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(16), indexWindowAngle);
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(20), windowAngle);

        return Create(transactionId, GroupSize, SetTickSettingCommandId, arguments);
    }

    public static byte[] CreateGetGeneratorSettingRequest(
        byte transactionId,
        byte profileId,
        byte stage)
    {
        ValidateProfile(profileId);
        ValidateStage(stage);
        return Create(transactionId, GroupSize, GetGeneratorSettingCommandId, profileId, stage);
    }

    public static byte[] CreateSetGeneratorSettingRequest(
        byte transactionId,
        byte profileId,
        byte stage,
        bool enabled,
        byte alternateCurrent,
        float reductionAngleCw,
        float indexCountAngleCw,
        float reductionAngleCcw,
        float indexCountAngleCcw,
        ushort currentTableSizeCw,
        ushort currentTableSizeCcw,
        ushort velocityFactorTableSize)
    {
        ValidateProfile(profileId);
        ValidateStage(stage);

        var arguments = new byte[26];
        arguments[0] = profileId;
        arguments[1] = stage;
        arguments[2] = enabled ? (byte)0x01 : (byte)0x00;
        arguments[3] = alternateCurrent;
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(4), reductionAngleCw);
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(8), indexCountAngleCw);
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(12), reductionAngleCcw);
        BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(16), indexCountAngleCcw);
        BinaryPrimitives.WriteUInt16BigEndian(arguments.AsSpan(20), currentTableSizeCw);
        BinaryPrimitives.WriteUInt16BigEndian(arguments.AsSpan(22), currentTableSizeCcw);
        BinaryPrimitives.WriteUInt16BigEndian(arguments.AsSpan(24), velocityFactorTableSize);

        return Create(transactionId, GroupSize, SetGeneratorSettingCommandId, arguments);
    }

    public static byte[] CreateGetGeneratorTableRequest(
        byte transactionId,
        byte profileId,
        byte stage,
        RazerHapticoreScrollTableClass tableClass,
        ushort pointOffset,
        ushort pointSize)
    {
        ValidateProfile(profileId);
        ValidateStage(stage);
        ValidateTableClass(tableClass);

        var arguments = new byte[7];
        arguments[0] = profileId;
        arguments[1] = stage;
        arguments[2] = (byte)tableClass;
        BinaryPrimitives.WriteUInt16BigEndian(arguments.AsSpan(3), pointOffset);
        BinaryPrimitives.WriteUInt16BigEndian(arguments.AsSpan(5), pointSize);

        return Create(transactionId, GroupSize, GetGeneratorTableCommandId, arguments);
    }

    public static byte[] CreateSetGeneratorTableRequest(
        byte transactionId,
        byte profileId,
        byte stage,
        RazerHapticoreScrollTableClass tableClass,
        ushort pointOffset,
        IReadOnlyList<RazerHapticoreScrollGeneratorPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        ValidateProfile(profileId);
        ValidateStage(stage);
        ValidateTableClass(tableClass);
        if (points.Count > MaxGeneratorTablePoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(points),
                points.Count,
                $"At most {MaxGeneratorTablePoints} generator table points fit.");
        }

        var arguments = new byte[7 + (points.Count * 8)];
        arguments[0] = profileId;
        arguments[1] = stage;
        arguments[2] = (byte)tableClass;
        BinaryPrimitives.WriteUInt16BigEndian(arguments.AsSpan(3), pointOffset);
        BinaryPrimitives.WriteUInt16BigEndian(arguments.AsSpan(5), (ushort)points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var offset = 7 + (index * 8);
            BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(offset), point.X);
            BinaryPrimitives.WriteSingleLittleEndian(arguments.AsSpan(offset + 4), point.Y);
        }

        return Create(transactionId, GroupSize, SetGeneratorTableCommandId, arguments);
    }

    public static byte[] CreateGetGeneratorTableMaxPointCountRequest(byte transactionId) =>
        Create(transactionId, MaxPointCountSize, GetGeneratorTableMaxPointCountCommandId);

    public static byte[] CreateGetLibraryVersionRequest(byte transactionId) =>
        Create(transactionId, LibraryVersionSize, GetLibraryVersionCommandId);

    public static byte[] CreateGetStageInformationRequest(byte transactionId, byte profileId)
    {
        ValidateProfile(profileId);
        return Create(transactionId, StageInformationSize, GetStageInformationCommandId, profileId);
    }

    public static byte[] CreateSetStageInformationRequest(
        byte transactionId,
        byte profileId,
        byte activeStage,
        byte maxStage,
        IReadOnlyList<RazerHapticoreScrollStageType> stageTypes)
    {
        ArgumentNullException.ThrowIfNull(stageTypes);
        ValidateProfile(profileId);
        ValidateStage(activeStage);
        if (stageTypes.Count > MaxStage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageTypes),
                stageTypes.Count,
                $"At most {MaxStage} stage types fit.");
        }

        var arguments = new byte[StageInformationSize];
        arguments[0] = profileId;
        arguments[1] = activeStage;
        arguments[2] = maxStage;
        for (var index = 0; index < stageTypes.Count; index++)
        {
            arguments[3 + index] = (byte)stageTypes[index];
        }

        return Create(transactionId, StageInformationSize, SetStageInformationCommandId, arguments);
    }

    // The bundle omits the payload for this getter; the tuple still declares one byte.
    public static byte[] CreateGetBrowserModeRequest(byte transactionId) =>
        Create(transactionId, BrowserModeSize, GetBrowserModeCommandId, 0x00);

    public static byte[] CreateSetBrowserModeRequest(byte transactionId, bool enabled) =>
        Create(
            transactionId,
            BrowserModeSize,
            SetBrowserModeCommandId,
            enabled ? (byte)0x01 : (byte)0x00);

    public static byte[] CreateGetGeneratorCustomMetaDataRequest(
        byte transactionId,
        byte profileId) =>
        Create(transactionId, GroupSize, GetGeneratorCustomMetaDataCommandId, profileId);

    public static byte[] CreateSetGeneratorCustomMetaDataRequest(
        byte transactionId,
        byte profileId,
        byte order,
        bool isActive,
        bool isLink,
        byte upScrollSteps,
        byte upScrollTension,
        byte downScrollSteps,
        byte downScrollTension,
        RazerHapticoreScrollDirection upDirection,
        RazerHapticoreScrollDirection downDirection)
    {
        ValidateDirection(upDirection, nameof(upDirection));
        ValidateDirection(downDirection, nameof(downDirection));

        var arguments = new byte[CustomMetaDataSize];
        arguments[0] = profileId;
        arguments[1] = CustomMetaDataSize;
        arguments[2] = order;
        arguments[3] = isActive ? (byte)0x01 : (byte)0x00;
        arguments[4] = isLink ? (byte)0x01 : (byte)0x00;
        arguments[5] = upScrollSteps;
        arguments[6] = upScrollTension;
        arguments[7] = downScrollSteps;
        arguments[8] = downScrollTension;
        WriteDirection(arguments, 9, upDirection);
        WriteDirection(arguments, 21, downDirection);

        return Create(transactionId, GroupSize, SetGeneratorCustomMetaDataCommandId, arguments);
    }

    public static byte[] CreateSetStageResetRequest(
        byte transactionId,
        byte profileId,
        byte activeStage) =>
        Create(transactionId, StateSize, SetStageResetCommandId, profileId, activeStage);

    public static RazerHapticoreScrollProfileStageState ParseActiveStage(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, StateSize, "Hapticore scroll active stage");
        return new RazerHapticoreScrollProfileStageState(arguments[0], arguments[1]);
    }

    public static RazerHapticoreScrollProfileMaxStageState ParseMaxStage(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, StateSize, "Hapticore scroll max stage");
        return new RazerHapticoreScrollProfileMaxStageState(arguments[0], arguments[1]);
    }

    public static RazerHapticoreScrollTickSettingState ParseTickSetting(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, 24, "Hapticore scroll tick setting");
        return new RazerHapticoreScrollTickSettingState(
            arguments[0],
            arguments[1],
            arguments[2] != 0,
            (RazerHapticoreScrollTickMode)arguments[3],
            ReadSingle(arguments, 4),
            ReadSingle(arguments, 8),
            ReadSingle(arguments, 12),
            ReadSingle(arguments, 16),
            ReadSingle(arguments, 20));
    }

    public static RazerHapticoreScrollGeneratorSettingState ParseGeneratorSetting(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, 26, "Hapticore scroll generator setting");
        return new RazerHapticoreScrollGeneratorSettingState(
            arguments[0],
            arguments[1],
            arguments[2] != 0,
            arguments[3],
            ReadSingle(arguments, 4),
            ReadSingle(arguments, 8),
            ReadSingle(arguments, 12),
            ReadSingle(arguments, 16),
            ReadUInt16BigEndian(arguments, 20),
            ReadUInt16BigEndian(arguments, 22),
            ReadUInt16BigEndian(arguments, 24));
    }

    public static RazerHapticoreScrollGeneratorTableState ParseGeneratorTable(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, 7, "Hapticore scroll generator table");
        var pointSize = ReadUInt16BigEndian(arguments, 5);
        var requiredSize = 7 + (pointSize * 8);
        if (requiredSize > arguments.Length)
        {
            throw new InvalidOperationException(
                $"Hapticore scroll generator table declares {pointSize} points but only {arguments.Length} data bytes.");
        }

        var points = new RazerHapticoreScrollGeneratorPoint[pointSize];
        for (var index = 0; index < points.Length; index++)
        {
            var offset = 7 + (index * 8);
            points[index] = new RazerHapticoreScrollGeneratorPoint(
                ReadSingle(arguments, offset),
                ReadSingle(arguments, offset + 4));
        }

        return new RazerHapticoreScrollGeneratorTableState(
            arguments[0],
            arguments[1],
            (RazerHapticoreScrollTableClass)arguments[2],
            ReadUInt16BigEndian(arguments, 3),
            pointSize,
            points);
    }

    public static RazerHapticoreScrollTableMaxPointCountState ParseGeneratorTableMaxPointCount(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, MaxPointCountSize, "Hapticore scroll table max point count");
        return new RazerHapticoreScrollTableMaxPointCountState(
            ReadUInt16BigEndian(arguments, 0),
            ReadUInt16BigEndian(arguments, 2),
            ReadUInt16BigEndian(arguments, 4));
    }

    public static RazerHapticoreScrollLibraryVersionState ParseLibraryVersion(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, LibraryVersionSize, "Hapticore scroll library version");
        return new RazerHapticoreScrollLibraryVersionState(arguments[0], arguments[1]);
    }

    public static RazerHapticoreScrollStageInformationState ParseStageInformation(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, StageInformationSize, "Hapticore scroll stage information");
        var stageTypes = new RazerHapticoreScrollStageType[MaxStage];
        for (var index = 0; index < stageTypes.Length; index++)
        {
            stageTypes[index] = (RazerHapticoreScrollStageType)arguments[3 + index];
        }

        return new RazerHapticoreScrollStageInformationState(
            arguments[0],
            arguments[1],
            arguments[2],
            stageTypes);
    }

    public static RazerHapticoreScrollBrowserModeState ParseBrowserMode(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, BrowserModeSize, "Hapticore scroll browser mode");
        return new RazerHapticoreScrollBrowserModeState(arguments[0] != 0);
    }

    public static RazerHapticoreScrollGeneratorCustomMetaDataState ParseGeneratorCustomMetaData(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, 31, "Hapticore scroll generator custom metadata");
        return new RazerHapticoreScrollGeneratorCustomMetaDataState(
            arguments[0],
            arguments[1],
            arguments[2],
            arguments[3] != 0,
            arguments[4] != 0,
            arguments[5],
            arguments[6],
            arguments[7],
            arguments[8],
            ReadDirection(arguments, 9),
            ReadDirection(arguments, 21));
    }

    // The middleware reports the request's profile and stage, not response bytes.
    public static RazerHapticoreScrollProfileStageState ParseStageReset(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        Arguments(response, request, StateSize, "Hapticore scroll stage reset");
        var requestArguments = request[RazerFeatureReport.ArgumentsOffset..];
        return new RazerHapticoreScrollProfileStageState(requestArguments[0], requestArguments[1]);
    }

    private static byte[] Create(
        byte transactionId,
        byte dataSize,
        byte commandId,
        params byte[] arguments) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            dataSize,
            CommandClass,
            commandId,
            arguments);

    private static ReadOnlySpan<byte> Arguments(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments,
        string feature)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments))
        {
            throw new InvalidOperationException(
                $"{feature} returned an invalid or out-of-order feature report.");
        }

        return response.Slice(
            RazerFeatureReport.ArgumentsOffset,
            response[RazerFeatureReport.DataSizeOffset]);
    }

    private static float ReadSingle(ReadOnlySpan<byte> arguments, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(arguments.Slice(offset, 4));

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> arguments, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(arguments.Slice(offset, 2));

    private static void ValidateProfile(byte profileId)
    {
        if (profileId is < 1 or > MaxProfileId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profileId),
                profileId,
                $"Hapticore scroll profile ids are 1 through {MaxProfileId}.");
        }
    }

    private static void ValidateStage(byte stage)
    {
        if (stage is < 1 or > MaxStage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                $"Hapticore scroll stages are 1 through {MaxStage}.");
        }
    }

    private static void ValidateTableClass(RazerHapticoreScrollTableClass tableClass)
    {
        if (!Enum.IsDefined(tableClass))
        {
            throw new ArgumentOutOfRangeException(nameof(tableClass), tableClass, "Unknown generator table class.");
        }
    }

    private static void ValidateDirection(
        RazerHapticoreScrollDirection direction,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(direction, parameterName);
        if (direction.ControlPoints is null ||
            direction.ControlPoints.Count != DirectionControlPointCount)
        {
            throw new ArgumentException(
                $"A Hapticore scroll direction needs {DirectionControlPointCount} control points.",
                parameterName);
        }
    }

    private static void WriteDirection(
        Span<byte> arguments,
        int offset,
        RazerHapticoreScrollDirection direction)
    {
        for (var index = 0; index < DirectionControlPointCount; index++)
        {
            var point = direction.ControlPoints[index];
            arguments[offset + (index * 2)] = point.X;
            arguments[offset + (index * 2) + 1] = point.Y;
        }

        arguments[offset + 10] = 0xFF;
        arguments[offset + 11] = 0xFF;
    }

    private static RazerHapticoreScrollDirection ReadDirection(
        ReadOnlySpan<byte> arguments,
        int offset)
    {
        var points = new RazerHapticoreScrollControlPoint[DirectionControlPointCount];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new RazerHapticoreScrollControlPoint(
                arguments[offset + (index * 2)],
                arguments[offset + (index * 2) + 1]);
        }

        return new RazerHapticoreScrollDirection(points);
    }
}
