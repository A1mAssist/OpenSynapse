using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = Gates.GetOrAdd(devicePath, static _ => new SemaphoreSlim(1, 1));

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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? lastReadError = null;
            for (var outAttempt = 0; outAttempt < MaxOutAttempts; outAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var handle = Open(devicePath);
                if (!HidD_SetFeature(handle, request, request.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Accessory feature write failed.");
                }

                for (var inAttempt = 0; inAttempt < MaxInAttempts; inAttempt++)
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    var response = new byte[request.Length];
                    response[0] = request[0];
                    if (!HidD_GetFeature(handle, response, response.Length))
                    {
                        lastReadError = new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Accessory feature read failed.").Message;
                        continue;
                    }

                    if (validateStatus && !IsRetryable(response))
                    {
                        if (response[1] == 0x05)
                        {
                            throw new NotSupportedException(
                                "The accessory does not support this command.");
                        }
                    }

                    if (isMatch(response))
                    {
                        return response;
                    }

                    if (validateStatus && response[1] is 0x00 or 0x03)
                    {
                        break;
                    }
                }
            }

            throw new TimeoutException(
                lastReadError ?? "The accessory did not return a matching feature report.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsRetryable(byte[] response) => response[1] is 0x01 or 0x04;

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
