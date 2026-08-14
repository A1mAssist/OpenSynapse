using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Lighting;

internal interface IAudioMeterSession : IDisposable
{
    string EndpointId { get; }
    float ReadPeak();
}

internal sealed class WasapiAudioMeterAdapter : ILightingInputAdapter
{
    private static readonly TimeSpan EndpointPollInterval = TimeSpan.FromSeconds(1);
    private readonly Func<IAudioMeterSession> _openDefaultSession;
    private readonly Func<DateTimeOffset> _clock;
    private IAudioMeterSession? _session;
    private DateTimeOffset _nextEndpointPoll;
    private int _disposed;

    internal WasapiAudioMeterAdapter()
        : this(CoreAudioMeterSession.OpenDefault, static () => DateTimeOffset.UtcNow)
    {
    }

    internal WasapiAudioMeterAdapter(
        Func<IAudioMeterSession> openDefaultSession,
        Func<DateTimeOffset> clock)
    {
        _openDefaultSession = openDefaultSession ?? throw new ArgumentNullException(nameof(openDefaultSession));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        _session = _openDefaultSession();
        _nextEndpointPoll = _clock() + EndpointPollInterval;
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync()
    {
        Interlocked.Exchange(ref _session, null)?.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            return StopAsync();
        }
        return ValueTask.CompletedTask;
    }

    internal double ReadLevel()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var current = _session ?? throw new InvalidOperationException("Audio Meter 尚未启动。");
        if (_clock() >= _nextEndpointPoll)
        {
            var candidate = _openDefaultSession();
            _nextEndpointPoll = _clock() + EndpointPollInterval;
            if (StringComparer.Ordinal.Equals(candidate.EndpointId, current.EndpointId))
            {
                candidate.Dispose();
            }
            else
            {
                _session = candidate;
                current.Dispose();
                current = candidate;
            }
        }

        try
        {
            return NormalizeLevel(current.ReadPeak());
        }
        catch (COMException)
        {
            var replacement = _openDefaultSession();
            current.Dispose();
            _session = replacement;
            _nextEndpointPoll = _clock() + EndpointPollInterval;
            return NormalizeLevel(replacement.ReadPeak());
        }
    }

    internal static double NormalizeLevel(float value) =>
        float.IsNaN(value) || float.IsNegativeInfinity(value)
            ? 0
            : Math.Clamp(value, 0, 1);

    private sealed class CoreAudioMeterSession : IAudioMeterSession
    {
        private static readonly Guid DeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private IMMDevice? _device;
        private IAudioMeterInformation? _meter;

        private CoreAudioMeterSession(IMMDevice device, IAudioMeterInformation meter, string endpointId)
        {
            _device = device;
            _meter = meter;
            EndpointId = endpointId;
        }

        public string EndpointId { get; }

        internal static IAudioMeterSession OpenDefault()
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            try
            {
                var enumeratorType = Type.GetTypeFromCLSID(DeviceEnumeratorClassId, throwOnError: true)!;
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                Marshal.ThrowExceptionForHR(device.GetId(out var endpointId));
                var iid = typeof(IAudioMeterInformation).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var value));
                var meter = (IAudioMeterInformation)value;
                var result = new CoreAudioMeterSession(device, meter, endpointId);
                device = null;
                return result;
            }
            finally
            {
                Release(device);
                Release(enumerator);
            }
        }

        public float ReadPeak()
        {
            var meter = _meter ?? throw new ObjectDisposedException(nameof(CoreAudioMeterSession));
            Marshal.ThrowExceptionForHR(meter.GetPeakValue(out var value));
            return value;
        }

        public void Dispose()
        {
            Release(Interlocked.Exchange(ref _meter, null));
            Release(Interlocked.Exchange(ref _device, null));
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
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out int count);
        [PreserveSig] int GetChannelsPeakValues(int count, [Out] float[] values);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
    }
}
