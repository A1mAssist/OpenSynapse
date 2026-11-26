using System.Threading.Channels;
using OpenSynapse.Windows.Protocols;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;

namespace OpenSynapse.Windows.Devices;

/// <summary>Finds paired AP-BLE audio devices on the three known GATT layouts.</summary>
public static class RazerApBleDiscovery
{
    public static async Task<IReadOnlyList<RazerBleDevice>> FindAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = new Dictionary<string, RazerBleDevice>(StringComparer.OrdinalIgnoreCase);
        var paired = await DeviceInformation.FindAllAsync(
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in paired)
        {
            if (!LooksLikeRazerAudio(item.Name))
            {
                continue;
            }

            try
            {
                using var bluetooth = await BluetoothLEDevice.FromIdAsync(item.Id).AsTask(cancellationToken)
                    .ConfigureAwait(false);
                if (bluetooth is null)
                {
                    continue;
                }

                var services = await bluetooth.GetGattServicesAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                if (services.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                var productId = (ushort)0;
                foreach (var service in services.Services)
                {
                    productId = RazerBleDevice.ParseProductId(service.DeviceId);
                    if (productId == 0)
                    {
                        productId = LookupProductId(item.Name);
                    }
                    if (RazerHeadsetChannelCatalog.IsKnown(productId) &&
                        RazerHeadsetChannelCatalog.Resolve(productId) != RazerHeadsetChannel.ApBle)
                    {
                        continue;
                    }
                    if (!await HasKnownLayoutAsync(service, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    var name = RazerHeadsetAudioService.Products.TryGetValue(productId, out var known)
                        ? known
                        : item.Name;
                    devices.TryAdd(
                        bluetooth.DeviceId,
                        new RazerBleDevice(bluetooth.DeviceId, name, productId));
                    break;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A paired classic endpoint without GATT services is not an error here.
            }
        }

        return devices.Values.ToArray();
    }

    internal static bool LooksLikeRazerAudio(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (AudioKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
         RazerHeadsetAudioService.Products.Values.Any(
             product => name.Contains(WithoutVendor(product), StringComparison.OrdinalIgnoreCase)));

    private static readonly string[] AudioKeywords =
    [
        "BlackShark", "Kraken", "Barracuda", "Hammerhead", "Leviathan", "Nommo",
        "Nari", "Thresher", "Kaira", "Opus", "Electra", "Mako", "Seiren", "Headset",
        // CTKD names from the iOS BLEDeviceManifest (e.g. RZR HHV30A6200).
        "RZR", "HHV3", "BCV2P",
    ];

    private static ushort LookupProductId(string name) =>
        RazerHeadsetAudioService.Products
            .Where(pair => RazerApBleHeadsetService.IsSupported(pair.Key) &&
                (string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase) ||
                 name.Contains(WithoutVendor(pair.Value), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(pair => pair.Value.Length)
            .Select(pair => pair.Key)
            .DefaultIfEmpty((ushort)0)
            .First();

    private static string WithoutVendor(string product) =>
        product.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase) ? product[6..] : product;

    private static async Task<bool> HasKnownLayoutAsync(
        GattDeviceService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success)
        {
            return false;
        }

        var uuids = result.Characteristics.Select(characteristic => characteristic.Uuid).ToHashSet();
        return RazerApBleProtocol.GattLayouts.Any(layout =>
            uuids.Contains(layout.Write) && uuids.Contains(layout.Notify));
    }
}

internal interface IRazerApBleTransport : IDisposable
{
    Task<RazerApBleResponse?> ExchangeAsync(
        ReadOnlyMemory<byte> frame, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>One AP-BLE round trip over a Razer audio GATT service.</summary>
public sealed class WindowsRazerApBleTransport : IRazerApBleTransport
{
    private readonly BluetoothLEDevice _bluetooth;
    private readonly GattDeviceService _service;
    private readonly GattCharacteristic _write;
    private readonly GattCharacteristic _notify;
    private readonly GattCharacteristic? _externalNotify;
    private readonly SemaphoreSlim _exchange = new(1, 1);
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();

    private WindowsRazerApBleTransport(
        BluetoothLEDevice bluetooth,
        GattDeviceService service,
        GattCharacteristic write,
        GattCharacteristic notify,
        GattCharacteristic? externalNotify)
    {
        _bluetooth = bluetooth;
        _service = service;
        _write = write;
        _notify = notify;
        _externalNotify = externalNotify;
        _notify.ValueChanged += OnValueChanged;
        if (_externalNotify is not null)
        {
            _externalNotify.ValueChanged += OnValueChanged;
        }
    }

    public static async Task<WindowsRazerApBleTransport> ConnectAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var bluetooth = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Bluetooth device could not be opened.");

        try
        {
            var services = await bluetooth.GetGattServicesAsync(BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken).ConfigureAwait(false);
            if (services.Status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Reading the GATT services failed: {services.Status}.");
            }

            foreach (var service in services.Services)
            {
                var layout = await FindLayoutAsync(service, cancellationToken).ConfigureAwait(false);
                if (layout is not { } found)
                {
                    service.Dispose();
                    continue;
                }

                var status = await found.Notify.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                if (status != GattCommunicationStatus.Success)
                {
                    service.Dispose();
                    throw new InvalidOperationException(
                        $"Subscribing to the Razer AP-BLE notify characteristic failed: {status}.");
                }

                if (found.ExternalNotify is { } external)
                {
                    try
                    {
                        await external.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Notify)
                            .AsTask(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // External notifications are optional; the command channel still works.
                    }
                }

                return new WindowsRazerApBleTransport(
                    bluetooth, service, found.Write, found.Notify, found.ExternalNotify);
            }

            throw new InvalidOperationException(
                "The device did not expose a known Razer AP-BLE characteristic pair.");
        }
        catch
        {
            bluetooth.Dispose();
            throw;
        }
    }

    public async Task<RazerApBleResponse?> ExchangeAsync(
        ReadOnlyMemory<byte> frame,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await _exchange.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (_incoming.Reader.TryRead(out _))
            {
                // Drop notifications left over from an earlier command.
            }

            var option = _write.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                ? GattWriteOption.WriteWithResponse
                : GattWriteOption.WriteWithoutResponse;
            var result = await _write.WriteValueWithResultAsync(
                CryptographicBuffer.CreateFromByteArray(frame.ToArray()),
                option).AsTask(cancellationToken).ConfigureAwait(false);
            if (result.Status != GattCommunicationStatus.Success)
            {
                return null;
            }

            var expectedRouting = frame.Span[1];
            var expectedCommand = (ushort)((frame.Span[6] << 8) | frame.Span[7]);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var buffer = new List<byte>(64);
            try
            {
                while (true)
                {
                    var fragment = await _incoming.Reader.ReadAsync(timeoutSource.Token)
                        .ConfigureAwait(false);
                    buffer.AddRange(fragment);
                    while (RazerApBleProtocol.TryTakeResponse(buffer, out var reply))
                    {
                        if (reply.Status == (byte)RazerApBleStatus.Broadcast)
                        {
                            continue;
                        }

                        if (reply.Routing == expectedRouting && reply.Command == expectedCommand)
                        {
                            return reply;
                        }
                    }

                    if (buffer.Count > 4096)
                    {
                        return null;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
        finally
        {
            _exchange.Release();
        }
    }

    public void Dispose()
    {
        _notify.ValueChanged -= OnValueChanged;
        if (_externalNotify is not null)
        {
            _externalNotify.ValueChanged -= OnValueChanged;
        }

        _incoming.Writer.TryComplete();
        _service.Dispose();
        _bluetooth.Dispose();
        _exchange.Dispose();
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out var value);
        _incoming.Writer.TryWrite(value);
    }

    private static async Task<Layout?> FindLayoutAsync(
        GattDeviceService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success)
        {
            return null;
        }

        var byUuid = result.Characteristics
            .GroupBy(characteristic => characteristic.Uuid)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var candidate in RazerApBleProtocol.GattLayouts)
        {
            if (!byUuid.TryGetValue(candidate.Write, out var write) ||
                !byUuid.TryGetValue(candidate.Notify, out var notify))
            {
                continue;
            }

            GattCharacteristic? external = null;
            if (candidate.External is { } externalUuid)
            {
                byUuid.TryGetValue(externalUuid, out external);
            }

            return new Layout(write, notify, external);
        }

        return null;
    }

    private readonly record struct Layout(
        GattCharacteristic Write,
        GattCharacteristic Notify,
        GattCharacteristic? ExternalNotify);
}
