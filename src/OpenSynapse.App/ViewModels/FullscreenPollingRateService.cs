using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Overrides the polling rate while an application owns the whole screen, then
/// puts the rate the device had before back. This is a host-side override, the
/// same shape Razer Synapse uses; no firmware profile is involved.
/// </summary>
internal sealed class FullscreenPollingRateService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(2);
    private static readonly IReadOnlyList<int> KeyboardPollingRates =
        RazerKeyboardProtocol.PollingRates.Select(item => item.Hertz).ToArray();

    private readonly Func<ProfileDocument> _getProfile;
    private readonly Func<PowerSourceKind> _currentPowerSource;
    private readonly RazerHidWorkspace _razerHid;
    private readonly UsbAccessoriesWorkspace _accessories;
    private readonly RefreshWorkspace _refresh;
    private readonly LocalDiagnosticLog _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, TargetState> _states = new(StringComparer.Ordinal);
    private readonly HashSet<string> _skippedRates = new(StringComparer.Ordinal);
    private CancellationTokenSource? _run;
    private Task? _loop;
    private bool _rawFullscreen;
    private bool _stableFullscreen;
    private bool _hasStable;
    private int _stableSamples;

    internal FullscreenPollingRateService(
        Func<ProfileDocument> getProfile,
        Func<PowerSourceKind> currentPowerSource,
        RazerHidWorkspace razerHid,
        UsbAccessoriesWorkspace accessories,
        RefreshWorkspace refresh,
        LocalDiagnosticLog log)
    {
        _getProfile = getProfile;
        _currentPowerSource = currentPowerSource;
        _razerHid = razerHid;
        _accessories = accessories;
        _refresh = refresh;
        _log = log;
    }

    public void Start(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

            _run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loop = RunAsync(_run.Token);
        }
    }

    public async Task StopAndRestoreAsync()
    {
        Task? loop;
        CancellationTokenSource? run;
        lock (_gate)
        {
            run = _run;
            loop = _loop;
            _run = null;
            _loop = null;
        }

        run?.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }
        run?.Dispose();
        await RestoreAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SampleInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await TickAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _log.TryWrite("fullscreen-polling", $"tick failed: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var fullscreen = WindowsFullscreenState.IsFullscreen(out var source);
        if (fullscreen == _rawFullscreen)
        {
            _stableSamples++;
        }
        else
        {
            _rawFullscreen = fullscreen;
            _stableSamples = 1;
        }

        // Alt-Tab bounces the foreground window; only a state that survives two
        // samples is allowed to touch a device.
        if (_stableSamples < 2)
        {
            return;
        }

        var stateChanged = !_hasStable || fullscreen != _stableFullscreen;
        _hasStable = true;
        _stableFullscreen = fullscreen;

        // On the desktop with nothing overridden there is nothing to resolve or
        // write; the one-second QUNS/foreground check is the only idle cost.
        if (!fullscreen && !stateChanged && !HasApplied())
        {
            return;
        }

        var targets = BuildTargets();
        var configChanged = UpdateSnapshots(targets);
        if (!stateChanged && !configChanged && !HasPending(targets, fullscreen, configChanged))
        {
            return;
        }

        if (stateChanged)
        {
            _log.TryWrite(
                "fullscreen-polling",
                $"detect={source} state={(fullscreen ? "true" : "false")}");
        }

        foreach (var target in targets)
        {
            LogUnsupportedRate(target, fullscreen);
            var state = StateFor(target.Key);
            if (DateTime.UtcNow < state.NextAttemptAt ||
                !NeedsWork(target, state, fullscreen, configChanged))
            {
                continue;
            }

            try
            {
                await SyncTargetAsync(target, state, fullscreen, configChanged, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                state.NextAttemptAt = DateTime.UtcNow + RetryDelay;
                _log.TryWrite(
                    "fullscreen-polling",
                    $"1532:{target.ProductId:X4}: {exception.Message}");
            }
        }
    }

    private async Task SyncTargetAsync(
        PollingRateTarget target,
        TargetState state,
        bool fullscreen,
        bool configChanged,
        CancellationToken cancellationToken)
    {
        var configured = fullscreen &&
            target.FullscreenRate is int rate &&
            target.SupportedRates.Contains(rate);

        int? current = null;
        if (configured != state.Applied)
        {
            current = await ReadRateAsync(target, cancellationToken).ConfigureAwait(false);
        }

        var decision = FullscreenPollingPolicy.Decide(
            configured,
            state.Applied,
            configChanged,
            target.FullscreenRate,
            state.Captured,
            target.NormalRate,
            current);

        if (decision.WriteRate is int writeRate)
        {
            var executed = await _refresh.RunDeviceOperationAsync(
                ct => target.WriteRateAsync(writeRate, ct),
                cancellationToken).ConfigureAwait(false);
            if (!executed)
            {
                return;
            }

            _log.TryWrite(
                "fullscreen-polling",
                $"{(configured ? "enter" : "exit")} 1532:{target.ProductId:X4} " +
                $"{(current is int observed ? observed.ToString() : "unknown")} -> {writeRate} Hz");
        }

        if (decision.CaptureCurrent)
        {
            state.Captured = current;
        }
        state.Applied = decision.Applied;
        state.NeedsReapply = false;
        if (!decision.Applied)
        {
            state.Captured = null;
        }
        state.NextAttemptAt = DateTime.MinValue;
    }

    private void LogUnsupportedRate(PollingRateTarget target, bool fullscreen)
    {
        if (!fullscreen ||
            target.FullscreenRate is not int rate ||
            target.SupportedRates.Contains(rate))
        {
            return;
        }

        if (_skippedRates.Add($"{target.Key}:{rate}"))
        {
            _log.TryWrite(
                "fullscreen-polling",
                $"skip 1532:{target.ProductId:X4}: {rate} Hz is not supported");
        }
    }

    private static async Task<int?> ReadRateAsync(
        PollingRateTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            return await target.ReadRateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or
                TimeoutException or NotSupportedException or ArgumentOutOfRangeException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private IReadOnlyList<PollingRateTarget> BuildTargets()
    {
        var document = _getProfile();
        var power = _currentPowerSource();
        var targets = new List<PollingRateTarget>();

        if (_razerHid.DeviceService is { } service)
        {
            foreach (var connection in _razerHid.CurrentConnections)
            {
                var category = connection.Definition.Category;
                if (!connection.IsReady ||
                    category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard) ||
                    connection.Definition.PollingRates.Count == 0 ||
                    !connection.Capabilities.Contains(RazerHidBackendCapability.PollingRateWrite))
                {
                    continue;
                }

                var descriptor = connection.ToDescriptor();
                var resolved = ProfileResolver.ResolveWithDeviceKey(
                    document,
                    descriptor,
                    power,
                    ProfileResolver.GetDeviceKey(descriptor));
                var mouse = category == DeviceCategory.Mouse;
                var key = $"hid:{connection.InstanceId}";
                var fullscreen = mouse
                    ? resolved.Viper.FullscreenPollingRateHertz
                    : resolved.Keyboard.FullscreenPollingRateHertz;
                if (fullscreen is null && !IsApplied(key))
                {
                    continue;
                }

                targets.Add(new PollingRateTarget(
                    key,
                    connection.Definition.ProductId,
                    connection.EffectivePollingRates,
                    mouse ? resolved.Viper.PollingRateHertz : resolved.Keyboard.PollingRateHertz,
                    fullscreen,
                    async ct => await service.GetPollingRateAsync(connection, ct)
                        .ConfigureAwait(false),
                    async (rate, ct) =>
                    {
                        var actual = await service.SetPollingRateAsync(connection, rate, ct)
                            .ConfigureAwait(false);
                        if (actual is int value && value != rate)
                        {
                            throw new InvalidOperationException(
                                $"Polling rate read back as {value} Hz, expected {rate} Hz.");
                        }
                    }));
            }
        }

        foreach (var connection in _accessories.KeyboardConnections)
        {
            var productId = RazerProductAliases.Canonical(connection.ProductId);
            var descriptor = new DeviceDescriptor(
                connection.DevicePath,
                connection.Name,
                0x1532,
                productId,
                DeviceAccessState.Available,
                DeviceCapabilityState.PendingValidation,
                0,
                0,
                0,
                "keyboard",
                DeviceCategory.Keyboard);
            var resolved = ProfileResolver.ResolveWithDeviceKey(
                document,
                descriptor,
                power,
                $"{0x1532:X4}:{productId:X4}");
            var key = $"kb:{connection.DevicePath}";
            if (resolved.Keyboard.FullscreenPollingRateHertz is null && !IsApplied(key))
            {
                continue;
            }

            RazerKeyboardService? keyboard = null;
            RazerKeyboardService Service() => keyboard ??= new RazerKeyboardService(
                new RazerFeatureTransport(),
                connection.DevicePath,
                TimeSpan.FromMilliseconds(20),
                message => _log.TryWrite("fullscreen-polling", message),
                connection.ProductId);
            targets.Add(new PollingRateTarget(
                key,
                connection.DisplayProductId,
                KeyboardPollingRates,
                resolved.Keyboard.PollingRateHertz,
                resolved.Keyboard.FullscreenPollingRateHertz,
                ct => Service().GetPollingRateAsync(ct),
                (rate, ct) => Service().SetPollingRateAsync(rate, ct)));
        }

        return targets;
    }

    private bool UpdateSnapshots(IReadOnlyList<PollingRateTarget> targets)
    {
        var changed = false;
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            present.Add(target.Key);
            var state = StateFor(target.Key);
            if (!state.Present)
            {
                state.Present = true;
                changed = true;
            }
            else if (state.NormalRate != target.NormalRate ||
                     state.FullscreenRate != target.FullscreenRate)
            {
                changed = true;
            }

            state.NormalRate = target.NormalRate;
            state.FullscreenRate = target.FullscreenRate;
        }

        lock (_gate)
        {
            foreach (var state in _states.Values)
            {
                if (!present.Contains(state.Key))
                {
                    state.Present = false;
                }
                else if (changed && state.Applied)
                {
                    // A profile switch writes the normal rate over the override;
                    // keep rewriting until the target rate lands again.
                    state.NeedsReapply = true;
                }
            }
        }

        return changed;
    }

    private bool IsApplied(string key)
    {
        lock (_gate)
        {
            return _states.TryGetValue(key, out var state) && state.Applied;
        }
    }

    private bool HasApplied()
    {
        lock (_gate)
        {
            return _states.Values.Any(state => state.Applied);
        }
    }

    private bool HasPending(
        IReadOnlyList<PollingRateTarget> targets,
        bool fullscreen,
        bool configChanged)
    {
        foreach (var target in targets)
        {
            var state = StateFor(target.Key);
            if (DateTime.UtcNow >= state.NextAttemptAt &&
                NeedsWork(target, state, fullscreen, configChanged))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NeedsWork(
        PollingRateTarget target,
        TargetState state,
        bool fullscreen,
        bool configChanged)
    {
        var configured = fullscreen &&
            target.FullscreenRate is int rate &&
            target.SupportedRates.Contains(rate);
        return configured
            ? !state.Applied || state.NeedsReapply || configChanged
            : state.Applied;
    }

    private async Task RestoreAsync()
    {
        TargetState[] applied;
        lock (_gate)
        {
            applied = _states.Values.Where(state => state.Applied).ToArray();
        }
        if (applied.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, PollingRateTarget> targets;
        try
        {
            targets = BuildTargets().ToDictionary(target => target.Key, StringComparer.Ordinal);
        }
        catch (Exception exception)
        {
            _log.TryWrite("fullscreen-polling", $"restore targets unavailable: {exception.Message}");
            return;
        }
        using var timeout = new CancellationTokenSource(RestoreTimeout);
        foreach (var state in applied)
        {
            if (!targets.TryGetValue(state.Key, out var target) ||
                (state.Captured ?? target.NormalRate) is not int restore)
            {
                continue;
            }

            try
            {
                if (await _refresh.RunDeviceOperationAsync(
                        ct => target.WriteRateAsync(restore, ct),
                        timeout.Token).ConfigureAwait(false))
                {
                    state.Applied = false;
                    state.Captured = null;
                    _log.TryWrite(
                        "fullscreen-polling",
                        $"exit 1532:{target.ProductId:X4} unknown -> {restore} Hz");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _log.TryWrite(
                    "fullscreen-polling",
                    $"restore failed 1532:{target.ProductId:X4}: {exception.Message}");
            }
        }
    }

    private TargetState StateFor(string key)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(key, out var state))
            {
                state = new TargetState(key);
                _states[key] = state;
            }

            return state;
        }
    }

    private sealed class TargetState(string key)
    {
        internal string Key { get; } = key;
        internal bool Present { get; set; }
        internal bool Applied { get; set; }
        internal bool NeedsReapply { get; set; }
        internal int? Captured { get; set; }
        internal int? NormalRate { get; set; }
        internal int? FullscreenRate { get; set; }
        internal DateTime NextAttemptAt { get; set; }
    }
}

internal sealed record PollingRateTarget(
    string Key,
    ushort ProductId,
    IReadOnlyList<int> SupportedRates,
    int? NormalRate,
    int? FullscreenRate,
    Func<CancellationToken, Task<int?>> ReadRateAsync,
    Func<int, CancellationToken, Task> WriteRateAsync);
