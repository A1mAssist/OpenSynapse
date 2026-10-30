using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.UI;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Owns the Blade page: the profile-backed performance, fan, lighting, charge and MUX state
/// plus every write that follows it. The shell only binds <c>Blade.*</c> and routes shortcuts.
/// </summary>
public sealed partial class BladeWorkspace : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan BrightnessVerificationDelay = TimeSpan.FromMilliseconds(150);

    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly IBladeLightingController? _bladeLightingController;
    private readonly WindowsTouchpadController? _touchpadController;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Action<ProfileDocument> _setProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly Func<string, Func<Task>, CancellationToken, Action?, string?, string?, Task> _runDeviceOperation;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _deviceDescriptors;
    private readonly Func<PowerSourceKind> _currentPowerSource;
    private readonly Func<RazerDeviceTelemetry?> _lastTelemetry;
    private readonly Func<IReadOnlySet<int>?> _refreshRateCycle;
    private readonly Func<bool> _isDisplayAvailable;
    private readonly Func<bool> _isBrightnessRestorePending;
    private readonly Func<bool> _isDisposed;
    private readonly Action<string> _setDeviceOperationError;
    private readonly Action _requestDeviceRefresh;
    private readonly Action<int> _internalDisplayRefreshRateChangedByUser;
    private readonly Action<string?> _applyFirmwareMcuVersion;
    private readonly Func<DisplayWorkspace> _display;
    private readonly Func<FanWorkspace> _fan;
    private readonly Func<LightingWorkspace> _lighting;
    private readonly Func<DeviceRowViewModel?> _bladeRow;
    private readonly Func<DeviceDescriptor?, CancellationToken, Task<string?>> _readSerialAsync;
    private readonly BladeViewModel _blade = new();

    private HashSet<BladePerformanceMode> _bladePerformanceCycleModes = [.. BladePerformanceModes];
    private string _activeBladeMappingPreset = BladeProfileSettings.Product710DefaultMappingPreset;
    private bool _activeSnapTapEnabled;
    private bool _suppressBladePerformanceApply;
    private bool _bladeCoolingPadConnected;
    private bool _padlessHyperBoostAccepted;
    private Func<Task<bool>>? _confirmPadlessHyperBoost;
    private string? _bladeSerial;
    private string? _bladeControlDevicePath;
    private ushort _bladeControlProductId;
    private int _bladeLightingPowerProfileIndex;
    private int _bladePerformancePowerProfileIndex;
    private int _bladeRefreshRatePowerProfileIndex;
    private int _bladeLogoPowerProfileIndex;
    private readonly object _bladeBrightnessGate = new();
    private Task _bladeBrightnessWriter = Task.CompletedTask;
    private byte? _desiredBladeBrightness;
    private bool _bladeBrightnessWriterActive;
    private Task _bladeBrightnessVerification = Task.CompletedTask;
    private long _bladeBrightnessVerificationGeneration;
    private bool _gpuClockOffsetProbeStarted;
    private DateTimeOffset _gpuClockOffsetNextProbe;
    private bool _bladeGpuClockLimitAvailable;
    private double _bladeGpuClockLimitMhz = NvApiGpuClockLimit.MaximumMhz;

    internal BladeWorkspace(
        IRazerDeviceTelemetryReader deviceTelemetryReader,
        IBladeLightingController? bladeLightingController,
        WindowsTouchpadController? touchpadController,
        LocalDiagnosticLog diagnosticLog,
        Func<ProfileDocument> getProfile,
        Action<ProfileDocument> setProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        Func<string, Func<Task>, CancellationToken, Action?, string?, string?, Task> runDeviceOperation,
        Func<IReadOnlyList<DeviceDescriptor>> deviceDescriptors,
        Func<PowerSourceKind> currentPowerSource,
        Func<RazerDeviceTelemetry?> lastTelemetry,
        Func<IReadOnlySet<int>?> refreshRateCycle,
        Func<bool> isDisplayAvailable,
        Func<bool> isBrightnessRestorePending,
        Func<bool> isDisposed,
        Action<string> setDeviceOperationError,
        Action requestDeviceRefresh,
        Action<int> internalDisplayRefreshRateChangedByUser,
        Action<string?> applyFirmwareMcuVersion,
        Func<DisplayWorkspace> display,
        Func<FanWorkspace> fan,
        Func<LightingWorkspace> lighting,
        Func<DeviceRowViewModel?> bladeRow,
        Func<DeviceDescriptor?, CancellationToken, Task<string?>> readSerialAsync)
    {
        _deviceTelemetryReader = deviceTelemetryReader;
        _bladeLightingController = bladeLightingController;
        _touchpadController = touchpadController;
        _diagnosticLog = diagnosticLog;
        _getProfile = getProfile;
        _setProfile = setProfile;
        _saveProfileAsync = saveProfileAsync;
        _runDeviceOperation = runDeviceOperation;
        _deviceDescriptors = deviceDescriptors;
        _currentPowerSource = currentPowerSource;
        _lastTelemetry = lastTelemetry;
        _refreshRateCycle = refreshRateCycle;
        _isDisplayAvailable = isDisplayAvailable;
        _isBrightnessRestorePending = isBrightnessRestorePending;
        _isDisposed = isDisposed;
        _setDeviceOperationError = setDeviceOperationError;
        _requestDeviceRefresh = requestDeviceRefresh;
        _internalDisplayRefreshRateChangedByUser = internalDisplayRefreshRateChangedByUser;
        _applyFirmwareMcuVersion = applyFirmwareMcuVersion;
        _display = display;
        _fan = fan;
        _lighting = lighting;
        _bladeRow = bladeRow;
        _readSerialAsync = readSerialAsync;
    }

    private BladeBatteryOptimizerWorkspace? _batteryOptimizer;

    public BladeBatteryOptimizerWorkspace BatteryOptimizer =>
        _batteryOptimizer ??= new BladeBatteryOptimizerWorkspace(
            new BladeBatteryOptimizerService(_deviceTelemetryReader),
            () => ActiveDescriptors,
            RunDeviceOperationAsync);

    internal void UpdateBatteryOptimizer(RazerDeviceTelemetry? telemetry) =>
        BatteryOptimizer.Update(telemetry);

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<string?, ushort>? ControlDeviceChanged;
    internal event Action<BladePerformanceMode>? PerformanceModeChangedByUser;
    internal event Action<bool>? GamingModeChangedByUser;
    internal event Action<bool>? GamingModeStateChanged;
    internal event Action<bool>? TouchpadChangedByUser;
    internal event Action<bool>? OneTimeFullChargeChangedByUser;
    internal event Action? InputProfileChanged;

    private ProfileDocument Profile { get => _getProfile(); set => _setProfile(value); }
    private IReadOnlyList<DeviceDescriptor> ActiveDescriptors => _deviceDescriptors();
    private RazerDeviceTelemetry? LastTelemetry => _lastTelemetry();
    private bool IsDisplayAvailable => _isDisplayAvailable();
    private bool IsDisposed => _isDisposed();
    private PowerSourceKind CurrentPowerSource => _currentPowerSource();

    private Task<bool> SaveProfileAsync(CancellationToken cancellationToken) =>
        _saveProfileAsync(cancellationToken);

    private Task RunDeviceOperationAsync(
        string label,
        Func<Task> operation,
        CancellationToken cancellationToken,
        Action? restoreSelection = null,
        string? successVerb = null,
        string? failureVerb = null) =>
        _runDeviceOperation(label, operation, cancellationToken, restoreSelection, successVerb, failureVerb);

    private void RequestDeviceRefresh() => _requestDeviceRefresh();

    private void SetDeviceOperationError(string error) => _setDeviceOperationError(error);

    private ProfileDefinition GetActiveProfile() =>
        Profile.Profiles.TryGetValue(Profile.ActiveProfileName, out var profile)
            ? profile
            : throw new InvalidOperationException("The active profile is missing from the profile catalog.");
    public ValueTask<BladeDisplayMuxStatus> ReadDisplayMuxStatusAsync(
        CancellationToken cancellationToken = default) =>
        _deviceTelemetryReader.ReadBladeDisplayMuxStatusAsync(ActiveDescriptors, cancellationToken);

    public ValueTask<BladeDisplayMuxSwitchResult> SetDisplayMuxModeAsync(
        BladeDisplayMuxMode mode,
        CancellationToken cancellationToken = default) =>
        _deviceTelemetryReader.SetBladeDisplayMuxModeAsync(ActiveDescriptors, mode, cancellationToken);

    public async Task ApplyBrightnessAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade._canSetBladeBrightness)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("Text_5F0C27DB"),
            () => ApplyBrightnessCoreAsync(cancellationToken),
            cancellationToken,
            () => BrightnessPercent = _blade._confirmedBladeBrightnessPercent);
    }

    private async Task ApplyBrightnessCoreAsync(CancellationToken cancellationToken)
    {
        var requested = ToBladeBrightness(BrightnessPercent);
        if (!IsSelectedLightingPowerActive || !IsDisplayAvailable)
        {
            EditableLightingBladeProfile.KeyboardBrightness = requested;
            SetBrightness(requested, confirm: false);
            RefreshLightingEditor();
            await SaveProfileAsync(cancellationToken);
            return;
        }

        var actual = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
            ActiveDescriptors,
            requested,
            cancellationToken);
        SetBrightness(actual);
        EditableLightingBladeProfile.KeyboardBrightness = actual;
        await SaveProfileAsync(cancellationToken);
    }

    internal Task QueueBrightnessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_blade._canSetBladeBrightness)
        {
            return Task.CompletedTask;
        }

        var requested = ToBladeBrightness(BrightnessPercent);
        if (!IsSelectedLightingPowerActive)
        {
            EditableLightingBladeProfile.KeyboardBrightness = requested;
            SetBrightness(requested, confirm: false);
            RefreshLightingEditor();
            return SaveProfileAsync(cancellationToken);
        }

        lock (_bladeBrightnessGate)
        {
            _desiredBladeBrightness = requested;
            Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
            if (!_bladeBrightnessWriterActive)
            {
                _bladeBrightnessWriterActive = true;
                _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
            }
        }
        return Task.CompletedTask;
    }

    public async Task ApplyLightingEffectAsync(
        BladeLightingEffect effect,
        CancellationToken cancellationToken = default)
    {
        if (!CanSetLighting ||
            _bladeLightingController is null)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("Text_09707A2C"),
            () => ApplyLightingEffectCoreAsync(effect, cancellationToken),
            cancellationToken,
            successVerb: AppStrings.Text("Text_945C2E42"));
    }

    private async Task ApplyLightingEffectCoreAsync(
        BladeLightingEffect effect,
        CancellationToken cancellationToken)
    {
        var previousProfile = Profile.Clone();
        var encoded = BladeLightingProfileCodec.Create(effect);
        var targetLighting = EditableLightingProfile;
        if (!IsSelectedLightingPowerActive || !IsDisplayAvailable)
        {
            targetLighting.Effect = encoded.Effect;
            targetLighting.Parameters = encoded.Parameters;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_9A87CD0C"));
            }

            RefreshLightingEditor();
            return;
        }

        if (!LightingEnabled)
        {
            targetLighting.Effect = encoded.Effect;
            targetLighting.Parameters = encoded.Parameters;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_9A87CD0C"));
            }

            RefreshLightingEditor();
            return;
        }

        try
        {
            await _bladeLightingController!.ApplyAsync(
                ActiveDescriptors, effect, cancellationToken);
        }
        catch
        {
            NotifyLightingDialectChangedIfDowngraded();
            throw;
        }
        targetLighting.Effect = encoded.Effect;
        targetLighting.Parameters = encoded.Parameters;
        if (!await SaveProfileAsync(cancellationToken))
        {
            Profile = previousProfile;
            _lighting().ShadowFingerprint = string.Empty;
            throw new InvalidOperationException(AppStrings.Text("Text_C7D0CD75"));
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
            device.Access == DeviceAccessState.Available);
        if (blade is not null)
        {
            var resolved = ProfileResolver.Resolve(
                Profile,
                blade,
                CurrentPowerSource);
            _lighting().LightingDevicePath = blade.Id;
            _lighting().ShadowFingerprint = _lighting().CreateFingerprint(
                resolved.Lighting,
                blade.Id,
                CurrentPowerSource,
                resolved.Blade.LightingEnabled ?? true,
                resolved.Blade.ChromaOverrideEnabled ?? true);
        }
        _ = _lighting().ObserveRuntime(
            _bladeLightingController!.RuntimeCompletion);
    }

    public Task ApplySelectedLightingEffectAsync(CancellationToken cancellationToken = default)
    {
        var modes = ActiveBladeLightingModes;
        if (LightingModeIndex < 0 || LightingModeIndex >= modes.Count ||
            WaveDirectionIndex < 0 || WaveDirectionIndex >= BladeWaveDirections.Length)
        {
            return Task.CompletedTask;
        }

        if (UsesFirmwareKeyboardLighting)
        {
            return ApplyFirmwareKeyboardLightingEffectAsync(modes[LightingModeIndex], cancellationToken);
        }

        var color = new RazerRgb(LightingColor.R, LightingColor.G, LightingColor.B);
        var secondColor = new RazerRgb(
            LightingSecondColor.R,
            LightingSecondColor.G,
            LightingSecondColor.B);
        var tertiaryColor = new RazerRgb(
            LightingTertiaryColor.R,
            LightingTertiaryColor.G,
            LightingTertiaryColor.B);
        var mode = modes[LightingModeIndex];
        if (mode == BladeLightingMode.ChromaFile && _blade._bladeChromaAnimation is null)
        {
            SetChromaStatus(AppStrings.Text("BladeChromaImportRequired"));
            return Task.CompletedTask;
        }

        var palette = mode is BladeLightingMode.Wave or BladeLightingMode.Spectrum
            ? LightingRenderModeIndex == 1
                ? LightingPalette.Create(color, secondColor, tertiaryColor)
                : null
            : BladeLightingPaletteResolver.Resolve(
                mode, color, secondColor, tertiaryColor, EditableLightingProfile);
        var effect = new BladeLightingEffect(
            mode,
            color,
            BladeWaveDirections[WaveDirectionIndex],
            secondColor,
            BladeReactiveSpeeds[Math.Clamp(ReactiveSpeedIndex, 0, BladeReactiveSpeeds.Length - 1)],
            BladeStarlightSpeeds[Math.Clamp(StarlightSpeedIndex, 0, BladeStarlightSpeeds.Length - 1)],
            BladeStarlightColorModes[Math.Clamp(StarlightColorModeIndex, 0, BladeStarlightColorModes.Length - 1)],
            palette,
            BladeAnimationSpeeds[Math.Clamp(AnimationSpeedIndex, 0, BladeAnimationSpeeds.Length - 1)],
            mode == BladeLightingMode.ChromaFile ? _blade._bladeChromaAnimation : null);
        return ApplyLightingEffectAsync(effect, cancellationToken);
    }

    public async Task ImportChromaFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!CanSetLighting || _bladeLightingController is null)
        {
            return;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var previousMode = LightingModeIndex;
        var previousAnimation = _blade._bladeChromaAnimation;
        await RunDeviceOperationAsync(
            AppStrings.Text("BladeImportChromaButton/Content"),
            async () =>
            {
                ChromaFileAnimation animation;
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists ||
                        info.Length > ChromaFileAnimationParser.MaximumFileSizeBytes)
                    {
                        throw new InvalidDataException("The .chroma file is unavailable or too large.");
                    }
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                        .ConfigureAwait(false);
                    animation = ChromaFileAnimationStore.Import(bytes);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("BladeChromaImportInvalid"), exception);
                }

                if (animation.IsUniform)
                {
                    SetChromaAnimation(null);
                    await ApplyLightingEffectCoreAsync(
                        new BladeLightingEffect(BladeLightingMode.Static, animation.UniformColor),
                        cancellationToken).ConfigureAwait(false);
                    LightingModeIndex = Array.IndexOf(
                        ActiveBladeLightingModes.ToArray(),
                        BladeLightingMode.Static);
                    LightingColor = Color.FromArgb(
                        0xFF,
                        animation.UniformColor.Red,
                        animation.UniformColor.Green,
                        animation.UniformColor.Blue);
                    SetChromaStatus(AppStrings.Text("BladeChromaImportUniform"));
                    return;
                }

                SetChromaAnimation(animation);
                await ApplyLightingEffectCoreAsync(
                    new BladeLightingEffect(
                        BladeLightingMode.ChromaFile,
                        ChromaAnimation: animation),
                    cancellationToken).ConfigureAwait(false);
                LightingModeIndex = Array.IndexOf(
                    ActiveBladeLightingModes.ToArray(),
                    BladeLightingMode.ChromaFile);
                SetChromaStatus(AppStrings.FormatText(
                    "BladeChromaImportApplied",
                    Path.GetFileName(path),
                    animation.Frames.Count));
            },
            cancellationToken,
            restoreSelection: () =>
            {
                LightingModeIndex = previousMode;
                SetChromaAnimation(previousAnimation);
            },
            successVerb: AppStrings.Text("Text_945C2E42"));
    }

    /// <summary>
    /// Asks the user to accept the cooling risk before HyperBoost is written without a
    /// Razer Cooling Pad. The UI owns the dialog, so the ViewModel stays headless.
    /// </summary>
    internal void SetPadlessHyperBoostConfirmation(Func<Task<bool>> confirmation) =>
        _confirmPadlessHyperBoost = confirmation;

    internal void SetPadlessHyperBoostAccepted(bool accepted) =>
        _padlessHyperBoostAccepted = accepted;

    private async Task<bool> ConfirmPadlessHyperBoostAsync()
    {
        if (_confirmPadlessHyperBoost is not { } confirmation)
        {
            return true;
        }

        return await confirmation();
    }

    /// <summary>
    /// Renders the adapter wattage report for the diagnostic log, so a refused
    /// HyperBoost shows which reading the decision was made on.
    /// </summary>
    private static string DescribeAdapterPower(BladeAdapterPowerSnapshot? adapterPower) =>
        adapterPower is null
            ? "not reported"
            : $"level {adapterPower.ActualLevel}/{adapterPower.RecommendedLevel}, " +
              $"watts {adapterPower.ActualWatts?.ToString() ?? "?"}/" +
              $"{adapterPower.RecommendedWatts?.ToString() ?? "?"}";

    public Visibility HyperboostOptionVisibility =>
        _blade.PerformanceModes.Contains(BladePerformanceMode.Hyperboost)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>
    /// True when HyperBoost is offered without a Razer Cooling Pad. The user accepts
    /// the cooling risk explicitly before the first pad-less write.
    /// </summary>
    public bool HyperboostNeedsCoolingPadWarning =>
        !_bladeCoolingPadConnected &&
        BladeModelCapabilities.SupportsHyperBoost(_blade._bladeProductId);

    public async Task ApplyPerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        var modes = _blade.PerformanceModes;
        if (_suppressBladePerformanceApply ||
            !CanWritePerformanceMode ||
            PerformanceModeIndex < 0 || PerformanceModeIndex >= modes.Count)
        {
            _diagnosticLog.TryWrite(
                "blade-performance",
                $"Performance apply skipped: suppressed={_suppressBladePerformanceApply}, " +
                $"canWrite={CanWritePerformanceMode}, " +
                $"canSet={CanSetPerformanceMode}, " +
                $"index={PerformanceModeIndex}, count={modes.Count}, " +
                $"productId=0x{_blade._bladeProductId:X4}.");
            return;
        }

        var requested = modes[PerformanceModeIndex];
        if (requested == BladePerformanceMode.Hyperboost &&
            !BladeModelCapabilities.IsHyperBoostAvailable(_blade._bladeProductId))
        {
            _diagnosticLog.TryWrite(
                "blade-performance",
                $"HyperBoost refused: productId=0x{_blade._bladeProductId:X4}, " +
                $"adapter={DescribeAdapterPower(LastTelemetry?.BladeAdapterPower)}.");
            _suppressBladePerformanceApply = true;
            try
            {
                PerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex;
            }
            finally
            {
                _suppressBladePerformanceApply = false;
            }
            SetDeviceOperationError(AppStrings.Text("BladeHyperboostUnsupportedModel"));
            return;
        }
        if (requested == BladePerformanceMode.Hyperboost &&
            HyperboostNeedsCoolingPadWarning &&
            !_padlessHyperBoostAccepted &&
            !await ConfirmPadlessHyperBoostAsync())
        {
            _diagnosticLog.TryWrite(
                "blade-performance",
                "HyperBoost declined: the cooling-pad disclaimer was dismissed.");
            _suppressBladePerformanceApply = true;
            try
            {
                PerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex;
            }
            finally
            {
                _suppressBladePerformanceApply = false;
            }
            return;
        }
        // The mode still applies when the adapter cannot hold the raised limit. The
        // middleware reports that shortage separately instead of hiding the option,
        // so the user is told after the write rather than blocked before it.
        var limitedAdapter = requested == BladePerformanceMode.Hyperboost &&
            LastTelemetry?.BladeAdapterPower is { } adapter &&
            adapter.IsLimitedAdapter;
        await RunDeviceOperationAsync(AppStrings.Text("Text_98C30F5D"), async () =>
        {
            if (!IsSelectedPerformancePowerActive)
            {
                EditablePerformanceProfile.PerformanceMode = (byte)requested;
                SetPerformanceMode(requested, confirm: false);
                if (!await SaveProfileAsync(cancellationToken))
                {
                    throw new InvalidOperationException(AppStrings.Text("Text_CC12A6F5"));
                }

                RefreshPerformanceEditor();
                return;
            }

            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                ActiveDescriptors, requested, cancellationToken, _bladeCoolingPadConnected);
            SetPerformanceMode(actual);
            EditablePerformanceProfile.PerformanceMode = (byte)actual;
            ClearFixedFanWhenLeavingCustom(actual);
            await SaveProfileAsync(cancellationToken);
            RequestDeviceRefresh();
            PerformanceModeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            PerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex);
        if (limitedAdapter)
        {
            SetDeviceOperationError(AppStrings.Text("BladeHyperboostLimitedAdapter"));
        }
    }

    /// <summary>
    /// Drops a stored fixed fan target when the model leaves Custom mode. The 028C EC
    /// stores a target in Balanced/Silent but keeps following its own curve, so the
    /// profile would otherwise carry a value the firmware ignores.
    /// </summary>
    private void ClearFixedFanWhenLeavingCustom(BladePerformanceMode mode)
    {
        if (mode == BladePerformanceMode.Custom ||
            !BladeModelCapabilities.FixedFanRequiresCustomMode(_blade._bladeProductId) ||
            EditablePerformanceProfile.FanMode is null)
        {
            return;
        }

        var target = EditablePerformanceProfile;
        target.FanMode = null;
        target.FanTargetRpm = null;
        target.FanCurve = null;
        _fan().ClearPendingMode();
    }

    public async Task ApplyChargeLimitAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetChargeLimit ||
            ChargeLimitIndex < 0 || ChargeLimitIndex >= BladeChargeLimits.Length)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_DEE979FD"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeChargeLimitAsync(
                ActiveDescriptors, BladeChargeLimits[ChargeLimitIndex], cancellationToken);
            SetChargeLimit(actual);
            Profile.Global.Blade.ChargeLimitPercent = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
            ChargeLimitIndex = _blade._confirmedBladeChargeLimitIndex);
    }

    public async Task ApplyCpuBoostAsync(CancellationToken cancellationToken = default)
    {
        var cpuBoostModes = BladeModelCapabilities.CpuBoostModesFor(_blade._bladeProductId);
        if (!CanSetCpuBoost || CpuBoostIndex < 0 ||
            CpuBoostIndex >= cpuBoostModes.Count)
        {
            return;
        }

        await RunDeviceOperationAsync("CPU Boost", async () =>
        {
            var previousProfile = Profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeCpuBoostModeAsync(
                ActiveDescriptors, cpuBoostModes[CpuBoostIndex], cancellationToken);
            SetCpuBoost(actual);
            Profile.Global.Blade.CpuBoostMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_A257CB56"));
            }
        }, cancellationToken, () => CpuBoostIndex = _blade._confirmedBladeCpuBoostIndex);
    }

    public async Task ApplyGpuBoostAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetGpuBoost || GpuBoostIndex < 0 || GpuBoostIndex >= BladeGpuBoostModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("GPU Boost", async () =>
        {
            var previousProfile = Profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeGpuBoostModeAsync(
                ActiveDescriptors, BladeGpuBoostModes[GpuBoostIndex], cancellationToken);
            SetGpuBoost(actual);
            Profile.Global.Blade.GpuBoostMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_AB07F412"));
            }
        }, cancellationToken, () => GpuBoostIndex = _blade._confirmedBladeGpuBoostIndex);
    }

    public async Task ApplyMaxFanAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetMaxFan)
        {
            return;
        }

        await RunDeviceOperationAsync("Max Fan", async () =>
        {
            var requested = MaxFanEnabled ? BladeMaxFanMode.Enabled : BladeMaxFanMode.Disabled;

            // Max Fan lives in the Custom performance card, so it follows the performance
            // power profile the same way the performance mode itself does. Writing the
            // global slot would silently drop a per-power-source setting.
            if (!IsSelectedPerformancePowerActive)
            {
                EditablePerformanceProfile.MaxFanMode = (byte)requested;
                SetMaxFan(requested);
                if (!await SaveProfileAsync(cancellationToken))
                {
                    throw new InvalidOperationException(AppStrings.Text("Text_CC12A6F5"));
                }

                return;
            }

            var previousProfile = Profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeMaxFanModeAsync(
                ActiveDescriptors, requested, cancellationToken);
            SetMaxFan(actual);
            EditablePerformanceProfile.MaxFanMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_AB07F412"));
            }

            RequestDeviceRefresh();
        }, cancellationToken, () => MaxFanEnabled = _blade._confirmedBladeMaxFanEnabled);
    }

    public async Task ApplyLogoAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetLogo || LogoIndex < 0 || LogoIndex >= BladeLogoModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_7D07A709"), async () =>
        {
            var previousProfile = Profile.Clone();
            var requested = BladeLogoModes[LogoIndex];
            var target = EditableLogoProfile;
            if (!IsSelectedLogoPowerActive)
            {
                target.LogoMode = (byte)requested;
                SetLogo(requested, confirm: false);
                if (!await SaveProfileAsync(cancellationToken))
                {
                    Profile = previousProfile;
                    throw new InvalidOperationException(AppStrings.Text("Text_359F8C1A"));
                }

                RefreshLogoEditor();
                return;
            }

            var actual = await _deviceTelemetryReader.SetBladeLogoModeAsync(
                ActiveDescriptors, requested, cancellationToken);
            SetLogo(actual);
            target.LogoMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_359F8C1A"));
            }
        }, cancellationToken, () => LogoIndex = _blade._confirmedBladeLogoIndex);
    }

    public async Task ToggleTouchpadAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(
            AppStrings.Text("Text_19D052FF"),
            async () =>
            {
                if (!_blade._canSetBladeTouchpad || _touchpadController is null)
                {
                    throw new InvalidOperationException(AppStrings.Text("Text_EA682784"));
                }

                var actual = await Task.Run(
                    _touchpadController.ToggleVerified,
                    cancellationToken);
                TouchpadEnabled = actual;
                _blade._confirmedBladeTouchpadEnabled = actual;
                TouchpadText = actual ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
                TouchpadChangedByUser?.Invoke(actual);
            },
            cancellationToken,
            () =>
            {
                TouchpadEnabled = _blade._confirmedBladeTouchpadEnabled;
                TouchpadText = _blade._confirmedBladeTouchpadEnabled ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
            },
            successVerb: AppStrings.Text("Text_5CF73A23"),
            failureVerb: AppStrings.Text("Text_B9EC9D4C"));
    }

    internal async Task CyclePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_98C30F5D"), async () =>
        {
            if (!CanWritePerformanceMode || _blade._confirmedBladePerformanceModeIndex < 0)
            {
                throw new InvalidOperationException(AppStrings.Text("Text_2263C356"));
            }

            var cycleModes = _bladePerformanceCycleModes;
            // The shortcut cycle has no place to ask for the cooling-risk
            // confirmation, so it skips HyperBoost unless the pad is connected or
            // the user already accepted the pad-less warning.
            if (!BladeModelCapabilities.IsHyperBoostAvailable(
                    _blade._bladeProductId) ||
                (HyperboostNeedsCoolingPadWarning && !_padlessHyperBoostAccepted))
            {
                cycleModes = [.. cycleModes.Where(mode => mode != BladePerformanceMode.Hyperboost)];
                if (cycleModes.Count == 0)
                {
                    cycleModes = [BladePerformanceMode.Performance];
                }
            }

            var modes = _blade.PerformanceModes;
            cycleModes = [.. cycleModes.Where(modes.Contains)];
            if (cycleModes.Count == 0)
            {
                cycleModes = [modes[0]];
            }

            var nextMode = BladePerformanceModeCycle.GetNext(
                modes[_blade._confirmedBladePerformanceModeIndex],
                modes,
                cycleModes);
            PerformanceModeIndex = modes.ToList().IndexOf(nextMode);
            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                ActiveDescriptors,
                modes[PerformanceModeIndex],
                cancellationToken,
                _bladeCoolingPadConnected);
            SetPerformanceMode(actual);
            (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade).PerformanceMode = (byte)actual;
            ClearFixedFanWhenLeavingCustom(actual);
            await SaveProfileAsync(cancellationToken);
            RequestDeviceRefresh();
            PerformanceModeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            PerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex);
    }

    internal async Task ToggleGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_EED40F9C"), async () =>
        {
            var current = _blade._bladeGameModeState is byte state && state != 2
                ? state
                : throw new InvalidOperationException(AppStrings.Text("Text_06C879B7"));
            var actual = await SetGameModeCoreAsync(
                current == 0,
                cancellationToken);
            SetGameMode(actual);
            RequestDeviceRefresh();
            GamingModeChangedByUser?.Invoke(actual.GameMode != 0);
        }, cancellationToken);
    }

    public async Task ApplyGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_EED40F9C"), async () =>
        {
            var actual = await SetGameModeCoreAsync(
                GameModeEnabled,
                cancellationToken);
            SetGameMode(actual);
            RequestDeviceRefresh();
            GamingModeChangedByUser?.Invoke(actual.GameMode != 0);
        }, cancellationToken, () =>
            GameModeEnabled = _blade._bladeGameModeState != 0);
    }

    public async Task ApplyStartupAnimationAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyStartupAnimation)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_AA91A1FF"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeStartupAnimationAsync(
                ActiveDescriptors, StartupAnimationEnabled, cancellationToken);
            _blade._bladeStartupAnimationEnabled = actual;
            StartupAnimationText = FormatOptionalState(actual);
            StartupAnimationEnabled = actual;
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            StartupAnimationEnabled = _blade._bladeStartupAnimationEnabled ?? false);
    }

    public async Task ApplyNativeDisplayModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyNativeDisplayMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeNativeDisplayModeTitle/Text"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeNativeDisplayModeAsync(
                ActiveDescriptors,
                (BladeNativeDisplayMode)NativeDisplayModeIndex,
                cancellationToken);
            _blade._bladeNativeDisplayMode = actual;
            NativeDisplayModeIndex = (int)actual;
            OnPropertyChanged(nameof(CanSetNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
            OnPropertyChanged(nameof(NativeDisplayModeVisibility));
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            NativeDisplayModeIndex = (int)(_blade._bladeNativeDisplayMode ?? BladeNativeDisplayMode.Uhd));
    }

    public async Task ApplyLocalDimmingAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyLocalDimming)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeLocalDimmingTitle/Text"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeLocalDimmingAsync(
                ActiveDescriptors, LocalDimmingEnabled, cancellationToken);
            _blade._bladeLocalDimmingEnabled = actual;
            LocalDimmingEnabled = actual;
            OnPropertyChanged(nameof(CanSetLocalDimming));
            OnPropertyChanged(nameof(CanApplyLocalDimming));
            OnPropertyChanged(nameof(LocalDimmingVisibility));
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            LocalDimmingEnabled = _blade._bladeLocalDimmingEnabled ?? false);
    }

    public async Task ApplyCpuPowerBoostAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyCpuPowerBoost)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeCpuPowerBoostTitle/Text"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeCpuPowerBoostAsync(
                ActiveDescriptors, CpuPowerBoostEnabled, cancellationToken);
            _blade._bladeCpuPowerBoostEnabled = actual;
            CpuPowerBoostEnabled = actual;
            OnPropertyChanged(nameof(CanSetCpuPowerBoost));
            OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
            OnPropertyChanged(nameof(CpuPowerBoostVisibility));
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            CpuPowerBoostEnabled = _blade._bladeCpuPowerBoostEnabled ?? false);
    }

    public async Task ApplyFnKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyFnKey)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeFnKeyLabel"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeFnKeyStateAsync(
                ActiveDescriptors, FnKeyMultimediaPrimary, cancellationToken);
            SetFnKey(actual);
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            FnKeyMultimediaPrimary = _blade._bladeFnKeyMultimediaPrimary ?? false);
    }

    internal async Task CycleInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_8423206D"), async () =>
        {
            var display = _display();
            if (!display.CanSetInternalDisplayRefreshRate ||
                display.Controller is null ||
                display.InternalDisplayRefreshRates.Count == 0)
            {
                throw new InvalidOperationException(AppStrings.Text("Text_36D4C146"));
            }

            var configuredCycle = _refreshRateCycle();
            var includedRates = configuredCycle is { Count: > 0 }
                ? configuredCycle
                : display.InternalDisplayRefreshRates.ToHashSet();
            display.InternalDisplayRefreshRateHertz = BladePerformanceModeCycle.GetNext(
                display.ConfirmedRefreshRateHertz,
                display.InternalDisplayRefreshRates,
                includedRates);
            var snapshot = display.Controller.SetRefreshRate(
                display.InternalDisplayRefreshRateHertz);
            display.ApplyInternalDisplaySnapshot(snapshot);
            (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade).RefreshRateHertz =
                snapshot.RefreshRateHertz;
            await SaveProfileAsync(cancellationToken);
            _internalDisplayRefreshRateChangedByUser(snapshot.RefreshRateHertz);
        }, cancellationToken, () =>
            _display().InternalDisplayRefreshRateHertz = _display().ConfirmedRefreshRateHertz);
    }

    internal Task StepBrightnessAsync(
        bool increase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_blade._canSetBladeBrightness)
        {
            throw new InvalidOperationException(AppStrings.Text("Text_F00EAF88"));
        }

        if (!IsDisplayAvailable)
        {
            var current = ToBladeBrightness(BrightnessPercent);
            var requested = (byte)Math.Clamp(current + (increase ? 16 : -16), 0, 255);
            if (requested == current)
            {
                return Task.CompletedTask;
            }
            BrightnessPercent = Math.Round(
                requested * 100d / 255,
                MidpointRounding.AwayFromZero);
            (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade).KeyboardBrightness = requested;
            return SaveProfileAsync(cancellationToken);
        }

        lock (_bladeBrightnessGate)
        {
            var current = _desiredBladeBrightness ?? ToBladeBrightness(BrightnessPercent);
            var requested = (byte)Math.Clamp(current + (increase ? 16 : -16), 0, 255);
            if (requested == current)
            {
                return Task.CompletedTask;
            }

            _desiredBladeBrightness = requested;
            BrightnessPercent = Math.Round(
                requested * 100d / 255,
                MidpointRounding.AwayFromZero);
            Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
            if (!_bladeBrightnessWriterActive)
            {
                _bladeBrightnessWriterActive = true;
                _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
            }
        }
        return Task.CompletedTask;
    }

    private async Task WriteDesiredBladeBrightnessAsync()
    {
        try
        {
            await WriteDesiredBladeBrightnessCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_bladeBrightnessGate)
            {
                _bladeBrightnessWriterActive = false;
                if (_desiredBladeBrightness is not null && !IsDisposed)
                {
                    _bladeBrightnessWriterActive = true;
                    _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
                }
            }
        }
    }

    private async Task WriteDesiredBladeBrightnessCoreAsync()
    {
        byte? lastWritten = null;
        while (!IsDisposed)
        {
            byte requested;
            lock (_bladeBrightnessGate)
            {
                if (_desiredBladeBrightness is not byte desired)
                {
                    break;
                }
                requested = desired;
            }

            var original = ToBladeBrightness(_blade._confirmedBladeBrightnessPercent);
            var applied = false;
            await RunDeviceOperationAsync(AppStrings.Text("Text_5F0C27DB"), async () =>
            {
                if (!IsDisplayAvailable)
                {
                    bool isCurrent;
                    lock (_bladeBrightnessGate)
                    {
                        isCurrent = _desiredBladeBrightness == requested;
                        if (isCurrent) _desiredBladeBrightness = null;
                    }
                    if (!isCurrent)
                    {
                        applied = true;
                        return;
                    }
                    (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade)
                        .KeyboardBrightness = requested;
                    SetBrightness(requested, confirm: false);
                    await SaveProfileAsync(CancellationToken.None);
                    return;
                }

                try
                {
                    await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                        ActiveDescriptors,
                        requested,
                        CancellationToken.None,
                        verifyReadback: false);
                }
                catch (Exception writeError) when (RuntimeErrors.IsExpected(writeError))
                {
                    bool hasNewerRequest;
                    lock (_bladeBrightnessGate)
                    {
                        hasNewerRequest = _desiredBladeBrightness is byte pending && pending != requested;
                        if (!hasNewerRequest) _desiredBladeBrightness = null;
                    }
                    try
                    {
                        var restored = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                            ActiveDescriptors,
                            original,
                            CancellationToken.None);
                        if (!hasNewerRequest) SetBrightness(restored);
                    }
                    catch (Exception restoreError) when (RuntimeErrors.IsExpected(restoreError))
                    {
                        throw new AggregateException(writeError, restoreError);
                    }

                    throw;
                }

                bool isCurrentRequest;
                lock (_bladeBrightnessGate)
                {
                    isCurrentRequest = _desiredBladeBrightness == requested;
                    if (isCurrentRequest) _desiredBladeBrightness = null;
                }
                if (isCurrentRequest)
                {
                    SetBrightness(requested);
                    lastWritten = requested;
                }
                applied = true;
            }, CancellationToken.None, () =>
            {
                bool restoreSelection;
                lock (_bladeBrightnessGate)
                {
                    restoreSelection = _desiredBladeBrightness == requested;
                    if (restoreSelection) _desiredBladeBrightness = null;
                }
                if (restoreSelection) BrightnessPercent = _blade._confirmedBladeBrightnessPercent;
            }, successVerb: AppStrings.Text("Text_D25102CE"));

            if (!applied)
            {
                return;
            }
        }

        if (lastWritten is byte persistedBrightness &&
            !IsDisplayAvailable)
        {
            (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade)
                .KeyboardBrightness = persistedBrightness;
            await SaveProfileAsync(CancellationToken.None);
            return;
        }

        if (lastWritten is not null && !IsDisposed)
        {
            ScheduleBladeBrightnessVerification();
        }
    }

    private void ScheduleBladeBrightnessVerification()
    {
        var generation = Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        _bladeBrightnessVerification = VerifyBladeBrightnessAfterIdleAsync(generation);
    }

    private async Task VerifyBladeBrightnessAfterIdleAsync(long generation)
    {
        await Task.Delay(BrightnessVerificationDelay);
        if (generation != Volatile.Read(ref _bladeBrightnessVerificationGeneration) ||
            !IsDisplayAvailable ||
            IsDisposed)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_5F0C27DB"), async () =>
        {
            bool hasPendingBrightness;
            lock (_bladeBrightnessGate)
            {
                hasPendingBrightness = _desiredBladeBrightness is not null;
            }
            if (generation != Volatile.Read(ref _bladeBrightnessVerificationGeneration) ||
                !IsDisplayAvailable ||
                hasPendingBrightness)
            {
                return;
            }

            var actual = await _deviceTelemetryReader.ReadBladeKeyboardBrightnessAsync(
                ActiveDescriptors,
                CancellationToken.None);
            if (generation != Volatile.Read(ref _bladeBrightnessVerificationGeneration) ||
                !IsDisplayAvailable)
            {
                return;
            }
            SetBrightness(actual);
            Profile.Global.Blade.KeyboardBrightness = actual;
            await SaveProfileAsync(CancellationToken.None);
        }, CancellationToken.None, () =>
            RequestDeviceRefresh());
    }

    private static byte ToBladeBrightness(double percent) => checked((byte)Math.Round(
        Math.Clamp(percent, 0, 100) * 255 / 100,
        MidpointRounding.AwayFromZero));

    internal async Task ToggleOneTimeFullChargeAsync(
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_6F8CB285"), async () =>
        {
            var current = _blade._bladeOneTimeFullChargeEnabled ??
                throw new InvalidOperationException(AppStrings.Text("Text_8F0D00AE"));
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                ActiveDescriptors,
                !current,
                cancellationToken);
            _blade._bladeOneTimeFullChargeEnabled = actual;
            OneTimeFullChargeEnabled = actual;
            OneTimeFullChargeText = FormatOptionalState(actual);
            RequestDeviceRefresh();
            OneTimeFullChargeChangedByUser?.Invoke(actual);
        }, cancellationToken);
    }

    public async Task ApplyOneTimeFullChargeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyOneTimeFullCharge)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_6F8CB285"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                ActiveDescriptors, OneTimeFullChargeEnabled, cancellationToken);
            _blade._bladeOneTimeFullChargeEnabled = actual;
            OneTimeFullChargeText = FormatOptionalState(actual);
            OneTimeFullChargeEnabled = actual;
            RequestDeviceRefresh();
            OneTimeFullChargeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            OneTimeFullChargeEnabled = _blade._bladeOneTimeFullChargeEnabled ?? false);
    }

    public string DeviceName { get => _blade._bladeDeviceName; private set => SetField(ref _blade._bladeDeviceName, value); }
    public string? Serial
    {
        get => _bladeSerial;
        private set
        {
            if (SetField(ref _bladeSerial, value))
            {
                OnPropertyChanged(nameof(SerialVisibility));
            }
        }
    }
    public Visibility SerialVisibility => string.IsNullOrWhiteSpace(Serial)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string ProtocolText => _bladeRow()?.Capability ?? StatusText;
    public string IdentityText => _bladeRow()?.Identity ?? "VID_1532 / PID_02C6";
    public string StatusText { get => _blade._bladeStatusText; private set => SetField(ref _blade._bladeStatusText, value); }
    public Visibility FilterDriverVisibility => _blade._bladeFilterDriverMissing
        ? Visibility.Visible
        : Visibility.Collapsed;

    internal void SetFilterDriverMissing(bool missing)
    {
        if (_blade._bladeFilterDriverMissing == missing)
        {
            return;
        }

        _blade._bladeFilterDriverMissing = missing;
        OnPropertyChanged(nameof(FilterDriverVisibility));
    }

    public string BrightnessText { get => _blade._bladeBrightnessText; private set => SetField(ref _blade._bladeBrightnessText, value); }
    public string BrightnessSelectionText { get => _blade._bladeBrightnessSelectionText; private set => SetField(ref _blade._bladeBrightnessSelectionText, value); }
    public double BrightnessPercent
    {
        get => _blade._bladeBrightnessPercent;
        set
        {
            if (SetField(ref _blade._bladeBrightnessPercent, Math.Clamp(value, 0, 100)))
            {
                BrightnessSelectionText = $"{_blade._bladeBrightnessPercent:0}%";
            }
        }
    }
    public bool CanSetBrightness
    {
        get => _blade._canSetBladeBrightness;
        private set
        {
            if (SetField(ref _blade._canSetBladeBrightness, value))
            {
                OnPropertyChanged(nameof(CanSetLighting));
                OnPropertyChanged(nameof(CanSetLightingPowerProfile));
                OnPropertyChanged(nameof(CanSetLightingSettings));
                OnPropertyChanged(nameof(CanWriteBrightness));
            }
        }
    }
    public bool CanWriteBrightness =>
        CanSetBrightness && BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId);
    public string PerformanceModeText { get => _blade._bladePerformanceModeText; private set => SetField(ref _blade._bladePerformanceModeText, value); }
    public IReadOnlyList<string> PerformanceModeOptions =>
        _blade.PerformanceModes
            .Select(mode => AppStrings.Text(BladePerformanceModeTextKey(mode)))
            .ToArray();

    private string BladePerformanceModeTextKey(BladePerformanceMode mode) => mode switch
    {
        BladePerformanceMode.Balanced => "Text_9753B259",
        BladePerformanceMode.Gaming when _blade._bladeProductId == BladeModelCapabilities.Blade18_2026ProductId =>
            "BladePerformanceModeTurbo",
        BladePerformanceMode.Gaming => "Text_GamingPerformanceMode",
        BladePerformanceMode.Performance => "Text_C1DB7AE1",
        BladePerformanceMode.BatterySaver => "Text_EE43F0B1",
        BladePerformanceMode.Custom => "Text_598C5804",
        BladePerformanceMode.Silent => "Text_60E54E25",
        BladePerformanceMode.BalancedDc => "Text_A8CF66DD",
        BladePerformanceMode.Hyperboost => "HyperBoost",
        _ => "Text_C1DB7AE1",
    };
    public int PerformanceModeIndex
    {
        get => _blade._bladePerformanceModeIndex;
        set
        {
            if (!SetField(ref _blade._bladePerformanceModeIndex, value)) return;
            // The fixed-speed range is per mode, so re-clamp the stored target on a switch.
            _fan().ReclampFixedRpm();
        }
    }
    public bool CanSetPerformanceMode
    {
        get => _blade._canSetBladePerformanceMode;
        private set
        {
            if (SetField(ref _blade._canSetBladePerformanceMode, value))
            {
                OnPropertyChanged(nameof(CanSetPerformancePowerProfile));
                OnPropertyChanged(nameof(PerformanceModeCardVisibility));
                OnPropertyChanged(nameof(CanWritePerformanceMode));
            }
        }
    }
    public bool CanWritePerformanceMode =>
        CanSetPerformanceMode && BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId);
    public bool CanSetLightingPowerProfile => CanSetBrightness;
    public bool CanSetPerformancePowerProfile => CanSetPerformanceMode;
    public bool CanSetLogoPowerProfile => CanSetLogo;
    public string FanText { get => _blade._bladeFanText; private set => SetField(ref _blade._bladeFanText, value); }
    public bool CanSetFan
    {
        get => _blade._canSetBladeFan;
        private set
        {
            if (SetField(ref _blade._canSetBladeFan, value))
            {
                OnPropertyChanged(nameof(FanCardVisibility));
                OnPropertyChanged(nameof(FanControlVisibility));
            }
        }
    }
    public string FanModeText { get => _blade._bladeFanModeText; private set => SetField(ref _blade._bladeFanModeText, value); }
    public string FanTargetRpmText { get => _blade._bladeFanTargetRpmText; private set => SetField(ref _blade._bladeFanTargetRpmText, value); }
    public string CurrentFanCpuRpmText { get => _blade._bladeCurrentFanCpuRpmText; private set => SetField(ref _blade._bladeCurrentFanCpuRpmText, value); }
    public string CurrentFanGpuRpmText { get => _blade._bladeCurrentFanGpuRpmText; private set => SetField(ref _blade._bladeCurrentFanGpuRpmText, value); }
    public string AdvancedFanCpuModeRawText { get => _blade._bladeAdvancedFanCpuModeRawText; private set => SetField(ref _blade._bladeAdvancedFanCpuModeRawText, value); }
    public string AdvancedFanGpuModeRawText { get => _blade._bladeAdvancedFanGpuModeRawText; private set => SetField(ref _blade._bladeAdvancedFanGpuModeRawText, value); }
    public string GameModeText { get => _blade._bladeGameModeText; private set => SetField(ref _blade._bladeGameModeText, value); }
    public bool GameModeEnabled
    {
        get => _blade._bladeGameModeEnabled;
        set
        {
            if (SetField(ref _blade._bladeGameModeEnabled, value))
            {
                OnPropertyChanged(nameof(CanApplyGamingMode));
            }
        }
    }
    public bool CanSetGamingMode =>
        _blade._bladeGameModeWriteSupported &&
        BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId) &&
        _blade._bladeGameModeState is byte state && state != 2;
    public bool CanApplyGamingMode =>
        CanSetGamingMode && GameModeEnabled != (_blade._bladeGameModeState != 0);
    public string StartupAnimationText { get => _blade._bladeStartupAnimationText; private set => SetField(ref _blade._bladeStartupAnimationText, value); }
    public bool StartupAnimationEnabled
    {
        get => _blade._bladeStartupAnimationSelection;
        set
        {
            if (SetField(ref _blade._bladeStartupAnimationSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyStartupAnimation));
            }
        }
    }
    public bool CanSetStartupAnimation => _blade._bladeStartupAnimationEnabled is not null;
    public bool CanApplyStartupAnimation => CanSetStartupAnimation && StartupAnimationEnabled != _blade._bladeStartupAnimationEnabled;
    public IReadOnlyList<string> NativeDisplayModeOptions => AppStrings.Texts("UHD", "FHD");
    public int NativeDisplayModeIndex
    {
        get => _blade._bladeNativeDisplayModeIndex;
        set
        {
            if (SetField(ref _blade._bladeNativeDisplayModeIndex, value))
            {
                OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
            }
        }
    }
    public bool CanSetNativeDisplayMode =>
        _blade._bladeNativeDisplayMode is not null &&
        _blade._bladeSkuHardwareConfiguration?.SupportsNativeDisplayMode == true;
    public bool CanApplyNativeDisplayMode =>
        CanSetNativeDisplayMode &&
        NativeDisplayModeIndex is >= 0 and <= 1 &&
        NativeDisplayModeIndex != (int)_blade._bladeNativeDisplayMode!.Value;
    public Visibility NativeDisplayModeVisibility =>
        CanSetNativeDisplayMode ? Visibility.Visible : Visibility.Collapsed;
    public string FnKeyText { get => _blade._bladeFnKeyText; private set => SetField(ref _blade._bladeFnKeyText, value); }
    public bool FnKeyMultimediaPrimary
    {
        get => _blade._bladeFnKeySelection;
        set
        {
            if (SetField(ref _blade._bladeFnKeySelection, value))
            {
                OnPropertyChanged(nameof(CanApplyFnKey));
            }
        }
    }
    public bool CanSetFnKey => _blade._bladeFnKeyMultimediaPrimary is not null;
    public bool CanApplyFnKey =>
        CanSetFnKey && FnKeyMultimediaPrimary != _blade._bladeFnKeyMultimediaPrimary;
    public bool LocalDimmingEnabled
    {
        get => _blade._bladeLocalDimmingSelection;
        set
        {
            if (SetField(ref _blade._bladeLocalDimmingSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyLocalDimming));
            }
        }
    }
    public bool CanSetLocalDimming => _blade._bladeLocalDimmingEnabled is not null;
    public bool CanApplyLocalDimming =>
        CanSetLocalDimming && LocalDimmingEnabled != _blade._bladeLocalDimmingEnabled;
    public Visibility LocalDimmingVisibility =>
        CanSetLocalDimming ? Visibility.Visible : Visibility.Collapsed;
    public bool CpuPowerBoostEnabled
    {
        get => _blade._bladeCpuPowerBoostSelection;
        set
        {
            if (SetField(ref _blade._bladeCpuPowerBoostSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
            }
        }
    }
    public bool CanSetCpuPowerBoost => _blade._bladeCpuPowerBoostEnabled is not null;
    public bool CanApplyCpuPowerBoost =>
        CanSetCpuPowerBoost && CpuPowerBoostEnabled != _blade._bladeCpuPowerBoostEnabled;
    public Visibility CpuPowerBoostVisibility =>
        CanSetCpuPowerBoost ? Visibility.Visible : Visibility.Collapsed;
    public string OneTimeFullChargeText { get => _blade._bladeOneTimeFullChargeText; private set => SetField(ref _blade._bladeOneTimeFullChargeText, value); }
    public bool OneTimeFullChargeEnabled
    {
        get => _blade._bladeOneTimeFullChargeSelection;
        set
        {
            if (SetField(ref _blade._bladeOneTimeFullChargeSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyOneTimeFullCharge));
            }
        }
    }
    public bool CanSetOneTimeFullCharge =>
        _blade._bladeOneTimeFullChargeEnabled is not null && _blade._confirmedBladeChargeLimitIndex >= 0 &&
        BladeChargeLimits[_blade._confirmedBladeChargeLimitIndex] < 100;
    public bool CanApplyOneTimeFullCharge =>
        CanSetOneTimeFullCharge && OneTimeFullChargeEnabled != _blade._bladeOneTimeFullChargeEnabled;
    public string ChargeLimitText { get => _blade._bladeChargeLimitText; private set => SetField(ref _blade._bladeChargeLimitText, value); }
    public IReadOnlyList<string> ChargeLimitOptions => AppStrings.Texts("50%", "55%", "60%", "65%", "70%", "75%", "80%", "Text_0E45FC12");
    public int ChargeLimitIndex { get => _blade._bladeChargeLimitIndex; set => SetField(ref _blade._bladeChargeLimitIndex, value); }
    public bool CanSetChargeLimit
    {
        get => _blade._canSetBladeChargeLimit;
        private set
        {
            if (SetField(ref _blade._canSetBladeChargeLimit, value))
            {
                OnPropertyChanged(nameof(ChargeLimitCardVisibility));
            }
        }
    }
    public IReadOnlyList<string> CpuBoostOptions
    {
        get
        {
            var modes = BladeModelCapabilities.CpuBoostModesFor(_blade._bladeProductId);
            return AppStrings.Texts(
                [.. modes.Select(mode => mode switch
                {
                    BladeCpuBoostMode.Low => "Text_CB5F70D1",
                    BladeCpuBoostMode.Medium => "Text_28619638",
                    BladeCpuBoostMode.High => "Text_5C1F32A7",
                    BladeCpuBoostMode.Boost => "BladeCpuBoostHighestOption",
                    BladeCpuBoostMode.Undervolt => "Text_BAA7D10B",
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                })]);
        }
    }
    public string CpuBoostText { get => _blade._bladeCpuBoostText; private set => SetField(ref _blade._bladeCpuBoostText, value); }
    public int CpuBoostIndex { get => _blade._bladeCpuBoostIndex; set => SetField(ref _blade._bladeCpuBoostIndex, value); }
    public bool CanSetCpuBoost => _blade._hasBladeCpuBoost && IsCustomMode;
    public IReadOnlyList<string> GpuBoostOptions => AppStrings.Texts("Text_CB5F70D1", "Text_28619638", "Text_5C1F32A7");
    public string GpuBoostText { get => _blade._bladeGpuBoostText; private set => SetField(ref _blade._bladeGpuBoostText, value); }
    public int GpuBoostIndex { get => _blade._bladeGpuBoostIndex; set => SetField(ref _blade._bladeGpuBoostIndex, value); }
    public bool CanSetGpuBoost => _blade._hasBladeGpuBoost && IsCustomMode;
    public string MaxFanText { get => _blade._bladeMaxFanText; private set => SetField(ref _blade._bladeMaxFanText, value); }
    public bool MaxFanEnabled { get => _blade._bladeMaxFanEnabled; set => SetField(ref _blade._bladeMaxFanEnabled, value); }
    public bool CanSetMaxFan => _blade._hasBladeMaxFan && _blade.SupportsMaxFan && IsCustomMode;

    /// <summary>
    /// Raises the view-model properties whose visibility or option list depends on the
    /// exact Blade product id.
    /// </summary>
    public void RefreshModelCapabilityUi()
    {
        // The model can change the exposed performance-mode list, so the selected index
        // and the shortcut cycle have to be rebuilt against the new list.
        var performanceModes = _blade.PerformanceModes;
        _blade._bladePerformanceModeIndex = -1;
        _blade._confirmedBladePerformanceModeIndex = -1;
        OnPropertyChanged(nameof(PerformanceModeIndex));
        _bladePerformanceCycleModes.RemoveWhere(mode => !performanceModes.Contains(mode));
        if (_bladePerformanceCycleModes.Count == 0)
        {
            _bladePerformanceCycleModes = [.. performanceModes];
        }
        OnPropertyChanged(nameof(PerformanceCycleModes));

        OnPropertyChanged(nameof(CanWriteBrightness));
        OnPropertyChanged(nameof(CanWritePerformanceMode));
        OnPropertyChanged(nameof(HyperboostOptionVisibility));
        OnPropertyChanged(nameof(HyperboostNeedsCoolingPadWarning));
        OnPropertyChanged(nameof(PerformanceModeOptions));
        OnPropertyChanged(nameof(CanSetGamingMode));
        OnPropertyChanged(nameof(CustomPerformanceVisibility));
        OnPropertyChanged(nameof(CpuBoostOptions));
        OnPropertyChanged(nameof(LightingCardVisibility));
        OnPropertyChanged(nameof(LightingModeOptions));
        OnPropertyChanged(nameof(LightingEffectVisibility));
        OnPropertyChanged(nameof(LightingRenderModeVisibility));
        OnPropertyChanged(nameof(LightingPresetVisibility));
        OnPropertyChanged(nameof(LightingColorVisibility));
        OnPropertyChanged(nameof(LightingSecondColorVisibility));
        OnPropertyChanged(nameof(LightingTertiaryColorVisibility));
        _lighting().RaiseChromaVisibilityChanged();
        OnPropertyChanged(nameof(WaveDirectionVisibility));
        OnPropertyChanged(nameof(LightingColorVisibility));
        OnPropertyChanged(nameof(LightingSecondColorVisibility));
        OnPropertyChanged(nameof(LightingTertiaryColorVisibility));
        OnPropertyChanged(nameof(CanSetLighting));
        OnPropertyChanged(nameof(MaxFanVisibility));
        OnPropertyChanged(nameof(CanSetMaxFan));
        OnPropertyChanged(nameof(CanSetCpuBoost));
        OnPropertyChanged(nameof(CpuBoostVisibility));
        OnPropertyChanged(nameof(CanSetCpuPowerBoost));
        OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
        OnPropertyChanged(nameof(CpuPowerBoostVisibility));
        OnPropertyChanged(nameof(GpuBoostVisibility));
        OnPropertyChanged(nameof(PerformanceModeCardVisibility));
        OnPropertyChanged(nameof(FanCardVisibility));
        OnPropertyChanged(nameof(FanControlVisibility));
        OnPropertyChanged(nameof(ChargeLimitCardVisibility));
        OnPropertyChanged(nameof(LogoCardVisibility));
        OnPropertyChanged(nameof(CanSetLightingSettings));
        _fan().RaiseCurveStateChanged();
    }

    /// <summary>True when the exact model exposes the given performance mode.</summary>
    internal bool IsPerformanceModeAvailable(BladePerformanceMode mode) =>
        _blade.PerformanceModes.Contains(mode);

    /// <summary>Collapses the Max Fan column entirely on models without that switch.</summary>
    public Visibility MaxFanVisibility => _blade.SupportsMaxFan
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>
    /// Collapses a whole control when the model answered none of its reads. An OpenRazer-only
    /// Blade never sees the Power Mode Control bitmask, so these cards would otherwise render
    /// a row of "--" placeholders that look like a broken device.
    /// </summary>
    public Visibility PerformanceModeCardVisibility => CanSetPerformanceMode
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility FanCardVisibility => CanSetFan
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>Only show fan mode controls a model has an admitted write for.</summary>
    public Visibility FanControlVisibility =>
        CanSetFan && (_blade.SupportsFixedFan || _blade.SupportsFanCurve)
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility ChargeLimitCardVisibility => CanSetChargeLimit
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility LogoCardVisibility => CanSetLogo
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility CpuBoostVisibility => _blade._hasBladeCpuBoost
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility GpuBoostVisibility => CanSetGpuBoost
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility CustomPerformanceVisibility =>
        IsCustomMode &&
        (_blade._hasBladeCpuBoost || _blade._hasBladeGpuBoost || _blade._hasBladeMaxFan)
            ? Visibility.Visible
            : Visibility.Collapsed;
    public IReadOnlyList<string> LogoOptions => AppStrings.Texts("Text_39B523BD", "Text_6295D9CB", "Text_7EDA32B9");
    public string LogoText { get => _blade._bladeLogoText; private set => SetField(ref _blade._bladeLogoText, value); }
    public int LogoIndex { get => _blade._bladeLogoIndex; set => SetField(ref _blade._bladeLogoIndex, value); }
    public bool CanSetLogo
    {
        get => _blade._canSetBladeLogo;
        private set
        {
            if (SetField(ref _blade._canSetBladeLogo, value))
            {
                OnPropertyChanged(nameof(CanSetLogoPowerProfile));
                OnPropertyChanged(nameof(LogoCardVisibility));
            }
        }
    }
    public string TouchpadText { get => _blade._bladeTouchpadText; private set => SetField(ref _blade._bladeTouchpadText, value); }
    public bool TouchpadEnabled { get => _blade._bladeTouchpadEnabled; private set => SetField(ref _blade._bladeTouchpadEnabled, value); }
    public bool CanSetTouchpad => _blade._canSetBladeTouchpad;
    /// <summary>
    /// Effect names parallel <see cref="BladeLightingModes"/>, so a model that admits only
    /// firmware-rendered uniform effects gets a matching short list instead of entries that
    /// would not apply to it.
    /// </summary>
    public IReadOnlyList<string> LightingModeOptions =>
        BladeLightingDialectPresentation.ModeTextKeys(
                LightingDialect,
                _blade._bladeChromaAnimation is not null)
            .Select(AppStrings.Text)
            .ToArray();

    public Visibility LightingEffectVisibility => ActiveBladeLightingModes.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    private void SetChromaAnimation(ChromaFileAnimation? animation)
    {
        var hadAnimation = _blade._bladeChromaAnimation is not null;
        _blade._bladeChromaAnimation = animation;
        if (hadAnimation == (animation is not null))
        {
            return;
        }

        OnPropertyChanged(nameof(LightingModeOptions));
        OnPropertyChanged(nameof(LightingEffectVisibility));
        if (animation is null &&
            LightingModeIndex ==
            Array.IndexOf(BladeLightingModes, BladeLightingMode.ChromaFile))
        {
            LightingModeIndex = -1;
        }
    }
    public IReadOnlyList<string> LightingPowerProfileOptions =>
        [
            AppStrings.Text("BladeLightingPowerCurrent"),
            AppStrings.Text("BladeLightingPowerPluggedIn"),
            AppStrings.Text("BladeLightingPowerLowAdapter"),
            AppStrings.Text("BladeLightingPowerBattery"),
        ];
    public IReadOnlyList<string> PerformancePowerProfileOptions => LightingPowerProfileOptions;
    public IReadOnlyList<string> RefreshRatePowerProfileOptions => LightingPowerProfileOptions;
    public IReadOnlyList<string> LogoPowerProfileOptions => LightingPowerProfileOptions;
    public bool LightingEnabled
    {
        get => _blade._bladeLightingEnabled;
        set => SetField(ref _blade._bladeLightingEnabled, value);
    }
    public bool ChromaOverrideEnabled
    {
        get => _blade._bladeChromaOverrideEnabled;
        set => SetField(ref _blade._bladeChromaOverrideEnabled, value);
    }
    public string ChromaStatusText => _blade._bladeChromaStatusText;
    public Visibility ChromaStatusVisibility =>
        string.IsNullOrWhiteSpace(_blade._bladeChromaStatusText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    private void SetChromaStatus(string text)
    {
        if (string.Equals(_blade._bladeChromaStatusText, text, StringComparison.Ordinal))
        {
            return;
        }

        _blade._bladeChromaStatusText = text;
        OnPropertyChanged(nameof(ChromaStatusText));
        OnPropertyChanged(nameof(ChromaStatusVisibility));
    }
    /// <summary>
    /// The 2023 generation writes brightness and effects through the telemetry reader, so it
    /// needs no lighting controller. Every other generation still requires one.
    /// </summary>
    public bool CanSetLightingSettings => _blade._canSetBladeBrightness &&
        BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId) &&
        CanApplyLightingDialect;
    public int LightingPowerProfileIndex
    {
        get => _bladeLightingPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladeLightingPowerProfileIndex, next))
            {
                RefreshLightingEditor();
            }
        }
    }
    public int PerformancePowerProfileIndex
    {
        get => _bladePerformancePowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladePerformancePowerProfileIndex, next))
            {
                RefreshPerformanceEditor();
            }
        }
    }
    public int RefreshRatePowerProfileIndex
    {
        get => _bladeRefreshRatePowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladeRefreshRatePowerProfileIndex, next))
            {
                RefreshInternalDisplayRateEditor();
            }
        }
    }
    public int LogoPowerProfileIndex
    {
        get => _bladeLogoPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladeLogoPowerProfileIndex, next))
            {
                RefreshLogoEditor();
            }
        }
    }
    public int LightingModeIndex
    {
        get => _blade._bladeLightingModeIndex;
        set
        {
            if (SetField(ref _blade._bladeLightingModeIndex, value))
            {
                if (SelectedLightingMode is BladeLightingMode.Wave or BladeLightingMode.Spectrum)
                {
                    LightingRenderModeIndex = 0;
                    var defaults = BladeLightingPaletteResolver.GetDefault(SelectedLightingMode.Value).Colors;
                    LightingColor = Color.FromArgb(0xFF, defaults[0].Red, defaults[0].Green, defaults[0].Blue);
                    LightingSecondColor = Color.FromArgb(0xFF, defaults[1].Red, defaults[1].Green, defaults[1].Blue);
                    LightingTertiaryColor = Color.FromArgb(0xFF, defaults[2].Red, defaults[2].Green, defaults[2].Blue);
                }
                OnPropertyChanged(nameof(LightingColorVisibility));
                OnPropertyChanged(nameof(LightingPresetVisibility));
                OnPropertyChanged(nameof(LightingRenderModeVisibility));
                OnPropertyChanged(nameof(LightingSecondColorVisibility));
                OnPropertyChanged(nameof(LightingTertiaryColorVisibility));
                OnPropertyChanged(nameof(WaveDirectionVisibility));
                OnPropertyChanged(nameof(ReactiveSpeedVisibility));
                OnPropertyChanged(nameof(StarlightSpeedVisibility));
                OnPropertyChanged(nameof(StarlightColorModeVisibility));
                OnPropertyChanged(nameof(AnimationSpeedVisibility));
            }
        }
    }
    /// <summary>
    /// Resolves the selected index against the list this model actually renders. Indexing
    /// the full catalog would map the short 2023 list onto the wrong effects.
    /// </summary>
    private BladeLightingMode? SelectedLightingMode
    {
        get
        {
            var modes = ActiveBladeLightingModes;
            return LightingModeIndex >= 0 && LightingModeIndex < modes.Count
                ? modes[LightingModeIndex]
                : null;
        }
    }
    public IReadOnlyList<string> LightingRenderModeOptions => AppStrings.Texts(
        "LightingRenderFirmware", "LightingRenderSoftware");
    public int LightingRenderModeIndex
    {
        get => _blade._bladeLightingRenderModeIndex;
        set
        {
            if (value is not (0 or 1)) return;
            if (SetField(ref _blade._bladeLightingRenderModeIndex, value))
            {
                OnPropertyChanged(nameof(LightingColorVisibility));
                OnPropertyChanged(nameof(LightingPresetVisibility));
                OnPropertyChanged(nameof(LightingSecondColorVisibility));
                OnPropertyChanged(nameof(LightingTertiaryColorVisibility));
            }
        }
    }
    public Visibility LightingRenderModeVisibility =>
        LightingDialect is not (BladeLightingDialect.FirmwareEffect or BladeLightingDialect.Unknown or BladeLightingDialect.LedMatrixBlade) &&
        SelectedLightingMode is BladeLightingMode.Wave or BladeLightingMode.Spectrum
        ? Visibility.Visible
        : Visibility.Collapsed;
    private bool BladeFirmwarePaletteHidden => LightingRenderModeVisibility == Visibility.Visible &&
        LightingRenderModeIndex == 0;
    public Visibility LightingPresetVisibility => SelectedLightingMode == BladeLightingMode.Starlight
        ? LightingColorVisibility : LightingSecondColorVisibility;
    public Visibility LightingColorVisibility => !BladeFirmwarePaletteHidden &&
        (LightingDialect == BladeLightingDialect.LedMatrixBlade
            ? SelectedLightingMode is BladeLightingMode.Static or BladeLightingMode.Breathing or BladeLightingMode.Reactive
            : !UsesFirmwareKeyboardLighting &&
                (SelectedLightingMode is
                BladeLightingMode.Static or BladeLightingMode.Breathing or BladeLightingMode.Reactive or
                BladeLightingMode.Ripple or BladeLightingMode.Tidal or BladeLightingMode.Spectrum or
                BladeLightingMode.Wave or BladeLightingMode.Fire or BladeLightingMode.Wheel or BladeLightingMode.AudioMeter ||
                (SelectedLightingMode == BladeLightingMode.Starlight && StarlightColorModeIndex != 0)))
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility LightingSecondColorVisibility => !BladeFirmwarePaletteHidden &&
        (LightingDialect == BladeLightingDialect.LedMatrixBlade
            ? false
            : !UsesFirmwareKeyboardLighting &&
                (SelectedLightingMode is BladeLightingMode.Tidal or
                BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or BladeLightingMode.Wheel or BladeLightingMode.AudioMeter ||
                (SelectedLightingMode == BladeLightingMode.Starlight && StarlightColorModeIndex == 2)))
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility LightingTertiaryColorVisibility => !BladeFirmwarePaletteHidden &&
        (LightingDialect == BladeLightingDialect.LedMatrixBlade
            ? false
            : !UsesFirmwareKeyboardLighting &&
                SelectedLightingMode is
                BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or
                BladeLightingMode.Wheel or BladeLightingMode.AudioMeter)
        ? Visibility.Visible
        : Visibility.Collapsed;
    /// <summary>
    /// The 2023 generation stores a fixed rightward wave direction, so offering the
    /// direction selector there would present a control that cannot change anything.
    /// </summary>
    public Visibility WaveDirectionVisibility => !UsesFirmwareKeyboardLighting &&
        SelectedLightingMode is BladeLightingMode.Wave or BladeLightingMode.Wheel
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ReactiveSpeedVisibility => SelectedLightingMode == BladeLightingMode.Reactive
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility StarlightSpeedVisibility => SelectedLightingMode == BladeLightingMode.Starlight
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility StarlightColorModeVisibility => SelectedLightingMode == BladeLightingMode.Starlight
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility AnimationSpeedVisibility => SelectedLightingMode is BladeLightingMode.Wheel or BladeLightingMode.Tidal
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> AnimationSpeedOptions => AppStrings.Texts("BladeAnimationSpeedSlow", "BladeAnimationSpeedMedium", "BladeAnimationSpeedFast");
    public int AnimationSpeedIndex { get => _blade._bladeAnimationSpeedIndex; set => SetField(ref _blade._bladeAnimationSpeedIndex, value); }
    public IReadOnlyList<string> WaveDirectionOptions => AppStrings.Texts("Text_FB3FF0D8", "Text_883A50D7");
    public int WaveDirectionIndex { get => _blade._bladeWaveDirectionIndex; set => SetField(ref _blade._bladeWaveDirectionIndex, value); }
    public IReadOnlyList<string> ReactiveSpeedOptions => AppStrings.Texts("BladeReactiveSpeedLevel1", "BladeReactiveSpeedLevel2", "BladeReactiveSpeedLevel3", "BladeReactiveSpeedLevel4");
    public int ReactiveSpeedIndex { get => _blade._bladeReactiveSpeedIndex; set => SetField(ref _blade._bladeReactiveSpeedIndex, value); }
    public IReadOnlyList<string> StarlightSpeedOptions => AnimationSpeedOptions;
    public int StarlightSpeedIndex { get => _blade._bladeStarlightSpeedIndex; set => SetField(ref _blade._bladeStarlightSpeedIndex, value); }
    public IReadOnlyList<string> StarlightColorModeOptions => AppStrings.Texts("BladeStarlightRandom", "BladeStarlightSingle", "BladeStarlightDual");
    public int StarlightColorModeIndex { get => _blade._bladeStarlightColorModeIndex; set { if (SetField(ref _blade._bladeStarlightColorModeIndex, value)) { OnPropertyChanged(nameof(LightingColorVisibility)); OnPropertyChanged(nameof(LightingPresetVisibility)); OnPropertyChanged(nameof(LightingSecondColorVisibility)); } } }
    public Color LightingColor { get => _blade._bladeLightingColor; set => SetField(ref _blade._bladeLightingColor, value); }
    public Color LightingSecondColor { get => _blade._bladeLightingSecondColor; set => SetField(ref _blade._bladeLightingSecondColor, value); }
    public Color LightingTertiaryColor { get => _blade._bladeLightingTertiaryColor; set => SetField(ref _blade._bladeLightingTertiaryColor, value); }
    /// <summary>
    /// Gate for the effect selector. The 2023 generation selects firmware effects through the
    /// telemetry reader, so the matrix-based lighting controller is not required there.
    /// </summary>
    public bool CanSetLighting => _blade._canSetBladeBrightness &&
        BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId) &&
        CanApplyLightingDialect;

    /// <summary>
    /// Both Blade generations have an admitted keyboard-lighting report, so the card is
    /// always available; only the effect list and color controls differ by model.
    /// </summary>
    public Visibility LightingCardVisibility => Visibility.Visible;

    /// <summary>
    /// Effect list for the selected model. A 2023 machine renders a short firmware list;
    /// every other generation keeps the full effect set with the optional Chroma entry.
    /// </summary>
    /// <summary>
    /// Page-facing view of the selected effect. Controls that need to react to the
    /// selection resolve it here so the per-model list stays in one place.
    /// </summary>
    public BladeLightingMode? SelectedLightingModeForPage => SelectedLightingMode;

    private IReadOnlyList<BladeLightingMode> ActiveBladeLightingModes =>
        BladeLightingDialectPresentation.Modes(
            LightingDialect,
            _blade._bladeChromaAnimation is not null);

    /// <summary>
    /// The Blade 16 2023 renders these effects in firmware through its own report, so the
    /// effect and brightness reach the device without the per-key frame path.
    /// </summary>
    private async Task ApplyFirmwareKeyboardLightingEffectAsync(
        BladeLightingMode mode,
        CancellationToken cancellationToken)
    {
        if (!BladeLighting2023Admitted.Allows(mode))
        {
            return;
        }

        var effect = BladeLighting2023Admitted.ToWireEffect(mode);

        await RunDeviceOperationAsync("Keyboard effect", async () =>
        {
            BladeLighting2023Effect actual;
            try
            {
                actual = await _deviceTelemetryReader.SetBlade2023KeyboardEffectAsync(
                    ActiveDescriptors, effect, cancellationToken);
            }
            catch
            {
                NotifyLightingDialectChangedIfDowngraded();
                throw;
            }
            _blade._bladeKeyboardEffect2023 = actual;

            // Go through the shared codec so the persisted key matches the one
            // BladeLightingProfileCodec.Parse accepts on the next read.
            var previousProfile = Profile.Clone();
            var target = EditableLightingProfile;
            var encoded = BladeLightingProfileCodec.Create(new BladeLightingEffect(mode));
            target.Effect = encoded.Effect;
            target.Parameters = encoded.Parameters;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_C7D0CD75"));
            }

            _lighting().ShadowFingerprint = string.Empty;
            RequestDeviceRefresh();
        }, cancellationToken);
    }

    public async Task ApplyLightingSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetLightingSettings)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("Text_09707A2C"),
            async () =>
            {
                var previousProfile = Profile.Clone();
                var target = EditableLightingBladeProfile;
                target.LightingEnabled = LightingEnabled;
                target.ChromaOverrideEnabled = ChromaOverrideEnabled;
                if (!await SaveProfileAsync(cancellationToken))
                {
                    Profile = previousProfile;
                    throw new InvalidOperationException(AppStrings.Text("Text_C7D0CD75"));
                }

                if (IsSelectedLightingPowerActive && IsDisplayAvailable)
                {
                    _lighting().ShadowFingerprint = string.Empty;
                    var blade = ActiveDescriptors.FirstOrDefault(device =>
                        DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                        device.Access == DeviceAccessState.Available);
                    await _lighting().ApplyLoadedProfileAsync(
                        blade,
                        CurrentPowerSource,
                        cancellationToken).ConfigureAwait(false);
                }
                RefreshLightingEditor();
            },
            cancellationToken,
            () => RefreshLightingEditor(),
            successVerb: AppStrings.Text("Text_945C2E42"));
    }
    private async Task<BladeGameModeTelemetry> SetGameModeCoreAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            var previousProfileValue = Profile.Global.Blade.GamingModeEnabled;
            var previousEnabled = _blade._bladeGameModeState != 0;
            var actual = await _deviceTelemetryReader.SetBladeGameModeAsync(
                ActiveDescriptors,
                enabled,
                cancellationToken);
            Profile.Global.Blade.GamingModeEnabled = actual.GameMode != 0;
            try
            {
                if (await SaveProfileAsync(cancellationToken))
                {
                    return actual;
                }
                throw new InvalidOperationException(AppStrings.Text("GamingModeProfileSaveFailed"));
            }
            catch
            {
                Profile.Global.Blade.GamingModeEnabled = previousProfileValue;
                await _deviceTelemetryReader.SetBladeGameModeAsync(
                    ActiveDescriptors,
                    previousEnabled,
                    CancellationToken.None);
                throw;
            }
        }
        catch (NotSupportedException)
        {
            _blade._bladeGameModeWriteSupported = false;
            OnPropertyChanged(nameof(CanSetGamingMode));
            OnPropertyChanged(nameof(CanApplyGamingMode));
            throw;
        }
    }

    private PowerSourceKind SelectedLightingPowerState =>
        SelectedPowerSource(_bladeLightingPowerProfileIndex);
    internal PowerSourceKind SelectedPerformancePowerState =>
        SelectedPowerSource(_bladePerformancePowerProfileIndex);
    private PowerSourceKind SelectedRefreshRatePowerState =>
        SelectedPowerSource(_bladeRefreshRatePowerProfileIndex);
    private PowerSourceKind SelectedLogoPowerState =>
        SelectedPowerSource(_bladeLogoPowerProfileIndex);

    private PowerSourceKind SelectedPowerSource(int index) => index switch
    {
        1 => PowerSourceKind.FullPowerAdapter,
        2 => PowerSourceKind.LowPowerAdapter,
        3 => PowerSourceKind.Battery,
        _ => CurrentPowerSource,
    };

    private bool IsSelectedLightingPowerActive =>
        _bladeLightingPowerProfileIndex == 0 || SelectedLightingPowerState == CurrentPowerSource;

    internal bool IsSelectedPerformancePowerActive =>
        _bladePerformancePowerProfileIndex == 0 || SelectedPerformancePowerState == CurrentPowerSource;

    internal bool IsSelectedRefreshRatePowerActive =>
        _bladeRefreshRatePowerProfileIndex == 0 || SelectedRefreshRatePowerState == CurrentPowerSource;

    private bool IsSelectedLogoPowerActive =>
        _bladeLogoPowerProfileIndex == 0 || SelectedLogoPowerState == CurrentPowerSource;

    private PowerProfileOverrides? SelectedLightingPowerOverrides =>
        SelectedPowerOverrides(_bladeLightingPowerProfileIndex);
    private PowerProfileOverrides? SelectedPerformancePowerOverrides =>
        SelectedPowerOverrides(_bladePerformancePowerProfileIndex);
    private PowerProfileOverrides? SelectedRefreshRatePowerOverrides =>
        SelectedPowerOverrides(_bladeRefreshRatePowerProfileIndex);
    private PowerProfileOverrides? SelectedLogoPowerOverrides =>
        SelectedPowerOverrides(_bladeLogoPowerProfileIndex);

    private PowerProfileOverrides? SelectedPowerOverrides(int index) => index switch
    {
        1 => GetActiveProfile().PluggedIn,
        2 => GetActiveProfile().LowPowerAdapter,
        3 => GetActiveProfile().OnBattery,
        _ => CurrentPowerOverrides,
    };

    private PowerProfileOverrides? CurrentPowerOverrides => CurrentPowerSource switch
    {
        PowerSourceKind.FullPowerAdapter => GetActiveProfile().PluggedIn,
        PowerSourceKind.LowPowerAdapter => GetActiveProfile().LowPowerAdapter,
        PowerSourceKind.Battery => GetActiveProfile().OnBattery,
        _ => null,
    };

    private BladeProfileSettings EditableLightingBladeProfile =>
        SelectedLightingPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    internal BladeProfileSettings EditablePerformanceProfile =>
        SelectedPerformancePowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    internal BladeProfileSettings EditableRefreshRateProfile =>
        SelectedRefreshRatePowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    private BladeProfileSettings EditableLogoProfile =>
        SelectedLogoPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    private LightingProfile EditableLightingProfile =>
        SelectedLightingPowerOverrides?.Lighting ?? GetActiveProfile().Global.Lighting;

    private void NotifyLightingDialectChangedIfDowngraded()
    {
        if (!BladeLightingRuntimeDialect.IsDowngraded(_blade._bladeProductId))
        {
            return;
        }

        OnPropertyChanged(nameof(LightingModeOptions));
        OnPropertyChanged(nameof(LightingEffectVisibility));
        OnPropertyChanged(nameof(LightingRenderModeVisibility));
        OnPropertyChanged(nameof(LightingPresetVisibility));
        OnPropertyChanged(nameof(LightingColorVisibility));
        OnPropertyChanged(nameof(LightingSecondColorVisibility));
        OnPropertyChanged(nameof(LightingTertiaryColorVisibility));
        OnPropertyChanged(nameof(WaveDirectionVisibility));
        OnPropertyChanged(nameof(ReactiveSpeedVisibility));
        OnPropertyChanged(nameof(StarlightSpeedVisibility));
        OnPropertyChanged(nameof(StarlightColorModeVisibility));
        OnPropertyChanged(nameof(AnimationSpeedVisibility));
        OnPropertyChanged(nameof(CanSetLighting));
        OnPropertyChanged(nameof(CanSetLightingSettings));
    }

    internal void RefreshLightingEditor()
    {
        if (Profile.Profiles.Count == 0)
        {
            return;
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        var powerState = SelectedLightingPowerState;
        var resolved = blade is null
            ? null
            : ProfileResolver.Resolve(Profile, blade, powerState);
        var lighting = resolved?.Lighting ?? EditableLightingProfile;
        var bladeProfile = resolved?.Blade ?? EditableLightingBladeProfile;
        LightingEnabled = bladeProfile.LightingEnabled ?? true;
        ChromaOverrideEnabled = bladeProfile.ChromaOverrideEnabled ?? true;
        var effect = LightingEnabled
            ? BladeLightingProfileCodec.Parse(lighting)
            : BladeLightingEffect.Off;
        SetChromaAnimation(effect.ChromaAnimation);

        // Index against the list this model actually renders; the full catalog would
        // select an effect the 2023 generation does not offer.
        var activeModes = ActiveBladeLightingModes;
        var lightingIndex = activeModes.ToList().IndexOf(effect.Mode);
        if (effect.Mode == BladeLightingMode.ChromaFile && effect.ChromaAnimation is null)
        {
            LightingModeIndex = -1;
        }
        else if (lightingIndex >= 0)
        {
            LightingModeIndex = lightingIndex;
            LightingRenderModeIndex = effect.Palette is not null ? 1 : 0;
            WaveDirectionIndex = Array.IndexOf(BladeWaveDirections, effect.Direction);
            LightingColor = Color.FromArgb(0xFF, effect.Color.Red, effect.Color.Green, effect.Color.Blue);
            LightingSecondColor = Color.FromArgb(
                0xFF, effect.SecondColor.Red, effect.SecondColor.Green, effect.SecondColor.Blue);
            var selectedPalette = effect.Palette;
            if (selectedPalette is null && effect.Mode is
                BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or
                BladeLightingMode.Wheel or BladeLightingMode.AudioMeter)
            {
                selectedPalette = BladeLightingPaletteResolver.GetDefault(effect.Mode);
            }
            if (selectedPalette is not null)
            {
                var palette = selectedPalette;
                LightingColor = Color.FromArgb(0xFF, palette.Colors[0].Red, palette.Colors[0].Green, palette.Colors[0].Blue);
                LightingSecondColor = Color.FromArgb(0xFF, palette.Colors[Math.Min(1, palette.Colors.Count - 1)].Red,
                    palette.Colors[Math.Min(1, palette.Colors.Count - 1)].Green, palette.Colors[Math.Min(1, palette.Colors.Count - 1)].Blue);
                LightingTertiaryColor = Color.FromArgb(0xFF, palette.Colors[Math.Min(2, palette.Colors.Count - 1)].Red,
                    palette.Colors[Math.Min(2, palette.Colors.Count - 1)].Green, palette.Colors[Math.Min(2, palette.Colors.Count - 1)].Blue);
            }
            ReactiveSpeedIndex = Array.IndexOf(BladeReactiveSpeeds, effect.ReactiveSpeed);
            StarlightSpeedIndex = Array.IndexOf(BladeStarlightSpeeds, effect.StarlightSpeed);
            StarlightColorModeIndex = (int)effect.StarlightColorMode;
            AnimationSpeedIndex = Array.IndexOf(BladeAnimationSpeeds, effect.AnimationSpeed);
        }

        if (bladeProfile.KeyboardBrightness is byte brightness)
        {
            SetBrightness(brightness, confirm: false);
        }
    }

    internal void RefreshPerformanceEditor()
    {
        if (Profile.Profiles.Count == 0)
        {
            return;
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        var powerState = SelectedPerformancePowerState;
        var bladeProfile = blade is null
            ? EditablePerformanceProfile
            : ProfileResolver.Resolve(Profile, blade, powerState).Blade;
        if (bladeProfile.PerformanceMode is byte rawPerformanceMode &&
            Enum.IsDefined(typeof(BladePerformanceMode), rawPerformanceMode))
        {
            SetPerformanceMode((BladePerformanceMode)rawPerformanceMode, confirm: false);
        }

        // Restore the Max Fan toggle from the same resolved profile, so the switch shows
        // the configured value instead of only the last value read back from the device.
        if (bladeProfile.MaxFanMode is byte rawMaxFan &&
            Enum.IsDefined(typeof(BladeMaxFanMode), rawMaxFan))
        {
            SetMaxFan((BladeMaxFanMode)rawMaxFan);
        }

        _fan().RefreshBladeFanCurveEditor();
    }

    internal void RefreshLogoEditor()
    {
        if (Profile.Profiles.Count == 0)
        {
            return;
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        var powerState = SelectedLogoPowerState;
        var bladeProfile = blade is null
            ? EditableLogoProfile
            : ProfileResolver.Resolve(Profile, blade, powerState).Blade;
        if (bladeProfile.LogoMode is byte rawLogoMode &&
            Enum.IsDefined(typeof(BladeLogoMode), (int)rawLogoMode))
        {
            SetLogo((BladeLogoMode)rawLogoMode, confirm: false);
        }
    }

    private void RefreshInternalDisplayRateEditor()
    {
        var configured = EditableRefreshRateProfile.RefreshRateHertz;
        if (configured is int hertz && _display().InternalDisplayRefreshRates.Contains(hertz))
        {
            _display().InternalDisplayRefreshRateHertz = hertz;
        }
        else
        {
            _display().RaiseRefreshRateIndexChanged();
        }
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_blade._bladeGameModeState is byte gameMode)
        {
            try
            {
                SetGameMode(await _deviceTelemetryReader.SetBladeGameModeAsync(
                    ActiveDescriptors,
                    gameMode != 0,
                    cancellationToken));
            }
            catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
            {
                _diagnosticLog.TryWrite(
                    "device-operation",
                    $"game mode indicator startup sync failed: {exception}");
                SetDeviceOperationError(AppStrings.FormatText("LabeledError",
                    AppStrings.Text("Text_BF98AC8A"),
                    exception.Message));
            }
        }
    }

    internal void ApplyTelemetry(RazerDeviceTelemetry telemetry)
    {
        UpdateBatteryOptimizer(telemetry);
        if (telemetry.BladeKeyboardBrightness is byte brightness)
        {
            if (!_isBrightnessRestorePending())
            {
                SetBrightness(brightness);
            }
            CanSetBrightness = true;
        }
        if (telemetry.BladePerformanceMode is BladePerformanceMode performanceMode)
        {
            SetPerformanceMode(performanceMode);
            CanSetPerformanceMode = true;
        }
        if (telemetry.BladeCpuBoostMode is BladeCpuBoostMode cpuBoost)
        {
            SetCpuBoost(cpuBoost);
        }
        if (telemetry.BladeGpuBoostMode is BladeGpuBoostMode gpuBoost)
        {
            SetGpuBoost(gpuBoost);
        }
        if (telemetry.BladeMaxFanMode is BladeMaxFanMode maxFan)
        {
            SetMaxFan(maxFan);
        }
        if (telemetry.BladeLogoMode is BladeLogoMode logo)
        {
            SetLogo(logo);
            CanSetLogo = true;
        }
        if (telemetry.BladeFanMode is BladeFanMode fanMode)
        {
            CanSetFan = true;
            FanText = fanMode switch
            {
                BladeFanMode.Automatic when telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.FormatText("AutomaticFanSpeed", cpu, gpu),
                BladeFanMode.Automatic => AppStrings.Text("Text_E3D822CF"),
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int target && telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.FormatText("ManualFanCurrentSpeed", target, cpu, gpu),
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int rpm =>
                    AppStrings.FormatText("ManualFanSpeed", rpm),
                BladeFanMode.Manual => AppStrings.Text("Text_85CC42C6"),
                _ => FanText,
            };
            FanModeText = fanMode switch
            {
                BladeFanMode.Automatic => AppStrings.Text("Text_E3D822CF"),
                BladeFanMode.Manual => AppStrings.Text("Text_71816F86"),
                _ => FanModeText,
            };
            if (fanMode == BladeFanMode.Automatic)
            {
                FanTargetRpmText = "--";
            }
            else if (telemetry.BladeFanTargetRpm is int targetRpm)
            {
                FanTargetRpmText = $"{targetRpm:N0} RPM";
            }
        }
        if (telemetry.BladeCurrentFanCpuRpm is int cpuRpm)
        {
            CurrentFanCpuRpmText = $"{cpuRpm:N0} RPM";
        }
        if (telemetry.BladeCurrentFanGpuRpm is int gpuRpm)
        {
            CurrentFanGpuRpmText = $"{gpuRpm:N0} RPM";
        }
        if (telemetry.BladeAdvancedFanCpuModeRaw is byte cpuFanMode)
        {
            AdvancedFanCpuModeRawText = FormatRawByte(cpuFanMode);
        }
        if (telemetry.BladeAdvancedFanGpuModeRaw is byte gpuFanMode)
        {
            AdvancedFanGpuModeRawText = FormatRawByte(gpuFanMode);
        }
        if (telemetry.BladeGameMode is { } gameMode)
        {
            SetGameMode(gameMode);
        }
        else if (ActiveDescriptors.FirstOrDefault(device =>
                     DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                     device.Access == DeviceAccessState.Available) is { } blade)
        {
            var enabled = ProfileResolver
                .Resolve(Profile, blade, CurrentPowerSource)
                .Blade.GamingModeEnabled == true;
            SetGameMode(new(enabled ? (byte)1 : (byte)0, 0, 0));
        }
        if (telemetry.BladeStartupAnimationEnabled is bool startupAnimationEnabled)
        {
            _blade._bladeStartupAnimationEnabled = startupAnimationEnabled;
            StartupAnimationEnabled = startupAnimationEnabled;
            StartupAnimationText = FormatOptionalState(startupAnimationEnabled);
            OnPropertyChanged(nameof(CanSetStartupAnimation));
            OnPropertyChanged(nameof(CanApplyStartupAnimation));
        }
        if (telemetry.BladeFnKeyMultimediaPrimary is bool fnKeyMultimediaPrimary)
        {
            SetFnKey(fnKeyMultimediaPrimary);
        }
        _applyFirmwareMcuVersion(telemetry.BladeMcuVersion);
        _blade._bladeSkuHardwareConfiguration = telemetry.BladeSkuHardwareConfiguration;
        if (_blade._bladeSkuHardwareConfiguration?.SupportsNativeDisplayMode == true &&
            telemetry.BladeNativeDisplayMode is BladeNativeDisplayMode nativeDisplayMode)
        {
            _blade._bladeNativeDisplayMode = nativeDisplayMode;
            NativeDisplayModeIndex = (int)nativeDisplayMode;
            OnPropertyChanged(nameof(CanSetNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
            OnPropertyChanged(nameof(NativeDisplayModeVisibility));
        }
        else
        {
            _blade._bladeNativeDisplayMode = null;
            NativeDisplayModeIndex = -1;
            OnPropertyChanged(nameof(CanSetNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
            OnPropertyChanged(nameof(NativeDisplayModeVisibility));
        }
        if (telemetry.BladeLocalDimmingEnabled is bool localDimmingEnabled)
        {
            _blade._bladeLocalDimmingEnabled = localDimmingEnabled;
            LocalDimmingEnabled = localDimmingEnabled;
            OnPropertyChanged(nameof(CanSetLocalDimming));
            OnPropertyChanged(nameof(CanApplyLocalDimming));
            OnPropertyChanged(nameof(LocalDimmingVisibility));
        }
        else
        {
            _blade._bladeLocalDimmingEnabled = null;
            LocalDimmingEnabled = false;
            OnPropertyChanged(nameof(CanSetLocalDimming));
            OnPropertyChanged(nameof(CanApplyLocalDimming));
            OnPropertyChanged(nameof(LocalDimmingVisibility));
        }
        if (telemetry.BladeCpuPowerBoostEnabled is bool cpuPowerBoostEnabled)
        {
            _blade._bladeCpuPowerBoostEnabled = cpuPowerBoostEnabled;
            CpuPowerBoostEnabled = cpuPowerBoostEnabled;
            OnPropertyChanged(nameof(CanSetCpuPowerBoost));
            OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
            OnPropertyChanged(nameof(CpuPowerBoostVisibility));
        }
        else
        {
            _blade._bladeCpuPowerBoostEnabled = null;
            CpuPowerBoostEnabled = false;
            OnPropertyChanged(nameof(CanSetCpuPowerBoost));
            OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
            OnPropertyChanged(nameof(CpuPowerBoostVisibility));
        }
        if (telemetry.BladeOneTimeFullChargeEnabled is bool oneTimeFullChargeEnabled)
        {
            _blade._bladeOneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
            OneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
            OneTimeFullChargeText = FormatOptionalState(oneTimeFullChargeEnabled);
            OnPropertyChanged(nameof(CanSetOneTimeFullCharge));
            OnPropertyChanged(nameof(CanApplyOneTimeFullCharge));
        }
        if (telemetry.BladeChargeLimitPercent is int chargeLimit)
        {
            SetChargeLimit(chargeLimit);
            CanSetChargeLimit = true;
        }
        if (ActiveDescriptors.Any(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) && device.Access == DeviceAccessState.Available))
        {
            if (_touchpadController?.GetEnabled() is bool touchpadEnabled)
            {
                TouchpadEnabled = touchpadEnabled;
                _blade._confirmedBladeTouchpadEnabled = touchpadEnabled;
                TouchpadText = touchpadEnabled ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
                _blade._canSetBladeTouchpad = true;
                OnPropertyChanged(nameof(CanSetTouchpad));
            }
            else
            {
                TouchpadText = AppStrings.Text("Text_72C29287");
                _blade._canSetBladeTouchpad = false;
                OnPropertyChanged(nameof(CanSetTouchpad));
            }

            StatusText = telemetry.BladeKeyboardBrightness is not null ||
                         telemetry.BladePerformanceMode is not null ||
                         telemetry.BladeChargeLimitPercent is not null
                ? AppStrings.Text("Text_A98448E8")
                : AppStrings.Text("Text_874ADE24");
        }
    }

    internal void ResetTelemetry()
    {
        UpdateBatteryOptimizer(null);
        StatusText = AppStrings.Text("Text_4626A505");
        BrightnessText = "--";
        BrightnessPercent = 0;
        _blade._confirmedBladeBrightnessPercent = 0;
        BrightnessSelectionText = "--";
        CanSetBrightness = false;
        PerformanceModeText = "--";
        PerformanceModeIndex = -1;
        _blade._confirmedBladePerformanceModeIndex = -1;
        OnPropertyChanged(nameof(CustomPerformanceVisibility));
        CanSetPerformanceMode = false;
        FanText = "--";
        CanSetFan = false;
        FanModeText = "--";
        FanTargetRpmText = "--";
        CurrentFanCpuRpmText = "--";
        CurrentFanGpuRpmText = "--";
        AdvancedFanCpuModeRawText = "--";
        AdvancedFanGpuModeRawText = "--";
        SetGameMode(null);
        StartupAnimationText = "--";
        _blade._bladeStartupAnimationEnabled = null;
        StartupAnimationEnabled = false;
        OnPropertyChanged(nameof(CanSetStartupAnimation));
        _blade._bladeFnKeyMultimediaPrimary = null;
        _blade._bladeFnKeyText = "--";
        OnPropertyChanged(nameof(FnKeyText));
        OnPropertyChanged(nameof(FnKeyMultimediaPrimary));
        OnPropertyChanged(nameof(CanSetFnKey));
        OnPropertyChanged(nameof(CanApplyFnKey));
        _blade._bladeNativeDisplayMode = null;
        _blade._bladeSkuHardwareConfiguration = null;
        NativeDisplayModeIndex = -1;
        _blade._bladeLocalDimmingEnabled = null;
        LocalDimmingEnabled = false;
        OnPropertyChanged(nameof(CanSetNativeDisplayMode));
        OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
        OnPropertyChanged(nameof(NativeDisplayModeVisibility));
        OnPropertyChanged(nameof(CanSetLocalDimming));
        OnPropertyChanged(nameof(CanApplyLocalDimming));
        OnPropertyChanged(nameof(LocalDimmingVisibility));
        _blade._bladeCpuPowerBoostEnabled = null;
        CpuPowerBoostEnabled = false;
        OnPropertyChanged(nameof(CanSetCpuPowerBoost));
        OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
        OnPropertyChanged(nameof(CpuPowerBoostVisibility));
        _blade._bladeOneTimeFullChargeEnabled = null;
        OneTimeFullChargeEnabled = false;
        OneTimeFullChargeText = "--";
        ChargeLimitText = "--";
        ChargeLimitIndex = -1;
        _blade._confirmedBladeChargeLimitIndex = -1;
        CanSetChargeLimit = false;
        ClearCustomPerformance();
        LogoText = "--";
        LogoIndex = -1;
        _blade._confirmedBladeLogoIndex = -1;
        CanSetLogo = false;
        TouchpadText = "--";
        TouchpadEnabled = false;
        _blade._confirmedBladeTouchpadEnabled = false;
        _blade._canSetBladeTouchpad = false;
        OnPropertyChanged(nameof(CanSetTouchpad));
        OnPropertyChanged(nameof(CanSetCpuBoost));
        OnPropertyChanged(nameof(CanSetGpuBoost));
        OnPropertyChanged(nameof(CanSetMaxFan));
    }

    internal void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private void SetBrightness(byte brightness, bool confirm = true)
    {
        var percent = Math.Round(brightness * 100d / 255, MidpointRounding.AwayFromZero);
        BrightnessText = $"{percent:0}%";
        lock (_bladeBrightnessGate)
        {
            if (_desiredBladeBrightness is null)
            {
                BrightnessPercent = percent;
                if (confirm)
                {
                    _blade._confirmedBladeBrightnessPercent = percent;
                }
            }
        }
    }

    internal void SetPerformanceMode(BladePerformanceMode mode, bool confirm = true)
    {
        _blade.SetPerformanceMode(mode, confirm);
        OnPropertyChanged(nameof(PerformanceModeText));
        OnPropertyChanged(nameof(PerformanceModeIndex));
        OnPropertyChanged(nameof(CpuBoostText));
        OnPropertyChanged(nameof(CpuBoostIndex));
        OnPropertyChanged(nameof(GpuBoostText));
        OnPropertyChanged(nameof(GpuBoostIndex));
        OnPropertyChanged(nameof(MaxFanText));
        OnPropertyChanged(nameof(MaxFanEnabled));
        OnPropertyChanged(nameof(CanSetCpuBoost));
        OnPropertyChanged(nameof(CanSetGpuBoost));
        OnPropertyChanged(nameof(CanSetMaxFan));
        OnPropertyChanged(nameof(CustomPerformanceVisibility));
    }

    private void SetGameMode(BladeGameModeTelemetry? gameMode)
    {
        _blade.SetGameMode(gameMode);
        GamingModeStateChanged?.Invoke(_blade._bladeGameModeEnabled);
        OnPropertyChanged(nameof(GameModeText));
        OnPropertyChanged(nameof(GameModeEnabled));
        OnPropertyChanged(nameof(CanSetGamingMode));
        OnPropertyChanged(nameof(CanApplyGamingMode));
    }

    private void SetFnKey(bool multimediaPrimary)
    {
        _blade.SetFnKey(multimediaPrimary);
        OnPropertyChanged(nameof(FnKeyText));
        OnPropertyChanged(nameof(FnKeyMultimediaPrimary));
        OnPropertyChanged(nameof(CanSetFnKey));
        OnPropertyChanged(nameof(CanApplyFnKey));
    }

    private void SetChargeLimit(int percent)
    {
        _blade.SetChargeLimit(percent);
        OnPropertyChanged(nameof(ChargeLimitText));
        OnPropertyChanged(nameof(ChargeLimitIndex));
        OnPropertyChanged(nameof(CanSetOneTimeFullCharge));
        OnPropertyChanged(nameof(CanApplyOneTimeFullCharge));
    }

    private bool IsCustomMode => _blade.IsCustomMode;

    private void ClearCustomPerformance()
    {
        _blade.ClearCustomPerformance();
        OnPropertyChanged(nameof(CpuBoostText));
        OnPropertyChanged(nameof(CpuBoostIndex));
        OnPropertyChanged(nameof(GpuBoostText));
        OnPropertyChanged(nameof(GpuBoostIndex));
        OnPropertyChanged(nameof(MaxFanText));
        OnPropertyChanged(nameof(MaxFanEnabled));
        _fan().RaiseCurveStateChanged();
    }

    private void SetCpuBoost(BladeCpuBoostMode mode)
    {
        _blade.SetCpuBoost(mode);
        OnPropertyChanged(nameof(CpuBoostText));
        OnPropertyChanged(nameof(CpuBoostIndex));
        OnPropertyChanged(nameof(CanSetCpuBoost));
    }

    private void SetGpuBoost(BladeGpuBoostMode mode)
    {
        _blade.SetGpuBoost(mode);
        OnPropertyChanged(nameof(GpuBoostText));
        OnPropertyChanged(nameof(GpuBoostIndex));
        OnPropertyChanged(nameof(CanSetGpuBoost));
    }

    private void SetMaxFan(BladeMaxFanMode mode)
    {
        _blade.SetMaxFan(mode);
        OnPropertyChanged(nameof(MaxFanText));
        OnPropertyChanged(nameof(MaxFanEnabled));
        OnPropertyChanged(nameof(CanSetMaxFan));
    }

    private void SetLogo(BladeLogoMode mode, bool confirm = true)
    {
        _blade.SetLogo(mode, confirm);
        OnPropertyChanged(nameof(LogoText));
        OnPropertyChanged(nameof(LogoIndex));
    }

    private static string FormatOptionalState(bool? value) => value switch
    {
        true => AppStrings.Text("Text_F55AD712"),
        false => AppStrings.Text("Text_7E3B0F3C"),
        null => "--",
    };

    private static string FormatRawByte(byte? value) =>
        value is byte raw ? $"0x{raw:X2} ({raw})" : AppStrings.Text("Text_D0003708");

    public async ValueTask DisposeAsync()
    {
        Task brightnessWriter;
        lock (_bladeBrightnessGate)
        {
            _desiredBladeBrightness = null;
            brightnessWriter = _bladeBrightnessWriter;
        }
        await brightnessWriter.ConfigureAwait(false);
        Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        await _bladeBrightnessVerification.ConfigureAwait(false);
    }

    internal void OnDisplayAvailabilityChanged(bool available)
    {
        if (!available)
        {
            Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        }
    }

    internal async Task RefreshCoolingPadPresenceAsync(CancellationToken cancellationToken)
    {
        var connected = await RazerCoolingPad.IsConnectedAsync(cancellationToken);
        if (_bladeCoolingPadConnected == connected)
        {
            return;
        }

        _bladeCoolingPadConnected = connected;
        OnPropertyChanged(nameof(HyperboostOptionVisibility));
        OnPropertyChanged(nameof(HyperboostNeedsCoolingPadWarning));
        OnPropertyChanged(nameof(PerformanceModeOptions));
    }

    internal bool CoolingPadConnected => _bladeCoolingPadConnected;

    internal bool PadlessHyperBoostAccepted => _padlessHyperBoostAccepted;

    internal void UpdateDevice(DeviceDescriptor? blade, string statusText)
    {
        DeviceName = blade?.Name ?? "Razer Blade";
        SetControlDevice(blade);
        if (_blade._bladeProductId != (blade?.ProductId ?? 0))
        {
            _blade._bladeProductId = blade?.ProductId ?? 0;
            RefreshModelCapabilityUi();
        }

        StatusText = statusText;
        if (blade is not null)
        {
            ProbeGpuClockOffset();
        }
    }

    internal void SetControlDevice(DeviceDescriptor? blade)
    {
        var value = blade is
        {
            Access: DeviceAccessState.Available,
            FeatureReportByteLength: RazerFeatureReport.Length,
        }
            ? blade.Id
            : null;
        var productId = blade?.ProductId ?? 0;
        if (StringComparer.OrdinalIgnoreCase.Equals(_bladeControlDevicePath, value) &&
            _bladeControlProductId == productId)
        {
            return;
        }

        _bladeControlDevicePath = value;
        _bladeControlProductId = productId;
        SetFilterDriverMissing(false);
        ControlDeviceChanged?.Invoke(value, productId);
    }

    internal void SetStatus(string statusText) => StatusText = statusText;

    internal void SetSerial(string? serial) => Serial = serial;

    internal void RefreshDeviceOverview()
    {
        OnPropertyChanged(nameof(ProtocolText));
        OnPropertyChanged(nameof(IdentityText));
    }

    internal void ApplyProfileShortcuts(
        HashSet<BladePerformanceMode> cycleModes,
        BladeProfileSettings bladeSettings)
    {
        _bladePerformanceCycleModes = cycleModes;
        var snapTap = bladeSettings.SnapTapEnabled == true;
        var mappingPreset = bladeSettings.MappingPreset ??
            BladeProfileSettings.Product710DefaultMappingPreset;
        var bladeInputChanged = snapTap != _activeSnapTapEnabled ||
            !StringComparer.Ordinal.Equals(mappingPreset, _activeBladeMappingPreset);
        _activeSnapTapEnabled = snapTap;
        _activeBladeMappingPreset = mappingPreset;
        RefreshLightingEditor();
        RefreshPerformanceEditor();
        OnPropertyChanged(nameof(PerformanceCycleModes));
        if (bladeInputChanged)
        {
            InputProfileChanged?.Invoke();
        }
    }

    internal void SetPerformanceCycleModes(IEnumerable<BladePerformanceMode> modes)
    {
        var selected = modes.Where(_blade.PerformanceModes.Contains).ToHashSet();
        if (selected.Count == 0)
        {
            throw new ArgumentException(
                "At least one performance mode must remain in the shortcut cycle.",
                nameof(modes));
        }
        _bladePerformanceCycleModes = selected;
    }

    internal async Task<bool> SavePerformanceCycleModesAsync(
        IEnumerable<BladePerformanceMode> modes,
        CancellationToken cancellationToken = default)
    {
        var selected = modes.Where(_blade.PerformanceModes.Contains).Distinct().ToArray();
        if (selected.Length == 0)
        {
            return false;
        }

        var previous = _bladePerformanceCycleModes;
        _bladePerformanceCycleModes = selected.ToHashSet();
        GetActiveProfile().Shortcuts.PerformanceCycleModes = selected.ToList();
        if (await SaveProfileAsync(cancellationToken))
        {
            OnPropertyChanged(nameof(PerformanceCycleModes));
            return true;
        }

        _bladePerformanceCycleModes = previous;
        GetActiveProfile().Shortcuts.PerformanceCycleModes = previous.ToList();
        return false;
    }

    internal async Task SetSnapTapEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var previous = Profile.Global.Blade.SnapTapEnabled;
        Profile.Global.Blade.SnapTapEnabled = enabled;
        if (!await SaveProfileAsync(cancellationToken))
        {
            Profile.Global.Blade.SnapTapEnabled = previous;
            return;
        }
        _activeSnapTapEnabled = enabled;
    }

    internal IReadOnlySet<BladePerformanceMode> PerformanceCycleModes => _bladePerformanceCycleModes;

    internal bool ActiveSnapTapEnabled => _activeSnapTapEnabled;

    internal string ActiveMappingPreset => _activeBladeMappingPreset;

    internal BladeViewModel State => _blade;

    internal IReadOnlyList<BladePerformanceMode> PerformanceModes => _blade.PerformanceModes;

    internal BladeLightingDialect LightingDialect =>
        BladeLightingRuntimeDialect.Get(_blade._bladeProductId);

    internal bool UsesFirmwareKeyboardLighting =>
        BladeLighting2023Admitted.UsesFirmwareKeyboardLighting(LightingDialect);

    private bool CanApplyLightingDialect =>
        LightingDialect == BladeLightingDialect.FirmwareEffect ||
        (_bladeLightingController is not null &&
         LightingDialect is BladeLightingDialect.LedMatrixBlade or
             BladeLightingDialect.LedMatrix710 or BladeLightingDialect.LedMatrixBlade2 or
             BladeLightingDialect.LedMatrix2026);

    /// <summary>
    /// The card stays visible when the service cannot answer, so a stale machine service
    /// explains itself instead of silently removing the feature.
    /// </summary>
    public bool CanEditGpuClockOffset => _blade._bladeGpuClockOffsetAvailable;

    public Visibility GpuClockLimitVisibility => _bladeGpuClockLimitAvailable
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double GpuClockLimitMhz
    {
        get => _bladeGpuClockLimitMhz;
        set
        {
            var clamped = Math.Clamp(
                value,
                NvApiGpuClockLimit.MinimumMhz,
                NvApiGpuClockLimit.MaximumMhz);
            if (SetField(ref _bladeGpuClockLimitMhz, clamped))
            {
                OnPropertyChanged(nameof(GpuClockLimitText));
            }
        }
    }

    public string GpuClockLimitText => _bladeGpuClockLimitMhz >= NvApiGpuClockLimit.MaximumMhz
        ? AppStrings.Text("BladeGpuClockLimitDefault")
        : $"{_bladeGpuClockLimitMhz:0} MHz";

    public double CoreClockOffsetMhz
    {
        get => _blade._bladeCoreClockOffsetMhz;
        set
        {
            if (SetField(ref _blade._bladeCoreClockOffsetMhz, value))
            {
                OnPropertyChanged(nameof(CoreClockOffsetText));
            }
        }
    }

    public double MemoryClockOffsetMhz
    {
        get => _blade._bladeMemoryClockOffsetMhz;
        set
        {
            if (SetField(ref _blade._bladeMemoryClockOffsetMhz, value))
            {
                OnPropertyChanged(nameof(MemoryClockOffsetText));
            }
        }
    }

    public string CoreClockOffsetText => FormatClockOffset(_blade._bladeCoreClockOffsetMhz);

    public string MemoryClockOffsetText => FormatClockOffset(_blade._bladeMemoryClockOffsetMhz);

    public string GpuClockOffsetStatusText
    {
        get => _blade._bladeGpuClockOffsetStatusText;
        private set => SetField(ref _blade._bladeGpuClockOffsetStatusText, value);
    }

    private static string FormatClockOffset(double megahertz) =>
        $"{(megahertz > 0 ? "+" : string.Empty)}{megahertz:0} MHz";

    /// <summary>Probes once per process; the answer only changes after a driver restart.</summary>
    internal void ProbeGpuClockOffset()
    {
        if (_gpuClockOffsetProbeStarted)
        {
            // A probe fails while the installed service is still the older build, which
            // only matters until the service restarts on its own. Retry slowly instead of
            // hiding the card for the rest of the process.
            if ((_blade._bladeGpuClockOffsetAvailable && _bladeGpuClockLimitAvailable) ||
                DateTimeOffset.UtcNow < _gpuClockOffsetNextProbe)
            {
                return;
            }
        }

        _gpuClockOffsetProbeStarted = true;
        _gpuClockOffsetNextProbe = DateTimeOffset.UtcNow.AddMinutes(2);
        _ = ProbeGpuClockOffsetCoreAsync();
    }

    private async Task ProbeGpuClockOffsetCoreAsync()
    {
        try
        {
            var state = await DedicatedGpuModeServiceClient.TryGetGpuClockOffsetAsync();
            _blade._bladeGpuClockOffsetAvailable = state?.Available == true;
            if (state is { Available: true })
            {
                CoreClockOffsetMhz = state.CoreMhz;
                MemoryClockOffsetMhz = state.MemoryMhz;
            }
            else
            {
                GpuClockOffsetStatusText = AppStrings.Text(state is null
                    ? "BladeGpuClockOffsetServiceMissing"
                    : "BladeGpuClockOffsetUnsupported");
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException or
                ObjectDisposedException or OperationCanceledException or
                System.ComponentModel.Win32Exception)
        {
            _blade._bladeGpuClockOffsetAvailable = false;
            GpuClockOffsetStatusText = AppStrings.Text("BladeGpuClockOffsetServiceMissing");
        }

        try
        {
            var limit = await DedicatedGpuModeServiceClient.TryGetGpuClockLimitAsync();
            _bladeGpuClockLimitAvailable = limit?.Available == true;
            if (limit is { Available: true })
            {
                GpuClockLimitMhz =
                    limit.Megahertz is >= NvApiGpuClockLimit.MinimumMhz and < NvApiGpuClockLimit.MaximumMhz
                        ? limit.Megahertz
                        : NvApiGpuClockLimit.MaximumMhz;
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException or
                ObjectDisposedException or OperationCanceledException or
                System.ComponentModel.Win32Exception)
        {
            _bladeGpuClockLimitAvailable = false;
        }

        OnPropertyChanged(nameof(CanEditGpuClockOffset));
        OnPropertyChanged(nameof(GpuClockLimitVisibility));
    }

    public async Task ApplyGpuClockOffsetAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade._bladeGpuClockOffsetAvailable)
        {
            return;
        }

        var core = (int)Math.Round(_blade._bladeCoreClockOffsetMhz);
        var memory = (int)Math.Round(_blade._bladeMemoryClockOffsetMhz);
        var limit = (int)Math.Round(_bladeGpuClockLimitMhz);
        await RunDeviceOperationAsync(
            AppStrings.Text("BladeGpuClockOffsetTitle"),
            async () =>
            {
                if (!await DedicatedGpuModeServiceClient.TrySetGpuClockOffsetAsync(
                        core, memory, cancellationToken))
                {
                    throw new InvalidOperationException(AppStrings.Text("BladeGpuClockOffsetFailed"));
                }

                await ApplyGpuClockLimitAsync(limit, cancellationToken);
                GpuClockOffsetStatusText = AppStrings.Text("BladeGpuClockOffsetApplied");
            },
            cancellationToken);
    }

    /// <summary>An older service has no clock-limit operation, so the control stays hidden.</summary>
    private async Task ApplyGpuClockLimitAsync(
        int megahertz,
        CancellationToken cancellationToken)
    {
        if (!_bladeGpuClockLimitAvailable)
        {
            return;
        }

        if (!await DedicatedGpuModeServiceClient.TrySetGpuClockLimitAsync(
                megahertz, cancellationToken))
        {
            throw new InvalidOperationException(AppStrings.Text("BladeGpuClockOffsetFailed"));
        }
    }

    /// <summary>Puts the driver back to stock clocks on the user's explicit request.</summary>
    public async Task ResetGpuClockOffsetDefaultsAsync(CancellationToken cancellationToken = default)
    {
        CoreClockOffsetMhz = 0;
        MemoryClockOffsetMhz = 0;
        GpuClockLimitMhz = NvApiGpuClockLimit.MaximumMhz;
        await ApplyGpuClockOffsetAsync(cancellationToken);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
