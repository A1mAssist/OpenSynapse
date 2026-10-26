using System.Threading.Channels;
using OpenSynapse.Windows.Protocols;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerBleDevice(string Id, string Name);

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
            .Select(item => new RazerBleDevice(item.Id, item.Name))
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
        _notify.ValueChanged += OnValueChanged;
    }

    public static async Task<WindowsRazerBleTransport> ConnectAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var service = await GattDeviceService.FromIdAsync(deviceId).AsTask(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Razer BLE service could not be opened.");

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

            // Protocol 30 writes use GATT Write Request unless the caller asks otherwise.
            // shortcut: frames longer than the negotiated MTU are not fragmented; only the small control commands use this path.
            var result = await _write.WriteValueWithResultAsync(
                CryptographicBuffer.CreateFromByteArray(frame.ToArray()),
                GattWriteOption.WriteWithResponse).AsTask(cancellationToken).ConfigureAwait(false);
            if (result.Status != GattCommunicationStatus.Success)
            {
                return null;
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                return await _incoming.Reader.ReadAsync(timeoutSource.Token).ConfigureAwait(false);
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
            throw new InvalidOperationException($"The Razer BLE characteristic {uuid} is missing: {result.Status}.");
        }

        return result.Characteristics[0];
    }
}


