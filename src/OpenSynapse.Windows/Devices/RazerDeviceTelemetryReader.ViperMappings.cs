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
                ?? throw new InvalidOperationException("The Viper control channel is unavailable.");

            await ValidateViperProduct184MetadataAsync(viper, cancellationToken);
            var assignments = await ReadAllViperObmAssignmentsAsync(viper, cancellationToken);

            _validatedViperButtonMappingsPath = viper.Descriptor.Id;
            return assignments.Select(ToPublicAssignment).ToArray();
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
                ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
            EnsureValidated(
                _validatedViperButtonMappingsPath,
                viper.Descriptor.Id,
                "Read the current mouse profile, button IDs, and all 16 onboard mappings first.");

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
                EnsureAssignmentsEqual(requested, actual, "target");

                var actualSibling = await ReadViperObmAssignmentAsync(
                    viper, requested.ButtonId, siblingMode, cancellationToken);
                EnsureAssignmentsEqual(originalSibling, actualSibling, "sibling-layer isolation");
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
                var message = "Viper onboard mapping update failed: " + exception.Message + " " +
                    (restorationError is null
                        ? "The original mapping and sibling layer were restored and verified by readback."
                        : "Original mapping restoration failed: " + restorationError + " Check the button mapping immediately.");
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

    public async ValueTask<IReadOnlyList<ViperButtonAssignment>> SetViperButtonAssignmentsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        IReadOnlyList<ViperButtonAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var requested = ValidateViperButtonAssignmentBatch(assignments);

        await _viperButtonMappingTransactionGate.WaitAsync(cancellationToken);
        try
        {
            _validatedViperButtonMappingsPath = null;
            var viper = FindReadyDevice(devices, "viper-184")
                ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
            await ValidateViperProduct184MetadataAsync(viper, cancellationToken);

            var original = await ReadAllViperObmAssignmentsAsync(viper, cancellationToken);
            var originalByKey = original.ToDictionary(AssignmentKey);
            var attempted = new List<ViperObmAssignment>();
            try
            {
                foreach (var target in requested)
                {
                    var current = originalByKey[AssignmentKey(target)];
                    if (AssignmentsEqual(current, target))
                    {
                        continue;
                    }

                    attempted.Add(current);
                    await WriteViperObmAssignmentAsync(viper, target, cancellationToken);
                    var actual = await ReadViperObmAssignmentAsync(
                        viper, target.ButtonId, target.Mode, cancellationToken);
                    EnsureAssignmentsEqual(target, actual, "batch target");
                }

                var final = await ReadAllViperObmAssignmentsAsync(viper, cancellationToken);
                foreach (var target in requested)
                {
                    EnsureAssignmentsEqual(target, final.Single(item =>
                        AssignmentKey(item) == AssignmentKey(target)), "final batch");
                }
                _validatedViperButtonMappingsPath = viper.Descriptor.Id;
                return final.Select(ToPublicAssignment).ToArray();
            }
            catch (Exception exception) when (
                IsExpectedHardwareException(exception) || exception is OperationCanceledException)
            {
                var restorationError = await RestoreViperObmAssignmentBatchAsync(
                    viper, original, attempted);
                if (restorationError is null)
                {
                    _validatedViperButtonMappingsPath = viper.Descriptor.Id;
                }

                var message = "Viper onboard mapping batch update failed: " + exception.Message + " " +
                    (restorationError is null
                        ? "The complete original mapping was restored and verified by readback."
                        : "Complete original mapping restoration failed: " + restorationError + " Check the mouse mappings immediately.");
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
                "Product 184 onboard metadata does not match the verified scope; mapping writes were rejected: " +
                $"max={maximumProfiles},count={profileCount}," +
                $"profiles={Convert.ToHexString(profileIds)},buttons={Convert.ToHexString(buttonIds)}.");
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

    private async Task<IReadOnlyList<ViperObmAssignment>> ReadAllViperObmAssignmentsAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var assignments = new List<ViperObmAssignment>(ViperProduct184ButtonIds.Length * 2);
        foreach (var buttonId in ViperProduct184ButtonIds)
        {
            assignments.Add(await ReadViperObmAssignmentAsync(
                device, buttonId, ViperObmMappingMode.Normal, cancellationToken));
            assignments.Add(await ReadViperObmAssignmentAsync(
                device, buttonId, ViperObmMappingMode.HyperShift, cancellationToken));
        }
        return assignments;
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
            errors.Add("Restore write failed: " + exception.Message);
        }

        try
        {
            var restored = await ReadViperObmAssignmentAsync(
                device, original.ButtonId, original.Mode, CancellationToken.None);
            EnsureAssignmentsEqual(original, restored, "restored target layer");
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            errors.Add("Restored target-layer readback failed: " + exception.Message);
        }

        try
        {
            var sibling = await ReadViperObmAssignmentAsync(
                device, originalSibling.ButtonId, originalSibling.Mode, CancellationToken.None);
            EnsureAssignmentsEqual(originalSibling, sibling, "restored sibling-layer isolation");
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            errors.Add("Sibling-layer readback failed: " + exception.Message);
        }

        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    private async Task<string?> RestoreViperObmAssignmentBatchAsync(
        ReadyDevice device,
        IReadOnlyList<ViperObmAssignment> original,
        IReadOnlyList<ViperObmAssignment> attempted)
    {
        var errors = new List<string>();
        foreach (var assignment in attempted.Reverse())
        {
            try
            {
                await WriteViperObmAssignmentAsync(device, assignment, CancellationToken.None);
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Restore write for {FormatAssignment(assignment)} failed: {exception.Message}");
            }
        }

        try
        {
            var restored = await ReadAllViperObmAssignmentsAsync(device, CancellationToken.None);
            var restoredByKey = restored.ToDictionary(AssignmentKey);
            foreach (var expected in original)
            {
                EnsureAssignmentsEqual(
                    expected,
                    restoredByKey[AssignmentKey(expected)],
                    "batch restore");
            }
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            errors.Add("Complete restore readback failed: " + exception.Message);
        }

        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    internal static IReadOnlyList<ViperObmAssignment> ValidateViperButtonAssignmentBatch(
        IReadOnlyList<ViperButtonAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Count != ViperProduct184ButtonIds.Length * 2)
        {
            throw new ArgumentException("A Product 184 mapping batch must contain exactly 16 records.", nameof(assignments));
        }

        var converted = assignments.Select(assignment =>
        {
            ArgumentNullException.ThrowIfNull(assignment);
            ArgumentNullException.ThrowIfNull(assignment.FunctionData);
            return ToProtocolAssignment(assignment);
        }).ToArray();
        if (converted.Select(AssignmentKey).Distinct().Count() != converted.Length ||
            ViperProduct184ButtonIds.Any(buttonId =>
                !converted.Any(item => item.ButtonId == buttonId && item.Mode == ViperObmMappingMode.Normal) ||
                !converted.Any(item => item.ButtonId == buttonId && item.Mode == ViperObmMappingMode.HyperShift)))
        {
            throw new ArgumentException(
                "A Product 184 mapping batch must contain one unique normal-layer and HyperShift-layer record for each button.",
                nameof(assignments));
        }

        return converted
            .OrderBy(item => Array.IndexOf(ViperProduct184ButtonIds, item.ButtonId))
            .ThenBy(item => item.Mode)
            .ToArray();
    }

    private static ViperObmAssignment ToProtocolAssignment(ViperButtonAssignment assignment)
    {
        if (assignment.ProfileId != ViperProduct184ProfileId)
        {
            throw new ArgumentOutOfRangeException(nameof(assignment), "Product 184 supports Profile 1 only.");
        }
        if (!ViperProduct184ButtonIds.Contains(assignment.ButtonId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(assignment),
                $"Product 184 does not define button ID {assignment.ButtonId}.");
        }

        var mode = assignment.Layer switch
        {
            ViperButtonMappingLayer.Normal => ViperObmMappingMode.Normal,
            ViperButtonMappingLayer.HyperShift => ViperObmMappingMode.HyperShift,
            _ => throw new ArgumentOutOfRangeException(nameof(assignment), "Unknown Viper mapping layer."),
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
            _ => throw new ArgumentOutOfRangeException(nameof(assignment), "Product 184 does not support this mapping function."),
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
                "This Product 184 mapping type has static protocol evidence only and has not passed physical write/readback/restore validation.");
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
                _ => throw new InvalidOperationException("The device returned an unknown Viper mapping layer."),
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
                    $"The device returned Product 184 mapping function {(byte)assignment.Function}, which is not enabled."),
            },
            assignment.FunctionData.ToArray());

    private static bool AssignmentsEqual(ViperObmAssignment left, ViperObmAssignment right) =>
        left.ProfileId == right.ProfileId &&
        left.ButtonId == right.ButtonId &&
        left.Mode == right.Mode &&
        left.Function == right.Function &&
        left.FunctionData.SequenceEqual(right.FunctionData);

    private static (byte ButtonId, ViperObmMappingMode Mode) AssignmentKey(
        ViperObmAssignment assignment) => (assignment.ButtonId, assignment.Mode);

    private static void EnsureAssignmentsEqual(
        ViperObmAssignment expected,
        ViperObmAssignment actual,
        string phase)
    {
        if (!AssignmentsEqual(expected, actual))
        {
            throw new InvalidOperationException(
                $"{phase} mapping readback mismatch: wrote {FormatAssignment(expected)}, " +
                $"read {FormatAssignment(actual)}.");
        }
    }

    private static string FormatAssignment(ViperObmAssignment assignment) =>
        $"profile={assignment.ProfileId},button={assignment.ButtonId},mode={assignment.Mode}," +
        $"function={assignment.Function},data={Convert.ToHexString(assignment.FunctionData.ToArray())}";
}
