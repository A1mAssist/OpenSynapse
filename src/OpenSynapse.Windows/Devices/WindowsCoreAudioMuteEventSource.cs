using System.Runtime.InteropServices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public readonly record struct WindowsAudioMuteSnapshot(bool SpeakerMuted, bool MicrophoneMuted);

internal interface IWindowsAudioMuteSnapshotReader : IDisposable
{
    WindowsAudioMuteSnapshot Read();
}

/// <summary>
/// Polls the current default Core Audio endpoints on one COM-initialized thread.
/// Reopening the reader after a failure also handles default endpoint changes.
/// </summary>
public sealed class WindowsCoreAudioMuteEventSource : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly Action<BladeAudioMuteState> _publish;
    private readonly Func<IWindowsAudioMuteSnapshotReader> _readerFactory;
    private readonly TimeSpan _pollInterval;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _sync = new();
    private Thread? _worker;
    private bool _disposed;
    private string? _lastError;

    public WindowsCoreAudioMuteEventSource(Action<BladeAudioMuteState> publish)
        : this(publish, static () => new CoreAudioMuteSnapshotReader(), DefaultPollInterval)
    {
    }

    internal WindowsCoreAudioMuteEventSource(
        Action<BladeAudioMuteState> publish,
        Func<IWindowsAudioMuteSnapshotReader> readerFactory,
        TimeSpan pollInterval)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        _pollInterval = pollInterval;
    }

    public string? LastError => Volatile.Read(ref _lastError);

    public event Action<Exception>? ReadFailed;

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is not null)
            {
                throw new InvalidOperationException("Core Audio 静音事件源已经启动。");
            }

            _worker = new Thread(Worker)
            {
                IsBackground = true,
                Name = "OpenSynapse Core Audio mute",
            };
            _worker.Start();
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

        if (worker is not null && worker != Thread.CurrentThread)
        {
            worker.Join();
        }
        _stop.Dispose();
    }

    private void Worker()
    {
        IWindowsAudioMuteSnapshotReader? reader = null;
        WindowsAudioMuteSnapshot? previous = null;
        try
        {
            while (!_stop.IsSet)
            {
                try
                {
                    reader ??= _readerFactory();
                    var current = reader.Read();
                    PublishChanges(previous, current);
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
                    reader?.Dispose();
                    reader = null;
                }

                _stop.Wait(_pollInterval);
            }
        }
        finally
        {
            reader?.Dispose();
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
                // Diagnostics must not stop the Core Audio polling thread.
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
        private readonly bool _uninitializeCom;

        internal CoreAudioMuteSnapshotReader()
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
            }
            catch
            {
                if (_uninitializeCom)
                {
                    CoUninitialize();
                }
                throw;
            }
        }

        public WindowsAudioMuteSnapshot Read() => new(ReadMute(ERender), ReadMute(ECapture));

        public void Dispose()
        {
            Release(Interlocked.Exchange(ref _enumerator, null));
            if (_uninitializeCom)
            {
                CoUninitialize();
            }
        }

        private bool ReadMute(int dataFlow)
        {
            var enumerator = _enumerator ??
                throw new ObjectDisposedException(nameof(CoreAudioMuteSnapshotReader));
            IMMDevice? device = null;
            IAudioEndpointVolume? volume = null;
            try
            {
                ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(dataFlow, EMultimedia, out device));
                var iid = AudioEndpointVolumeInterfaceId;
                ThrowIfFailed(device.Activate(ref iid, ClsctxAll, 0, out var value));
                volume = (IAudioEndpointVolume)value;
                ThrowIfFailed(volume.GetMute(out var muted));
                return muted;
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
        [PreserveSig] int RegisterEndpointNotificationCallback(nint client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(nint client);
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
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
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
