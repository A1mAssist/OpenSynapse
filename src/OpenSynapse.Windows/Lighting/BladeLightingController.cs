using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public enum BladeLightingMode
{
    Off,
    Static,
    Breathing,
    Spectrum,
    Wave,
    Fire,
    Reactive,
    Ripple,
    AudioMeter,
    Ambient,
    Wheel,
    Starlight,
    Tidal,
    ChromaFile,
}

public enum BladeStarlightColorMode
{
    Random,
    Single,
    Dual,
}

public sealed record BladeLightingEffect(
    BladeLightingMode Mode,
    RazerRgb Color = default,
    BladeWaveDirection Direction = BladeWaveDirection.Right,
    RazerRgb SecondColor = default,
    byte ReactiveSpeed = 2,
    byte StarlightSpeed = 2,
    BladeStarlightColorMode StarlightColorMode = BladeStarlightColorMode.Single,
    LightingPalette? Palette = null,
    byte AnimationSpeed = 2,
    ChromaFileAnimation? ChromaAnimation = null)
{
    public static BladeLightingEffect Off { get; } = new(BladeLightingMode.Off);
    public static BladeLightingEffect Spectrum { get; } = new(BladeLightingMode.Spectrum);
    public static BladeLightingEffect Fire { get; } = new(BladeLightingMode.Fire);
}

public interface IBladeLightingController : IAsyncDisposable
{
    event Action<string, Exception>? RestoreFailed;
    Task RuntimeCompletion { get; }

    Task ApplyAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLightingEffect effect,
        CancellationToken cancellationToken = default);

    Task StopAsync();

    Task SetDisplayAvailableAsync(bool available, IReadOnlyList<DeviceDescriptor>? devices = null);

    Task ApplyExternalAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ISoftwareLightingFrameSource source,
        bool restorePersistentEffect = true,
        CancellationToken cancellationToken = default);
}

public sealed partial class BladeLightingController : IBladeLightingController
{
    // The matrix path is seven feature reports per frame. Keep only the latest
    // frame if the device cannot sustain this target; never build a stale queue.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000d / 60d);
    // Stay well below the observed sub-30-second firmware idle window without becoming a frame pump.
    private static readonly TimeSpan NativeEffectKeepAliveInterval = TimeSpan.FromSeconds(5);
    private static readonly RazerRgb DefaultRestoreColor = new(0x99, 0xDD, 0x72);
    private static readonly TimeSpan MatrixWait = TimeSpan.FromMilliseconds(1);

    private readonly IRazerFeatureTransport _transport;
    private readonly RazerDeviceRegistry _registry;
    private readonly RazerRgb[] _restoreFrame;
    private readonly BladeSoftwareModeCoordinator _modeCoordinator;
    private readonly TimeSpan _nativeEffectKeepAliveInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SoftwareLightingRuntime? _runtime;
    private ISoftwareLightingFrameSource? _externalSource;
    private WindowsKeyboardLightingAdapter? _keyboardInput;
    private BladeSoftwareModeCoordinator.BladeSoftwareModeLease? _modeLease;
    private Task _runtimeCompletion = Task.CompletedTask;
    private byte _transactionId;
    private bool _nativeEffectActive;
    private string? _nativeEffectDevicePath;
    private CancellationTokenSource? _nativeEffectKeepAliveCancellation;
    private Task _nativeEffectKeepAliveTask = Task.CompletedTask;
    private int _turnOffOnStop;
    private int _displayAvailable = 1;
    private BladeLightingDialect _lightingDialect;
    private int _matrixRows = BladeLightingProtocol.Rows;
    private int _matrixColumns = BladeLightingProtocol.Columns;
    private int _disposed;

    public event Action<string, Exception>? RestoreFailed;
    public Task RuntimeCompletion => _runtimeCompletion;
}
