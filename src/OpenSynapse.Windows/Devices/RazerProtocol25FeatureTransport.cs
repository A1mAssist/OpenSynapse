using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerProtocol25FeatureTransport
{
    Task<byte[]> QueryAsync(byte[] request, CancellationToken cancellationToken);
}

/// <summary>
/// 64/65-byte protocol-25 audio feature transport. The bundle's
/// <c>hid.sendFeatureReport</c>/<c>hid.getFeatureReport</c> pair maps directly to
/// HidD_SetFeature/HidD_GetFeature; this is deliberately not RazerFeatureTransport,
/// whose prepared-report contract is fixed at 91 bytes.
/// </summary>
internal sealed class RazerProtocol25FeatureTransport(string devicePath)
    : IRazerProtocol25FeatureTransport
{
    private const int MaxOutAttempts = 20;
    private const int MaxInAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(3);
    private readonly SemaphoreSlim _gate = RazerFeatureTransport.EndpointGate(devicePath);

    public async Task<byte[]> QueryAsync(byte[] request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Length is not (64 or 65))
        {
            throw new ArgumentException("A protocol-25 audio request must be 64 or 65 bytes.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            throw new ArgumentException("A HID device path is required.", nameof(devicePath));
        }

        if (!await _gate.WaitAsync(ExchangeTimeout, cancellationToken).ConfigureAwait(false))
            throw new TimeoutException("The audio channel is still occupied by a pending HID call.");
        try
        {
            RazerFeatureTransport.ThrowIfNativeCallPending(devicePath);
            return await CancellableNativeCall.RunAsync(token => QueryCore(request, token),
                ExchangeTimeout, cancellationToken, task => RazerFeatureTransport.TrackPendingCall(devicePath, task))
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[] QueryCore(byte[] request, CancellationToken cancellationToken)
    {
        try
        {
            string? lastReadError = null;
            byte[]? lastResponse = null;
            for (var outAttempt = 0; outAttempt < MaxOutAttempts; outAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var handle = Open(devicePath);
                cancellationToken.ThrowIfCancellationRequested();
                if (!HidD_SetFeature(handle, request, request.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "Protocol-25 audio feature write failed.");
                }

                for (var inAttempt = 0; inAttempt < MaxInAttempts; inAttempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(RetryDelay);
                    cancellationToken.ThrowIfCancellationRequested();
                    var response = new byte[request.Length];
                    response[0] = request[0];
                    if (!HidD_GetFeature(handle, response, response.Length))
                    {
                        lastReadError = new Win32Exception(
                            Marshal.GetLastWin32Error(), "Protocol-25 audio feature read failed.").Message;
                        continue;
                    }
                    lastResponse = response;
                    if (!RazerProtocol25FeatureProtocol.Matches(request, response))
                    {
                        continue;
                    }

                    switch (response[1])
                    {
                        case 0x02:
                            return response;
                        case 0x01:
                        case 0x04:
                            continue;
                        case 0x00:
                        case 0x03:
                            break;
                        case 0x05:
                            throw new NotSupportedException("The audio device does not support this command.");
                        default:
                            throw new InvalidOperationException(
                                $"Protocol-25 audio command failed with status 0x{response[1]:X2}.");
                    }

                    break;
                }
            }

            throw new TimeoutException(
                (lastReadError ?? "The audio device did not return a matching protocol-25 response.") +
                $" Request header={Convert.ToHexString(request.AsSpan(0, 9))};" +
                $" last response header={(lastResponse is null ? "none" : Convert.ToHexString(lastResponse.AsSpan(0, 9)))}.");
        }
        catch (OperationCanceledException) { throw; }
    }

    private static SafeFileHandle Open(string path)
    {
        var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "Could not open the protocol-25 audio feature channel.");
        }

        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string path,
        uint access,
        uint share,
        IntPtr security,
        uint creation,
        uint flags,
        IntPtr template);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] report, int length);
}
