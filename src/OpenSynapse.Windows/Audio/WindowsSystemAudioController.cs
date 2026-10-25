using System.Runtime.InteropServices;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

/// <summary>
/// Source-backed replacement for Razer Synapse's simple_service audio calls.
/// The wire contract in the local Electron service exposes device enumeration,
/// default endpoint selection, and volume/mute reads and writes; this class maps
/// those operations onto Windows Core Audio.
/// </summary>
public sealed class WindowsSystemAudioController : ISystemAudioController
{
    private const int DeviceStateActive = 0x01;
    private const int StgmRead = 0;
    private const int ClsCtxAll = 0x17;
    private const int CoInitMultithreaded = 0x0;
    private static readonly Guid AudioEndpointVolumeInterfaceId =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly PropertyKey FriendlyNameKey =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public async Task<SystemAudioSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = new SystemAudioSnapshot([], []);
        await RunComAsync(() =>
        {
            var enumerator = CreateEnumerator();
            try
            {
                var renderDefault = TryGetDefaultId(enumerator, EDataFlow.Render);
                var captureDefault = TryGetDefaultId(enumerator, EDataFlow.Capture);
                result = new SystemAudioSnapshot(
                    ReadDirection(enumerator, EDataFlow.Render, renderDefault),
                    ReadDirection(enumerator, EDataFlow.Capture, captureDefault));
            }
            finally
            {
                Release(enumerator);
            }
        }, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public Task SetDefaultAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        return RunComAsync(() =>
        {
            if (TrySetDefaultWithPrimaryClient(endpointId) ||
                TrySetDefaultWithVistaClient(endpointId))
            {
                return;
            }

            throw new InvalidOperationException("Windows did not expose a PolicyConfig default-endpoint interface.");
        }, cancellationToken);
    }

    public Task SetVolumeAsync(
        string endpointId,
        int volumePercent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        var scalar = Math.Clamp(volumePercent, 0, 100) / 100f;
        return RunComAsync(() =>
        {
            using var endpoint = OpenEndpoint(endpointId);
            ThrowIfFailed(endpoint.Volume.SetMasterVolumeLevelScalar(scalar, IntPtr.Zero));
        }, cancellationToken);
    }

    public Task SetMuteAsync(
        string endpointId,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        return RunComAsync(() =>
        {
            using var endpoint = OpenEndpoint(endpointId);
            ThrowIfFailed(endpoint.Volume.SetMute(muted, IntPtr.Zero));
        }, cancellationToken);
    }

    private static bool TrySetDefaultWithPrimaryClient(string endpointId)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"))!)!;
            var policy = (IPolicyConfig)instance;
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                ThrowIfFailed(policy.SetDefaultEndpoint(endpointId, role));
            }
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        finally
        {
            Release(instance);
        }
    }

    private static bool TrySetDefaultWithVistaClient(string endpointId)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("294935CE-F637-4E7C-A41B-AB255460B862"))!)!;
            var policy = (IPolicyConfigVista)instance;
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                ThrowIfFailed(policy.SetDefaultEndpoint(endpointId, role));
            }
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        finally
        {
            Release(instance);
        }
    }

    private static IReadOnlyList<SystemAudioEndpoint> ReadDirection(
        IMMDeviceEnumerator enumerator,
        EDataFlow flow,
        string? defaultId)
    {
        ThrowIfFailed(enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out var collection));
        try
        {
            ThrowIfFailed(collection.GetCount(out var count));
            var endpoints = new List<SystemAudioEndpoint>(checked((int)count));
            for (uint index = 0; index < count; index++)
            {
                ThrowIfFailed(collection.Item(index, out var device));
                try
                {
                    ThrowIfFailed(device.GetId(out var id));
                    var name = ReadFriendlyName(device) ?? id;
                    var muted = false;
                    var volume = 0;
                    try
                    {
                        using var endpoint = OpenEndpoint(device);
                        ThrowIfFailed(endpoint.Volume.GetMute(out muted));
                        ThrowIfFailed(endpoint.Volume.GetMasterVolumeLevelScalar(out var scalar));
                        volume = checked((int)Math.Round(scalar * 100, MidpointRounding.AwayFromZero));
                    }
                    catch (Exception exception) when (IsAudioFailure(exception))
                    {
                        // A device can be present without a usable volume interface. Keep it
                        // selectable so the default endpoint can still be changed from the UI.
                    }

                    endpoints.Add(new SystemAudioEndpoint(
                        id,
                        name,
                        flow == EDataFlow.Render ? SystemAudioDirection.Render : SystemAudioDirection.Capture,
                        muted,
                        Math.Clamp(volume, 0, 100),
                        StringComparer.OrdinalIgnoreCase.Equals(id, defaultId)));
                }
                finally
                {
                    Release(device);
                }
            }

            return endpoints
                .OrderByDescending(endpoint => endpoint.IsDefault)
                .ThenBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            Release(collection);
        }
    }

    private static string? TryGetDefaultId(IMMDeviceEnumerator enumerator, EDataFlow flow)
    {
        IMMDevice? device = null;
        try
        {
            var result = enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out device);
            if (result < 0 || device is null)
            {
                return null;
            }

            ThrowIfFailed(device.GetId(out var id));
            return id;
        }
        catch (Exception exception) when (IsAudioFailure(exception))
        {
            return null;
        }
        finally
        {
            Release(device);
        }
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            ThrowIfFailed(device.OpenPropertyStore(StgmRead, out store));
            var key = FriendlyNameKey;
            ThrowIfFailed(store.GetValue(ref key, out var value));
            try
            {
                return value.VarType == 31
                    ? Marshal.PtrToStringUni(value.PointerValue)
                    : null;
            }
            finally
            {
                _ = PropVariantClear(ref value);
            }
        }
        catch (Exception exception) when (IsAudioFailure(exception))
        {
            return null;
        }
        finally
        {
            Release(store);
        }
    }

    private static EndpointVolume OpenEndpoint(string endpointId)
    {
        var enumerator = CreateEnumerator();
        try
        {
            ThrowIfFailed(enumerator.GetDevice(endpointId, out var device));
            try
            {
                return OpenEndpoint(device);
            }
            finally
            {
                Release(device);
            }
        }
        finally
        {
            Release(enumerator);
        }
    }

    private static EndpointVolume OpenEndpoint(IMMDevice device)
    {
        var iid = AudioEndpointVolumeInterfaceId;
        ThrowIfFailed(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance));
        return new EndpointVolume((IAudioEndpointVolume)instance);
    }

    private static IMMDeviceEnumerator CreateEnumerator() =>
        (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;

    private static Task RunComAsync(Action action, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var initialized = CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);
            try
            {
                action();
            }
            finally
            {
                if (initialized >= 0)
                {
                    CoUninitialize();
                }
            }
        }, cancellationToken);

    private static bool IsAudioFailure(Exception exception) =>
        exception is COMException or InvalidCastException or UnauthorizedAccessException or
            NotSupportedException or IOException or InvalidOperationException;

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            throw Marshal.GetExceptionForHR(hresult) ?? new COMException("Core Audio call failed.", hresult);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.ReleaseComObject(value);
        }
    }

    private sealed class EndpointVolume(IAudioEndpointVolume volume) : IDisposable
    {
        public IAudioEndpointVolume Volume { get; } = volume;

        public void Dispose() => Release(Volume);
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All,
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, int propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly int PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr PointerValue;
        [FieldOffset(8)] public long LongValue;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class MmDeviceEnumeratorComObject;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(
            ref Guid iid,
            int classContext,
            IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, IntPtr eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, IntPtr eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(IntPtr eventContext);
        [PreserveSig] int VolumeStepDown(IntPtr eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
    }

    [ComImport]
    [Guid("568B9108-44BF-40B4-9006-86AFE5B5A620")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfigVista
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool defaultFormat, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool defaultPeriod, out long period, out long minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool visible);
    }

    [ComImport]
    [Guid("F8679F50-850A-43D3-9374-8C0B0F9F48E9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool defaultFormat, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool defaultPeriod, out long period, out long minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool visible);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
