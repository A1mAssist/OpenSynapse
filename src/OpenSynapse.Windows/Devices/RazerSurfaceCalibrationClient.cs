using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>Short ordered exchanges; input subscriptions, human waits and persistence belong to the session.</summary>
public sealed partial class RazerSurfaceCalibrationClient : IRazerSurfaceCalibrationClient
{
    private const byte TransactionId = 0x1F;
    private readonly Func<byte[], CancellationToken, Task<byte[]>> _execute;
    private readonly Func<Func<CancellationToken, Task>, CancellationToken, Task> _transaction;
    public RazerProximitySensorProfile Profile { get; }

    public RazerSurfaceCalibrationClient(RazerProximitySensorProfile profile,
        Func<byte[], CancellationToken, Task<byte[]>> execute,
        Func<Func<CancellationToken, Task>, CancellationToken, Task>? transaction = null)
    {
        Profile = profile;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _transaction = transaction ?? ((action, token) => action(token));
    }

    public Task<RazerSurfaceCalibrationState> ReadAsync(CancellationToken cancellationToken = default) =>
        TransactionAsync(async token =>
        {
            var state = new RazerSurfaceCalibrationState();
            var stateReport = RazerProximitySensorProtocol.CreateGetProximitySensorStateRequest(TransactionId, Profile.ClassId, Profile.SensorId);
            var stateResponse = await OptionalReadAsync(stateReport, token).ConfigureAwait(false);
            if (stateResponse is not null)
                state = state with { SensorState = RazerProximitySensorProtocol.ParseState(stateResponse, stateReport).State };
            if (Profile.Kind == RazerProximityCalibrationKind.Philips)
            {
                var dataReport = RazerProximitySensorProtocol.CreateGetProximitySensorCalibrationDataRequest(TransactionId, Profile.ClassId, Profile.SensorId);
                var data = RazerProximitySensorProtocol.ParseCalibrationData(await _execute(dataReport, token).ConfigureAwait(false), dataReport);
                var thresholdReport = RazerProximitySensorProtocol.CreateGetProximitySensorThresholdRequest(TransactionId, Profile.ClassId, Profile.SensorId);
                var thresholds = RazerProximitySensorProtocol.ParseThreshold(await _execute(thresholdReport, token).ConfigureAwait(false), thresholdReport);
                state = state with { CalibrationData = data, Thresholds = [thresholds.MaxThreshold, thresholds.MinThreshold, thresholds.OverspeedThreshold] };
            }
            else if (Profile.Kind is RazerProximityCalibrationKind.Pixart or RazerProximityCalibrationKind.Pixart3349)
                state = state with { Parameters = await ReadParametersAsync(token).ConfigureAwait(false) };
            else
            {
                var report = RazerProximitySensorProtocol.CreateGetProximitySensorLiftSettingRequest(TransactionId, Profile.ClassId, Profile.SensorId);
                var lift = RazerProximitySensorProtocol.ParseLiftSetting(await _execute(report, token).ConfigureAwait(false), report);
                state = state with { LiftMode = lift.LiftMode, LiftHeight = lift.LiftHeight };
                if (lift.LiftMode is >= 3 and <= 6)
                    state = state with { Parameters = await ReadParametersAsync(token).ConfigureAwait(false) };
                if (Profile.Kind == RazerProximityCalibrationKind.SmartPixart && lift.LiftMode is 4 or 6)
                {
                    var down = RazerProximitySensorProtocol.CreateGetProximitySensorLiftToTrackCalibrationDataRequest(TransactionId, Profile.ClassId, Profile.SensorId);
                    var up = RazerProximitySensorProtocol.CreateGetProximitySensorTrackToLiftCalibrationDataRequest(TransactionId, Profile.ClassId, Profile.SensorId);
                    var downResponse = await OptionalReadAsync(down, token).ConfigureAwait(false);
                    var upResponse = await OptionalReadAsync(up, token).ConfigureAwait(false);
                    state = state with
                    {
                        LiftToTrackData = downResponse is null ? [] : RazerProximitySensorProtocol.ParseTrackingCalibrationData(downResponse, down),
                        TrackToLiftData = upResponse is null ? [] : RazerProximitySensorProtocol.ParseTrackingCalibrationData(upResponse, up),
                    };
                }
            }
            return state with { Configuration = DecodeConfiguration(state) };
        }, cancellationToken);

    public Task ApplyAsync(RazerSurfaceCalibrationConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var plan = BuildPlan(configuration);
        return _transaction(async token =>
        {
            await SetStateAsync(plan.Enabled, token).ConfigureAwait(false);
            if (!plan.Enabled && configuration.Mode == RazerSurfaceCalibrationMode.Disabled) return;
            if (plan.LiftMode is { } mode) await SetLiftAsync(mode, plan.LiftHeight, token).ConfigureAwait(false);
            await WriteDataAsync(plan, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task PrepareAsync(RazerSurfaceCalibrationConfiguration candidate, CancellationToken cancellationToken = default)
    {
        RequireSampling(candidate.Mode);
        ValidateDistances(candidate);
        byte? liftMode = null;
        if (Profile.Kind == RazerProximityCalibrationKind.Pixart && Product.LodConversion.Length < candidate.Lod)
            throw new NotSupportedException("This product's manual LOD conversion table has not been confirmed.");
        if (Profile.Kind == RazerProximityCalibrationKind.SmartPixart)
            liftMode = SmartMode(candidate, manual: true);
        if (candidate.Mode == RazerSurfaceCalibrationMode.Mini)
        {
            var preset = FindPreset(candidate.PresetId);
            if (preset.SurfaceDistance is null || preset.SqList.Length < candidate.Lod)
                throw new InvalidDataException("The product's Philips preset table is incomplete.");
        }
        return _transaction(async token =>
        {
            await SetStateAsync(true, token).ConfigureAwait(false);
            if (liftMode is { } mode)
                await SetLiftAsync(mode, 0, token).ConfigureAwait(false);
            else if (Profile.Kind == RazerProximityCalibrationKind.Philips)
                await WriteAsync(RazerProximitySensorProtocol.CreateSetProximitySensorCalibrationRequest(
                    TransactionId, Profile.ClassId, Profile.SensorId, 10, 0, 0), token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task StartAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default) => ControlAsync(mode, 0, cancellationToken);
    public Task StopAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default) => ControlAsync(mode, 1, cancellationToken);

    private Task ControlAsync(RazerSurfaceCalibrationMode mode, byte stage, CancellationToken cancellationToken)
    {
        RequireSampling(mode);
        return _transaction(token => WriteAsync(RazerProximitySensorProtocol.CreateSetProximitySensorControlRequest(
            TransactionId, Profile.ClassId, Profile.SensorId, mode == RazerSurfaceCalibrationMode.Mini ? (byte)1 : (byte)0, stage), token), cancellationToken);
    }

    public Task<RazerSurfaceCalibrationRawData> ReadResultAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default)
    {
        RequireSampling(mode);
        return TransactionAsync(async token =>
        {
            if (Profile.Kind != RazerProximityCalibrationKind.Philips)
                return new RazerSurfaceCalibrationRawData { Parameters = await ReadParametersAsync(token).ConfigureAwait(false) };
            var report = RazerProximitySensorProtocol.CreateGetProximitySensorCalibrationDataRequest(TransactionId, Profile.ClassId, Profile.SensorId);
            return new RazerSurfaceCalibrationRawData
            {
                CalibrationData = RazerProximitySensorProtocol.ParseCalibrationData(await _execute(report, token).ConfigureAwait(false), report),
            };
        }, cancellationToken);
    }

    public RazerSurfaceCalibrationConfiguration CreateCandidate(RazerSurfaceCalibrationConfiguration configuration, RazerSurfaceCalibrationRawData raw)
    {
        var candidate = configuration with { RawData = raw with
        {
            Parameters = raw.Parameters.ToArray(), CalibrationData = raw.CalibrationData.ToArray(),
            LiftToTrackData = raw.LiftToTrackData.ToArray(), TrackToLiftData = raw.TrackToLiftData.ToArray(),
            Z = raw.Z.ToArray(), Q = raw.Q.ToArray(),
        } };
        BuildPlan(candidate);
        return candidate;
    }

    public Task RestoreAsync(RazerSurfaceCalibrationState snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SensorState is null || snapshot.SensorState > 1)
            throw new InvalidOperationException("The original sensor state is unknown; automatic restoration is unavailable.");
        if (Profile.Kind == RazerProximityCalibrationKind.SmartPixart && snapshot.LiftMode is 4 or 6 &&
            (snapshot.LiftToTrackData.Length == 0 || snapshot.TrackToLiftData.Length == 0))
            throw new InvalidOperationException("The original asymmetric calibration curves could not be read.");
        return _transaction(async token =>
        {
            await SetStateAsync(snapshot.SensorState == 1, token).ConfigureAwait(false);
            if (snapshot.LiftMode is { } mode) await SetLiftAsync(mode, snapshot.LiftHeight ?? 0, token).ConfigureAwait(false);
            var parameters = Profile.Kind == RazerProximityCalibrationKind.SmartPixart && snapshot.LiftMode is 4 or 6
                ? [] : snapshot.Parameters.Take(8).ToArray();
            await WriteDataAsync(new CalibrationPlan(true, null, 0, parameters,
                snapshot.LiftToTrackData, snapshot.TrackToLiftData, snapshot.CalibrationData, snapshot.Thresholds), token).ConfigureAwait(false);
            await RestoreDeviceModeAsync(snapshot, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    // These engine flows never call their JS setDriverMode helper. Do not invent a mode change.
    public Task RestoreDeviceModeAsync(RazerSurfaceCalibrationState snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public RazerSurfaceCalibrationReadback CompareReadback(RazerSurfaceCalibrationConfiguration configuration, RazerSurfaceCalibrationState state)
    {
        var plan = BuildPlan(configuration);
        var unavailable = state.SensorState is null;
        if (state.SensorState is { } enabled && enabled != (plan.Enabled ? 1 : 0)) return RazerSurfaceCalibrationReadback.Mismatch;
        if (plan.LiftMode is { } mode)
        {
            if (state.LiftMode is null || state.LiftHeight is null) unavailable = true;
            else if (state.LiftMode != mode || state.LiftHeight != plan.LiftHeight) return RazerSurfaceCalibrationReadback.Mismatch;
        }
        foreach (var pair in new[] { (plan.Parameters, state.Parameters), (plan.LiftToTrack, state.LiftToTrackData),
            (plan.TrackToLift, state.TrackToLiftData), (plan.CalibrationData, state.CalibrationData) })
        {
            if (pair.Item1.Length == 0) continue;
            if (pair.Item2.Length < pair.Item1.Length) unavailable = true;
            else if (!pair.Item1.AsSpan().SequenceEqual(pair.Item2.AsSpan(0, pair.Item1.Length))) return RazerSurfaceCalibrationReadback.Mismatch;
        }
        if (plan.Thresholds.Length > 0)
        {
            if (state.Thresholds.Length == 0) unavailable = true;
            else if (!plan.Thresholds.SequenceEqual(state.Thresholds)) return RazerSurfaceCalibrationReadback.Mismatch;
        }
        return unavailable ? RazerSurfaceCalibrationReadback.Unavailable : RazerSurfaceCalibrationReadback.Consistent;
    }

    private async Task<byte[]> ReadParametersAsync(CancellationToken token)
    {
        var report = RazerProximitySensorProtocol.CreateGetProximitySensorConfigurationRequest(TransactionId, Profile.ClassId, Profile.SensorId);
        return RazerProximitySensorProtocol.ParseConfiguration(await _execute(report, token).ConfigureAwait(false), report).Parameters.ToArray();
    }

    private async Task<byte[]?> OptionalReadAsync(byte[] report, CancellationToken token)
    {
        try { return await _execute(report, token).ConfigureAwait(false); }
        catch (NotSupportedException) { return null; }
    }

    private Task SetStateAsync(bool enabled, CancellationToken token) => WriteAsync(
        RazerProximitySensorProtocol.CreateSetProximitySensorStateRequest(TransactionId, Profile.ClassId, Profile.SensorId, enabled ? (byte)1 : (byte)0), token);
    private Task SetLiftAsync(byte mode, byte height, CancellationToken token) => WriteAsync(
        RazerProximitySensorProtocol.CreateSetProximitySensorLiftSettingRequest(TransactionId, Profile.ClassId, Profile.SensorId, mode, height), token);

    private async Task WriteDataAsync(CalibrationPlan plan, CancellationToken token)
    {
        if (plan.Parameters.Length > 0)
            await WriteAsync(RazerProximitySensorProtocol.CreateSetProximitySensorConfigurationRequest(TransactionId, Profile.ClassId, Profile.SensorId, plan.Parameters), token).ConfigureAwait(false);
        if (plan.LiftToTrack.Length > 0)
        {
            await WriteAsync(RazerProximitySensorProtocol.CreateSetProximitySensorLiftToTrackCalibrationDataRequest(TransactionId, Profile.ClassId, Profile.SensorId, plan.LiftToTrack), token).ConfigureAwait(false);
            await WriteAsync(RazerProximitySensorProtocol.CreateSetProximitySensorTrackToLiftCalibrationDataRequest(TransactionId, Profile.ClassId, Profile.SensorId, plan.TrackToLift), token).ConfigureAwait(false);
        }
        if (plan.CalibrationData.Length > 0)
            await WriteAsync(RazerProximitySensorProtocol.CreateSetProximitySensorCalibrationDataRequest(TransactionId, Profile.ClassId, Profile.SensorId, plan.CalibrationData), token).ConfigureAwait(false);
        if (plan.Thresholds.Length > 0)
            await WriteAsync(RazerProximitySensorProtocol.CreateSetProximitySensorThresholdRequest(TransactionId, Profile.ClassId, Profile.SensorId,
                plan.Thresholds[0], plan.Thresholds[1], plan.Thresholds[2]), token).ConfigureAwait(false);
    }

    private async Task WriteAsync(byte[] report, CancellationToken token)
    {
        var response = await _execute(report, token).ConfigureAwait(false);
        if (!RazerFeatureReport.MatchesCommand(report, response) || response[1] != 0x02)
            throw new InvalidDataException("The calibration write was not acknowledged successfully.");
    }

    private async Task<T> TransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        T result = default!;
        await _transaction(async ct => { result = await action(ct).ConfigureAwait(false); }, token).ConfigureAwait(false);
        return result;
    }
}
