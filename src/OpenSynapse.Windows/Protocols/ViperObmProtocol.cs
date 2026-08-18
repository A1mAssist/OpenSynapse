namespace OpenSynapse.Windows.Protocols;

public enum ViperObmMappingMode : byte
{
    Normal = 0,
    HyperShift = 1,
}

public enum ViperObmFunctionId : byte
{
    Off = 0,
    ButtonCode = 1,
    KeyCode = 2,
    MacroTypeI = 3,
    MacroTypeII = 4,
    MacroTypeIII = 5,
    Dpi = 6,
    Profile = 7,
    Lighting = 8,
    PowerKeys = 9,
    MediaKeys = 10,
    DoubleClick = 11,
    ModeButtonKey = 12,
    TurboModeKey = 13,
    TurboModeButton = 14,
    MacroTypeIV = 15,
    Controller = 16,
    RazerKey = 17,
    WindowsShortcutsKey = 18,
}

public sealed record ViperObmAssignment(
    byte ProfileId,
    byte ButtonId,
    ViperObmMappingMode Mode,
    ViperObmFunctionId Function,
    IReadOnlyList<byte> FunctionData);

/// <summary>
/// Product 184 onboard-memory commands used by Synapse's obmEngineMouse.
/// SET callers must preserve the original assignment and verify readback.
/// </summary>
public static class ViperObmProtocol
{
    public static byte[] CreateGetMaximumProfilesRequest() => Create(0x01, 0x05, 0x8A);

    public static byte[] CreateGetProfileCountRequest() => Create(0x01, 0x05, 0x80);

    public static byte[] CreateGetProfileIdsRequest() => Create(0x50, 0x05, 0x81);

    public static byte[] CreateGetButtonIdsRequest() => Create(0x50, 0x02, 0x84);

    public static byte[] CreateGetAssignmentRequest(
        byte profileId,
        byte buttonId,
        ViperObmMappingMode mode)
    {
        ValidateId(profileId, nameof(profileId));
        ValidateId(buttonId, nameof(buttonId));
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return Create(0x50, 0x02, 0x8C, profileId, buttonId, (byte)mode);
    }

    public static byte[] CreateSetAssignmentRequest(ViperObmAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(assignment.FunctionData);
        ValidateId(assignment.ProfileId, nameof(assignment.ProfileId));
        ValidateId(assignment.ButtonId, nameof(assignment.ButtonId));
        if (!Enum.IsDefined(assignment.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(assignment.Mode));
        }
        if (!Enum.IsDefined(assignment.Function))
        {
            throw new ArgumentOutOfRangeException(nameof(assignment.Function));
        }
        ValidateProduct184Function(assignment.Function, assignment.FunctionData);

        var arguments = new byte[10];
        arguments[0] = assignment.ProfileId;
        arguments[1] = assignment.ButtonId;
        arguments[2] = (byte)assignment.Mode;
        arguments[3] = (byte)assignment.Function;
        arguments[4] = checked((byte)assignment.FunctionData.Count);
        for (var index = 0; index < assignment.FunctionData.Count; index++)
        {
            arguments[index + 5] = assignment.FunctionData[index];
        }

        return Create(0x50, 0x02, 0x0C, arguments);
    }

    public static byte ParseMaximumProfiles(ReadOnlySpan<byte> response) =>
        ParseNonZeroByte(response, CreateGetMaximumProfilesRequest(), "最大 Profile 数");

    public static byte ParseMaximumProfiles(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request) =>
        ParseNonZeroByte(response, request, "最大 Profile 数");

    public static byte ParseProfileCount(ReadOnlySpan<byte> response) =>
        ParseByte(response, CreateGetProfileCountRequest());

    public static byte ParseProfileCount(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request) =>
        ParseByte(response, request);

    public static byte[] ParseProfileIds(ReadOnlySpan<byte> response) =>
        ParseIdList(response, CreateGetProfileIdsRequest(), "Profile");

    public static byte[] ParseProfileIds(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request) =>
        ParseIdList(response, request, "Profile");

    public static byte[] ParseButtonIds(ReadOnlySpan<byte> response) =>
        ParseIdList(response, CreateGetButtonIdsRequest(), "Button");

    public static byte[] ParseButtonIds(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request) =>
        ParseIdList(response, request, "Button");

    public static ViperObmAssignment ParseAssignment(
        ReadOnlySpan<byte> response,
        byte profileId,
        byte buttonId,
        ViperObmMappingMode mode)
    {
        var request = CreateGetAssignmentRequest(profileId, buttonId, mode);
        return ParseAssignment(response, request, profileId, buttonId, mode);
    }

    public static ViperObmAssignment ParseAssignment(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte profileId,
        byte buttonId,
        ViperObmMappingMode mode)
    {
        ValidateResponse(response, request, 10);
        if (response[6] != 0x0A)
        {
            throw new InvalidOperationException(
                $"Viper OBM 映射响应长度不是 Product 184 的固定 0x0A：0x{response[6]:X2}。");
        }
        var offset = RazerFeatureReport.ArgumentsOffset;
        if (response[offset] != profileId || response[offset + 1] != buttonId)
        {
            throw new InvalidOperationException("Viper OBM 映射响应与请求的 Profile 或 Button 不一致。");
        }

        // Product 184 returns 1 in the mode byte even for a Normal request.
        // Synapse likewise assigns the result by request context instead of this echo.

        var function = (ViperObmFunctionId)response[offset + 3];
        if (!Enum.IsDefined(function))
        {
            throw new InvalidOperationException($"Viper OBM 返回了未知 functionId 0x{(byte)function:X2}。");
        }

        var dataSize = response[offset + 4];
        if (dataSize > 5)
        {
            throw new InvalidOperationException($"Viper OBM 返回了超出官方五字节布局的 functionDataSize {dataSize}。");
        }

        var functionData = response.Slice(offset + 5, dataSize).ToArray();
        if (response.Slice(offset + 5 + dataSize, 5 - dataSize).ContainsAnyExcept((byte)0))
        {
            throw new InvalidOperationException("Viper OBM 映射响应包含非零的未声明 functionData 尾部。");
        }
        try
        {
            ValidateProduct184Function(function, functionData);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Viper OBM 返回了无效的 function/payload 组合。", exception);
        }

        return new ViperObmAssignment(
            profileId,
            buttonId,
            mode,
            function,
            functionData);
    }

    private static byte[] Create(byte dataSize, byte commandClass, byte commandId, params byte[] arguments) =>
        RazerFeatureReport.CreateRequest(
            ViperProduct184Protocol.TransactionId,
            dataSize,
            commandClass,
            commandId,
            arguments);

    private static byte ParseNonZeroByte(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        string field)
    {
        var value = ParseByte(response, request);
        return value != 0
            ? value
            : throw new InvalidOperationException($"Viper OBM 返回了无效的{field} 0。");
    }

    private static byte ParseByte(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        ValidateResponse(response, request, 1);
        if (response[6] != 1)
        {
            throw new InvalidOperationException("Viper OBM 标量响应的数据长度不是固定 1 字节。");
        }

        return response[RazerFeatureReport.ArgumentsOffset];
    }

    private static byte[] ParseIdList(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        string field)
    {
        ValidateResponse(response, request, 1);
        var offset = RazerFeatureReport.ArgumentsOffset;
        var count = response[offset];
        if (count == 0 || count > response[6] - 1)
        {
            throw new InvalidOperationException($"Viper OBM {field} ID 数量为空或超出响应长度。");
        }

        var ids = response.Slice(offset + 1, count).ToArray();
        var trailing = response.Slice(offset + 1 + count, response[6] - 1 - count);
        if (trailing.ContainsAnyExcept((byte)0) ||
            ids.Contains((byte)0) || ids.Distinct().Count() != ids.Length)
        {
            throw new InvalidOperationException($"Viper OBM 返回了无效、重复或带有非零尾随字段的 {field} ID。");
        }

        return ids;
    }

    private static void ValidateId(byte value, string parameterName)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "OBM ID 不能为 0。");
        }
    }

    private static void ValidateProduct184Function(
        ViperObmFunctionId function,
        IReadOnlyList<byte> data)
    {
        var valid = function switch
        {
            ViperObmFunctionId.Off => data.Count == 0,
            ViperObmFunctionId.ButtonCode => data.Count == 1,
            ViperObmFunctionId.KeyCode => data.Count == 2,
            ViperObmFunctionId.Dpi => data.Count == 1 && data[0] is 1 or 2 or 6 or 7 ||
                data.Count == 5 && data[0] == 5,
            ViperObmFunctionId.MediaKeys => data.Count == 2,
            ViperObmFunctionId.DoubleClick => data.Count == 1 && data[0] == 1,
            ViperObmFunctionId.ModeButtonKey => data.Count == 1 && data[0] == 1,
            ViperObmFunctionId.TurboModeKey => data.Count == 4,
            ViperObmFunctionId.TurboModeButton => data.Count == 3,
            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException(
                $"Product 184 不支持该 function/payload 组合：{function}，长度 {data.Count}。",
                nameof(data));
        }
    }

    private static void ValidateResponse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments)
    {
        if (response.Length != RazerFeatureReport.Length ||
            response[1] != 0x02 ||
            response[6] < minimumArguments ||
            response[6] > 80 ||
            !RazerFeatureReport.Matches(request, response))
        {
            throw new InvalidOperationException("Viper OBM 返回了无效或错序的 feature report。");
        }
    }
}
