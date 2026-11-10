using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerValkyrieT3FeatureIo : IDisposable
{
    void Send(byte[] report);
    byte[] Read();
}

internal sealed class RazerValkyrieT3Transport : IRazerMxicHeadsetTransport
{
    private readonly IRazerValkyrieT3FeatureIo _io;
    private readonly int _reportLength;
    private readonly bool _makoX;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte _transaction;
    private bool _disposed;

    internal RazerValkyrieT3Transport(string path, bool makoX = false)
        : this(
            new NativeFeatureIo(
                path,
                makoX ? RazerValkyrieT3Protocol.MakoXReportLength : RazerValkyrieT3Protocol.ReportLength),
            makoX)
    {
    }

    internal RazerValkyrieT3Transport(IRazerValkyrieT3FeatureIo io, bool makoX = false)
    {
        _io = io;
        _makoX = makoX;
        _reportLength = makoX
            ? RazerValkyrieT3Protocol.MakoXReportLength
            : RazerValkyrieT3Protocol.ReportLength;
    }

    public async Task SendAsync(byte[] report, CancellationToken cancellationToken) =>
        _ = await QueryAsync(report, [], cancellationToken).ConfigureAwait(false);

    public async Task<byte[]?> QueryAsync(byte[] report, byte[] expectedHeader, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (report.Length != _reportLength || report[0] != 7)
                throw new ArgumentException(
                    _makoX
                        ? "A Mako X feature65/report7 request is required."
                        : "A Valkyrie T3 feature64/report7 request is required.",
                    nameof(report));
            var request = report.ToArray();
            // rzDevice25ValkyrieT3 increments before sending and wraps 31 -> 1,
            // so a transaction of 0 is never put on the wire.
            _transaction = (byte)((_transaction % 31) + 1);
            request[2] = _transaction;
            if (_makoX) RazerValkyrieT3Protocol.SealMakoX(request);
            // Keep ownership until the synchronous HID call has actually completed.
            await Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); _io.Send(request); }, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                var reply = await Task.Run(_io.Read, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (_makoX
                        ? !RazerValkyrieT3Protocol.MatchesMakoX(request, reply)
                        : !RazerValkyrieT3Protocol.Matches(request, reply))
                    continue;
                if (reply[1] is 0 or 1 or 4) continue;
                var payload = _makoX
                    ? RazerValkyrieT3Protocol.ReadPayloadMakoX(reply)
                    : RazerValkyrieT3Protocol.ReadPayload(reply);
                // Getters echo profile/group. Setters still require the command ACK,
                // but the bundle does not require an echoed data payload for them.
                if ((request[8] & 0x80) == 0) return payload;
                if (!payload.AsSpan().StartsWith(expectedHeader))
                    throw new InvalidDataException("Valkyrie T3 response profile/group mismatch.");
                return payload[expectedHeader.Length..];
            }
            throw new InvalidOperationException("Valkyrie T3 returned no matching successful ACK.");
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { _disposed = true; _io.Dispose(); } }
        finally { _gate.Release(); }
    }

    private sealed class NativeFeatureIo : IRazerValkyrieT3FeatureIo
    {
        private readonly SafeFileHandle _handle;
        private readonly int _reportLength;
        internal NativeFeatureIo(string path, int reportLength)
        {
            _reportLength = reportLength;
            _handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open Valkyrie T3 feature channel.");
        }
        public void Send(byte[] report)
        {
            if (!HidD_SetFeature(_handle, report, report.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public byte[] Read()
        {
            var report = new byte[_reportLength];
            report[0] = 7;
            if (!HidD_GetFeature(_handle, report, report.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return report;
        }
        public void Dispose() => _handle.Dispose();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] report, int length);
    }
}
