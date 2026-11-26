using System.Collections.Immutable;
using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

[Flags]
public enum RazerCalibrationCapabilities
{
    None = 0,
    Preset = 1,
    Manual = 2,
    Mini = 4,
    Adaptive = 8,
    Asymmetric = 16,
    EnableDisable = 32,
    LiftOff = 64,
}

public enum RazerCalibrationConnection { Wired, Wireless, Ble }
public enum RazerCalibrationSupport { Unknown, Supported, Unsupported }
public enum RazerCalibrationDistanceUnit { Level, Millimeters }

public sealed record RazerCalibrationRange(
    int Minimum,
    int Maximum,
    int DefaultValue,
    RazerCalibrationDistanceUnit Unit = RazerCalibrationDistanceUnit.Level,
    int Step = 1)
{
    public bool Contains(int value) =>
        value >= Minimum && value <= Maximum && (value - Minimum) % Step == 0;
}

public sealed record RazerCalibrationPreset
{
    public required string Id { get; init; }
    // Pixart rows retain Param0..Param8; the protocol writer accepts only Param0..Param7.
    public ImmutableArray<ImmutableArray<byte>> Values { get; init; } = [];
    public int? SurfaceDistance { get; init; }
    public ImmutableArray<int> SqList { get; init; } = [];
}

public sealed record RazerSmartPixartProductData
{
    public ImmutableDictionary<int, byte> SmartTrackingLiftMode { get; init; } = ImmutableDictionary<int, byte>.Empty;
    public ImmutableDictionary<int, byte> SmartTrackingLiftCutOffTable { get; init; } = ImmutableDictionary<int, byte>.Empty;
    public ImmutableDictionary<int, byte> SmartTrackingLiftHysteresTable { get; init; } = ImmutableDictionary<int, byte>.Empty;
    public ImmutableDictionary<int, byte> PresetCalibrationLiftMode { get; init; } = ImmutableDictionary<int, byte>.Empty;
    public ImmutableDictionary<int, byte> ManualCalibrationLiftMode { get; init; } = ImmutableDictionary<int, byte>.Empty;
    public ImmutableDictionary<int, ImmutableArray<byte>> PresetCalibrationSurfaceDistanceTable { get; init; } = ImmutableDictionary<int, ImmutableArray<byte>>.Empty;
    public ImmutableDictionary<int, ImmutableArray<byte>> PresetCalibrationLiftToTrackTable { get; init; } = ImmutableDictionary<int, ImmutableArray<byte>>.Empty;
    public ImmutableDictionary<int, ImmutableArray<byte>> PresetCalibrationTrackToLiftScalingTable { get; init; } = ImmutableDictionary<int, ImmutableArray<byte>>.Empty;
}

public sealed record RazerCalibrationProductData
{
    public required ushort ProductId { get; init; }
    public required string ProductName { get; init; }
    public ImmutableArray<ushort> UsbProductIds { get; init; } = [];
    public ImmutableArray<ushort> BleProductIds { get; init; } = [];
    public RazerCalibrationSupport WiredSupport { get; init; }
    public RazerCalibrationSupport WirelessSupport { get; init; }
    public RazerCalibrationSupport BleSupport { get; init; }
    public RazerCalibrationCapabilities Capabilities { get; init; }
    public RazerCalibrationRange? TrackingDistance { get; init; }
    public RazerCalibrationRange? LiftOffDistance { get; init; }
    public RazerCalibrationRange? LandingDistance { get; init; }
    public RazerCalibrationRange? Lod { get; init; }
    public RazerCalibrationRange? SurfaceLiftOffDistance { get; init; }
    public RazerCalibrationRange? SurfaceLandingDistance { get; init; }
    public ImmutableArray<RazerCalibrationPreset> Presets { get; init; } = [];
    public ImmutableArray<double> LodConversion { get; init; } = [];
    public RazerSmartPixartProductData? SmartPixart { get; init; }
    public JsonElement InitialCalibration { get; init; }
    public required string Evidence { get; init; }
}
