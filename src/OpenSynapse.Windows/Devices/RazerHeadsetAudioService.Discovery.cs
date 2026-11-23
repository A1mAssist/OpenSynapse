using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    internal static IReadOnlyList<RazerHeadsetAudioConnection> SelectConnections(
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> interfaces,
        IReadOnlyList<WindowsUsbDevice> usbDevices,
        Action<string>? diagnostic = null)
    {
        var result = new List<RazerHeadsetAudioConnection>();
        foreach (var group in interfaces
                     .Where(item => IsSupported(item.ProductId) &&
                         RazerHeadsetChannelCatalog.Resolve(item.ProductId) != RazerHeadsetChannel.DedicatedBlackShark)
                     .GroupBy(item => (item.ProductId, PhysicalDeviceKey: AudioPhysicalKey(item)))
                     .OrderBy(group => group.Key.ProductId))
        {
            var channel = RazerHeadsetChannelCatalog.Resolve(group.Key.ProductId);
            var chosen = group
                .OrderByDescending(item => MatchesChannel(channel, item))
                .ThenByDescending(item => RazerAudioFeatureProtocol.InterfaceEvidenceRank(item.ProductId, item.FeatureReportIds))
                .ThenByDescending(item => item.Access == DeviceAccessState.Available)
                .ThenByDescending(item => item.UsagePage == 0xFF14)
                .First();
            var hasControl = MatchesChannel(channel, chosen);
            if (hasControl && RazerAudioFeatureProtocol.HasProductCapabilities(chosen.ProductId) &&
                RazerAudioFeatureProtocol.InterfaceEvidenceRank(chosen.ProductId, chosen.FeatureReportIds) < 2 &&
                group.Where(item => MatchesChannel(channel, item))
                    .Select(item => item.DevicePath).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            {
                // FindAllAsync resolves ambiguous collections with a bounded firmware read.
                hasControl = false;
            }
            if (!hasControl && RazerAudioFeatureProtocol.HasProductCapabilities(chosen.ProductId))
            {
                var requiredId = RazerAudioFeatureProtocol.IsNommoV2(chosen.ProductId) ? 7 : 16;
                diagnostic?.Invoke($"headset-audio unresolved pid={chosen.ProductId:X4}: " +
                    $"could not confirm a matching feature collection for report-id {requiredId}");
            }
            diagnostic?.Invoke(
                $"headset-audio selected {(hasControl ? "" : "unresolved ")}{channel} " +
                $"pid={chosen.ProductId:X4} product='{chosen.ProductName}' " +
                $"path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
                $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} " +
                $"in={chosen.InputReportByteLength} out={chosen.OutputReportByteLength} " +
                $"feature={chosen.FeatureReportByteLength} " +
                $"featureIds={(chosen.FeatureReportIds is { } ids ? string.Join(",", ids.Order()) : "unknown")} " +
                $"access={chosen.Access}");
            result.Add(new RazerHeadsetAudioConnection(
                chosen.DevicePath, chosen.ProductId, Products[chosen.ProductId],
                chosen.FeatureReportByteLength, chosen.ContainerId ?? ToContainerId(group.Key.PhysicalDeviceKey),
                ToTransport(channel), HasControlInterface: hasControl,
                OutputReportByteLength: chosen.OutputReportByteLength,
                PhysicalDeviceKey: group.Key.PhysicalDeviceKey,
                UsbParentDeviceId: chosen.UsbParentDeviceId));
        }

        // A missing control collection must stay visible even when another headset works.
        foreach (var device in usbDevices.OrderBy(item => item.ProductId))
        {
            if (CreateUsbFallback(device) is not { } fallback) continue;
            if (result.Any(item => IsSamePhysicalDevice(item, fallback))) continue;
            diagnostic?.Invoke(
                $"headset-audio selected usb-only pid={device.ProductId:X4} path={device.InstanceId}");
            result.Add(fallback);
        }
        return result.OrderByDescending(item => item.HasControlInterface)
            .ThenBy(item => item.ProductId).ToArray();
    }

    private static string AudioPhysicalKey(WindowsHidDiscovery.HidInterfaceDescriptor item) =>
        (string.IsNullOrWhiteSpace(item.PhysicalDeviceKey)
            ? WindowsHidDiscovery.GetPhysicalDeviceKey(item.DevicePath) : item.PhysicalDeviceKey).ToUpperInvariant();

    internal static async Task<IReadOnlyList<RazerHeadsetAudioConnection>> SelectConnectionsAsync(
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> interfaces,
        IReadOnlyList<WindowsUsbDevice> usbDevices,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default,
        Func<WindowsHidDiscovery.HidInterfaceDescriptor, CancellationToken, Task<bool>>? firmwareProbe = null,
        TimeSpan? candidateTimeout = null,
        TimeSpan? deviceTimeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = SelectConnections(interfaces, usbDevices, diagnostic).ToArray();
        firmwareProbe ??= (item, token) => new RazerHeadsetAudioService(
            new RazerFeatureTransport(), item.DevicePath, TimeSpan.FromMilliseconds(20), diagnostic,
            productId: item.ProductId).ProbeFeatureAudioFirmwareAsync(token);
        for (var index = 0; index < result.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = result[index];
            if (connection.HasControlInterface || !RazerAudioFeatureProtocol.HasProductCapabilities(connection.ProductId)) continue;
            var candidates = interfaces.Where(item => item.ProductId == connection.ProductId &&
                    AudioPhysicalKey(item) == connection.PhysicalDeviceKey &&
                    MatchesChannel(RazerHeadsetChannel.Feature91, item))
                .DistinctBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(item => RazerAudioFeatureProtocol.InterfaceEvidenceRank(item.ProductId, item.FeatureReportIds))
                .ThenByDescending(item => item.Access == DeviceAccessState.Available).ToArray();
            if (candidates.Length < 2) continue;

            using var deviceBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deviceBudget.CancelAfter(deviceTimeout ?? TimeSpan.FromSeconds(3));
            foreach (var candidate in candidates.Where(item => item.Access == DeviceAccessState.Available))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (deviceBudget.IsCancellationRequested) break;
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deviceBudget.Token);
                attempt.CancelAfter(candidateTimeout ?? TimeSpan.FromMilliseconds(750));
                try
                {
                    if (!await firmwareProbe(candidate, attempt.Token).WaitAsync(attempt.Token).ConfigureAwait(false)) continue;
                    attempt.Token.ThrowIfCancellationRequested();
                    result[index] = connection with
                    {
                        DevicePath = candidate.DevicePath,
                        FeatureReportByteLength = candidate.FeatureReportByteLength,
                        OutputReportByteLength = candidate.OutputReportByteLength,
                        ContainerId = candidate.ContainerId ?? connection.ContainerId,
                        UsbParentDeviceId = candidate.UsbParentDeviceId ?? connection.UsbParentDeviceId,
                        HasControlInterface = true,
                    };
                    diagnostic?.Invoke($"headset-audio firmware confirmed pid={candidate.ProductId:X4} " +
                        $"path={RazerFeatureTransport.DescribeDevicePath(candidate.DevicePath)}");
                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    diagnostic?.Invoke($"headset-audio firmware probe deadline pid={candidate.ProductId:X4}");
                }
                catch (Exception exception) when (IsExpected(exception) || exception is TimeoutException)
                {
                    diagnostic?.Invoke($"headset-audio firmware probe failed pid={candidate.ProductId:X4}: {exception.Message}");
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result.OrderByDescending(item => item.HasControlInterface).ThenBy(item => item.ProductId).ToArray();
    }

    internal static IReadOnlyList<RazerHeadsetAudioConnection> MergeSerialConnections(
        IReadOnlyList<RazerHeadsetAudioConnection> connections,
        IReadOnlyList<RazerNxpSerialDevice> serialDevices,
        Action<string>? diagnostic = null)
    {
        var result = connections.ToList();
        foreach (var serial in serialDevices)
        {
            var connection = new RazerHeadsetAudioConnection(serial.DevicePath, serial.ProductId,
                Products[serial.ProductId], 0, serial.ContainerId, RazerHeadsetAudioTransport.NxpSerial,
                PhysicalDeviceKey: WindowsHidDiscovery.GetPhysicalDeviceKey(serial.DevicePath,
                    Guid.TryParse(serial.ContainerId, out var container) ? container : null, serial.UsbParentDeviceId),
                UsbParentDeviceId: serial.UsbParentDeviceId);
            if (result.Any(item => item.HasControlInterface && IsSamePhysicalDevice(item, connection))) continue;
            result.RemoveAll(item => !item.HasControlInterface && IsSamePhysicalDevice(item, connection));
            result.Add(connection);
            diagnostic?.Invoke($"headset-audio selected NxpSerial pid={serial.ProductId:X4} path={serial.DevicePath}");
        }
        return result.OrderByDescending(item => item.HasControlInterface)
            .ThenBy(item => item.ProductId).ToArray();
    }

    private static bool IsSamePhysicalDevice(RazerHeadsetAudioConnection left, RazerHeadsetAudioConnection right)
    {
        if (left.ProductId != right.ProductId) return false;
        if (ValidContainer(left.ContainerId) is { } leftId && ValidContainer(right.ContainerId) is { } rightId)
            return leftId == rightId;
        if (!string.IsNullOrWhiteSpace(left.UsbParentDeviceId) && !string.IsNullOrWhiteSpace(right.UsbParentDeviceId))
            return StringComparer.OrdinalIgnoreCase.Equals(left.UsbParentDeviceId, right.UsbParentDeviceId);
        if (!string.IsNullOrWhiteSpace(left.PhysicalDeviceKey) && !string.IsNullOrWhiteSpace(right.PhysicalDeviceKey) &&
            StringComparer.OrdinalIgnoreCase.Equals(left.PhysicalDeviceKey, right.PhysicalDeviceKey)) return true;
        return StringComparer.OrdinalIgnoreCase.Equals(left.DevicePath, right.DevicePath);

        static Guid? ValidContainer(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty &&
            id != new Guid("00000000-0000-0000-FFFF-FFFFFFFFFFFF") ? id : null;
    }
}
