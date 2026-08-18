using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerDeviceTelemetryReader
{
    private delegate byte ViperObmByteParser(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request);

    private delegate byte[] ViperObmIdsParser(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request);

    private const byte ViperProduct184ProfileId = 1;
    private static readonly byte[] ViperProduct184ButtonIds = [1, 2, 3, 4, 5, 9, 10, 96];
    private static readonly byte[] ViperProduct184MouseButtonCodes = [1, 2, 3, 4, 5, 9, 10];
    private readonly SemaphoreSlim _viperButtonMappingTransactionGate = new(1, 1);
    private string? _validatedViperButtonMappingsPath;

    public async ValueTask<IReadOnlyList<ViperButtonAssignment>> ReadViperButtonAssignmentsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        await _viperButtonMappingTransactionGate.WaitAsync(cancellationToken);
        try
        {
            _validatedViperButtonMappingsPath = null;
            var viper = FindReadyDevice(devices, "viper-184")
                ?? throw new InvalidOperationException("Viper 控制通道不可用。");

            await ValidateViperProduct184MetadataAsync(viper, cancellationToken);
            var assignments = new List<ViperButtonAssignment>(ViperProduct184ButtonIds.Length * 2);
            foreach (var buttonId in ViperProduct184ButtonIds)
            {
                assignments.Add(ToPublicAssignment(await ReadViperObmAssignmentAsync(
                    viper, buttonId, ViperObmMappingMode.Normal, cancellationToken)));
                assignments.Add(ToPublicAssignment(await ReadViperObmAssignmentAsync(
                    viper, buttonId, ViperObmMappingMode.HyperShift, cancellationToken)));
            }

            _validatedViperButtonMappingsPath = viper.Descriptor.Id;
            return assignments;
        }
        finally
        {
            _viperButtonMappingTransactionGate.Release();
        }
    }

    public async ValueTask<ViperButtonAssignment> SetViperButtonAssignmentAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ViperButtonAssignment assignment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(assignment.FunctionData);
        var requested = ToProtocolAssignment(assignment);
        await _viperButtonMappingTransactionGate.WaitAsync(cancellationToken);
        try
        {
            var viper = FindReadyDevice(devices, "viper-184")
                ?? throw new InvalidOperationException("Viper 控制通道不可用。");
            EnsureValidated(
                _validatedViperButtonMappingsPath,
                viper.Descriptor.Id,
                "请先完整读取当前鼠标的 Profile、Button ID 和全部 16 条板载映射。");

            var original = await ReadViperObmAssignmentAsync(
                viper, requested.ButtonId, requested.Mode, cancellationToken);
            if (AssignmentsEqual(original, requested))
            {
                return ToPublicAssignment(original);
            }

            var siblingMode = requested.Mode == ViperObmMappingMode.Normal
                ? ViperObmMappingMode.HyperShift
                : ViperObmMappingMode.Normal;
            var originalSibling = await ReadViperObmAssignmentAsync(
                viper, requested.ButtonId, siblingMode, cancellationToken);

            try
            {
                await WriteViperObmAssignmentAsync(viper, requested, cancellationToken);
                var actual = await ReadViperObmAssignmentAsync(
                    viper, requested.ButtonId, requested.Mode, cancellationToken);
                EnsureAssignmentsEqual(requested, actual, "目标");

                var actualSibling = await ReadViperObmAssignmentAsync(
                    viper, requested.ButtonId, siblingMode, cancellationToken);
                EnsureAssignmentsEqual(originalSibling, actualSibling, "另一层隔离");
                return ToPublicAssignment(actual);
            }
            catch (Exception exception) when (
                IsExpectedHardwareException(exception) || exception is OperationCanceledException)
            {
                var restorationError = await RestoreViperObmAssignmentAsync(
                    viper, original, originalSibling);
                if (restorationError is not null)
                {
                    _validatedViperButtonMappingsPath = null;
                }
                var message = "鼠标板载映射设置失败：" + exception.Message + " " +
                    (restorationError is null
                        ? "原映射及另一层已恢复并读回确认。"
                        : "原映射恢复失败：" + restorationError + " 请立即在 Synapse 中检查该按键。");
                if (exception is OperationCanceledException)
                {
                    throw new OperationCanceledException(message, exception, cancellationToken);
                }
                throw new InvalidOperationException(message, exception);
            }
        }
        finally
        {
            _viperButtonMappingTransactionGate.Release();
        }
    }

    private async Task ValidateViperProduct184MetadataAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var maximumProfiles = await ReadViperObmByteAsync(
            device,
            "obm-maximum-profiles.get",
            ViperObmProtocol.CreateGetMaximumProfilesRequest(),
            ViperObmProtocol.ParseMaximumProfiles,
            cancellationToken);
        var profileCount = await ReadViperObmByteAsync(
            device,
            "obm-profile-count.get",
            ViperObmProtocol.CreateGetProfileCountRequest(),
            ViperObmProtocol.ParseProfileCount,
            cancellationToken);
        var profileIds = await ReadViperObmIdsAsync(
            device,
            "obm-profile-ids.get",
            ViperObmProtocol.CreateGetProfileIdsRequest(),
            ViperObmProtocol.ParseProfileIds,
            cancellationToken);
        var buttonIds = await ReadViperObmIdsAsync(
            device,
            "obm-button-ids.get",
            ViperObmProtocol.CreateGetButtonIdsRequest(),
            ViperObmProtocol.ParseButtonIds,
            cancellationToken);

        if (maximumProfiles != 1 || profileCount != 1 ||
            !profileIds.SequenceEqual(new byte[] { ViperProduct184ProfileId }) ||
            !buttonIds.Order().SequenceEqual(ViperProduct184ButtonIds))
        {
            throw new InvalidOperationException(
                "Product 184 板载元数据与已验证范围不一致，已拒绝开放映射写入：" +
                $"max={maximumProfiles},count={profileCount}," +
                $"profiles={Convert.ToHexString(profileIds)},buttons={Convert.ToHexString(buttonIds)}。");
        }
    }

    private async Task<byte> ReadViperObmByteAsync(
        ReadyDevice device,
        string capabilityId,
        byte[] builtRequest,
        ViperObmByteParser parser,
        CancellationToken cancellationToken)
    {
        var (request, response) = await QueryViperObmAsync(
            device, capabilityId, builtRequest, cancellationToken);
        return parser(response, request);
    }

    private async Task<byte[]> ReadViperObmIdsAsync(
        ReadyDevice device,
        string capabilityId,
        byte[] builtRequest,
        ViperObmIdsParser parser,
        CancellationToken cancellationToken)
    {
        var (request, response) = await QueryViperObmAsync(
            device, capabilityId, builtRequest, cancellationToken);
        return parser(response, request);
    }

    private async Task<ViperObmAssignment> ReadViperObmAssignmentAsync(
        ReadyDevice device,
        byte buttonId,
        ViperObmMappingMode mode,
        CancellationToken cancellationToken)
    {
        var (request, response) = await QueryViperObmAsync(
            device,
            "obm-assignment.get",
            ViperObmProtocol.CreateGetAssignmentRequest(ViperProduct184ProfileId, buttonId, mode),
            cancellationToken);
        return ViperObmProtocol.ParseAssignment(
            response, request, ViperProduct184ProfileId, buttonId, mode);
    }

    private async Task WriteViperObmAssignmentAsync(
        ReadyDevice device,
        ViperObmAssignment assignment,
        CancellationToken cancellationToken)
    {
        _ = await QueryViperObmAsync(
            device,
            "obm-assignment.set",
            ViperObmProtocol.CreateSetAssignmentRequest(assignment),
            cancellationToken);
    }

    private async Task<(byte[] Request, byte[] Response)> QueryViperObmAsync(
        ReadyDevice device,
        string capabilityId,
        byte[] builtRequest,
        CancellationToken cancellationToken)
    {
        var request = CreateConfiguredRequest(device, capabilityId, builtRequest);
        var response = await QueryCapabilityAsync(
            device,
            capabilityId,
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            cancellationToken,
            request[6]);
        return (request, response);
    }

    private async Task<string?> RestoreViperObmAssignmentAsync(
        ReadyDevice device,
        ViperObmAssignment original,
        ViperObmAssignment originalSibling)
    {
        var errors = new List<string>();
        try
        {
            await WriteViperObmAssignmentAsync(device, original, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            errors.Add("恢复写入失败：" + exception.Message);
        }

        try
        {
            var restored = await ReadViperObmAssignmentAsync(
                device, original.ButtonId, original.Mode, CancellationToken.None);
            EnsureAssignmentsEqual(original, restored, "恢复目标层");
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            errors.Add("恢复目标层读回失败：" + exception.Message);
        }

        try
        {
            var sibling = await ReadViperObmAssignmentAsync(
                device, originalSibling.ButtonId, originalSibling.Mode, CancellationToken.None);
            EnsureAssignmentsEqual(originalSibling, sibling, "恢复另一层隔离");
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            errors.Add("另一层读回失败：" + exception.Message);
        }

        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    private static ViperObmAssignment ToProtocolAssignment(ViperButtonAssignment assignment)
    {
        if (assignment.ProfileId != ViperProduct184ProfileId)
        {
            throw new ArgumentOutOfRangeException(nameof(assignment), "Product 184 只允许 Profile 1。");
        }
        if (!ViperProduct184ButtonIds.Contains(assignment.ButtonId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(assignment),
                $"Product 184 不存在 button ID {assignment.ButtonId}。");
        }

        var mode = assignment.Layer switch
        {
            ViperButtonMappingLayer.Normal => ViperObmMappingMode.Normal,
            ViperButtonMappingLayer.HyperShift => ViperObmMappingMode.HyperShift,
            _ => throw new ArgumentOutOfRangeException(nameof(assignment), "未知的 Viper 映射层。"),
        };
        var function = assignment.Function switch
        {
            ViperButtonMappingFunction.Off => ViperObmFunctionId.Off,
            ViperButtonMappingFunction.MouseButton => ViperObmFunctionId.ButtonCode,
            ViperButtonMappingFunction.KeyboardKey => ViperObmFunctionId.KeyCode,
            ViperButtonMappingFunction.Dpi => ViperObmFunctionId.Dpi,
            ViperButtonMappingFunction.MediaKey => ViperObmFunctionId.MediaKeys,
            ViperButtonMappingFunction.DoubleClick => ViperObmFunctionId.DoubleClick,
            ViperButtonMappingFunction.HyperShift => ViperObmFunctionId.ModeButtonKey,
            ViperButtonMappingFunction.KeyboardTurbo => ViperObmFunctionId.TurboModeKey,
            ViperButtonMappingFunction.MouseTurbo => ViperObmFunctionId.TurboModeButton,
            _ => throw new ArgumentOutOfRangeException(nameof(assignment), "Product 184 不支持该映射 function。"),
        };
        var snapshot = new ViperObmAssignment(
            assignment.ProfileId,
            assignment.ButtonId,
            mode,
            function,
            assignment.FunctionData.ToArray());

        // Reuse the source-backed Product 184 payload validator before any HID read or write.
        _ = ViperObmProtocol.CreateSetAssignmentRequest(snapshot);
        var verified = snapshot.Function switch
        {
            ViperObmFunctionId.Off => true,
            ViperObmFunctionId.ButtonCode =>
                ViperProduct184MouseButtonCodes.Contains(snapshot.FunctionData[0]),
            ViperObmFunctionId.KeyCode => true,
            ViperObmFunctionId.Dpi => true,
            ViperObmFunctionId.MediaKeys => true,
            ViperObmFunctionId.DoubleClick => true,
            ViperObmFunctionId.ModeButtonKey => true,
            ViperObmFunctionId.TurboModeKey => true,
            ViperObmFunctionId.TurboModeButton => true,
            _ => false,
        };
        if (!verified)
        {
            throw new NotSupportedException(
                "该 Product 184 映射类型只有静态协议证据，尚未完成实机写入/读回/恢复验证。");
        }
        return snapshot;
    }

    private static ViperButtonAssignment ToPublicAssignment(ViperObmAssignment assignment) =>
        new(
            assignment.ProfileId,
            assignment.ButtonId,
            assignment.Mode switch
            {
                ViperObmMappingMode.Normal => ViperButtonMappingLayer.Normal,
                ViperObmMappingMode.HyperShift => ViperButtonMappingLayer.HyperShift,
                _ => throw new InvalidOperationException("设备返回了未知的 Viper 映射层。"),
            },
            assignment.Function switch
            {
                ViperObmFunctionId.Off => ViperButtonMappingFunction.Off,
                ViperObmFunctionId.ButtonCode => ViperButtonMappingFunction.MouseButton,
                ViperObmFunctionId.KeyCode => ViperButtonMappingFunction.KeyboardKey,
                ViperObmFunctionId.Dpi => ViperButtonMappingFunction.Dpi,
                ViperObmFunctionId.MediaKeys => ViperButtonMappingFunction.MediaKey,
                ViperObmFunctionId.DoubleClick => ViperButtonMappingFunction.DoubleClick,
                ViperObmFunctionId.ModeButtonKey => ViperButtonMappingFunction.HyperShift,
                ViperObmFunctionId.TurboModeKey => ViperButtonMappingFunction.KeyboardTurbo,
                ViperObmFunctionId.TurboModeButton => ViperButtonMappingFunction.MouseTurbo,
                _ => throw new InvalidOperationException(
                    $"设备返回了 Product 184 未开放的映射 function {(byte)assignment.Function}。"),
            },
            assignment.FunctionData.ToArray());

    private static bool AssignmentsEqual(ViperObmAssignment left, ViperObmAssignment right) =>
        left.ProfileId == right.ProfileId &&
        left.ButtonId == right.ButtonId &&
        left.Mode == right.Mode &&
        left.Function == right.Function &&
        left.FunctionData.SequenceEqual(right.FunctionData);

    private static void EnsureAssignmentsEqual(
        ViperObmAssignment expected,
        ViperObmAssignment actual,
        string phase)
    {
        if (!AssignmentsEqual(expected, actual))
        {
            throw new InvalidOperationException(
                $"{phase}映射读回不一致：写入 {FormatAssignment(expected)}，" +
                $"读回 {FormatAssignment(actual)}。");
        }
    }

    private static string FormatAssignment(ViperObmAssignment assignment) =>
        $"profile={assignment.ProfileId},button={assignment.ButtonId},mode={assignment.Mode}," +
        $"function={assignment.Function},data={Convert.ToHexString(assignment.FunctionData.ToArray())}";
}
