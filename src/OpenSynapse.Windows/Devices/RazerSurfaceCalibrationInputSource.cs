using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.Windows.Devices;

public interface IRazerSurfaceCalibrationInputSource
{
    Task<IDisposable> SubscribeAsync(Guid sessionId, bool philips,
        Action<RazerSurfaceCalibrationInput> handler, CancellationToken cancellationToken = default);
}

/// <summary>Session-scoped input, sharing the one process-wide Raw Input registration owner.</summary>
public sealed class RazerSurfaceCalibrationInputSource(string instanceId) : IRazerSurfaceCalibrationInputSource
{
    private static readonly object PathsGate = new();
    private static readonly Dictionary<string, int> ActivePaths = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsActiveDevicePath(string devicePath)
    {
        lock (PathsGate) return ActivePaths.ContainsKey(devicePath);
    }
    public async Task<IDisposable> SubscribeAsync(Guid sessionId, bool philips,
        Action<RazerSurfaceCalibrationInput> handler, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(handler);
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(0x1532, cancellationToken).ConfigureAwait(false);
        var endpoints = interfaces.Where(endpoint =>
            StringComparer.OrdinalIgnoreCase.Equals(endpoint.PhysicalDeviceKey, instanceId)).ToArray();
        var mousePaths = endpoints.Where(endpoint => endpoint.UsagePage == 1 && endpoint.Usage == 2)
            .Select(endpoint => endpoint.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eventPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usages = new HashSet<RawInputRouter.HidUsage>();
        var hasNotifications = false;
        var hasSamples = false;
        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (endpoint.InputReportByteLength < 3 || endpoint.UsagePage == 1 && endpoint.Usage is 2 or 6) continue;
            var reports = ReadSupportedReports(endpoint.DevicePath, philips);
            hasNotifications |= reports.Contains(0x05);
            hasSamples |= reports.Contains(0x5A);
            if (reports.Count == 0) continue;
            eventPaths.Add(endpoint.DevicePath);
            usages.Add(new(endpoint.UsagePage, endpoint.Usage));
        }
        if (!hasNotifications)
            throw new NotSupportedException("No HID input collection for this exact mouse exposes calibration notification report 05.");
        if (philips && !hasSamples)
            throw new NotSupportedException("This Philips connection has no proven Z/Q input report 5A; its driver redirect route is not available.");

        var subscription = new Subscription(instanceId, sessionId, philips, mousePaths, eventPaths, handler,
            endpoints.Select(endpoint => endpoint.DevicePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        subscription.Token = RawInputRouter.Subscribe(subscription.OnFrame, includeMouse: true, usages);
        subscription.Activate();
        if (cancellationToken.IsCancellationRequested)
        {
            subscription.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return subscription;
    }

    internal sealed class Subscription(string instanceId, Guid sessionId, bool philips,
        HashSet<string> mousePaths, HashSet<string> eventPaths, Action<RazerSurfaceCalibrationInput> handler,
        string[]? endpointPaths = null) : IDisposable
    {
        private int _disposed;
        internal IDisposable? Token { get; set; }

        internal void Activate()
        {
            lock (PathsGate)
                foreach (var path in endpointPaths ?? []) ActivePaths[path] = ActivePaths.GetValueOrDefault(path) + 1;
        }

        internal void OnFrame(RawInputFrame frame)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            RazerSurfaceCalibrationEventKind kind;
            byte z = 0, q = 0;
            if (frame.IsMouse)
            {
                // Movement never enters a task queue; only this exact physical mouse's left press does.
                if ((frame.MouseFlags & 1) == 0 || !mousePaths.Contains(frame.DevicePath)) return;
                kind = RazerSurfaceCalibrationEventKind.LeftButtonDown;
            }
            else if (frame.IsHid)
            {
                if (!eventPaths.Contains(frame.DevicePath) || frame.Report is null ||
                    !RazerSurfaceCalibrationEventDecoder.TryDecode(frame.Report, philips, out kind, out z, out q)) return;
                // A redirected Philips click and RAWMOUSE can describe the same physical press.
                if (kind == RazerSurfaceCalibrationEventKind.LeftButtonDown && mousePaths.Count > 0) return;
            }
            else
            {
                if (frame.VirtualKey != 0x1B || (frame.Flags & 1) != 0) return;
                kind = RazerSurfaceCalibrationEventKind.Escape;
            }
            handler(new(instanceId, sessionId, kind, z, q));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { Token?.Dispose(); }
            finally
            {
                lock (PathsGate)
                    foreach (var path in endpointPaths ?? [])
                    {
                        if (ActivePaths.GetValueOrDefault(path) <= 1) ActivePaths.Remove(path);
                        else ActivePaths[path]--;
                    }
            }
        }
    }

    private static HashSet<byte> ReadSupportedReports(string path, bool philips)
    {
        // Metadata-only handle and HID parser initialization: neither reads nor writes a device report.
        using var handle = CreateFile(path, 0, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect the calibration input collection.");
        if (!HidD_GetPreparsedData(handle, out var preparsed)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (HidP_GetCaps(preparsed, out var caps) < 0 || caps.InputReportByteLength < 3) return [];
            var report = new byte[caps.InputReportByteLength];
            var result = new HashSet<byte>();
            foreach (var id in philips ? new byte[] { 0x05, 0x4D, 0x5A } : new byte[] { 0x05 })
                if (HidP_InitializeReportForID(0, id, preparsed, report, (uint)report.Length) == 0x00110000) result.Add(id);
            return result;
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        internal ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] internal ushort[] Reserved;
        internal ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        internal ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        internal ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsed);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(nint preparsed);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(nint preparsed, out HidCaps caps);
    [DllImport("hid.dll")]
    private static extern int HidP_InitializeReportForID(int type, byte reportId, nint preparsed, byte[] report, uint length);
}
