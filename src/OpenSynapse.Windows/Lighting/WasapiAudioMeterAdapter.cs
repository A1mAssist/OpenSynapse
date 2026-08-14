using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace OpenSynapse.Windows.Lighting;

internal readonly record struct AudioMeterSample(double Rms, double Peak)
{
    public static AudioMeterSample Silence { get; } = new(0, 0);
}

internal enum AudioSampleEncoding
{
    Pcm,
    IeeeFloat,
}

internal readonly record struct AudioSampleFormat(
    AudioSampleEncoding Encoding,
    ushort Channels,
    ushort BitsPerSample,
    ushort ValidBitsPerSample,
    ushort BlockAlign);

internal interface IAudioLoopbackSession : IDisposable
{
    string EndpointId { get; }
    bool TryReadSample(out AudioMeterSample sample);
}

internal sealed class WasapiAudioMeterAdapter : ILightingInputAdapter
{
    private static readonly TimeSpan DefaultEndpointPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromMilliseconds(20);
    private readonly Func<string> _getDefaultEndpointId;
    private readonly Func<string, IAudioLoopbackSession> _openSession;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _endpointPollInterval;
    private readonly TimeSpan _retryInterval;
    private readonly Channel<AudioMeterSample> _samples = Channel.CreateBounded<AudioMeterSample>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private AudioMeterSample _latest;
    private int _disposed;

    internal WasapiAudioMeterAdapter()
        : this(
            CoreAudioLoopbackSession.GetDefaultEndpointId,
            CoreAudioLoopbackSession.Open,
            static () => DateTimeOffset.UtcNow,
            DefaultEndpointPollInterval,
            DefaultRetryInterval)
    {
    }

    internal WasapiAudioMeterAdapter(
        Func<string> getDefaultEndpointId,
        Func<string, IAudioLoopbackSession> openSession,
        Func<DateTimeOffset> clock,
        TimeSpan endpointPollInterval,
        TimeSpan retryInterval)
    {
        _getDefaultEndpointId = getDefaultEndpointId ?? throw new ArgumentNullException(nameof(getDefaultEndpointId));
        _openSession = openSession ?? throw new ArgumentNullException(nameof(openSession));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (endpointPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(endpointPollInterval));
        }
        if (retryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryInterval));
        }
        _endpointPollInterval = endpointPollInterval;
        _retryInterval = retryInterval;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_worker is not null)
            {
                throw new InvalidOperationException("Audio Meter 已经启动。");
            }

            _latest = AudioMeterSample.Silence;
            while (_samples.Reader.TryRead(out _))
            {
            }
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _stop.Token;
            _worker = Task.Factory.StartNew(
                () => CaptureLoop(token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync() => new(StopCoreAsync());

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
    }

    internal AudioMeterSample ReadSample()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_lifecycleGate)
        {
            if (_worker is null)
            {
                throw new InvalidOperationException("Audio Meter 尚未启动。");
            }
        }
        while (_samples.Reader.TryRead(out var sample))
        {
            _latest = sample;
        }
        return _latest;
    }

    internal double ReadLevel() => ReadSample().Peak;

    internal static double NormalizeLevel(double value) =>
        double.IsNaN(value) || value <= 0 ? 0 : Math.Min(value, 1);

    internal static AudioMeterSample ComputeSample(
        ReadOnlySpan<byte> data,
        AudioSampleFormat format,
        int frameCount)
    {
        if (format.Channels == 0 || format.BlockAlign == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
        if (frameCount < 0 || data.Length < checked(frameCount * format.BlockAlign))
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        var bytesPerSample = (format.BitsPerSample + 7) / 8;
        if (bytesPerSample == 0 || format.BlockAlign < format.Channels * bytesPerSample)
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
        if (!Enum.IsDefined(format.Encoding))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
        if (format.Encoding == AudioSampleEncoding.IeeeFloat &&
            format.BitsPerSample is not 32 and not 64)
        {
            throw new NotSupportedException($"不支持 {format.BitsPerSample} 位浮点音频。");
        }
        if (format.Encoding == AudioSampleEncoding.Pcm &&
            format.BitsPerSample is not 8 and not 16 and not 24 and not 32)
        {
            throw new NotSupportedException($"不支持 {format.BitsPerSample} 位 PCM 音频。");
        }
        if (format.ValidBitsPerSample == 0 || format.ValidBitsPerSample > format.BitsPerSample)
        {
            throw new NotSupportedException("音频有效位数无效。");
        }
        if (format.Encoding == AudioSampleEncoding.IeeeFloat &&
            format.ValidBitsPerSample != format.BitsPerSample)
        {
            throw new NotSupportedException("浮点音频有效位数与容器位数不一致。");
        }

        double sumOfSquares = 0;
        double peak = 0;
        var sampleCount = checked(frameCount * format.Channels);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * format.BlockAlign;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var offset = frameOffset + channel * bytesPerSample;
                var value = ReadNormalizedSample(data[offset..], format);
                if (double.IsNaN(value))
                {
                    value = 0;
                }
                var magnitude = Math.Abs(value);
                peak = Math.Max(peak, magnitude);
                sumOfSquares += value * value;
            }
        }

        return sampleCount == 0
            ? AudioMeterSample.Silence
            : new AudioMeterSample(
                NormalizeLevel(Math.Sqrt(sumOfSquares / sampleCount)),
                NormalizeLevel(peak));
    }

    private async Task StopCoreAsync()
    {
        Task? worker;
        CancellationTokenSource? stop;
        lock (_lifecycleGate)
        {
            worker = _worker;
            stop = _stop;
            stop?.Cancel();
        }

        try
        {
            if (worker is not null)
            {
                await worker.ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_lifecycleGate)
            {
                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                    _stop = null;
                    stop?.Dispose();
                }
            }
        }
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        var comResult = CoInitializeEx(0, 0);
        var uninitializeCom = comResult >= 0;
        if (comResult < 0 && comResult != unchecked((int)0x80010106))
        {
            Marshal.ThrowExceptionForHR(comResult);
        }

        IAudioLoopbackSession? session = null;
        try
        {
            var nextEndpointPoll = DateTimeOffset.MinValue;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (session is null)
                    {
                        var endpointId = _getDefaultEndpointId();
                        session = _openSession(endpointId);
                        if (!StringComparer.Ordinal.Equals(endpointId, session.EndpointId))
                        {
                            throw new InvalidOperationException("打开的音频端点与请求端点不一致。");
                        }
                        nextEndpointPoll = _clock() + _endpointPollInterval;
                    }
                    else if (_clock() >= nextEndpointPoll)
                    {
                        var endpointId = _getDefaultEndpointId();
                        nextEndpointPoll = _clock() + _endpointPollInterval;
                        if (!StringComparer.Ordinal.Equals(endpointId, session.EndpointId))
                        {
                            Publish(AudioMeterSample.Silence);
                            session.Dispose();
                            session = null;
                            continue;
                        }
                    }

                    var readPacket = false;
                    while (session.TryReadSample(out var sample))
                    {
                        Publish(sample);
                        readPacket = true;
                    }
                    if (!readPacket && cancellationToken.WaitHandle.WaitOne(_retryInterval))
                    {
                        break;
                    }
                }
                catch (COMException) when (!cancellationToken.IsCancellationRequested)
                {
                    Publish(AudioMeterSample.Silence);
                    session?.Dispose();
                    session = null;
                    if (cancellationToken.WaitHandle.WaitOne(_retryInterval))
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            session?.Dispose();
            if (uninitializeCom)
            {
                CoUninitialize();
            }
        }
    }

    private void Publish(AudioMeterSample sample) =>
        _samples.Writer.TryWrite(new AudioMeterSample(
            NormalizeLevel(sample.Rms),
            NormalizeLevel(sample.Peak)));

    private static double ReadNormalizedSample(ReadOnlySpan<byte> data, AudioSampleFormat format)
    {
        if (format.Encoding == AudioSampleEncoding.IeeeFloat)
        {
            return format.BitsPerSample switch
            {
                32 => BitConverter.Int32BitsToSingle(
                    System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data)),
                64 => BitConverter.Int64BitsToDouble(
                    System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(data)),
                _ => throw new NotSupportedException($"不支持 {format.BitsPerSample} 位浮点音频。"),
            };
        }

        var validBits = format.ValidBitsPerSample == 0
            ? format.BitsPerSample
            : format.ValidBitsPerSample;
        if (validBits == 0 || validBits > format.BitsPerSample)
        {
            throw new NotSupportedException("PCM 有效位数无效。");
        }

        if (format.BitsPerSample == 8)
        {
            var signed = (data[0] - 128) >> (8 - validBits);
            return signed / Math.Pow(2, validBits - 1);
        }

        long value = format.BitsPerSample switch
        {
            16 => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data),
            24 => ReadInt24(data),
            32 => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data),
            _ => throw new NotSupportedException($"不支持 {format.BitsPerSample} 位 PCM 音频。"),
        };
        value >>= format.BitsPerSample - validBits;
        return value / Math.Pow(2, validBits - 1);
    }

    private static int ReadInt24(ReadOnlySpan<byte> data)
    {
        var value = data[0] | (data[1] << 8) | (data[2] << 16);
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xFF000000);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private sealed class CoreAudioLoopbackSession : IAudioLoopbackSession
    {
        private const int ERender = 0;
        private const int EConsole = 0;
        private const uint ClsctxAll = 23;
        private const int AudclntSharemodeShared = 0;
        private const uint AudclntStreamflagsLoopback = 0x00020000;
        private const uint AudclntBufferflagsSilent = 0x00000002;
        private static readonly Guid DeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid AudioClientInterfaceId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        private static readonly Guid AudioCaptureClientInterfaceId = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00AA00389B71");
        private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");
        private IMMDevice? _device;
        private IAudioClient? _audioClient;
        private IAudioCaptureClient? _captureClient;
        private readonly AudioSampleFormat _format;

        private CoreAudioLoopbackSession(
            string endpointId,
            IMMDevice device,
            IAudioClient audioClient,
            IAudioCaptureClient captureClient,
            AudioSampleFormat format)
        {
            EndpointId = endpointId;
            _device = device;
            _audioClient = audioClient;
            _captureClient = captureClient;
            _format = format;
        }

        public string EndpointId { get; }

        internal static string GetDefaultEndpointId()
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            try
            {
                enumerator = CreateEnumerator();
                ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(ERender, EConsole, out device));
                ThrowIfFailed(device.GetId(out var endpointId));
                return endpointId;
            }
            finally
            {
                Release(device);
                Release(enumerator);
            }
        }

        internal static IAudioLoopbackSession Open(string endpointId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            IAudioClient? audioClient = null;
            IAudioCaptureClient? captureClient = null;
            nint mixFormat = 0;
            try
            {
                enumerator = CreateEnumerator();
                ThrowIfFailed(enumerator.GetDevice(endpointId, out device));
                var audioClientId = AudioClientInterfaceId;
                ThrowIfFailed(device.Activate(ref audioClientId, ClsctxAll, 0, out var audioClientObject));
                audioClient = (IAudioClient)audioClientObject;
                ThrowIfFailed(audioClient.GetMixFormat(out mixFormat));
                var format = ParseFormat(mixFormat);
                ThrowIfFailed(audioClient.Initialize(
                    AudclntSharemodeShared,
                    AudclntStreamflagsLoopback,
                    0,
                    0,
                    mixFormat,
                    0));
                var captureClientId = AudioCaptureClientInterfaceId;
                ThrowIfFailed(audioClient.GetService(ref captureClientId, out var captureClientObject));
                captureClient = (IAudioCaptureClient)captureClientObject;
                ThrowIfFailed(audioClient.Start());

                var result = new CoreAudioLoopbackSession(
                    endpointId,
                    device,
                    audioClient,
                    captureClient,
                    format);
                device = null;
                audioClient = null;
                captureClient = null;
                return result;
            }
            finally
            {
                if (mixFormat != 0)
                {
                    Marshal.FreeCoTaskMem(mixFormat);
                }
                Release(captureClient);
                Release(audioClient);
                Release(device);
                Release(enumerator);
            }
        }

        public bool TryReadSample(out AudioMeterSample sample)
        {
            var captureClient = _captureClient ?? throw new ObjectDisposedException(nameof(CoreAudioLoopbackSession));
            ThrowIfFailed(captureClient.GetNextPacketSize(out var packetFrames));
            if (packetFrames == 0)
            {
                sample = default;
                return false;
            }

            ThrowIfFailed(captureClient.GetBuffer(
                out var data,
                out var frameCount,
                out var flags,
                out _,
                out _));
            try
            {
                if ((flags & AudclntBufferflagsSilent) != 0 || frameCount == 0)
                {
                    sample = AudioMeterSample.Silence;
                    return true;
                }

                var byteCount = checked((int)frameCount * _format.BlockAlign);
                var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
                try
                {
                    Marshal.Copy(data, buffer, 0, byteCount);
                    sample = ComputeSample(buffer.AsSpan(0, byteCount), _format, checked((int)frameCount));
                    return true;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            finally
            {
                ThrowIfFailed(captureClient.ReleaseBuffer(frameCount));
            }
        }

        public void Dispose()
        {
            var audioClient = Interlocked.Exchange(ref _audioClient, null);
            if (audioClient is not null)
            {
                _ = audioClient.Stop();
            }
            Release(Interlocked.Exchange(ref _captureClient, null));
            Release(audioClient);
            Release(Interlocked.Exchange(ref _device, null));
        }

        private static IMMDeviceEnumerator CreateEnumerator()
        {
            var enumeratorType = Type.GetTypeFromCLSID(DeviceEnumeratorClassId, throwOnError: true)!;
            return (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        }

        private static AudioSampleFormat ParseFormat(nint format)
        {
            var tag = unchecked((ushort)Marshal.ReadInt16(format, 0));
            var channels = unchecked((ushort)Marshal.ReadInt16(format, 2));
            var blockAlign = unchecked((ushort)Marshal.ReadInt16(format, 12));
            var bits = unchecked((ushort)Marshal.ReadInt16(format, 14));
            var validBits = bits;
            AudioSampleEncoding encoding;
            if (tag == 1)
            {
                encoding = AudioSampleEncoding.Pcm;
            }
            else if (tag == 3)
            {
                encoding = AudioSampleEncoding.IeeeFloat;
            }
            else if (tag == 0xFFFE)
            {
                var extraSize = unchecked((ushort)Marshal.ReadInt16(format, 16));
                if (extraSize < 22)
                {
                    throw new NotSupportedException("WAVEFORMATEXTENSIBLE 长度无效。");
                }
                validBits = unchecked((ushort)Marshal.ReadInt16(format, 18));
                var subFormat = Marshal.PtrToStructure<Guid>(format + 24);
                encoding = subFormat == PcmSubFormat
                    ? AudioSampleEncoding.Pcm
                    : subFormat == FloatSubFormat
                        ? AudioSampleEncoding.IeeeFloat
                        : throw new NotSupportedException($"不支持的 WASAPI 子格式 {subFormat}。");
            }
            else
            {
                throw new NotSupportedException($"不支持的 WASAPI 格式标签 0x{tag:X4}。");
            }

            var result = new AudioSampleFormat(encoding, channels, bits, validBits, blockAlign);
            _ = ComputeSample([], result, 0);
            return result;
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
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint paddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closestMatch);
        [PreserveSig] int GetMixFormat(out nint format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(nint eventHandle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
