using System.Runtime.InteropServices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public readonly record struct WindowsAudioMuteSnapshot(bool SpeakerMuted, bool MicrophoneMuted);

internal interface IWindowsAudioMuteSnapshotReader : IDisposable
{
    event Action? Changed;
    WindowsAudioMuteSnapshot Read();
}

/// <summary>
/// Observes default Core Audio endpoints on one COM-initialized thread.
/// </summary>
public sealed class WindowsCoreAudioMuteEventSource : IDisposable
{
    private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(5);

    private readonly Action<BladeAudioMuteState> _publish;
    private readonly Func<IWindowsAudioMuteSnapshotReader> _readerFactory;
    private readonly TimeSpan _retryInterval;
    private readonly ManualResetEvent _stop = new(false);
    private readonly AutoResetEvent _changed = new(false);
    private readonly object _sync = new();
    private Thread? _worker;
    private int _forcePublish;
    private bool _disposed;
    private string? _lastError;

    public WindowsCoreAudioMuteEventSource(Action<BladeAudioMuteState> publish)
        : this(publish, static () => new CoreAudioMuteSnapshotReader(), DefaultRetryInterval)
    {
    }

    internal WindowsCoreAudioMuteEventSource(
        Action<BladeAudioMuteState> publish,
        Func<IWindowsAudioMuteSnapshotReader> readerFactory,
        TimeSpan retryInterval)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        if (retryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryInterval));
        }

        _retryInterval = retryInterval;
    }

    public string? LastError => Volatile.Read(ref _lastError);

    public event Action<Exception>? ReadFailed;

    internal static void ToggleDefaultCaptureMute()
    {
        using var reader = new CoreAudioMuteSnapshotReader(observe: false);
        reader.ToggleMute(1);
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is not null)
            {
                throw new InvalidOperationException("Core Audio mute event source is already running.");
            }

            _worker = new Thread(Worker)
            {
                IsBackground = true,
                Name = "OpenSynapse Core Audio mute",
            };
            _worker.Start();
        }
    }

    public void Refresh()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is null)
            {
                throw new InvalidOperationException("Core Audio mute event source is not running.");
            }

            Interlocked.Exchange(ref _forcePublish, 1);
            _changed.Set();
        }
    }

    public void Dispose()
    {
        Thread? worker;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            worker = _worker;
            _worker = null;
            _stop.Set();
        }

        if (worker is null)
        {
            _stop.Dispose();
            _changed.Dispose();
        }
        else if (worker != Thread.CurrentThread)
        {
            worker.Join();
        }
    }

    private void Worker()
    {
        IWindowsAudioMuteSnapshotReader? reader = null;
        WindowsAudioMuteSnapshot? previous = null;
        WaitHandle[] waitHandles = [_stop, _changed];
        try
        {
            while (true)
            {
                if (_stop.WaitOne(0))
                {
                    break;
                }
                try
                {
                    if (reader is null)
                    {
                        reader = _readerFactory();
                        reader.Changed += SignalChanged;
                    }
                    var current = reader.Read();
                    var forcePublish = Interlocked.Exchange(ref _forcePublish, 0) != 0;
                    PublishChanges(forcePublish ? null : previous, current);
                    previous = current;
                    Volatile.Write(ref _lastError, null);
                }
                catch (Exception exception)
                {
                    var previousError = Volatile.Read(ref _lastError);
                    Volatile.Write(ref _lastError, exception.Message);
                    if (!StringComparer.Ordinal.Equals(previousError, exception.Message))
                    {
                        NotifyReadFailed(exception);
                    }
                    if (reader is not null)
                    {
                        reader.Changed -= SignalChanged;
                        reader.Dispose();
                    }
                    reader = null;
                }

                if (WaitHandle.WaitAny(waitHandles, reader is null ? _retryInterval : Timeout.InfiniteTimeSpan) == 0)
                {
                    break;
                }
            }
        }
        finally
        {
            try
            {
                if (reader is not null)
                {
                    reader.Changed -= SignalChanged;
                    reader.Dispose();
                }
            }
            finally
            {
                _stop.Dispose();
                _changed.Dispose();
            }
        }
    }

    private void SignalChanged()
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }
        try
        {
            _changed.Set();
        }
        catch (ObjectDisposedException)
        {
            // An already-dispatched COM callback may finish after unregistering.
        }
    }

    private void NotifyReadFailed(Exception exception)
    {
        foreach (var handler in ReadFailed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<Exception>)handler)(exception);
            }
            catch
            {
                // Diagnostics must not stop Core Audio observation.
            }
        }
    }

    private void PublishChanges(
        WindowsAudioMuteSnapshot? previous,
        WindowsAudioMuteSnapshot current)
    {
        try
        {
            if (previous?.SpeakerMuted != current.SpeakerMuted)
            {
                _publish(new(BladeAudioMuteTarget.Speaker, current.SpeakerMuted));
            }
            if (previous?.MicrophoneMuted != current.MicrophoneMuted)
            {
                _publish(new(BladeAudioMuteTarget.Microphone, current.MicrophoneMuted));
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception.Message);
        }
    }

    private sealed class CoreAudioMuteSnapshotReader : IWindowsAudioMuteSnapshotReader
    {
        private const int ERender = 0;
        private const int ECapture = 1;
        private const int EMultimedia = 1;
        private const uint ClsctxAll = 23;
        private const uint CoInitMultithreaded = 0;
        private const int RpcEChangedMode = unchecked((int)0x80010106);
        private static readonly Guid DeviceEnumeratorClassId =
            new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid AudioEndpointVolumeInterfaceId =
            new("5CDF2C82-841E-4546-9722-0CF74078229A");

        private IMMDeviceEnumerator? _enumerator;
        private readonly EndpointNotification? _endpointNotification;
        private readonly VolumeNotification? _volumeNotification;
        private IAudioEndpointVolume? _speaker;
        private IAudioEndpointVolume? _microphone;
        private int _rebind = 1;
        private readonly bool _uninitializeCom;

        public event Action? Changed;

        internal CoreAudioMuteSnapshotReader(bool observe = true)
        {
            var result = CoInitializeEx(0, CoInitMultithreaded);
            if (result < 0 && result != RpcEChangedMode)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            _uninitializeCom = result >= 0;
            try
            {
                var type = Type.GetTypeFromCLSID(DeviceEnumeratorClassId, throwOnError: true)!;
                _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
                if (observe)
                {
                    _endpointNotification = new EndpointNotification(this);
                    _volumeNotification = new VolumeNotification(this);
                    ThrowIfFailed(_enumerator.RegisterEndpointNotificationCallback(_endpointNotification));
                }
            }
            catch
            {
                Release(_enumerator);
                _enumerator = null;
                if (_uninitializeCom)
                {
                    CoUninitialize();
                }
                throw;
            }
        }

        public WindowsAudioMuteSnapshot Read()
        {
            if (Interlocked.Exchange(ref _rebind, 0) != 0)
            {
                BindEndpoints();
            }

            return new(ReadMute(_speaker), ReadMute(_microphone));
        }

        public void Dispose()
        {
            var enumerator = Interlocked.Exchange(ref _enumerator, null);
            if (enumerator is not null && _endpointNotification is not null)
            {
                _ = enumerator.UnregisterEndpointNotificationCallback(_endpointNotification);
            }
            ReleaseVolume(ref _speaker);
            ReleaseVolume(ref _microphone);
            Release(enumerator);
            if (_uninitializeCom)
            {
                CoUninitialize();
            }
        }

        private void BindEndpoints()
        {
            ReleaseVolume(ref _speaker);
            ReleaseVolume(ref _microphone);
            _speaker = OpenVolume(ERender);
            _microphone = OpenVolume(ECapture);
        }

        private IAudioEndpointVolume OpenVolume(int dataFlow)
        {
            var enumerator = _enumerator ?? throw new ObjectDisposedException(nameof(CoreAudioMuteSnapshotReader));
            IMMDevice? device = null;
            try
            {
                ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(dataFlow, EMultimedia, out device));
                var iid = AudioEndpointVolumeInterfaceId;
                ThrowIfFailed(device.Activate(ref iid, ClsctxAll, 0, out var value));
                var volume = (IAudioEndpointVolume)value;
                try
                {
                    if (_volumeNotification is not null)
                    {
                        ThrowIfFailed(volume.RegisterControlChangeNotify(_volumeNotification));
                    }
                    return volume;
                }
                catch
                {
                    Release(volume);
                    throw;
                }
            }
            finally
            {
                Release(device);
            }
        }

        private static bool ReadMute(IAudioEndpointVolume? volume)
        {
            if (volume is null)
            {
                throw new InvalidOperationException("The default Core Audio endpoint is unavailable.");
            }
            ThrowIfFailed(volume.GetMute(out var muted));
            return muted;
        }

        private void ReleaseVolume(ref IAudioEndpointVolume? volume)
        {
            var current = volume;
            volume = null;
            if (current is not null && _volumeNotification is not null)
            {
                _ = current.UnregisterControlChangeNotify(_volumeNotification);
            }
            Release(current);
        }

        private void EndpointChanged()
        {
            Volatile.Write(ref _rebind, 1);
            Changed?.Invoke();
        }

        private void VolumeChanged() => Changed?.Invoke();

        [ComVisible(true)]
        private sealed class EndpointNotification(CoreAudioMuteSnapshotReader owner) : IMMNotificationClient
        {
            public int OnDeviceStateChanged(string deviceId, uint newState) => 0;
            public int OnDeviceAdded(string deviceId) => 0;
            public int OnDeviceRemoved(string deviceId) => 0;
            public int OnDefaultDeviceChanged(int dataFlow, int role, string? deviceId)
            {
                if (role == EMultimedia && (dataFlow == ERender || dataFlow == ECapture))
                {
                    owner.EndpointChanged();
                }
                return 0;
            }
            public int OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) => 0;
        }

        [ComVisible(true)]
        private sealed class VolumeNotification(CoreAudioMuteSnapshotReader owner) : IAudioEndpointVolumeCallback
        {
            public int OnNotify(nint notificationData)
            {
                owner.VolumeChanged();
                return 0;
            }
        }

        internal void ToggleMute(int dataFlow)
        {
            var enumerator = _enumerator ?? throw new ObjectDisposedException(nameof(CoreAudioMuteSnapshotReader));
            IMMDevice? device = null;
            IAudioEndpointVolume? volume = null;
            try
            {
                ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(dataFlow, EMultimedia, out device));
                var iid = AudioEndpointVolumeInterfaceId;
                ThrowIfFailed(device.Activate(ref iid, ClsctxAll, 0, out var value));
                volume = (IAudioEndpointVolume)value;
                ThrowIfFailed(volume.GetMute(out var muted));
                ThrowIfFailed(volume.SetMute(!muted, 0));
            }
            finally
            {
                Release(volume);
                Release(device);
            }
        }

        private static void ThrowIfFailed(int result)
        {
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }
        }

        private static void Release(object? value)
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                _ = Marshal.FinalReleaseComObject(value);
            }
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice endpoint);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComVisible(true)]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int OnDefaultDeviceChanged(int dataFlow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? deviceId);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey propertyKey);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey
    {
        public readonly Guid FormatId;
        public readonly uint PropertyId;
    }

    [ComVisible(true)]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(nint notificationData);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(
            ref Guid iid,
            uint classContext,
            nint activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out nint properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, nint eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, nint eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, nint eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, nint eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, nint eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(nint eventContext);
        [PreserveSig] int VolumeStepDown(nint eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
