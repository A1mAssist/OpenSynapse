using System.Runtime.InteropServices;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.ChromaSdk.Shim;

public static class Exports
{
    private const uint WmChromaEvent = 0xA000;
    private const int AppInfoDescriptionOffset = 512;
    private const int AppInfoAuthorNameOffset = 2560;
    private const int AppInfoAuthorContactOffset = 3072;
    private const int AppInfoSupportedDeviceOffset = 3584;
    private const int AppInfoCategoryOffset = 3588;
    private const int AppInfoTitleLength = 256;
    private const int AppInfoDescriptionLength = 1024;
    private const int AppInfoAuthorLength = 256;
    private const uint AllDevices = ChromaSdkDeviceMask.Keyboard |
                                    ChromaSdkDeviceMask.Mouse |
                                    ChromaSdkDeviceMask.Headset |
                                    ChromaSdkDeviceMask.Mousepad |
                                    ChromaSdkDeviceMask.Keypad |
                                    ChromaSdkDeviceMask.ChromaLink;
    private const string DefaultTitle = "OpenSynapse Chroma SDK";

    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, ChromaSdkEffect> Effects = new();
    private static readonly Dictionary<ChromaSdkDeviceType, int> EffectCounts = new();
    private static readonly HashSet<ChromaSdkDeviceType> AppliedDevices = new();
    private static ChromaPipeClient? _pipe;
    private static IntPtr _notificationWindow;

    [UnmanagedCallersOnly(EntryPoint = "Init")]
    public static int Init()
    {
        try
        {
            return Initialize(DefaultTitle, AllDevices);
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "InitSDK")]
    public static int InitSDK(IntPtr appInfo)
    {
        try
        {
            if (appInfo == IntPtr.Zero) return SdkResult.InvalidParameter;

            var supportedDevices = Marshal.ReadInt32(appInfo, AppInfoSupportedDeviceOffset);
            var category = Marshal.ReadInt32(appInfo, AppInfoCategoryOffset);
            if (supportedDevices is < 1 or > 0x3F || category is < 1 or > 2)
            {
                return SdkResult.InvalidParameter;
            }

            var title = ReadFixedString(appInfo, 0, AppInfoTitleLength);
            var description = ReadFixedString(appInfo, AppInfoDescriptionOffset, AppInfoDescriptionLength);
            var authorName = ReadFixedString(appInfo, AppInfoAuthorNameOffset, AppInfoAuthorLength);
            var authorContact = ReadFixedString(appInfo, AppInfoAuthorContactOffset, AppInfoAuthorLength);
            if (title.Length == 0 || description.Length == 0 ||
                authorName.Length == 0 || authorContact.Length == 0)
            {
                return SdkResult.InvalidParameter;
            }

            return Initialize(title, (uint)supportedDevices);
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "UnInit")]
    public static int UnInit()
    {
        try
        {
            lock (Gate)
            {
                try
                {
                    _pipe?.Dispose();
                }
                catch
                {
                    // UnInit always releases local state, even if the host vanished mid-write.
                }

                _pipe = null;
                Effects.Clear();
                EffectCounts.Clear();
                AppliedDevices.Clear();
                _notificationWindow = IntPtr.Zero;
            }
            return SdkResult.Success;
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "CreateEffect")]
    public static int CreateEffect(Guid deviceId, int effect, IntPtr param, IntPtr effectId)
    {
        try
        {
            // Deprecated path needs per-device GUID mapping we do not have.
            return SdkResult.NotSupported;
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "CreateKeyboardEffect")]
    public static int CreateKeyboardEffect(int effect, IntPtr param, IntPtr effectId)
    {
        try
        {
            return Create(ChromaSdkDeviceType.Keyboard, effect, param, effectId);
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "CreateMouseEffect")]
    public static int CreateMouseEffect(int effect, IntPtr param, IntPtr effectId)
    {
        try
        {
            return Create(ChromaSdkDeviceType.Mouse, effect, param, effectId);
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "CreateHeadsetEffect")]
    public static int CreateHeadsetEffect(int effect, IntPtr param, IntPtr effectId)
    {
        try
        {
            return Create(ChromaSdkDeviceType.Headset, effect, param, effectId);
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "CreateMousepadEffect")]
    public static int CreateMousepadEffect(int effect, IntPtr param, IntPtr effectId)
    {
        try
        {
            return Create(ChromaSdkDeviceType.Mousepad, effect, param, effectId);
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "CreateKeypadEffect")]
    public static int CreateKeypadEffect(int effect, IntPtr param, IntPtr effectId)
    {
        try
        {
            return Create(ChromaSdkDeviceType.Keypad, effect, param, effectId);
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "CreateChromaLinkEffect")]
    public static int CreateChromaLinkEffect(int effect, IntPtr param, IntPtr effectId)
    {
        try
        {
            return Create(ChromaSdkDeviceType.ChromaLink, effect, param, effectId);
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "DeleteEffect")]
    public static int DeleteEffect(Guid effectId)
    {
        try
        {
            lock (Gate)
            {
                if (!Effects.Remove(effectId, out var effect)) return SdkResult.NotFound;

                var remaining = EffectCounts[effect.DeviceType] - 1;
                if (remaining <= 0)
                {
                    EffectCounts.Remove(effect.DeviceType);
                    if (AppliedDevices.Remove(effect.DeviceType))
                    {
                        TryEnqueue(new ChromaSdkClearMessage(effect.DeviceType));
                    }
                }
                else
                {
                    EffectCounts[effect.DeviceType] = remaining;
                }
                return SdkResult.Success;
            }
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "SetEffect")]
    public static int SetEffect(Guid effectId)
    {
        try
        {
            lock (Gate)
            {
                if (_pipe is not { IsConnected: true }) return SdkResult.NotValidState;
                if (!Effects.TryGetValue(effectId, out var effect)) return SdkResult.NotFound;
                // The official SDK returns success even when no matching device is present.
                if (!_pipe.Enqueue(new ChromaSdkSetEffectMessage(effect)))
                {
                    return SdkResult.NotValidState;
                }
                AppliedDevices.Add(effect.DeviceType);
                return SdkResult.Success;
            }
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "RegisterEventNotification")]
    public static int RegisterEventNotification(IntPtr hwnd)
    {
        try
        {
            lock (Gate)
            {
                _notificationWindow = hwnd;
                if (_pipe is { IsConnected: true }) PostChromaEvent(hwnd);
                return SdkResult.Success;
            }
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "UnregisterEventNotification")]
    public static int UnregisterEventNotification()
    {
        try
        {
            lock (Gate)
            {
                _notificationWindow = IntPtr.Zero;
                return SdkResult.Success;
            }
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "QueryDevice")]
    public static int QueryDevice(Guid deviceId, IntPtr deviceInfo)
    {
        try
        {
            if (deviceInfo == IntPtr.Zero) return SdkResult.InvalidParameter;

            lock (Gate)
            {
                try
                {
                    _pipe?.Query();
                }
                catch
                {
                    // QueryDevice still reports the SDK limitation rather than pipe I/O failures.
                }
            }

            // Official device GUID mapping is not verified.
            return SdkResult.NotSupported;
        }
        catch
        {
            return SdkResult.Failed;
        }
    }

    private static string ReadFixedString(IntPtr source, int offset, int length)
    {
        var value = Marshal.PtrToStringUni(IntPtr.Add(source, offset), length) ?? string.Empty;
        var terminator = value.IndexOf('\0');
        return terminator >= 0 ? value[..terminator] : value;
    }
    private static int Initialize(string title, uint supportedDevices)
    {
        if (title.Length > 64) title = title[..64];
        lock (Gate)
        {
            if (_pipe is { IsConnected: true }) return SdkResult.Success;

            try
            {
                _pipe?.Dispose();
            }
            catch
            {
                // A broken previous connection must not prevent a fresh Init.
            }
            _pipe = null;
            var result = ChromaPipeClient.Connect(title, supportedDevices, out var client);
            if (result != SdkResult.Success) return result;

            _pipe = client;
            if (_notificationWindow != IntPtr.Zero) PostChromaEvent(_notificationWindow);
            return SdkResult.Success;
        }
    }

    private static int Create(ChromaSdkDeviceType deviceType, int effect, IntPtr param, IntPtr effectId)
    {
        lock (Gate)
        {
            if (effectId == IntPtr.Zero) return SdkResult.InvalidParameter;

            var result = ChromaEffects.Translate(deviceType, effect, param, out var effectValue);
            if (result != SdkResult.Success) return result;
            if (_pipe is not { IsConnected: true }) return SdkResult.NotValidState;

            var id = Guid.NewGuid();
            Effects[id] = effectValue!;
            EffectCounts[deviceType] = EffectCounts.GetValueOrDefault(deviceType) + 1;
            WriteGuid(effectId, id);
            return SdkResult.Success;
        }
    }

    private static void TryEnqueue(ChromaSdkMessage message)
    {
        if (_pipe is { IsConnected: true }) _pipe.Enqueue(message);
    }

    private static unsafe void WriteGuid(IntPtr destination, Guid value) => *(Guid*)destination = value;

    private static void PostChromaEvent(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) PostMessageW(hwnd, WmChromaEvent, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
