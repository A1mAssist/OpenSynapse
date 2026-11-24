using System.Collections.Frozen;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    /// <summary>
    /// Sweeps the read half of the HID feature command space for commands this catalog does not describe.
    /// The firmware answers 0x02 for a supported command and 0x05 for an unsupported one, so
    /// the sweep reveals a device's real capability table without sending state-changing writes.
    /// </summary>
    public async Task<string> ScanCommandSpaceAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        // Mice are scannable now that the sweep is read-only. The old guard existed when
        // the sweep still covered command 00:04, which puts a wireless mouse into a mode
        // where it never rejoins the 2.4 GHz link.
        var report = new StringBuilder();
        report.AppendLine($"generated: {DateTimeOffset.Now:O}");
        report.Append(HidCommandScan.FormatHeader(
            connection.Definition.DisplayName, connection.Definition.ProductId));

        var accepted = 0;
        foreach (var commandClass in HidCommandScan.ScanClasses)
        {
            // The high bit marks a device-to-host read. Never sweep the write half:
            // class 00 command 04 alone can change the device mode.
            for (var commandId = 0x80; commandId <= 0xFF; commandId++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = RazerFeatureReport.CreateRequest(
                    0x1F, 0x00, commandClass, (byte)commandId, ReadOnlySpan<byte>.Empty);
                byte[]? response;
                try
                {
                    response = await _transport.QueryPreparedForProductAsync(
                        connection.DevicePath!, connection.Definition.ProductId, request,
                        connection.Definition.DeviceWait,
                        cancellationToken, allowRemainingPacketsMismatch: true).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is NotSupportedException or RazerCommandRejectedException)
                {
                    continue;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception or IOException)
                {
                    continue;
                }

                var status = response![1];
                if (status != 0x05)
                {
                    accepted++;
                }

                report.AppendLine(HidCommandScan.FormatRow(commandClass, (byte)commandId, response));

            }
        }

        report.AppendLine();
        report.Append(CultureInfo.InvariantCulture, $"total accepted: {accepted}");
        report.AppendLine();
        report.AppendLine("## builders already in the bundled catalog");
        foreach (var builder in connection.Definition.Transactions.Keys
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            report.AppendLine(builder);
        }

        return report.ToString();
    }

    /// <summary>
    /// Sends the middleware-only GETs once per device and caches which command families
    /// answer, so a control appears only when the firmware really understands it. The cache
    /// keeps device-watch rescans from re-sending a HID request every few seconds.
    /// </summary>
    internal async Task<IReadOnlySet<RazerHidBackendCapability>> ProbeMiddlewareCapabilitiesAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return FrozenSet<RazerHidBackendCapability>.Empty;
        }

        var result = new HashSet<RazerHidBackendCapability>();
        var isProbeDefinition =
            definition.SourceCapabilities.Count == 0 && definition.Transactions.Count == 0;
        var buttonList = RazerHidMappingProtocol.GetButtonList();
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "mapping", buttonList.Report,
                response => RazerHidMappingProtocol.ParseButtonList(response).Any(id => id != 0),
                cancellationToken).ConfigureAwait(false))
        {
            result.UnionWith(MappingCapabilities);
        }
        else if (definition.Category == DeviceCategory.Keyboard &&
                 await ProbeKeyAssignmentAsync(definition, devicePath, cancellationToken).ConfigureAwait(false))
        {
            // Keypads such as 0244 keep their key ids in the product bundle and only answer
            // the key-assignment table, not the button-list GET.
            result.UnionWith(MappingCapabilities);
        }
        else
        {
            // 0083 and other OBM mice answer the assignment table even when the button
            // list GET is silent, so read one known assignment as the capability probe.
            var assignment = RazerHidMappingProtocol.GetAssignment(
                RazerHidMappingProtocol.ProfileId, 0x01, RazerHidMappingProtocol.NormalMode);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "mapping.assignment", assignment.Report,
                    response =>
                    {
                        _ = RazerHidMappingProtocol.ParseAssignment(response);
                        return true;
                    }, cancellationToken).ConfigureAwait(false))
            {
                result.UnionWith(MappingCapabilities);
            }
        }

        if (isProbeDefinition)
        {
            return result.ToFrozenSet();
        }

        if (definition.Category == DeviceCategory.Mouse)
        {
            var freeNotched = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(MiddlewareTransactionId);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "scroll.freeNotched", freeNotched, response =>
                {
                    _ = RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(response, freeNotched);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
            {
                result.Add(RazerHidBackendCapability.ScrollFreeNotchedModeRead);
                result.Add(RazerHidBackendCapability.ScrollFreeNotchedModeWrite);
            }

            // Profile 1 is the standard OBM slot; reads and writes resolve the active slot.
            var tactileMode = RazerScrollWheelProtocol.CreateGetScrollTactileModeRequest(
                MiddlewareTransactionId, RazerHidMappingProtocol.ProfileId);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "scroll.tactileMode", tactileMode, response =>
                {
                    _ = RazerScrollWheelProtocol.ParseScrollTactileMode(response, tactileMode);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
            {
                result.Add(RazerHidBackendCapability.ScrollTactileModeRead);
                result.Add(RazerHidBackendCapability.ScrollTactileModeWrite);
            }

            var accelerationLevel =
                RazerScrollWheelProtocol.CreateGetScrollVirtualAccelerationLevelRequest(MiddlewareTransactionId);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "scroll.accelerationLevel", accelerationLevel, response =>
                {
                    _ = RazerScrollWheelProtocol.ParseScrollVirtualAccelerationLevel(response, accelerationLevel);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
            {
                result.Add(RazerHidBackendCapability.ScrollAccelerationLevelRead);
                result.Add(RazerHidBackendCapability.ScrollAccelerationLevelWrite);
            }

        }

        await ProbeOnboardProfileAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeWirelessPairingAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeKnobAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeAnalogKeyAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeAnalogJoystickAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeOledAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeHapticScrollAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeScrollSmartReelAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeDpiStagesAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);

        if (RazerHidDeviceCatalog.IsMiddlewareMouseReadOnly(definition.ProductId))
            result.RemoveWhere(capability => capability.ToString().EndsWith("Write", StringComparison.Ordinal));
        return result.ToFrozenSet();
    }

    /// <summary>
    /// Sends one middleware GET and caches whether the firmware answered, so device-watch
    /// rescans do not re-send it. Shared by every protocol family that has no RazerHid
    /// capability of its own to gate on.
    /// </summary>
    private async Task<bool> ProbeMiddlewareAsync(
        ushort productId,
        string devicePath,
        TimeSpan deviceWait,
        string probe,
        byte[] request,
        Func<byte[], bool> accepts,
        CancellationToken cancellationToken,
        Action<byte[]?, Exception?>? observed = null)
    {
        var now = DateTimeOffset.UtcNow;
        var key = (devicePath.ToUpperInvariant(), productId, probe);
        if (_middlewareProbeCache.TryGetValue(key, out var expiresAt) && expiresAt > now)
        {
            return true;
        }

        var supported = false;
        byte[]? response = null;
        Exception? error = null;
        try
        {
            response = await _transport.QueryPreparedForProductAsync(
                devicePath, productId, request, deviceWait, cancellationToken).ConfigureAwait(false);
            supported = accepts(response);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or
            UnauthorizedAccessException or TimeoutException)
        {
            error = exception;
            WriteDiagnostic(productId, $"{probe} probe failed: {exception.GetType().Name}: {exception.Message}");
        }

        observed?.Invoke(response, error);
        if (supported)
        {
            _middlewareProbeCache[key] = now + MiddlewareProbePositiveTtl;
        }
        WriteDiagnostic(productId, $"{probe} probe: {supported}");
        return supported;
    }

    /// <summary>
    /// The OpenRazer tables list Smart Reel and DPI stages for products whose firmware
    /// never answers them (00D7 already returns 0x05 for the 0xB0 tactile trio), so both
    /// cards are gated on the device instead of on the catalog.
    /// </summary>
    internal async Task ProbeScrollSmartReelAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        ISet<RazerHidBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category != DeviceCategory.Mouse ||
            !definition.Transactions.ContainsKey("razer_chroma_misc_get_scroll_smart_reel"))
        {
            return;
        }

        var request = RazerHidMouseProtocol.GetSmartReel(definition);
        var answered = await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
            "scroll.smartReel", request.Report, response =>
            {
                _ = RazerHidMouseProtocol.ParseBooleanSecondArgument(response);
                return true;
            }, cancellationToken).ConfigureAwait(false);

        if (!answered)
        {
            result.Remove(RazerHidBackendCapability.SmartReelRead);
            result.Remove(RazerHidBackendCapability.SmartReelWrite);
            return;
        }

        result.Add(RazerHidBackendCapability.SmartReelRead);
        if (definition.Transactions.ContainsKey("razer_chroma_misc_set_scroll_smart_reel"))
        {
            result.Add(RazerHidBackendCapability.SmartReelWrite);
        }
    }

    internal async Task ProbeDpiStagesAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        ISet<RazerHidBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category != DeviceCategory.Mouse ||
            !definition.Transactions.ContainsKey("razer_chroma_misc_get_dpi_stages"))
        {
            return;
        }

        var request = RazerHidMouseProtocol.GetDpiStages(definition);
        var answered = await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
            "dpi.stages", request.Report, response =>
            {
                _ = RazerHidMouseProtocol.ParseDpiStages(definition, response);
                return true;
            }, cancellationToken).ConfigureAwait(false);

        if (!answered)
        {
            result.Remove(RazerHidBackendCapability.DpiStagesRead);
            result.Remove(RazerHidBackendCapability.DpiStagesWrite);
            return;
        }

        result.Add(RazerHidBackendCapability.DpiStagesRead);
        if (definition.Transactions.ContainsKey("razer_chroma_misc_set_dpi_stages"))
        {
            result.Add(RazerHidBackendCapability.DpiStagesWrite);
        }
    }

    private void InvalidateMiddlewareProbeCache(string devicePath)
    {
        var normalizedPath = devicePath.ToUpperInvariant();
        foreach (var key in _middlewareProbeCache.Keys.Where(key => key.DevicePath == normalizedPath))
        {
            _middlewareProbeCache.TryRemove(key, out _);
        }
    }

    private enum EndpointProbeOutcome { Answered, Unsupported, Failed }

    private readonly record struct EndpointProbeResult(EndpointProbeOutcome Outcome, string? Error);

    private async Task<EndpointProbeResult> ProbeEndpointAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        var probes = CreateSafeProbesWithDiagnostics(
            definition, message => WriteDiagnostic(definition.ProductId, message));
        // An uncatalogued device has no identity reads to try; the mapping button list
        // below is the only side-effect-free probe that can admit it.
        string? lastError = probes.Count == 0
            ? "No catalog GET is declared; trying the mapping button list."
            : null;
        foreach (var probe in probes)
        {
            WriteDiagnostic(definition.ProductId,
                $"safe probe builder={probe.BuilderName}, declared-txn=0x{probe.Report[2]:X2}, class=0x{probe.Report[7]:X2}, command=0x{probe.Report[8]:X2}");
            try
            {
                await _transport.QueryPreparedAsync(devicePath, probe.Report, definition.DeviceWait,
                    cancellationToken).ConfigureAwait(false);
                return new(EndpointProbeOutcome.Answered, null);
            }
            catch (NotSupportedException exception)
            {
                // A matched status 0x05 reply is a well-formed answer: the path is the
                // control channel even when this particular GET is not implemented.
                WriteDiagnostic(definition.ProductId,
                    $"safe probe builder={probe.BuilderName}, declared-txn=0x{probe.Report[2]:X2}, class=0x{probe.Report[7]:X2}, command=0x{probe.Report[8]:X2}: 0x05 unsupported, control channel answered");
                return new(EndpointProbeOutcome.Unsupported, exception.Message);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or COMException or
                InvalidOperationException or NotSupportedException or InvalidDataException or TimeoutException)
            {
                // One rejected or unavailable probe must not hide the later reads that
                // would still open this endpoint. Keep the raw reason for the connection
                // error and the classified reason in the diagnostic log.
                lastError = exception.Message;
                WriteDiagnostic(definition.ProductId,
                    $"safe probe failed builder={probe.BuilderName}, declared-txn=0x{probe.Report[2]:X2}, class=0x{probe.Report[7]:X2}, command=0x{probe.Report[8]:X2}: {DescribeProbeFailure(exception)}");
            }
        }

        if (definition.Category is DeviceCategory.Mouse or DeviceCategory.Keyboard)
        {
            // This read-only GET is the last-resort endpoint probe. Some mapping-only
            // firmware answers it when the catalog's identity probes time out, so the
            // runtime button list, not a static PID list, decides whether the page opens.
            var mapping = RazerHidMappingProtocol.GetButtonList();
            try
            {
                await _transport.QueryPreparedAsync(
                    devicePath,
                    mapping.Report,
                    definition.DeviceWait,
                    cancellationToken).ConfigureAwait(false);
                return new(EndpointProbeOutcome.Answered, null);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or
                COMException or InvalidOperationException or NotSupportedException or
                InvalidDataException or TimeoutException)
            {
                lastError = exception.Message;
                WriteDiagnostic(definition.ProductId,
                    $"mapping fallback probe class=0x{mapping.Report[7]:X2}, command=0x{mapping.Report[8]:X2}: {DescribeProbeFailure(exception)}");
            }
        }

        return new(EndpointProbeOutcome.Failed,
            lastError ?? "No side-effect-free GET answered on this endpoint.");
    }

    private static string DescribeProbeFailure(Exception exception) => exception switch
    {
        RazerCommandRejectedException => $"0x03 rejected: {exception.Message}",
        RazerDeviceTimeoutException { IsRetryableBusy: true } => $"busy/timeout: {exception.Message}",
        RazerDeviceTimeoutException => $"timeout: {exception.Message}",
        RazerReportMismatchException => $"txn/report mismatch: {exception.Message}",
        COMException => $"COM 0x{exception.HResult:X8}: {exception.Message}",
        Win32Exception win32 => $"Win32 {win32.NativeErrorCode}: {exception.Message}",
        _ => $"{exception.GetType().Name}: {exception.Message}",
    };

    internal async Task<(string? Path, string? Error)> ResolveEndpointPathAsync(
        RazerHidDeviceDefinition definition,
        IEnumerable<string> orderedPaths,
        CancellationToken cancellationToken = default)
    {
        string? lastError = null;
        string? unsupportedPath = null;
        foreach (var devicePath in orderedPaths)
        {
            WriteDiagnostic(definition.ProductId,
                $"probing path={RazerFeatureTransport.DescribeDevicePath(devicePath)}");
            try
            {
                var probe = await ProbeEndpointAsync(definition, devicePath, cancellationToken).ConfigureAwait(false);
                if (probe.Outcome == EndpointProbeOutcome.Answered)
                {
                    WriteDiagnostic(definition.ProductId,
                        $"resolved path={RazerFeatureTransport.DescribeDevicePath(devicePath)}");
                    return (devicePath, null);
                }

                lastError = probe.Error;
                if (probe.Outcome == EndpointProbeOutcome.Unsupported)
                {
                    // Keep looking for an endpoint that answers a real GET; a 0x05-only
                    // collection is still a usable control channel if nothing better exists.
                    unsupportedPath ??= devicePath;
                    WriteDiagnostic(definition.ProductId,
                        $"path={RazerFeatureTransport.DescribeDevicePath(devicePath)} answered only 0x05: {probe.Error}");
                    continue;
                }
                WriteDiagnostic(definition.ProductId,
                    $"no answer on path={RazerFeatureTransport.DescribeDevicePath(devicePath)}: {probe.Error}");
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or
                InvalidOperationException or NotSupportedException or InvalidDataException)
            {
                lastError = exception.Message;
                WriteDiagnostic(definition.ProductId,
                    $"probe failed path={RazerFeatureTransport.DescribeDevicePath(devicePath)}: {exception.GetType().Name}: {exception.Message}");
            }
        }
        if (unsupportedPath is not null)
        {
            WriteDiagnostic(definition.ProductId,
                $"resolved path={RazerFeatureTransport.DescribeDevicePath(unsupportedPath)} via 0x05");
            return (unsupportedPath, null);
        }
        return (null, lastError);
    }

    private void LogObservedInterface(WindowsHidDiscovery.HidInterfaceDescriptor item)
    {
        if (_catalog.Find(item.VendorId, item.ProductId) is null)
        {
            return;
        }
        WriteDiagnostic(item.ProductId,
            $"interface path={RazerFeatureTransport.DescribeDevicePath(item.DevicePath)}, input={item.InputReportByteLength}, output={item.OutputReportByteLength}, feature={item.FeatureReportByteLength}, usage=0x{item.UsagePage:X4}:0x{item.Usage:X4}, access={item.Access}");
    }

    private void WriteDiagnostic(ushort productId, string message) =>
        _diagnosticLog?.TryWrite($"razer-hid-{productId:X4}", message);

    /// <summary>
    /// Records why a control was skipped before it reached the device, so a write that
    /// never leaves the app is distinguishable from one the firmware refused.
    /// </summary>
    public void LogSkippedControl(ushort productId, string message) =>
        _diagnosticLog?.TryWrite($"razer-hid-{productId:X4}", message);

    internal static string DescribeRejected(IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> candidates) =>
        string.Join("; ", candidates.Select(candidate =>
            $"{RazerFeatureTransport.DescribeDevicePath(candidate.DevicePath)} " +
            $"feature={candidate.FeatureReportByteLength} " +
            $"usage=0x{candidate.UsagePage:X4}:0x{candidate.Usage:X4} " +
            $"access={candidate.Access}"));

    private static IReadOnlyList<RazerHidRequest> CreateSafeProbes(RazerHidDeviceDefinition definition) =>
        CreateSafeProbesWithDiagnostics(definition, null);

    private static IReadOnlyList<RazerHidRequest> CreateSafeProbesWithDiagnostics(
        RazerHidDeviceDefinition definition,
        Action<string>? diagnostic)
    {
        var probes = new List<RazerHidRequest>();
        AddIfResolved("razer_chroma_standard_get_firmware_version", () => RazerHidStandardProtocol.GetFirmware(definition));
        if (definition.Transactions.ContainsKey("razer_chroma_standard_get_firmware_version"))
        {
            try
            {
                var fallback = RazerHidStandardProtocol.GetFirmwareFallback(definition);
                probes.Add(new RazerHidRequest(
                    "razer_chroma_standard_get_firmware_version_fallback", fallback.Report));
            }
            catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
            {
                diagnostic?.Invoke(
                    $"safe probe builder=razer_chroma_standard_get_firmware_version_fallback could not be constructed: {exception.GetType().Name}: {exception.Message}");
            }
        }
        AddIfResolved("razer_chroma_standard_get_serial", () => RazerHidStandardProtocol.GetSerial(definition));
        AddIfResolved("razer_chroma_standard_get_device_mode", () => RazerHidStandardProtocol.GetDeviceMode(definition));
        AddIfResolved("razer_chroma_misc_get_battery_level", () => RazerHidMouseProtocol.GetBattery(definition));
        AddIfResolved("razer_chroma_misc_get_dpi_xy", () => RazerHidMouseProtocol.GetDpi(definition));
        AddIfResolved("razer_chroma_misc_get_dpi_xy_byte", () => RazerHidMouseProtocol.GetDpi(definition));
        AddIfResolved("razer_chroma_misc_get_polling_rate2", () => RazerHidMouseProtocol.GetPollingRate(definition));
        AddIfResolved("razer_chroma_misc_get_polling_rate", () => RazerHidMouseProtocol.GetPollingRate(definition));
        // The soundbar has no All region: the firmware only answers a real region id, so
        // probing with region 0 fails on every unit. Its brightness path reads the region
        // list first, and the firmware probe above is enough to open the channel.
        if (!RazerHidDeviceCatalog.IsLeviathanV2X(definition.ProductId))
        {
            AddIfResolved("razer_chroma_extended_matrix_get_brightness", () =>
                RazerHidLightingProtocol.GetBrightness(definition, null, null));
        }
        AddIfResolved("razer_chroma_standard_get_led_brightness", () =>
            RazerHidLightingProtocol.GetBrightness(definition, null, null));
        AddIfResolved("razer_chroma_misc_get_blade_brightness", () =>
            RazerHidLightingProtocol.GetBrightness(definition, null, null));
        AddIfResolved("razer_chroma_misc_get_dock_brightness", () =>
            RazerHidLightingProtocol.GetBrightness(definition, null, null));
        return probes;

        void AddIfResolved(string builder, Func<RazerHidRequest> create)
        {
            if (!definition.Transactions.ContainsKey(builder) || probes.Any(item => item.BuilderName == builder))
            {
                return;
            }

            try
            {
                probes.Add(create());
            }
            catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
            {
                // One builder that cannot be constructed must not hide the other safe
                // probes. Keep the reason in the log so an empty probe list is not
                // mistaken for a device that declared no reads at all.
                diagnostic?.Invoke(
                    $"safe probe builder={builder} could not be constructed: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
