using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

/// <summary>Nommo's DLL uses a container-bound render endpoint for volume and mute.</summary>
internal static class WindowsSpeakerEndpoint
{
    internal static Task<(int Volume, bool Muted)> UpdateAsync(
        Guid containerId, int? percent, bool? muted, CancellationToken cancellationToken) =>
        Task.Run(() => Update(containerId, percent, muted, cancellationToken), cancellationToken);

    private static (int, bool) Update(Guid containerId, int? percent, bool? muted, CancellationToken token)
    {
        if (containerId == Guid.Empty) throw new InvalidOperationException("The speaker container is unavailable.");
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        var initialized = CoInitializeEx(0, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialized);
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? devices = null;
        IMMDevice? selected = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out devices));
            Marshal.ThrowExceptionForHR(devices.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(devices.Item(i, out var device));
                IPropertyStore? properties = null;
                var value = new PropVariant();
                var keep = false;
                try
                {
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
                    if (properties.GetValue(ref key, out value) >= 0 && value.Type == 72 &&
                        value.Pointer != 0 && Marshal.PtrToStructure<Guid>(value.Pointer) == containerId)
                    {
                        if (selected is not null)
                            throw new InvalidOperationException("More than one render endpoint belongs to this speaker.");
                        selected = device;
                        keep = true;
                    }
                }
                finally
                {
                    PropVariantClear(ref value);
                    Release(properties);
                    if (!keep) Release(device);
                }
            }
            if (selected is null) throw new InvalidOperationException("The speaker's Windows audio endpoint is unavailable.");
            var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            Marshal.ThrowExceptionForHR(selected.Activate(ref iid, 23, 0, out var instance));
            volume = (IAudioEndpointVolume)instance;
            token.ThrowIfCancellationRequested();
            if (percent is { } level) Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(level / 100f, 0));
            if (muted is { } mute) Marshal.ThrowExceptionForHR(volume.SetMute(mute, 0));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var actual));
            Marshal.ThrowExceptionForHR(volume.GetMute(out var actualMute));
            var actualPercent = (int)Math.Round(actual * 100);
            if (percent is { } requested && Math.Abs(actualPercent - requested) > 1 ||
                muted is { } requestedMute && actualMute != requestedMute)
                throw new InvalidOperationException("The speaker endpoint did not retain the requested volume or mute.");
            return (actualPercent, actualMute);
        }
        finally
        {
            Release(volume); Release(selected); Release(devices); Release(enumerator);
            if (initialized >= 0) CoUninitialize();
        }
    }

    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint id) { public Guid FormatId = formatId; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-C0A3B2B46B0E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, nint context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, nint context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, nint context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, nint context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, nint context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}
