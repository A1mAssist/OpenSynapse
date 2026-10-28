namespace OpenSynapse.Windows.Protocols;

public readonly record struct RazerAnalogActuationPoint(
    byte KeyId,
    ushort ThresholdH,
    ushort ThresholdL);

public readonly record struct RazerAnalogRapidTrigger(
    byte KeyId,
    bool Enabled,
    ushort UpSensitivity,
    ushort DownSensitivity);

public readonly record struct RazerAnalogModTapAssignment(
    byte KeyId,
    bool Enabled,
    byte KeyCode,
    byte HoldTime);

public sealed record RazerAnalogKeyAssignment(
    byte KeyId,
    RazerButtonAssignmentMode Mode,
    RazerButtonFunctionType FunctionId,
    byte DataSize,
    IReadOnlyList<byte> Data);

public readonly record struct RazerAnalogKeyAssignmentSelector(
    byte KeyId,
    RazerButtonAssignmentMode Mode);

public sealed record RazerAnalogKeyActuationTable(
    byte DataType,
    byte ClassId,
    IReadOnlyList<RazerAnalogActuationPoint> Points);

public sealed record RazerAnalogKeyRapidTriggerTable(
    byte DataType,
    byte ClassId,
    IReadOnlyList<RazerAnalogRapidTrigger> Entries);

public sealed record RazerAnalogKeyModTapTable(
    byte ClassId,
    IReadOnlyList<RazerAnalogModTapAssignment> Entries);

public sealed record RazerAnalogKeyAssignmentTable(
    byte ClassId,
    IReadOnlyList<RazerAnalogKeyAssignment> Entries);

public readonly record struct RazerAnalogKeyContinuousRapidTrigger(byte ClassId, bool Enabled);

public readonly record struct RazerAnalogKeyBottomDeadZone(byte ClassId, byte Value);

public readonly record struct RazerAnalogKeyKillSwitch(
    byte ClassId,
    bool Enabled,
    byte KeyId1,
    byte KeyId2);

/// <summary>
/// Protocol 2 analog-keyboard commands from the Huntsman V3 Pro and Mako middleware.
/// The tuple's first byte sizes the middleware's staging buffer, not the wire length:
/// list commands send 2 + stride * count (or 1 + stride * count) bytes instead.
/// </summary>
public static class RazerAnalogKeyProtocol
{
    public const int MaxActuationPoints = 15;
    public const int MaxRapidTriggerKeys = 13;
    public const int MaxModTapAssignments = 19;
    public const int MaxKeyAssignments = 8;

    private const byte CommandClass = 0x02;
    // Tuples are [staging size, class, id] from the Huntsman V3 Pro / Mako bundles.
    // List commands pass their actual record length to sendCommand, not the staging size.
    private const byte GetMultiAnalogKeyActuationPoint = 0x99;
    private const byte SetMultiAnalogKeyActuationPoint = 0x19;
    private const byte GetMultiAnalogKeyRapidTrigger = 0x9A;
    private const byte SetMultiAnalogKeyRapidTrigger = 0x1A;
    private const byte GetMultiModTapKeyAssignment = 0x9B;
    private const byte SetMultiModTapKeyAssignment = 0x1B;
    private const byte GetMultiKeyAssignment = 0x9E;
    private const byte SetMultiKeyAssignment = 0x1E;
    private const byte GetAnalogKeyContinuousRapidTrigger = 0xA0;
    private const byte SetAnalogKeyContinuousRapidTrigger = 0x20;
    private const byte GetAnalogKeyBottomDeadZone = 0xA8;
    private const byte SetAnalogKeyBottomDeadZone = 0x28;
    private const byte GetAnalogReportMode = 0xAA;
    private const byte SetAnalogReportMode = 0x2A;
    private const byte GetKeyboardKillSwitchCtrl = 0xA1;
    private const byte SetKeyboardKillSwitchCtrl = 0x21;

    // The OBM call site passes (0, profileId) for these two prefix bytes.
    public static byte[] CreateGetMultiAnalogKeyActuationPointRequest(
        byte transactionId,
        byte dataType,
        byte classId,
        IReadOnlyList<byte> keyIds)
    {
        ArgumentNullException.ThrowIfNull(keyIds);
        if (keyIds.Count > MaxActuationPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(keyIds), $"At most {MaxActuationPoints} key ids fit.");
        }

        var arguments = new byte[2 + (keyIds.Count * 5)];
        arguments[0] = dataType;
        arguments[1] = classId;
        for (var index = 0; index < keyIds.Count; index++)
        {
            arguments[2 + (index * 5)] = keyIds[index];
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            GetMultiAnalogKeyActuationPoint,
            arguments);
    }

    public static byte[] CreateSetMultiAnalogKeyActuationPointRequest(
        byte transactionId,
        byte dataType,
        byte classId,
        IReadOnlyList<RazerAnalogActuationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count > MaxActuationPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(points), $"At most {MaxActuationPoints} points fit.");
        }

        var arguments = new byte[2 + (points.Count * 5)];
        arguments[0] = dataType;
        arguments[1] = classId;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var offset = 2 + (index * 5);
            arguments[offset] = point.KeyId;
            WriteBigEndian(arguments.AsSpan(offset + 1), point.ThresholdH);
            WriteBigEndian(arguments.AsSpan(offset + 3), point.ThresholdL);
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            SetMultiAnalogKeyActuationPoint,
            arguments);
    }

    public static byte[] CreateGetMultiAnalogKeyRapidTriggerRequest(
        byte transactionId,
        byte dataType,
        byte classId,
        IReadOnlyList<byte> keyIds)
    {
        ArgumentNullException.ThrowIfNull(keyIds);
        if (keyIds.Count > MaxRapidTriggerKeys)
        {
            throw new ArgumentOutOfRangeException(nameof(keyIds), $"At most {MaxRapidTriggerKeys} key ids fit.");
        }

        var arguments = new byte[2 + (keyIds.Count * 6)];
        arguments[0] = dataType;
        arguments[1] = classId;
        for (var index = 0; index < keyIds.Count; index++)
        {
            arguments[2 + (index * 6)] = keyIds[index];
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            GetMultiAnalogKeyRapidTrigger,
            arguments);
    }

    public static byte[] CreateSetMultiAnalogKeyRapidTriggerRequest(
        byte transactionId,
        byte dataType,
        byte classId,
        IReadOnlyList<RazerAnalogRapidTrigger> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count > MaxRapidTriggerKeys)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), $"At most {MaxRapidTriggerKeys} keys fit.");
        }

        var arguments = new byte[2 + (settings.Count * 6)];
        arguments[0] = dataType;
        arguments[1] = classId;
        for (var index = 0; index < settings.Count; index++)
        {
            var setting = settings[index];
            var offset = 2 + (index * 6);
            arguments[offset] = setting.KeyId;
            arguments[offset + 1] = setting.Enabled ? (byte)0x01 : (byte)0x00;
            WriteBigEndian(arguments.AsSpan(offset + 2), setting.UpSensitivity);
            WriteBigEndian(arguments.AsSpan(offset + 4), setting.DownSensitivity);
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            SetMultiAnalogKeyRapidTrigger,
            arguments);
    }

    // The bundle has no singular "Analog Key Rapid Trigger" string; these are the
    // "Get/Set Analog Key Continuous Rapid Trigger" commands.
    public static byte[] CreateGetAnalogKeyContinuousRapidTriggerRequest(byte transactionId, byte classId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            2,
            CommandClass,
            GetAnalogKeyContinuousRapidTrigger,
            new[] { classId });

    public static byte[] CreateSetAnalogKeyContinuousRapidTriggerRequest(
        byte transactionId,
        byte classId,
        bool enabled) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            2,
            CommandClass,
            SetAnalogKeyContinuousRapidTrigger,
            new[] { classId, enabled ? (byte)0x01 : (byte)0x00 });

    public static byte[] CreateGetMultiModTapKeyAssignmentRequest(
        byte transactionId,
        byte classId,
        IReadOnlyList<byte> keyIds)
    {
        ArgumentNullException.ThrowIfNull(keyIds);
        if (keyIds.Count > MaxModTapAssignments)
        {
            throw new ArgumentOutOfRangeException(nameof(keyIds), $"At most {MaxModTapAssignments} key ids fit.");
        }

        var arguments = new byte[1 + (keyIds.Count * 4)];
        arguments[0] = classId;
        for (var index = 0; index < keyIds.Count; index++)
        {
            arguments[1 + (index * 4)] = keyIds[index];
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            GetMultiModTapKeyAssignment,
            arguments);
    }

    public static byte[] CreateSetMultiModTapKeyAssignmentRequest(
        byte transactionId,
        byte classId,
        IReadOnlyList<RazerAnalogModTapAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Count > MaxModTapAssignments)
        {
            throw new ArgumentOutOfRangeException(nameof(assignments), $"At most {MaxModTapAssignments} assignments fit.");
        }

        var arguments = new byte[1 + (assignments.Count * 4)];
        arguments[0] = classId;
        for (var index = 0; index < assignments.Count; index++)
        {
            var assignment = assignments[index];
            var offset = 1 + (index * 4);
            arguments[offset] = assignment.KeyId;
            arguments[offset + 1] = assignment.Enabled ? (byte)0x01 : (byte)0x00;
            arguments[offset + 2] = assignment.KeyCode;
            arguments[offset + 3] = assignment.HoldTime;
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            SetMultiModTapKeyAssignment,
            arguments);
    }

    public static byte[] CreateGetMultiKeyAssignmentRequest(
        byte transactionId,
        byte classId,
        IReadOnlyList<RazerAnalogKeyAssignmentSelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        if (selectors.Count > MaxKeyAssignments)
        {
            throw new ArgumentOutOfRangeException(nameof(selectors), $"At most {MaxKeyAssignments} selectors fit.");
        }

        var arguments = new byte[1 + (selectors.Count * 9)];
        arguments[0] = classId;
        for (var index = 0; index < selectors.Count; index++)
        {
            var selector = selectors[index];
            var offset = 1 + (index * 9);
            arguments[offset] = selector.KeyId;
            arguments[offset + 1] = (byte)selector.Mode;
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            GetMultiKeyAssignment,
            arguments);
    }

    public static byte[] CreateSetMultiKeyAssignmentRequest(
        byte transactionId,
        byte classId,
        IReadOnlyList<RazerAnalogKeyAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Count > MaxKeyAssignments)
        {
            throw new ArgumentOutOfRangeException(nameof(assignments), $"At most {MaxKeyAssignments} assignments fit.");
        }

        var arguments = new byte[1 + (assignments.Count * 9)];
        arguments[0] = classId;
        for (var index = 0; index < assignments.Count; index++)
        {
            var assignment = assignments[index];
            if (assignment.Data.Count > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(assignments), "Each assignment carries at most 5 data bytes.");
            }

            var offset = 1 + (index * 9);
            arguments[offset] = assignment.KeyId;
            arguments[offset + 1] = (byte)assignment.Mode;
            arguments[offset + 2] = (byte)assignment.FunctionId;
            arguments[offset + 3] = assignment.DataSize;
            for (var dataIndex = 0; dataIndex < assignment.Data.Count; dataIndex++)
            {
                arguments[offset + 4 + dataIndex] = assignment.Data[dataIndex];
            }
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            SetMultiKeyAssignment,
            arguments);
    }

    // Get is [2,2,168] and set is [2,2,40]; the adjacent [2,2,169]/[2,2,41]
    // pair is the data-report-delta command.
    public static byte[] CreateGetAnalogKeyBottomDeadZoneRequest(byte transactionId, byte classId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            2,
            CommandClass,
            GetAnalogKeyBottomDeadZone,
            new[] { classId });

    public static byte[] CreateSetAnalogKeyBottomDeadZoneRequest(
        byte transactionId,
        byte classId,
        byte bottomDeadZone) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            2,
            CommandClass,
            SetAnalogKeyBottomDeadZone,
            new[] { classId, bottomDeadZone });

    // Get is [1,2,170] and set is [1,2,42]; the adjacent [10,0,221]/[10,0,93]
    // pair is game-mode custom keys.
    public static byte[] CreateGetAnalogReportModeRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            1,
            CommandClass,
            GetAnalogReportMode,
            new byte[] { 0x00 });

    public static byte[] CreateSetAnalogReportModeRequest(byte transactionId, byte reportMode) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            1,
            CommandClass,
            SetAnalogReportMode,
            new[] { reportMode });

    public static byte[] CreateGetKeyboardKillSwitchRequest(byte transactionId, byte classId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            4,
            CommandClass,
            GetKeyboardKillSwitchCtrl,
            new[] { classId });

    public static byte[] CreateSetKeyboardKillSwitchRequest(
        byte transactionId,
        byte classId,
        bool enabled,
        byte keyId1,
        byte keyId2) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            4,
            CommandClass,
            SetKeyboardKillSwitchCtrl,
            new[] { classId, enabled ? (byte)0x01 : (byte)0x00, keyId1, keyId2 });

    public static RazerAnalogKeyActuationTable ParseMultiAnalogKeyActuationPoints(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 2);
        RequireStride(data.Length - 2, 5, "actuation points");
        var points = new List<RazerAnalogActuationPoint>((data.Length - 2) / 5);
        for (var offset = 2; offset < data.Length; offset += 5)
        {
            points.Add(new RazerAnalogActuationPoint(
                data[offset],
                ReadBigEndian(data[offset + 1], data[offset + 2]),
                ReadBigEndian(data[offset + 3], data[offset + 4])));
        }

        return new RazerAnalogKeyActuationTable(data[0], data[1], points);
    }

    public static RazerAnalogKeyRapidTriggerTable ParseMultiAnalogKeyRapidTriggers(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 2);
        RequireStride(data.Length - 2, 6, "rapid trigger entries");
        var entries = new List<RazerAnalogRapidTrigger>((data.Length - 2) / 6);
        for (var offset = 2; offset < data.Length; offset += 6)
        {
            entries.Add(new RazerAnalogRapidTrigger(
                data[offset],
                data[offset + 1] != 0,
                ReadBigEndian(data[offset + 2], data[offset + 3]),
                ReadBigEndian(data[offset + 4], data[offset + 5])));
        }

        return new RazerAnalogKeyRapidTriggerTable(data[0], data[1], entries);
    }

    public static RazerAnalogKeyContinuousRapidTrigger ParseAnalogKeyContinuousRapidTrigger(
        ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 2);
        return new RazerAnalogKeyContinuousRapidTrigger(data[0], data[1] != 0);
    }

    public static RazerAnalogKeyModTapTable ParseMultiModTapKeyAssignments(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 1);
        RequireStride(data.Length - 1, 4, "mod tap assignments");
        var entries = new List<RazerAnalogModTapAssignment>((data.Length - 1) / 4);
        for (var offset = 1; offset < data.Length; offset += 4)
        {
            entries.Add(new RazerAnalogModTapAssignment(
                data[offset],
                data[offset + 1] != 0,
                data[offset + 2],
                data[offset + 3]));
        }

        return new RazerAnalogKeyModTapTable(data[0], entries);
    }

    public static RazerAnalogKeyAssignmentTable ParseMultiKeyAssignments(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 1);
        RequireStride(data.Length - 1, 9, "key assignments");
        var entries = new List<RazerAnalogKeyAssignment>((data.Length - 1) / 9);
        for (var offset = 1; offset < data.Length; offset += 9)
        {
            entries.Add(new RazerAnalogKeyAssignment(
                data[offset],
                (RazerButtonAssignmentMode)data[offset + 1],
                (RazerButtonFunctionType)data[offset + 2],
                data[offset + 3],
                data.Slice(offset + 4, 5).ToArray()));
        }

        return new RazerAnalogKeyAssignmentTable(data[0], entries);
    }

    public static RazerAnalogKeyBottomDeadZone ParseAnalogKeyBottomDeadZone(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 2);
        return new RazerAnalogKeyBottomDeadZone(data[0], data[1]);
    }

    public static byte ParseAnalogReportMode(ReadOnlySpan<byte> response) => Arguments(response, 1)[0];

    public static RazerAnalogKeyKillSwitch ParseKeyboardKillSwitch(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 4);
        return new RazerAnalogKeyKillSwitch(data[0], data[1] != 0, data[2], data[3]);
    }

    private static ReadOnlySpan<byte> Arguments(ReadOnlySpan<byte> response, byte minimumLength) =>
        OpenRazerStandardProtocol.Arguments(response, minimumLength);

    private static void RequireStride(int length, int stride, string name)
    {
        if (length % stride != 0)
        {
            throw new InvalidDataException($"Razer response has a partial {name} record.");
        }
    }

    private static ushort ReadBigEndian(byte high, byte low) => (ushort)((high << 8) | low);

    private static void WriteBigEndian(Span<byte> destination, ushort value)
    {
        destination[0] = (byte)(value >> 8);
        destination[1] = (byte)value;
    }
}
