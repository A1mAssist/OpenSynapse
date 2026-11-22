using System.Globalization;
using System.Threading.Channels;
using OpenSynapse.Windows.Protocols;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerBleDevice(string Id, string Name, ushort ProductId = 0)
{
    /// <summary>The device path carries the wired/BLE product id as <c>_PID&amp;00B5_REV&amp;</c>.</summary>
    public static ushort ParseProductId(string id)
    {
        var marker = id.IndexOf("_PID&", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 &&
            id.Length >= marker + 9 &&
            ushort.TryParse(
                id.AsSpan(marker + 5, 4),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var productId)
            ? productId
            : (ushort)0;
    }
}

/// <summary>One GATT round trip: write a protocol 30 frame and return the next notification.</summary>
public interface IRazerBleTransport : IDisposable
{
    Task<byte[]?> ExchangeAsync(
        ReadOnlyMemory<byte> frame,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public static class RazerBleDiscovery
{
    /// <summary>
    /// Devices that expose Razer's BLE service. The proprietary service UUID is the filter;
    /// the middleware additionally checks the vendor id in the advertisement, which Windows
    /// does not surface through this selector.
    /// </summary>
    public static async Task<IReadOnlyList<RazerBleDevice>> FindAsync(CancellationToken cancellationToken = default)
    {
        var selector = GattDeviceService.GetDeviceSelectorFromUuid(RazerBleProtocol30.ServiceUuid);
        var found = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken).ConfigureAwait(false);
        return found
            .Select(item => new RazerBleDevice(item.Id, item.Name, RazerBleDevice.ParseProductId(item.Id)))
            .ToArray();
    }
}

/// <summary>
/// GATT client for protocol 30: service 52401523, write 52401524, notify 52401525.
/// </summary>
public sealed class WindowsRazerBleTransport : IRazerBleTransport
{
    private readonly GattDeviceService _service;
    private readonly GattCharacteristic _write;
    private readonly GattCharacteristic _notify;
    private readonly GattWriteOption _writeOption;
    private readonly SemaphoreSlim _exchange = new(1, 1);
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();

    private WindowsRazerBleTransport(
        GattDeviceService service,
        GattCharacteristic write,
        GattCharacteristic notify)
    {
        _service = service;
        _write = write;
        _notify = notify;
        // Razer's BLEProtocol30 uses writeWithoutResponse whenever the characteristic
        // supports it, and a write request otherwise.
        _writeOption = (write.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse) != 0
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;
        _notify.ValueChanged += OnValueChanged;
    }

    public static async Task<WindowsRazerBleTransport> ConnectAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var service = await GattDeviceService.FromIdAsync(deviceId).AsTask(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Razer BLE service could not be opened.");
        // The middleware keeps the peripheral connected for the whole session; without
        // this Windows drops idle BLE mice between the page reads.
        service.Session.MaintainConnection = true;

        var write = await FindCharacteristicAsync(
            service, RazerBleProtocol30.WriteCharacteristicUuid, cancellationToken).ConfigureAwait(false);
        var notify = await FindCharacteristicAsync(
            service, RazerBleProtocol30.NotifyCharacteristicUuid, cancellationToken).ConfigureAwait(false);

        var status = await notify.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cancellationToken).ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Subscribing to the Razer BLE notify characteristic failed: {status}.");
        }

        return new WindowsRazerBleTransport(service, write, notify);
    }

    public async Task<byte[]?> ExchangeAsync(
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

            await WriteFrameAsync(frame, cancellationToken).ConfigureAwait(false);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var response = new RazerBleResponseBuffer(frame.ToArray());
            try
            {
                while (true)
                {
                    var fragment = await _incoming.Reader.ReadAsync(timeoutSource.Token).ConfigureAwait(false);
                    if (response.Append(fragment) is { } complete) return complete;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return response.Finish();
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
        _incoming.Writer.TryComplete();
        _service.Dispose();
        _exchange.Dispose();
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out var value);
        _incoming.Writer.TryWrite(value);
    }

    private async Task WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        // Razer writes the 8-byte header as its own GATT write and sends the payload
        // in 20-byte chunks; one long write exceeds the default BLE MTU.
        var headerLength = Math.Min(RazerBleProtocol30.RequestHeaderLength, frame.Length);
        await WriteAsync(frame.Slice(0, headerLength), cancellationToken).ConfigureAwait(false);
        for (var offset = headerLength; offset < frame.Length; offset += RazerBleProtocol30.PayloadChunkLength)
        {
            await WriteAsync(
                frame.Slice(offset, Math.Min(RazerBleProtocol30.PayloadChunkLength, frame.Length - offset)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        var result = await _write.WriteValueWithResultAsync(
            CryptographicBuffer.CreateFromByteArray(value.ToArray()),
            _writeOption).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new IOException($"GATT write failed: {result.Status}.");
        }
    }

    private static async Task<GattCharacteristic> FindCharacteristicAsync(
        GattDeviceService service,
        Guid uuid,
        CancellationToken cancellationToken)
    {
        var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
        {
            throw new InvalidOperationException($"The Razer BLE characteristic {uuid} is missing: {result.Status}.");
        }

        return result.Characteristics[0];
    }
}


