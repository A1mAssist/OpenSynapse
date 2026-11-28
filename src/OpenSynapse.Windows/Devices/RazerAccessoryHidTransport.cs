using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Raw feature-report exchange for standalone accessories. Protocol-25 accessories use
/// 65-byte reports, while the 91-byte family keeps the legacy layout; both are sent with
/// HidD_SetFeature/HidD_GetFeature because their response headers are not guaranteed to
/// match the request verbatim.
/// </summary>
internal sealed class RazerAccessoryHidTransport(string devicePath)
{
    private const int MaxOutAttempts = 20;
    private const int MaxInAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(3);
    private readonly SemaphoreSlim _gate = RazerFeatureTransport.EndpointGate(devicePath);

    public async Task<byte[]> QueryAsync(
        byte[] request,
        Func<byte[], bool> isMatch,
        bool validateStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(isMatch);
        if (request.Length is not (64 or 65 or 91))
        {
            throw new ArgumentException("An accessory feature report must be 64, 65 or 91 bytes.", nameof(request));
        }

        if (!await _gate.WaitAsync(ExchangeTimeout, cancellationToken).ConfigureAwait(false))
            throw new TimeoutException("The accessory channel is still occupied by a pending HID call.");
        try
        {
            RazerFeatureTransport.ThrowIfNativeCallPending(devicePath);
            return await CancellableNativeCall.RunAsync(
                token => QueryCore(request, isMatch, validateStatus, token), ExchangeTimeout,
                cancellationToken, task => RazerFeatureTransport.TrackPendingCall(devicePath, task))
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[] QueryCore(byte[] request, Func<byte[], bool> isMatch,
        bool validateStatus, CancellationToken cancellationToken)
    {
        try
        {
            string? lastReadError = null;
            for (var outAttempt = 0; outAttempt < MaxOutAttempts; outAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var handle = Open(devicePath);
                cancellationToken.ThrowIfCancellationRequested();
                if (!HidD_SetFeature(handle, request, request.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Accessory feature write failed.");
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
                            Marshal.GetLastWin32Error(),
                            "Accessory feature read failed.").Message;
                        continue;
                    }

                    if (!isMatch(response) || response[0] != request[0] || response[2] != request[2]) continue;
                    if (IsSuccessfulResponse(request, response, validateStatus)) return response;
                }
            }

            throw new TimeoutException(
                lastReadError ?? "The accessory did not return a matching feature report.");
        }
        catch (OperationCanceledException) { throw; }
    }

    internal static bool IsSuccessfulResponse(byte[] request, byte[] response, bool validateStatus = true)
    {
        if (response.Length != request.Length || response[0] != request[0] ||
            response[2] != request[2] || response[7] != request[7] || response[8] != request[8]) return false;
        if (!validateStatus) return true;
        return response[1] switch
        {
            2 => true,
            1 or 4 => false,
            5 => throw new NotSupportedException("The accessory does not support this command."),
            _ => throw new InvalidOperationException($"Accessory command failed with status 0x{response[1]:X2}."),
        };
    }

    private static SafeFileHandle Open(string path)
    {
        var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not open the accessory feature channel.");
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
