using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

public sealed partial class WindowsCoreAudioMuteEventSource
{
    internal static void CyclePlaybackDevice()
    {
        using var reader = new CoreAudioMuteSnapshotReader(observe: false);
        reader.CyclePlayback();
    }

    internal static string NextPlaybackDevice(IReadOnlyList<string> ids, string current)
    {
        if (ids.Count == 0) throw new InvalidOperationException("No active playback device is available.");
        var index = ids.ToList().FindIndex(id => StringComparer.OrdinalIgnoreCase.Equals(id, current));
        return ids[(index + 1) % ids.Count];
    }

    private sealed partial class CoreAudioMuteSnapshotReader
    {
        internal void CyclePlayback()
        {
            var current = ReadPlayback().EndpointId;
            ILaylaDeviceCollection? collection = null;
            object? policyObject = null;
            try
            {
                ThrowIfFailed(_enumerator!.EnumAudioEndpoints(ERender, 1, out var pointer));
                try { collection = (ILaylaDeviceCollection)Marshal.GetObjectForIUnknown(pointer); }
                finally { Marshal.Release(pointer); }
                ThrowIfFailed(collection.GetCount(out var count));
                var ids = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    ThrowIfFailed(collection.Item(i, out var device));
                    try { ThrowIfFailed(device.GetId(out var id)); ids.Add(id); }
                    finally { Release(device); }
                }
                var next = NextPlaybackDevice(ids, current);
                if (next == current) return;
                policyObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), true)!)!;
                var policy = (ILaylaPolicyConfig)policyObject;
                ThrowIfFailed(policy.SetDefaultEndpoint(next, 0));
                ThrowIfFailed(policy.SetDefaultEndpoint(next, EMultimedia));
                ThrowIfFailed(_enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out var actual));
                try
                {
                    ThrowIfFailed(actual.GetId(out var actualId));
                    if (!StringComparer.OrdinalIgnoreCase.Equals(actualId, next))
                        throw new InvalidOperationException("Windows did not retain the selected playback device.");
                }
                finally { Release(actual); }
            }
            finally { Release(policyObject); Release(collection); }
        }
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-C0A3B2B46B0E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILaylaDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    // Windows PolicyConfig ABI; only default render endpoint selection is used.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILaylaPolicyConfig
    {
        void GetMixFormat();
        void GetDeviceFormat();
        void ResetDeviceFormat();
        void SetDeviceFormat();
        void GetProcessingPeriod();
        void SetProcessingPeriod();
        void GetShareMode();
        void SetShareMode();
        void GetPropertyValue();
        void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}
