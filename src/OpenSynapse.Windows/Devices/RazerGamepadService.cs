using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerGamepadConnection(
    string DevicePath,
    ushort ProductId,
    string Name,
    ushort FeatureReportByteLength);

public sealed record RazerGamepadStickState(
    byte ControlId,
    byte? Sensitivity,
    RazerGamepadZones? Zones,
    RazerGamepadCircularity? Circularity);

public sealed record RazerGamepadTriggerState(
    byte ControlId,
    RazerGamepadTriggerMode? Mode,
    byte? StartRange,
    byte? EndRange,
    RazerGamepadDigitalTrigger? Digital);

public sealed record RazerGamepadState(
    byte ProfileId,
    IReadOnlyList<byte> ControlIds,
    IReadOnlyList<byte> ButtonIds,
    IReadOnlyList<RazerGamepadStickState> Sticks,
    IReadOnlyList<RazerGamepadTriggerState> Triggers,
    byte? VibrationScale,
    bool? IgnoreDPadDiagonals,
    RazerGamepadSocdMode? SocdMode,
    ushort? AdcMask,
    RazerGamepadRawInput? RawInput,
    IReadOnlyList<string> Errors);

/// <summary>
/// Protocol 25 gamepad settings. The device exposes a 91-byte feature report on
/// report id 4; every value is probed, so a model that does not answer a command
/// simply hides that row instead of receiving a guessed payload.
/// </summary>
public sealed class RazerGamepadService
{
    public static readonly IReadOnlyDictionary<ushort, string> Products =
        new Dictionary<ushort, string>
        {
            [0x0A45] = "Razer Wolverine V3 Tournament Edition",
            [0x0A4C] = "Razer Wolverine V3 Pro",
            [0x0A57] = "Razer Wolverine V3 Pro PC",
            [0x0A5A] = "Razer Wolverine V3 TE PC",
            [0x0A74] = "Razer Wolverine V4 Pro",
            [0x0A7C] = "Razer Wolverine V4 Tournament Edition",
            [0x1025] = "Razer Wolverine V3 Pro Playstation",
            [0x1030] = "Razer Raiju V3 Pro Signature Edition",
            [0x1013] = "Razer Kitsune",
        };

    // The JS feature modules address the two sticks/triggers as control 1/2 and the
    // four motors as 5..8; the control-id list is diagnostic, not a replacement for
    // those fixed middleware handles.
    private static readonly byte[] StickControlIds = [1, 2];
    private static readonly byte[] VibrationMotorIds = [5, 6, 7, 8];

    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private byte _transactionId = 0x1F;
    private byte _profileId;

    internal RazerGamepadService(
        IRazerFeatureTransport transport,
        string devicePath,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _devicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
    }

    public static bool IsSupported(ushort productId) => Products.ContainsKey(productId);

    public static async Task<RazerGamepadConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            0x1532,
            RazerFeatureReport.Length,
            cancellationToken).ConfigureAwait(false);

        var candidates = interfaces
            .Where(item => Products.ContainsKey(item.ProductId))
            .Where(item => OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, item.ProductId) is null)
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenBy(item => item.ProductId)
            .ToArray();
        if (candidates.Length == 0)
        {
            diagnostic?.Invoke(
                "gamepad found no admitted 91-byte interface; " +
                $"enumerated={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}"))}");
            return null;
        }

        var chosen = candidates[0];
        diagnostic?.Invoke(
            $"gamepad selected pid={chosen.ProductId:X4} path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} access={chosen.Access}");
        return new RazerGamepadConnection(
            chosen.DevicePath,
            chosen.ProductId,
            Products[chosen.ProductId],
            chosen.FeatureReportByteLength);
    }

    public async Task<RazerGamepadState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        _profileId = await TryReadAsync(
            "profile",
            RazerGamepadProtocol.CreateGetActiveProfileIdRequest,
            RazerGamepadProtocol.ReadActiveProfileId,
            cancellationToken).ConfigureAwait(false) is { Ok: true } profile
            ? profile.Value
            : (byte)0;

        IReadOnlyList<byte> controlIds = [];
        try
        {
            controlIds = RazerGamepadProtocol.ReadControlIdList(
                await QueryAsync(
                    RazerGamepadProtocol.CreateGetControlIdListRequest,
                    "read control id list",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"gamepad control list unavailable: {exception.Message}");
        }

        IReadOnlyList<byte> buttonIds = [];
        try
        {
            buttonIds = RazerGamepadProtocol.ReadButtonIdList(
                await QueryAsync(
                    RazerGamepadProtocol.CreateGetButtonIdListRequest,
                    "read button id list",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"gamepad button list unavailable: {exception.Message}");
        }

        var sticks = new List<RazerGamepadStickState>();
        var triggers = new List<RazerGamepadTriggerState>();
        foreach (var controlId in StickControlIds)
        {
            sticks.Add(new RazerGamepadStickState(
                controlId,
                await TryReadAsync(
                    $"sensitivity {controlId}",
                    transaction => RazerGamepadProtocol.CreateGetSensitivityRequest(transaction, _profileId, controlId),
                    response => RazerGamepadProtocol.ReadSensitivity(response, controlId),
                    cancellationToken).ConfigureAwait(false) is { Ok: true } sensitivity
                    ? sensitivity.Value
                    : null,
                await TryReadAsync(
                    $"zones {controlId}",
                    transaction => RazerGamepadProtocol.CreateGetZonesRequest(transaction, _profileId, controlId),
                    response => RazerGamepadProtocol.ReadZones(response, controlId),
                    cancellationToken).ConfigureAwait(false) is { Ok: true } zones
                    ? zones.Value
                    : null,
                await TryReadAsync(
                    $"circularity {controlId}",
                    transaction => RazerGamepadProtocol.CreateGetCircularityRequest(transaction, _profileId, controlId),
                    response => (RazerGamepadCircularity)RazerGamepadProtocol.ReadCircularity(response, controlId),
                    cancellationToken).ConfigureAwait(false) is { Ok: true } circularity
                    ? circularity.Value
                    : null));

            var range = await TryReadAsync(
                $"trigger range {controlId}",
                transaction => RazerGamepadProtocol.CreateGetTriggerRangeRequest(transaction, _profileId, controlId),
                response => RazerGamepadProtocol.ReadTriggerRange(response, controlId),
                cancellationToken).ConfigureAwait(false);
            var mode = await TryReadAsync(
                $"trigger mode {controlId}",
                transaction => RazerGamepadProtocol.CreateGetTriggerModeRequest(transaction, _profileId, controlId),
                response => (RazerGamepadTriggerMode)RazerGamepadProtocol.ReadTriggerMode(response, controlId),
                cancellationToken).ConfigureAwait(false);
            var digital = await TryReadAsync(
                $"digital trigger {controlId}",
                transaction => RazerGamepadProtocol.CreateGetDigitalTriggerRequest(transaction, _profileId, controlId),
                response => RazerGamepadProtocol.ReadDigitalTrigger(response, controlId),
                cancellationToken).ConfigureAwait(false);
            triggers.Add(new RazerGamepadTriggerState(
                controlId,
                mode.Ok ? mode.Value : null,
                range.Ok ? range.Value.Item1 : null,
                range.Ok ? range.Value.Item2 : null,
                digital.Ok ? digital.Value : null));
        }

        var vibration = await TryReadAsync(
            "vibration scale",
            transaction => RazerGamepadProtocol.CreateGetVibrationScaleRequest(transaction, _profileId, 5),
            response => RazerGamepadProtocol.ReadVibrationScale(response, 5),
            cancellationToken).ConfigureAwait(false);
        var ignoreDPad = await TryReadAsync(
            "ignore d-pad diagonals",
            transaction => RazerGamepadProtocol.CreateGetIgnoreDPadDiagonalsRequest(transaction, _profileId),
            response => RazerGamepadProtocol.ReadIgnoreDPadDiagonals(response) != 0,
            cancellationToken).ConfigureAwait(false);
        var socd = await TryReadAsync(
            "socd",
            transaction => RazerGamepadProtocol.CreateGetSocdRequest(transaction, _profileId),
            response => RazerGamepadProtocol.ReadSocdMode(response),
            cancellationToken).ConfigureAwait(false);
        var adc = await TryReadAsync(
            "adc mask",
            RazerGamepadProtocol.CreateGetAdcMaskRequest,
            RazerGamepadProtocol.ReadAdcMask,
            cancellationToken).ConfigureAwait(false);
        var raw = await TryReadAsync(
            "raw input",
            RazerGamepadProtocol.CreateGetRawInputRequest,
            RazerGamepadProtocol.ReadRawInput,
            cancellationToken).ConfigureAwait(false);

        return new RazerGamepadState(
            _profileId,
            controlIds,
            buttonIds,
            sticks,
            triggers,
            vibration.Ok ? vibration.Value : null,
            ignoreDPad.Ok ? ignoreDPad.Value : null,
            socd.Ok ? socd.Value : null,
            adc.Ok ? adc.Value : null,
            raw.Ok ? raw.Value : null,
            errors);
    }

    /// <summary>Reads the Normal/Hypershift assignment of every button the device reports.</summary>
    public async Task<IReadOnlyList<RazerGamepadButtonAssignment>> ReadButtonAssignmentsAsync(
        RazerGamepadButtonMode mode = RazerGamepadButtonMode.Normal,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<byte> buttonIds;
        try
        {
            buttonIds = RazerGamepadProtocol.ReadButtonIdList(
                await QueryAsync(
                    RazerGamepadProtocol.CreateGetButtonIdListRequest,
                    "read button id list",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"gamepad button list unavailable: {exception.Message}");
            return [];
        }

        var assignments = new List<RazerGamepadButtonAssignment>();
        foreach (var buttonId in buttonIds)
        {
            try
            {
                var assignment = RazerGamepadProtocol.ReadButtonAssignment(
                    await QueryAsync(
                        transaction => RazerGamepadProtocol.CreateGetButtonAssignmentRequest(
                            transaction, _profileId, buttonId, mode),
                        $"read button {buttonId}",
                        cancellationToken).ConfigureAwait(false));
                assignments.Add(assignment);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _diagnostic?.Invoke($"gamepad button {buttonId} unavailable: {exception.Message}");
            }
        }

        return assignments;
    }

    public Task SetSensitivityAsync(byte controlId, byte value, CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "sensitivity",
            (transaction, desired) => RazerGamepadProtocol.CreateSetSensitivityRequest(transaction, _profileId, controlId, desired),
            transaction => RazerGamepadProtocol.CreateGetSensitivityRequest(transaction, _profileId, controlId),
            response => RazerGamepadProtocol.ReadSensitivity(response, controlId),
            value,
            cancellationToken);

    public Task SetZonesAsync(
        byte controlId,
        RazerGamepadZones zones,
        CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "zones",
            (transaction, desired) => RazerGamepadProtocol.CreateSetZonesRequest(transaction, _profileId, controlId, desired),
            transaction => RazerGamepadProtocol.CreateGetZonesRequest(transaction, _profileId, controlId),
            response => RazerGamepadProtocol.ReadZones(response, controlId),
            zones,
            cancellationToken);

    public Task SetCircularityAsync(
        byte controlId,
        RazerGamepadCircularity circularity,
        CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "circularity",
            (transaction, desired) => RazerGamepadProtocol.CreateSetCircularityRequest(transaction, _profileId, controlId, desired),
            transaction => RazerGamepadProtocol.CreateGetCircularityRequest(transaction, _profileId, controlId),
            response => (RazerGamepadCircularity)RazerGamepadProtocol.ReadCircularity(response, controlId),
            circularity,
            cancellationToken);

    public Task SetTriggerRangeAsync(
        byte controlId,
        byte start,
        byte end,
        CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "trigger range",
            (transaction, desired) => RazerGamepadProtocol.CreateSetTriggerRangeRequest(
                transaction, _profileId, controlId, desired.Item1, desired.Item2),
            transaction => RazerGamepadProtocol.CreateGetTriggerRangeRequest(transaction, _profileId, controlId),
            response => RazerGamepadProtocol.ReadTriggerRange(response, controlId),
            (start, end),
            cancellationToken);

    public Task SetTriggerModeAsync(
        byte controlId,
        RazerGamepadTriggerMode mode,
        CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "trigger mode",
            (transaction, desired) => RazerGamepadProtocol.CreateSetTriggerModeRequest(
                transaction, _profileId, controlId, (byte)desired),
            transaction => RazerGamepadProtocol.CreateGetTriggerModeRequest(transaction, _profileId, controlId),
            response => (RazerGamepadTriggerMode)RazerGamepadProtocol.ReadTriggerMode(response, controlId),
            mode,
            cancellationToken);

    public Task SetDigitalTriggerAsync(
        byte controlId,
        RazerGamepadDigitalTrigger trigger,
        CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "digital trigger",
            (transaction, desired) => RazerGamepadProtocol.CreateSetDigitalTriggerRequest(
                transaction, _profileId, controlId, desired),
            transaction => RazerGamepadProtocol.CreateGetDigitalTriggerRequest(transaction, _profileId, controlId),
            response => RazerGamepadProtocol.ReadDigitalTrigger(response, controlId),
            trigger,
            cancellationToken);

    public Task SetIgnoreDPadDiagonalsAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "ignore d-pad diagonals",
            (transaction, desired) => RazerGamepadProtocol.CreateSetIgnoreDPadDiagonalsRequest(transaction, _profileId, desired),
            transaction => RazerGamepadProtocol.CreateGetIgnoreDPadDiagonalsRequest(transaction, _profileId),
            response => RazerGamepadProtocol.ReadIgnoreDPadDiagonals(response) != 0,
            enabled,
            cancellationToken);

    public Task SetSocdAsync(RazerGamepadSocdMode mode, CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "socd",
            (transaction, desired) => RazerGamepadProtocol.CreateSetSocdRequest(transaction, _profileId, desired),
            transaction => RazerGamepadProtocol.CreateGetSocdRequest(transaction, _profileId),
            response => RazerGamepadProtocol.ReadSocdMode(response),
            mode,
            cancellationToken);

    public async Task SetVibrationScaleAsync(byte scale, CancellationToken cancellationToken = default)
    {
        var previous = await TryReadAsync(
            "vibration scale",
            transaction => RazerGamepadProtocol.CreateGetVibrationScaleRequest(transaction, _profileId, 5),
            response => RazerGamepadProtocol.ReadVibrationScale(response, 5),
            cancellationToken).ConfigureAwait(false);
        if (previous.Ok && previous.Value == scale) return;

        try
        {
            foreach (var motorId in VibrationMotorIds)
            {
                await QueryAsync(
                    transaction => RazerGamepadProtocol.CreateSetVibrationScaleRequest(
                        transaction, _profileId, motorId, scale),
                    $"set vibration {motorId}",
                    cancellationToken).ConfigureAwait(false);
            }

            var actual = RazerGamepadProtocol.ReadVibrationScale(
                await QueryAsync(
                    transaction => RazerGamepadProtocol.CreateGetVibrationScaleRequest(transaction, _profileId, 5),
                    "read back vibration",
                    cancellationToken).ConfigureAwait(false),
                5);
            if (actual != scale)
            {
                throw new InvalidOperationException($"Vibration readback was {actual}, expected {scale}.");
            }
        }
        catch
        {
            if (previous.Ok)
            {
                foreach (var motorId in VibrationMotorIds)
                {
                    try
                    {
                        await QueryAsync(
                            transaction => RazerGamepadProtocol.CreateSetVibrationScaleRequest(
                                transaction, _profileId, motorId, previous.Value),
                            $"restore vibration {motorId}",
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (IsExpected(exception))
                    {
                        _diagnostic?.Invoke($"gamepad restore vibration failed: {exception.Message}");
                    }
                }
            }

            throw;
        }
    }

    public Task SetAdcMaskAsync(ushort mask, CancellationToken cancellationToken = default) =>
        WriteVerifiedAsync(
            "adc mask",
            (transaction, desired) => RazerGamepadProtocol.CreateSetAdcMaskRequest(transaction, desired),
            RazerGamepadProtocol.CreateGetAdcMaskRequest,
            RazerGamepadProtocol.ReadAdcMask,
            mask,
            cancellationToken);

    public async Task<RazerGamepadButtonAssignment> ReadButtonAssignmentAsync(
        byte buttonId,
        RazerGamepadButtonMode mode = RazerGamepadButtonMode.Normal,
        CancellationToken cancellationToken = default) =>
        RazerGamepadProtocol.ReadButtonAssignment(
            await QueryAsync(
                transaction => RazerGamepadProtocol.CreateGetButtonAssignmentRequest(
                    transaction, _profileId, buttonId, mode),
                $"read button {buttonId}",
                cancellationToken).ConfigureAwait(false));

    public async Task SetButtonAssignmentAsync(
        byte buttonId,
        RazerGamepadButtonMode mode,
        RazerGamepadFunctionData assignment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Size > 5 || assignment.Data.Length > 5)
        {
            throw new ArgumentException("Button-assignment data is limited to five bytes.", nameof(assignment));
        }

        RazerGamepadButtonAssignment? previous = null;
        try
        {
            previous = await ReadButtonAssignmentAsync(buttonId, mode, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"gamepad read button {buttonId} failed: {exception.Message}");
        }

        if (previous is not null && Matches(previous, assignment)) return;

        try
        {
            await WriteButtonAssignmentAsync(buttonId, mode, assignment, cancellationToken).ConfigureAwait(false);
            var actual = await ReadButtonAssignmentAsync(buttonId, mode, cancellationToken).ConfigureAwait(false);
            if (!Matches(actual, assignment))
            {
                throw new InvalidOperationException(
                    $"Button {buttonId} readback was {actual.Function}/{actual.DataSize}, " +
                    $"expected {assignment.Function}/{assignment.Size}.");
            }
        }
        catch
        {
            if (previous is not null)
            {
                try
                {
                    var restore = new RazerGamepadFunctionData(
                        previous.Function,
                        previous.DataSize,
                        previous.Data.ToArray());
                    await WriteButtonAssignmentAsync(buttonId, mode, restore, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    _diagnostic?.Invoke($"gamepad restore button {buttonId} failed: {exception.Message}");
                }
            }

            throw;
        }
    }

    public async Task<RazerGamepadCalibration> ReadCalibrationAsync(
        RazerGamepadCalibrationControl control,
        CancellationToken cancellationToken = default) =>
        RazerGamepadProtocol.ReadCalibration(
            await QueryAsync(
                transaction => RazerGamepadProtocol.CreateGetCalibrationRequest(transaction, control),
                "read calibration",
                cancellationToken).ConfigureAwait(false));

    private async Task WriteVerifiedAsync<T>(
        string label,
        Func<byte, T, byte[]> setRequest,
        Func<byte, byte[]> getRequest,
        Func<ReadOnlySpan<byte>, T> parse,
        T desired,
        CancellationToken cancellationToken) where T : struct
    {
        var previous = await TryReadAsync(label, getRequest, parse, cancellationToken).ConfigureAwait(false);
        if (previous.Ok && EqualityComparer<T>.Default.Equals(previous.Value, desired)) return;

        try
        {
            await QueryAsync(transaction => setRequest(transaction, desired), $"set {label}", cancellationToken)
                .ConfigureAwait(false);
            var actual = parse(await QueryAsync(getRequest, $"read back {label}", cancellationToken).ConfigureAwait(false));
            if (!EqualityComparer<T>.Default.Equals(actual, desired))
            {
                throw new InvalidOperationException($"{label} readback was {actual}, expected {desired}.");
            }
        }
        catch
        {
            if (previous.Ok)
            {
                try
                {
                    await QueryAsync(
                        transaction => setRequest(transaction, previous.Value),
                        $"restore {label}",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    _diagnostic?.Invoke($"gamepad restore {label} failed: {exception.Message}");
                }
            }

            throw;
        }
    }

    private async Task<(bool Ok, T Value)> TryReadAsync<T>(
        string label,
        Func<byte, byte[]> request,
        Func<ReadOnlySpan<byte>, T> parse,
        CancellationToken cancellationToken) where T : struct
    {
        try
        {
            return (true, parse(await QueryAsync(request, $"read {label}", cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"gamepad {label} unavailable: {exception.Message}");
            return (false, default);
        }
    }

    private async Task<byte[]> QueryAsync(
        Func<byte, byte[]> request,
        string label,
        CancellationToken cancellationToken)
    {
        var report = request(NextTransactionId());
        _diagnostic?.Invoke(
            $"gamepad {label}: size={report[6]}, class=0x{report[7]:X2}, command=0x{report[8]:X2}");
        return await _transport.QueryPreparedAsync(
            _devicePath, report, _deviceWait, cancellationToken).ConfigureAwait(false);
    }

    private Task<byte[]> WriteButtonAssignmentAsync(
        byte buttonId,
        RazerGamepadButtonMode mode,
        RazerGamepadFunctionData assignment,
        CancellationToken cancellationToken) =>
        QueryAsync(
            transaction => RazerGamepadProtocol.CreateSetButtonAssignmentRequest(
                transaction, _profileId, buttonId, mode, assignment.Function,
                assignment.Size, assignment.Data),
            $"set button {buttonId}",
            cancellationToken);

    private static bool Matches(
        RazerGamepadButtonAssignment actual,
        RazerGamepadFunctionData expected) =>
        actual.Function == expected.Function &&
        actual.DataSize == expected.Size &&
        actual.Data.Take(actual.DataSize).SequenceEqual(expected.Data.Take(expected.Size));

    private byte NextTransactionId()
    {
        if (_transactionId >= 0x1F)
        {
            _transactionId = 0;
        }
        else
        {
            _transactionId++;
        }

        return _transactionId;
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;
}
