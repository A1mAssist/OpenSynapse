using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerCoolingPadConnection(
    string DevicePath,
    ushort ProductId,
    ushort FeatureReportByteLength);

/// <summary>
/// Fan control for the Razer Laptop Cooling Pad (1532:0F43). The pad exposes one
/// thermal fan; speed is read and written through the existing rzDevice25 thermal
/// commands, and every write is verified by a read-back before it is reported.
/// </summary>
public sealed partial class RazerCoolingPadService
{
    public const string DeviceName = "Razer Laptop Cooling Pad";
    public static readonly TimeSpan DefaultDeviceWait = TimeSpan.FromMilliseconds(500);

    private readonly IRazerFeatureTransport _transport;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;

    internal RazerCoolingPadService(
        IRazerFeatureTransport transport,
        string devicePath,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        DevicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
    }

    public string DevicePath { get; }

    public ushort ProductId => RazerCoolingPadProtocol.ProductId;

    public async Task<int> ReadSpeedRpmAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await QueryAsync(
                RazerCoolingPadProtocol.CreateGetSpeedRequest(),
                "read speed",
                cancellationToken).ConfigureAwait(false);
            return RazerCoolingPadProtocol.ParseSpeedRpm(response);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException($"Cooling pad speed read failed: {exception.Message}", exception);
        }
    }

    public async Task<int> SetSpeedRpmAsync(int rpm, CancellationToken cancellationToken = default)
    {
        // Validate the target before reading the device so an invalid value touches nothing.
        _ = RazerCoolingPadProtocol.CreateSetSpeedRequest(rpm);
        int? original = null;
        try
        {
            original = await ReadSpeedRpmAsync(cancellationToken).ConfigureAwait(false);
            await QueryAsync(
                RazerCoolingPadProtocol.CreateSetSpeedRequest(rpm),
                "set speed",
                cancellationToken).ConfigureAwait(false);
            var actual = await ReadSpeedRpmAsync(cancellationToken).ConfigureAwait(false);
            if (actual != rpm)
            {
                throw new InvalidOperationException(
                    $"Cooling pad speed readback mismatch: wrote {rpm}, read {actual} RPM.");
            }

            _diagnostic?.Invoke($"cooling pad speed set to {rpm} RPM (readback {actual})");
            return actual;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (original is { } restore && restore != rpm && CanRestore(restore))
            {
                try
                {
                    await QueryAsync(
                        RazerCoolingPadProtocol.CreateSetSpeedRequest(restore),
                        "restore speed",
                        cancellationToken).ConfigureAwait(false);
                    _diagnostic?.Invoke($"cooling pad speed restored to {restore} RPM after a failed update");
                }
                catch (Exception restoreException) when (IsExpected(restoreException))
                {
                    _diagnostic?.Invoke($"cooling pad speed restore failed: {restoreException.Message}");
                }
            }

            throw new InvalidOperationException($"Cooling pad speed update failed: {exception.Message}", exception);
        }
    }

    public static async Task<RazerCoolingPadConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            RazerCoolingPadProtocol.VendorId,
            RazerFeatureReport.Length,
            cancellationToken).ConfigureAwait(false);
        var chosen = interfaces
            .Where(item => item.ProductId == RazerCoolingPadProtocol.ProductId)
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (chosen is null)
        {
            diagnostic?.Invoke(
                "cooling pad found no admitted 91-byte interface; " +
                $"enumerated={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}"))}");
            return null;
        }

        diagnostic?.Invoke(
            $"cooling pad selected pid={chosen.ProductId:X4} path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} access={chosen.Access}");
        return new RazerCoolingPadConnection(
            chosen.DevicePath,
            chosen.ProductId,
            chosen.FeatureReportByteLength);
    }

    public static async Task<RazerCoolingPadService?> ConnectAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await FindAsync(diagnostic, cancellationToken).ConfigureAwait(false);
        return connection is null
            ? null
            : new RazerCoolingPadService(
                new RazerFeatureTransport(),
                connection.DevicePath,
                DefaultDeviceWait,
                diagnostic);
    }

    private async Task<byte[]> QueryAsync(
        byte[] request,
        string label,
        CancellationToken cancellationToken)
    {
        _diagnostic?.Invoke(
            $"cooling pad {label}: size={request[6]}, class=0x{request[7]:X2}, command=0x{request[8]:X2}");
        return await _transport.QueryPreparedAsync(
            DevicePath,
            request,
            _deviceWait,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool CanRestore(int rpm) =>
        rpm is >= RazerCoolingPadProtocol.MinimumRpm and <= RazerCoolingPadProtocol.MaximumRpm &&
        rpm % RazerCoolingPadProtocol.RpmPerUnit == 0;

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException or ArgumentException;
}
