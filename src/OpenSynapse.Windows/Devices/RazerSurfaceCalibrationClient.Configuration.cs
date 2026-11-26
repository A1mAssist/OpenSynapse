namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerSurfaceCalibrationClient
{
    private sealed record CalibrationPlan(bool Enabled, byte? LiftMode = null, byte LiftHeight = 0,
        byte[]? Configuration = null, byte[]? Landing = null, byte[]? LiftOff = null,
        byte[]? PhilipsData = null, ushort[]? Threshold = null)
    {
        public byte[] Parameters => Configuration ?? [];
        public byte[] LiftToTrack => Landing ?? [];
        public byte[] TrackToLift => LiftOff ?? [];
        public byte[] CalibrationData => PhilipsData ?? [];
        public ushort[] Thresholds => Threshold ?? [];
    }

    private RazerCalibrationProductData Product => Profile.Calibration
        ?? throw new NotSupportedException("This product has no confirmed calibration parameter table.");

    private CalibrationPlan BuildPlan(RazerSurfaceCalibrationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var mode = configuration.Mode;
        if (mode == RazerSurfaceCalibrationMode.Unknown || !Enum.IsDefined(mode))
            throw new ArgumentException("Select a confirmed calibration mode.", nameof(configuration));
        if (mode == RazerSurfaceCalibrationMode.Disabled)
        {
            RequireCapability(RazerCalibrationCapabilities.EnableDisable);
            return new(false);
        }
        ValidateDistances(configuration);
        switch (Profile.Kind)
        {
            case RazerProximityCalibrationKind.Orca5:
            case RazerProximityCalibrationKind.Orca5Plus:
                if (mode == RazerSurfaceCalibrationMode.Adaptive)
                {
                    RequireCapability(RazerCalibrationCapabilities.Adaptive);
                    return new(Profile.Kind == RazerProximityCalibrationKind.Orca5Plus, 1,
                        checked((byte)(configuration.TrackingDistance - 1)));
                }
                if (mode != RazerSurfaceCalibrationMode.Adjustable) throw UnsupportedMode();
                RequireCapability(RazerCalibrationCapabilities.Asymmetric);
                return new(true, 4, Configuration:
                    [checked((byte)(configuration.LiftOffDistance - 1)), checked((byte)(configuration.LandingDistance - 1))]);
            case RazerProximityCalibrationKind.Pixart3349:
                if (mode != RazerSurfaceCalibrationMode.Preset) throw UnsupportedMode();
                RequireCapability(RazerCalibrationCapabilities.LiftOff);
                return new(true, Configuration: [checked((byte)configuration.LiftOffDistance)]);
            case RazerProximityCalibrationKind.Pixart:
                if (mode == RazerSurfaceCalibrationMode.Preset)
                {
                    RequireCapability(RazerCalibrationCapabilities.Preset);
                    var preset = FindPreset(configuration.PresetId);
                    if (preset.Values.Length < configuration.Lod || preset.Values[configuration.Lod - 1].Length != 9)
                        throw new InvalidDataException("The product's Pixart preset must contain nine parameters for this LOD.");
                    return new(true, Configuration: preset.Values[configuration.Lod - 1].Take(8).ToArray());
                }
                if (mode != RazerSurfaceCalibrationMode.Manual) throw UnsupportedMode();
                RequireCapability(RazerCalibrationCapabilities.Manual);
                return new(true, Configuration: RazerSurfaceCalibrationAlgorithms.PixartManual(
                    RequireRaw(configuration), configuration.Lod, Product.LodConversion));
            case RazerProximityCalibrationKind.SmartPixart:
                return BuildSmartPlan(configuration);
            case RazerProximityCalibrationKind.Philips:
                if (mode is not (RazerSurfaceCalibrationMode.Manual or RazerSurfaceCalibrationMode.Preset or RazerSurfaceCalibrationMode.Mini))
                    throw UnsupportedMode();
                RequireCapability(mode == RazerSurfaceCalibrationMode.Manual ? RazerCalibrationCapabilities.Manual :
                    mode == RazerSurfaceCalibrationMode.Mini ? RazerCalibrationCapabilities.Mini : RazerCalibrationCapabilities.Preset);
                var (data, threshold) = RazerSurfaceCalibrationAlgorithms.Philips(RequireRaw(configuration), configuration.Lod,
                    mode == RazerSurfaceCalibrationMode.Manual ? null : FindPreset(configuration.PresetId));
                return new(true, PhilipsData: data, Threshold: [threshold, 0, 0]);
            default:
                throw UnsupportedMode();
        }
    }

    private CalibrationPlan BuildSmartPlan(RazerSurfaceCalibrationConfiguration configuration)
    {
        var tables = Product.SmartPixart ?? throw new InvalidDataException("This product has no SmartPixart tables.");
        var asymmetric = configuration.IsAsymmetric;
        if (asymmetric) RequireCapability(RazerCalibrationCapabilities.Asymmetric);
        if (configuration.Mode == RazerSurfaceCalibrationMode.Adaptive)
        {
            RequireCapability(RazerCalibrationCapabilities.Adaptive);
            var key = asymmetric ? (configuration.LandingDistance, configuration.LiftOffDistance) switch
            {
                (1, 2) => 1, (1, 3) => 2, (2, 3) => 3,
                _ => throw new ArgumentException("Smart tracking supports landing/lift pairs 1/2, 1/3 or 2/3."),
            } : configuration.TrackingDistance;
            return new(false, Lookup(tables.SmartTrackingLiftMode, asymmetric ? 1 : 0),
                Lookup(asymmetric ? tables.SmartTrackingLiftHysteresTable : tables.SmartTrackingLiftCutOffTable, key));
        }
        if (configuration.Mode == RazerSurfaceCalibrationMode.Preset)
        {
            RequireCapability(RazerCalibrationCapabilities.Preset);
            return asymmetric
                // Source export names are inverted: Ie feeds lift-to-track (8 bytes), Ee track-to-lift (5).
                ? new(true, SmartMode(configuration, false), Landing: Lookup(tables.PresetCalibrationTrackToLiftScalingTable, configuration.LandingDistance).ToArray(),
                    LiftOff: Lookup(tables.PresetCalibrationLiftToTrackTable, configuration.LiftOffDistance).ToArray())
                : new(true, SmartMode(configuration, false), Configuration: Lookup(tables.PresetCalibrationSurfaceDistanceTable, configuration.Lod).ToArray());
        }
        if (configuration.Mode != RazerSurfaceCalibrationMode.Manual) throw UnsupportedMode();
        RequireCapability(RazerCalibrationCapabilities.Manual);
        var raw = RequireRaw(configuration).Parameters;
        if (raw.Length != 9) throw new InvalidDataException("SmartPixart sampling requires all nine raw parameters.");
        return asymmetric
            ? new(true, SmartMode(configuration, true), Landing: RazerSurfaceCalibrationAlgorithms.SmartPixartManual(configuration.LandingDistance, raw[0], raw[1], true),
                LiftOff: RazerSurfaceCalibrationAlgorithms.SmartPixartManual(configuration.LiftOffDistance, raw[0], raw[1], false))
            : new(true, SmartMode(configuration, true), Configuration: RazerSurfaceCalibrationAlgorithms.SmartPixartManual(configuration.Lod, raw[0], raw[1]));
    }

    private byte SmartMode(RazerSurfaceCalibrationConfiguration configuration, bool manual)
    {
        var tables = Product.SmartPixart ?? throw new InvalidDataException("This product has no SmartPixart tables.");
        return Lookup(manual ? tables.ManualCalibrationLiftMode : tables.PresetCalibrationLiftMode, configuration.IsAsymmetric ? 1 : 0);
    }

    private void ValidateDistances(RazerSurfaceCalibrationConfiguration configuration)
    {
        if (configuration.Mode == RazerSurfaceCalibrationMode.Disabled) return;
        var asymmetric = configuration.Mode == RazerSurfaceCalibrationMode.Adjustable || configuration.IsAsymmetric;
        if (asymmetric)
        {
            RequireCapability(RazerCalibrationCapabilities.Asymmetric);
            var surface = Profile.Kind == RazerProximityCalibrationKind.SmartPixart && configuration.Mode != RazerSurfaceCalibrationMode.Adaptive;
            ValidateRange(surface ? Product.SurfaceLiftOffDistance : Product.LiftOffDistance,
                configuration.LiftOffDistance, nameof(configuration.LiftOffDistance));
            ValidateRange(surface ? Product.SurfaceLandingDistance : Product.LandingDistance,
                configuration.LandingDistance, nameof(configuration.LandingDistance));
            if (configuration.LandingDistance >= configuration.LiftOffDistance)
                throw new ArgumentException("Landing distance must be below lift-off distance.");
        }
        else if (configuration.Mode == RazerSurfaceCalibrationMode.Adaptive)
            ValidateRange(Product.TrackingDistance, configuration.TrackingDistance, nameof(configuration.TrackingDistance));
        else if (Profile.Kind == RazerProximityCalibrationKind.Pixart3349)
            ValidateRange(Product.LiftOffDistance, configuration.LiftOffDistance, nameof(configuration.LiftOffDistance));
        else
            ValidateRange(Product.Lod, configuration.Lod, nameof(configuration.Lod));
    }

    private void RequireSampling(RazerSurfaceCalibrationMode mode)
    {
        if (mode is not (RazerSurfaceCalibrationMode.Manual or RazerSurfaceCalibrationMode.Mini)) throw UnsupportedMode();
        if (Profile.Kind is not (RazerProximityCalibrationKind.Pixart or RazerProximityCalibrationKind.SmartPixart or RazerProximityCalibrationKind.Philips))
            throw UnsupportedMode();
        if (mode == RazerSurfaceCalibrationMode.Mini && Profile.Kind != RazerProximityCalibrationKind.Philips) throw UnsupportedMode();
        RequireCapability(mode == RazerSurfaceCalibrationMode.Mini ? RazerCalibrationCapabilities.Mini : RazerCalibrationCapabilities.Manual);
    }

    private void RequireCapability(RazerCalibrationCapabilities capability)
    {
        if (!Product.Capabilities.HasFlag(capability)) throw new NotSupportedException($"This product does not support {capability} calibration.");
    }
    private RazerCalibrationPreset FindPreset(string? id) => Product.Presets.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("Select a preset from this product's calibration table.", nameof(id));
    private static RazerSurfaceCalibrationRawData RequireRaw(RazerSurfaceCalibrationConfiguration configuration) => configuration.RawData
        ?? throw new InvalidOperationException("This calibration needs its original sampling data.");
    private static T Lookup<T>(IReadOnlyDictionary<int, T> table, int key) => table.TryGetValue(key, out var value) ? value
        : throw new InvalidDataException($"The product calibration table has no entry for {key}.");
    private static void ValidateRange(RazerCalibrationRange? range, int value, string name)
    {
        if (range is null) throw new NotSupportedException($"This product has no confirmed range for {name}.");
        if (!range.Contains(value)) throw new ArgumentOutOfRangeException(name, value, $"Expected {range.Minimum}..{range.Maximum} in steps of {range.Step}.");
    }
    private static NotSupportedException UnsupportedMode() => new("This engine does not support that calibration mode.");

    private RazerSurfaceCalibrationConfiguration DecodeConfiguration(RazerSurfaceCalibrationState state)
    {
        var mode = RazerSurfaceCalibrationMode.Unknown;
        var tracking = 0;
        var lift = 0;
        var landing = 0;
        var lod = 0;
        string? presetId = null;
        var asymmetric = false;
        if (Profile.Kind is RazerProximityCalibrationKind.Orca5 or RazerProximityCalibrationKind.Orca5Plus)
        {
            mode = state.LiftMode switch { 1 => RazerSurfaceCalibrationMode.Adaptive, 4 => RazerSurfaceCalibrationMode.Adjustable, _ => mode };
            if (mode == RazerSurfaceCalibrationMode.Adaptive) tracking = (state.LiftHeight ?? 0) + 1;
            if (mode == RazerSurfaceCalibrationMode.Adjustable && state.Parameters.Length >= 2)
            { lift = state.Parameters[0] + 1; landing = state.Parameters[1] + 1; asymmetric = true; }
        }
        else if (Profile.Kind == RazerProximityCalibrationKind.SmartPixart)
        {
            mode = state.LiftMode switch
            {
                1 or 2 => RazerSurfaceCalibrationMode.Adaptive,
                3 or 4 => RazerSurfaceCalibrationMode.Preset,
                5 or 6 => RazerSurfaceCalibrationMode.Manual,
                _ => mode,
            };
            asymmetric = state.LiftMode is 2 or 4 or 6;
            if (state.LiftMode == 1) tracking = (state.LiftHeight ?? 0) + 1;
            else if (state.LiftMode == 2 && state.LiftHeight <= 2)
                (landing, lift) = state.LiftHeight switch { 0 => (1, 2), 1 => (1, 3), _ => (2, 3) };
            if (Profile.Calibration?.SmartPixart is { } tables)
            {
                if (state.LiftMode == 3)
                    lod = MatchTable(tables.PresetCalibrationSurfaceDistanceTable, state.Parameters);
                else if (state.LiftMode == 4)
                {
                    lift = MatchTable(tables.PresetCalibrationLiftToTrackTable, state.TrackToLiftData);
                    landing = MatchTable(tables.PresetCalibrationTrackToLiftScalingTable, state.LiftToTrackData);
                }
            }
        }
        else if (Profile.Kind == RazerProximityCalibrationKind.Pixart3349 && state.SensorState == 1 && state.Parameters.Length > 0)
        { mode = RazerSurfaceCalibrationMode.Preset; lift = state.Parameters[0]; }
        else if (Profile.Kind == RazerProximityCalibrationKind.Pixart && state.SensorState == 1 &&
                 state.Parameters.Length >= 8 && Profile.Calibration is { } product)
        {
            var matches = product.Presets.SelectMany(preset => preset.Values.Select((values, index) =>
                (preset.Id, Lod: index + 1, Values: values)))
                .Where(row => row.Values.Length >= 8 && row.Values.AsSpan(0, 8).SequenceEqual(state.Parameters.AsSpan(0, 8)))
                .Take(2).ToArray();
            if (matches.Length == 1)
            {
                mode = RazerSurfaceCalibrationMode.Preset;
                presetId = matches[0].Id;
                lod = matches[0].Lod;
            }
        }
        if (state.SensorState == 0 && Profile.Kind is RazerProximityCalibrationKind.Pixart or RazerProximityCalibrationKind.Pixart3349 or RazerProximityCalibrationKind.Philips)
            mode = RazerSurfaceCalibrationMode.Disabled;
        if (state.SensorState == 0 && Profile.Kind == RazerProximityCalibrationKind.SmartPixart && state.LiftMode is >= 3 and <= 6)
            mode = RazerSurfaceCalibrationMode.Disabled;
        return new() { Mode = mode, TrackingDistance = tracking, LiftOffDistance = lift, LandingDistance = landing,
            Lod = lod, PresetId = presetId, IsAsymmetric = asymmetric };
    }

    private static int MatchTable(IReadOnlyDictionary<int, System.Collections.Immutable.ImmutableArray<byte>> table, byte[] data)
    {
        var matches = table.Where(entry => data.Length >= entry.Value.Length &&
            entry.Value.AsSpan().SequenceEqual(data.AsSpan(0, entry.Value.Length))).Select(entry => entry.Key).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : 0;
    }
}
