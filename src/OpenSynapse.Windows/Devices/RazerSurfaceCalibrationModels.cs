namespace OpenSynapse.Windows.Devices;

public enum RazerSurfaceCalibrationMode
{
    Unknown,
    Disabled,
    Adaptive,
    Adjustable,
    Preset,
    Manual,
    Mini,
}

public sealed record RazerSurfaceCalibrationRawData
{
    public byte[] Parameters { get; init; } = [];
    public byte[] CalibrationData { get; init; } = [];
    public byte[] LiftToTrackData { get; init; } = [];
    public byte[] TrackToLiftData { get; init; } = [];
    public byte[] Z { get; init; } = [];
    public byte[] Q { get; init; } = [];
}

public sealed record RazerSurfaceCalibrationConfiguration
{
    public RazerSurfaceCalibrationMode Mode { get; init; }
    public int TrackingDistance { get; init; }
    public int LiftOffDistance { get; init; }
    public int LandingDistance { get; init; }
    public int Lod { get; init; }
    public bool IsAsymmetric { get; init; }
    public string? PresetId { get; init; }
    public RazerSurfaceCalibrationRawData? RawData { get; init; }
}

public sealed record RazerSurfaceCalibrationState
{
    public RazerSurfaceCalibrationConfiguration Configuration { get; init; } = new();
    public byte? SensorState { get; init; }
    public byte? LiftMode { get; init; }
    public byte? LiftHeight { get; init; }
    public byte[] Parameters { get; init; } = [];
    public byte[] CalibrationData { get; init; } = [];
    public byte[] LiftToTrackData { get; init; } = [];
    public byte[] TrackToLiftData { get; init; } = [];
    public byte[] DeviceMode { get; init; } = [];
    public ushort[] Thresholds { get; init; } = [];
}

public enum RazerSurfaceCalibrationReadback
{
    Consistent,
    Unavailable,
    Mismatch,
}

public interface IRazerSurfaceCalibrationClient
{
    RazerProximitySensorProfile Profile { get; }
    Task<RazerSurfaceCalibrationState> ReadAsync(CancellationToken cancellationToken = default);
    Task ApplyAsync(RazerSurfaceCalibrationConfiguration configuration, CancellationToken cancellationToken = default);
    Task PrepareAsync(RazerSurfaceCalibrationConfiguration candidate, CancellationToken cancellationToken = default);
    Task StartAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default);
    Task StopAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default);
    Task<RazerSurfaceCalibrationRawData> ReadResultAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default);
    Task RestoreAsync(RazerSurfaceCalibrationState snapshot, CancellationToken cancellationToken = default);
    Task RestoreDeviceModeAsync(RazerSurfaceCalibrationState snapshot, CancellationToken cancellationToken = default);
    RazerSurfaceCalibrationConfiguration CreateCandidate(RazerSurfaceCalibrationConfiguration configuration, RazerSurfaceCalibrationRawData raw);
    RazerSurfaceCalibrationReadback CompareReadback(RazerSurfaceCalibrationConfiguration configuration, RazerSurfaceCalibrationState state);
}
