using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerElsaTransport(string devicePath, int inputLength, int outputLength)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _gate = RazerFeatureTransport.EndpointGate(devicePath);

    internal async Task<byte[]> QueryAsync(byte[] request, bool acknowledgement, CancellationToken cancellationToken)
    {
        if (request.Length != 65 || request[0] != 0 || request[2] != 1 || inputLength is not (64 or 65) || outputLength < 65)
            throw new ArgumentException("Elsa needs a 65-byte report-0 request and a 64-byte input payload.");
        if (!await _gate.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false))
            throw new TimeoutException("Elsa still has a pending HID operation.");
        try
        {
            RazerFeatureTransport.ThrowIfNativeCallPending(devicePath);
            return await CancellableNativeCall.RunAsync(token =>
            {
                using var handle = CreateFile(devicePath, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open Elsa.");
                token.ThrowIfCancellationRequested();
                var output = RazerKrakenBaseProtocol.PadOutputReport(request, outputLength);
                if (!WriteFile(handle, output, output.Length, out var written, IntPtr.Zero) || written != output.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Elsa output report failed.");
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var input = new byte[inputLength];
                    if (!ReadFile(handle, input, input.Length, out var read, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Elsa input report failed.");
                    var response = NormalizeInput(input.AsSpan(0, read));
                    if (!RazerThermalAccessoryProtocol.MatchesHanbo(response, request[1], request[2])) continue;
                    if (acknowledgement) RazerThermalAccessoryProtocol.ValidateHanboAck(response);
                    return response;
                }
            }, Timeout, cancellationToken, task => RazerFeatureTransport.TrackPendingCall(devicePath, task))
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static byte[] NormalizeInput(ReadOnlySpan<byte> report) => report.Length switch
    {
        64 => report.ToArray(),
        65 when report[0] == 0 => report[1..].ToArray(),
        _ => throw new InvalidDataException("Elsa input must contain exactly 64 payload bytes."),
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle handle, byte[] report, int length, out int written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle handle, byte[] report, int length, out int read, IntPtr overlapped);
}
