using System.Threading.Channels;
using OpenSynapse.Windows.Protocols;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;

namespace OpenSynapse.Windows.Devices;

/// <summary>Finds paired headsets that expose the AudioAWKittyBLE GATT service.</summary>
public static class RazerAwBleDiscovery
{
    public static async Task<IReadOnlyList<RazerBleDevice>> FindAsync(
        CancellationToken cancellationToken = default)
    {
        var selector = GattDeviceService.GetDeviceSelectorFromUuid(RazerAwBleProtocol.ServiceUuid);
        var found = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken).ConfigureAwait(false);
        return found
            .Select(item => new RazerBleDevice(item.Id, item.Name))
            .ToArray();
    }
}

/// <summary>
/// GATT client for the AudioAWKittyBLE service. The middleware writes without a
/// response and reads only from the notify characteristic; every battery reply is
/// one small frame, so no protocol-30 header or CRC is involved.
/// </summary>
public sealed class WindowsRazerAwBleTransport : IRazerBleTransport
{
    private readonly GattDeviceService _service;
    private readonly GattCharacteristic _write;
    private readonly GattCharacteristic _notify;
    private readonly SemaphoreSlim _exchange = new(1, 1);
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();

    private WindowsRazerAwBleTransport(
        GattDeviceService service,
        GattCharacteristic write,
        GattCharacteristic notify)
    {
        _service = service;
        _write = write;
        _notify = notify;
        _notify.ValueChanged += OnValueChanged;
    }

    public static async Task<WindowsRazerAwBleTransport> ConnectAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var service = await GattDeviceService.FromIdAsync(deviceId).AsTask(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Razer audio BLE service could not be opened.");

        var write = await FindCharacteristicAsync(
            service, RazerAwBleProtocol.WriteCharacteristicUuid, cancellationToken).ConfigureAwait(false);
        var notify = await FindCharacteristicAsync(
            service, RazerAwBleProtocol.NotifyCharacteristicUuid, cancellationToken).ConfigureAwait(false);

        var status = await notify.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cancellationToken).ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException(
                $"Subscribing to the Razer audio BLE notify characteristic failed: {status}.");
        }

        return new WindowsRazerAwBleTransport(service, write, notify);
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

            var result = await _write.WriteValueWithResultAsync(
                CryptographicBuffer.CreateFromByteArray(frame.ToArray()),
                GattWriteOption.WriteWithoutResponse).AsTask(cancellationToken).ConfigureAwait(false);
            if (result.Status != GattCommunicationStatus.Success)
            {
                return null;
            }

            var expectedCommand = frame.Span[0];
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var buffer = new List<byte>(16);
            try
            {
                while (true)
                {
                    var fragment = await _incoming.Reader.ReadAsync(timeoutSource.Token).ConfigureAwait(false);
                    buffer.AddRange(fragment);
                    while (TryTakeFrame(buffer, out var reply))
                    {
                        if (reply.Length > 0 && reply[0] == expectedCommand)
                        {
                            return reply;
                        }
                    }

                    if (buffer.Count > 64)
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
        _incoming.Writer.TryComplete();
        _service.Dispose();
        _exchange.Dispose();
    }

    private static bool TryTakeFrame(List<byte> buffer, out byte[] reply)
    {
        reply = [];
        if (buffer.Count < RazerAwBleProtocol.ResponseHeaderLength + 1)
        {
            return false;
        }

        // A garbage byte before a valid header would stall the loop; drop it.
        if (buffer[1] != RazerAwBleProtocol.ResponseTransferType)
        {
            buffer.RemoveAt(0);
            return true;
        }

        var length = RazerAwBleProtocol.ResponseHeaderLength + buffer[2];
        if (buffer.Count < length)
        {
            return false;
        }

        reply = buffer.Take(length).ToArray();
        buffer.RemoveRange(0, length);
        return true;
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out var value);
        _incoming.Writer.TryWrite(value);
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
            throw new InvalidOperationException(
                $"The Razer audio BLE characteristic {uuid} is missing: {result.Status}.");
        }

        return result.Characteristics[0];
    }
}
