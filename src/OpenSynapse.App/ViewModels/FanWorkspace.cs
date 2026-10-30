using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// How the Blade fans are driven, matching what Synapse exposes: firmware automatic,
/// a fixed speed, or a temperature curve the app evaluates.
/// </summary>
public enum BladeFanControlMode
{
    Automatic = 0,
    Fixed = 1,
    Curve = 2,
}

public sealed class FanWorkspace : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly BladeFanCurveRuntime _bladeFanRuntime;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Action<ProfileDocument> _setProfile;
    private readonly Func<BladeProfileSettings> _editablePerformanceBladeProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly Action _refreshBladePerformanceEditor;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _deviceDescriptors;
    private readonly Func<PowerSourceKind> _currentPowerSource;
    private readonly Func<bool> _isSelectedPerformancePowerActive;
    private readonly Func<PowerSourceKind> _selectedPerformancePowerState;
    private readonly BladeViewModel _blade;
    private readonly Func<int> _bladePerformanceModeIndex;
    private readonly Func<bool> _canSetBladeFan;
    private readonly Action<BladePerformanceMode> _setBladePerformanceMode;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly Func<string, Func<Task>, CancellationToken, Action?, Task> _runDeviceOperation;
    private readonly Action _requestDeviceRefresh;
    private BladeFanCurveEditorViewModel? _bladeFanCurveEditor;
    private int _bladeFanFixedRpm = BladeFanLimits.MinimumRpm;
    private BladeFanControlMode? _pendingMode;
    private string _bladeFanControlFingerprint = string.Empty;
    private Task? _bladeFanControlCompletion;

    internal FanWorkspace(
        IRazerDeviceTelemetryReader deviceTelemetryReader,
        IPerformanceMonitor performanceMonitor,
        Func<ProfileDocument> getProfile,
        Action<ProfileDocument> setProfile,
        Func<BladeProfileSettings> editablePerformanceBladeProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        Action refreshBladePerformanceEditor,
        Func<IReadOnlyList<DeviceDescriptor>> deviceDescriptors,
        Func<PowerSourceKind> currentPowerSource,
        Func<bool> isSelectedPerformancePowerActive,
        Func<PowerSourceKind> selectedPerformancePowerState,
        BladeViewModel blade,
        Func<int> bladePerformanceModeIndex,
        Func<bool> canSetBladeFan,
        Action<BladePerformanceMode> setBladePerformanceMode,
        LocalDiagnosticLog diagnosticLog,
        Func<string, Func<Task>, CancellationToken, Action?, Task> runDeviceOperation,
        Action requestDeviceRefresh)
    {
        _bladeFanRuntime = new BladeFanCurveRuntime(deviceTelemetryReader, performanceMonitor);
        _getProfile = getProfile;
        _setProfile = setProfile;
        _editablePerformanceBladeProfile = editablePerformanceBladeProfile;
        _saveProfileAsync = saveProfileAsync;
        _refreshBladePerformanceEditor = refreshBladePerformanceEditor;
        _deviceDescriptors = deviceDescriptors;
        _currentPowerSource = currentPowerSource;
        _isSelectedPerformancePowerActive = isSelectedPerformancePowerActive;
        _selectedPerformancePowerState = selectedPerformancePowerState;
        _blade = blade;
        _bladePerformanceModeIndex = bladePerformanceModeIndex;
        _canSetBladeFan = canSetBladeFan;
        _setBladePerformanceMode = setBladePerformanceMode;
        _diagnosticLog = diagnosticLog;
        _runDeviceOperation = runDeviceOperation;
        _requestDeviceRefresh = requestDeviceRefresh;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Draggable curve editor. Created lazily so non-Blade sessions pay nothing.</summary>
    public BladeFanCurveEditorViewModel BladeFanCurveEditor =>
        _bladeFanCurveEditor ??= new BladeFanCurveEditorViewModel();

    /// <summary>
    /// Which of the three fan control surfaces is showing. Curve mode only exists on models
    /// with audited fan-target evidence, so the picker never offers a mode we cannot drive.
    /// This is a display of the stored profile, not an editable selection: the radio buttons
    /// each own their own commit, so picking one must never silently reinterpret the others.
    /// </summary>
    public int BladeFanControlModeIndex => (int)(_pendingMode ?? StoredBladeFanControlMode);

    /// <summary>
    /// Fixed-speed target, snapped to the protocol step so the stored value is always one
    /// the device accepts.
    /// </summary>
    public int BladeFanFixedRpm
    {
        get => _bladeFanFixedRpm;
        set => SetField(ref _bladeFanFixedRpm, SnapBladeFanFixedRpm(value));
    }

    public int BladeFanFixedMinimumRpm =>
        BladeModelCapabilities.FanTargetRangeFor(_blade._bladeProductId, CurrentBladePerformanceMode).MinimumRpm;

    public int BladeFanFixedMaximumRpm =>
        BladeModelCapabilities.FanTargetRangeFor(_blade._bladeProductId, CurrentBladePerformanceMode).MaximumRpm;

    public IReadOnlyList<string> BladeFanControlModeOptions =>
        _blade.SupportsFanCurve
            ? AppStrings.Texts("BladeFanModeAutomatic", "BladeFanModeFixed", "BladeFanModeCurve")
            : _blade.SupportsFixedFan
                ? AppStrings.Texts("BladeFanModeAutomatic", "BladeFanModeFixed")
                : AppStrings.Texts("BladeFanModeAutomatic");

    public bool BladeFanCurveModeSelected => BladeFanControlModeIndex == (int)BladeFanControlMode.Curve;
    public bool BladeFanFixedModeSelected => BladeFanControlModeIndex == (int)BladeFanControlMode.Fixed;
    public bool BladeFanAutomaticModeSelected => BladeFanControlModeIndex == (int)BladeFanControlMode.Automatic;

    public Visibility BladeFanFixedVisibility =>
        _blade.SupportsFixedFan && _canSetBladeFan() ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BladeFanFixedRpmVisibility =>
        BladeFanFixedVisibility == Visibility.Visible && BladeFanFixedModeSelected
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Collapses the curve block on models with no audited fan-target protocol.</summary>
    public Visibility BladeFanCurveVisibility =>
        _blade.SupportsFanCurve && _canSetBladeFan() ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The editor itself only shows while curve mode is the selected control.</summary>
    public Visibility BladeFanCurvePanelVisibility =>
        BladeFanCurveVisibility == Visibility.Visible && BladeFanCurveModeSelected
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>True once a curve is applied, so the card can say so instead of showing defaults.</summary>
    public bool BladeFanCurveActive =>
        _editablePerformanceBladeProfile().FanCurve is not null;

    public IReadOnlyList<string> BladeFanCurveModeOptions =>
        AppStrings.Texts(
            "BladeFanCurveModeCpu",
            "BladeFanCurveModeGpu",
            "BladeFanCurveModeBoth",
            "BladeFanCurveModeIndependent");

    public int BladeFanCurveModeIndex
    {
        get => (int)BladeFanCurveEditor.TemperatureMode;
        set
        {
            if (!Enum.IsDefined(typeof(BladeFanCurveTemperatureMode), value))
            {
                return;
            }

            BladeFanCurveEditor.TemperatureMode = (BladeFanCurveTemperatureMode)value;
            OnPropertyChanged();
        }
    }

    private BladeFanControlMode StoredBladeFanControlMode =>
        _editablePerformanceBladeProfile().FanCurve is not null
            ? BladeFanControlMode.Curve
            : _editablePerformanceBladeProfile().FanMode is byte rawMode && rawMode == (byte)BladeFanMode.Manual
                ? BladeFanControlMode.Fixed
                : BladeFanControlMode.Automatic;

    /// <summary>
    /// Synapse publishes a separate fixed-speed range per performance mode, so the slider
    /// follows the mode the device is currently in instead of one model-wide range.
    /// </summary>
    private BladePerformanceMode CurrentBladePerformanceMode
    {
        get
        {
            var modes = _blade.PerformanceModes;
            var index = _bladePerformanceModeIndex();
            return index >= 0 && index < modes.Count ? modes[index] : BladePerformanceMode.Balanced;
        }
    }

    public async Task ApplyBladeFixedFanAsync(
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken = default) =>
        await ApplyBladeFanFixedAsync(mode, targetRpm, cancellationToken);

    public async Task ApplyBladeFanCurveAsync(
        BladeFanCurve curve,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curve);
        curve = curve.ClampMaximumFanSpeed(BladeModelCapabilities.MaximumWritableFanTargetRpmFor(_blade._bladeProductId));
        await _runDeviceOperation(AppStrings.Text("Text_0982EC10"), async () =>
        {
            var previous = _getProfile().Clone();

            // The curve lives in the same card as Max Fan, so it follows the performance
            // power profile. Writing the global slot would drop a per-power-source curve.
            var target = _editablePerformanceBladeProfile();
            target.FanCurve = BladeFanCurveProfile.FromCurve(curve);
            target.FanMode = null;
            target.FanTargetRpm = null;

            var blade = _deviceDescriptors().FirstOrDefault(device =>
                DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) && device.Access == DeviceAccessState.Available);
            if (_isSelectedPerformancePowerActive())
            {
                var result = await ApplyProfileAsync(blade, _currentPowerSource(), cancellationToken);
                if (result.Error is not null)
                {
                    _setProfile(previous);
                    throw new InvalidOperationException(result.Error);
                }

                if (!await _saveProfileAsync(cancellationToken))
                {
                    _setProfile(previous);
                    await ApplyProfileAsync(blade, _currentPowerSource(), cancellationToken);
                    throw new InvalidOperationException(AppStrings.Text("Text_BA5FB914"));
                }

                _refreshBladePerformanceEditor();
                return;
            }

            // Editing a power source the machine is not on: store it and let the existing
            // profile-change path push it when that source becomes active.
            if (!await _saveProfileAsync(cancellationToken))
            {
                _setProfile(previous);
                throw new InvalidOperationException(AppStrings.Text("Text_CC12A6F5"));
            }

            _refreshBladePerformanceEditor();
        }, cancellationToken, null);
    }

    public Task StopBladeFanControlAsync() => StopAsync("explicit-stop");

    /// <summary>Loads the resolved curve into the editor so the UI shows the configured tiers.</summary>
    public void RefreshBladeFanCurveEditor()
    {
        if (_blade._bladeProductId == 0)
        {
            return;
        }

        var blade = _deviceDescriptors().FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        var resolved = blade is null
            ? _editablePerformanceBladeProfile()
            : ProfileResolver.Resolve(_getProfile(), blade, _selectedPerformancePowerState()).Blade;

        // The editor owns the value until Apply. Seeding it from the stored profile must
        // not override a target the user is currently typing.
        if (resolved.FanTargetRpm is int targetRpm)
        {
            _bladeFanFixedRpm = SnapBladeFanFixedRpm(targetRpm);
        }
        else
        {
            _bladeFanFixedRpm = Math.Max(_bladeFanFixedRpm, BladeFanFixedMinimumRpm);
        }

        OnPropertyChanged(nameof(BladeFanFixedRpm));
        OnPropertyChanged(nameof(BladeFanFixedMinimumRpm));
        OnPropertyChanged(nameof(BladeFanFixedMaximumRpm));

        _bladeFanCurveEditor = new BladeFanCurveEditorViewModel(TryCreateCurve(resolved));
        OnPropertyChanged(nameof(BladeFanCurveEditor));
        OnPropertyChanged(nameof(BladeFanCurveModeIndex));
        OnPropertyChanged(nameof(BladeFanCurveActive));
        OnPropertyChanged(nameof(BladeFanCurveVisibility));
        RaiseBladeFanModeSelection();
    }

    public void AddBladeFanCurvePoint() => BladeFanCurveEditor.AddPoint();

    public void RemoveBladeFanCurvePoint() => BladeFanCurveEditor.RemoveSelected();

    public void ResetBladeFanCurveToDefault()
    {
        BladeFanCurveEditor.ResetToDefault();
        OnPropertyChanged(nameof(BladeFanCurveModeIndex));
    }

    /// <summary>Applies the editor tiers and switches the fans over to curve control.</summary>
    public async Task ApplyBladeFanCurveEditorAsync(CancellationToken cancellationToken = default)
    {
        if (BladeFanCurveVisibility != Visibility.Visible)
        {
            return;
        }

        // BuildCurve normalises temperatures, so a rejected edit still reaches the device
        // in a valid shape instead of failing the whole apply.
        var curve = BladeFanCurveEditor.BuildCurve();
        await ApplyBladeFanCurveAsync(curve, cancellationToken);
        _pendingMode = null;
        RaiseBladeFanModeSelection();
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    /// <summary>
    /// Shows the curve surface. Choosing the mode only reveals the editor; the tiers still
    /// need Apply, so the radio holds a pending selection the profile does not know about yet.
    /// </summary>
    public void ShowBladeFanCurveMode()
    {
        if (BladeFanCurveVisibility != Visibility.Visible)
        {
            return;
        }

        _pendingMode = BladeFanControlMode.Curve;
        RaiseBladeFanModeSelection();
    }

    /// <summary>Shows the fixed-speed surface without committing a speed yet.</summary>
    public void ShowBladeFanFixedMode()
    {
        if (!_blade.SupportsFixedFan)
        {
            return;
        }

        _pendingMode = BladeFanControlMode.Fixed;
        RaiseBladeFanModeSelection();
    }

    /// <summary>Hands the fans back to firmware automatic control.</summary>
    public async Task SelectBladeFanAutomaticModeAsync(CancellationToken cancellationToken = default)
    {
        _pendingMode = null;
        await ApplyBladeFanFixedAsync(BladeFanMode.Automatic, null, cancellationToken);
        RaiseBladeFanModeSelection();
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    /// <summary>
    /// Commits the target RPM the user typed. Switching into the fixed row without a commit
    /// would leave the device on the previous mode while the radio showed a new one.
    /// </summary>
    public async Task SelectBladeFanFixedModeAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade.SupportsFixedFan)
        {
            return;
        }

        _pendingMode = null;
        await ApplyBladeFanFixedAsync(BladeFanMode.Manual, BladeFanFixedRpm, cancellationToken);
        RaiseBladeFanModeSelection();
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    /// <summary>Drops a pending radio selection after the stored profile cleared the curve.</summary>
    internal void ClearPendingMode()
    {
        _pendingMode = null;
        RaiseBladeFanModeSelection();
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    /// <summary>Re-raises the curve state after a performance-mode change rewrote the profile.</summary>
    internal void RaiseCurveStateChanged()
    {
        OnPropertyChanged(nameof(BladeFanFixedVisibility));
        OnPropertyChanged(nameof(BladeFanCurveVisibility));
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    /// <summary>The fixed-speed range is per mode, so re-clamp the stored target on a switch.</summary>
    internal void ReclampFixedRpm()
    {
        OnPropertyChanged(nameof(BladeFanFixedMinimumRpm));
        OnPropertyChanged(nameof(BladeFanFixedMaximumRpm));
        BladeFanFixedRpm = BladeFanFixedRpm;
    }

    /// <summary>
    /// Pushes the resolved fan profile to the device. A stored fixed target or automatic
    /// mode is only written on an explicit user action, because a profile persisted from an
    /// earlier session must not pin the fans on the next launch. A curve is different: the
    /// runtime has to keep driving it, so it stays eligible for a profile apply.
    /// </summary>
    internal async Task<BladeFanProfileApplyResult> ApplyProfileAsync(
        DeviceDescriptor? blade,
        PowerSourceKind powerState,
        CancellationToken cancellationToken,
        bool writeStoredTarget = true)
    {
        if (blade is null || blade.Access != DeviceAccessState.Available)
        {
            return new(await StopAsync("device-unavailable"), Changed: true);
        }

        // The 597 family is read-only until its fan and thermal setters are exercised.
        if (blade.ProtocolFamily == "blade-597")
        {
            return new(null, Changed: false);
        }

        var profile = ProfileResolver.Resolve(_getProfile(), blade, powerState).Blade;
        BladeFanCurve? curve = null;
        try
        {
            if (profile.FanCurve is not null)
            {
                if (profile.FanMode is not null || profile.FanTargetRpm is not null)
                {
                    return new(AppStrings.Text("Text_780EB5E5"), Changed: false);
                }

                curve = profile.FanCurve.CreateCurve().ClampMaximumFanSpeed(BladeModelCapabilities.MaximumWritableFanTargetRpmFor(_blade._bladeProductId));
            }
            else if (profile.FanMode is byte rawMode)
            {
                if (!Enum.IsDefined(typeof(BladeFanMode), rawMode))
                {
                    return new(AppStrings.FormatText("InvalidBladeFanMode", rawMode), Changed: false);
                }

                var mode = (BladeFanMode)rawMode;
                if (mode == BladeFanMode.Manual && !_blade.SupportsFixedFan)
                {
                    return new(AppStrings.Text("Text_43154C61"), Changed: false);
                }
                if (mode == BladeFanMode.Manual && profile.FanTargetRpm is null ||
                    mode == BladeFanMode.Automatic && profile.FanTargetRpm is not null)
                {
                    return new(AppStrings.Text("Text_F76B82D3"), Changed: false);
                }
            }
            else if (profile.FanTargetRpm is not null)
            {
                return new(AppStrings.Text("Text_FF784DE9"), Changed: false);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new(exception.Message, Changed: false);
        }

        // A stored fixed target or automatic mode is not a command to the hardware: it only
        // records what the user last asked for. Pushing it on every refresh would pin the
        // fans at that speed as soon as the app starts, with no way to tell why.
        if (!writeStoredTarget && curve is null)
        {
            return new(null, Changed: false);
        }

        var fingerprint = CreateBladeFanFingerprint(profile, blade.Id, powerState);
        if (StringComparer.Ordinal.Equals(_bladeFanControlFingerprint, fingerprint) &&
            _bladeFanControlCompletion is { IsCompleted: false })
        {
            return new(null, Changed: false);
        }

        var hadControl = _bladeFanRuntime.IsRunning || _bladeFanControlFingerprint.Length > 0;
        var stopError = await StopAsync("profile-change");
        if (stopError is not null)
        {
            return new(stopError, Changed: true);
        }

        if (fingerprint.Length == 0)
        {
            return new(null, Changed: hadControl);
        }

        try
        {
            if (curve is not null)
            {
                await _bladeFanRuntime.StartAsync(
                    _deviceDescriptors(),
                    curve,
                    cancellationToken);
            }
            else
            {
                var mode = (BladeFanMode)profile.FanMode!.Value;
                await _bladeFanRuntime.StartFixedAsync(
                    _deviceDescriptors(),
                    mode,
                    profile.FanTargetRpm,
                    cancellationToken);
            }

            _bladeFanControlFingerprint = fingerprint;
            _bladeFanControlCompletion = _bladeFanRuntime.Completion;
            _ = ObserveBladeFanControlAsync(_bladeFanControlCompletion);
            return new(null, Changed: true);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _bladeFanControlFingerprint = string.Empty;
            _bladeFanControlCompletion = null;
            return new(RuntimeErrors.Format(exception), Changed: true);
        }
    }

    internal async Task<string?> StopAsync(string reason)
    {
        _bladeFanControlCompletion = null;
        _bladeFanControlFingerprint = string.Empty;
        if (!_bladeFanRuntime.IsRunning)
        {
            return null;
        }

        try
        {
            await _bladeFanRuntime.StopAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _diagnosticLog.TryWrite("blade-fan", $"{reason} restore failed: {exception}");
            return RuntimeErrors.Format(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _bladeFanRuntime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _diagnosticLog.TryWrite("blade-fan", $"dispose restore failed: {exception}");
        }
    }

    private async Task ApplyBladeFanFixedAsync(
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken)
    {
        await _runDeviceOperation(AppStrings.Text("Text_B436A1F2"), async () =>
        {
            var previous = _getProfile().Clone();
            var target = _editablePerformanceBladeProfile();
            target.FanCurve = null;
            target.FanMode = (byte)mode;
            target.FanTargetRpm = targetRpm;

            // The 028C EC only acts on a fixed target in Custom mode. Selecting a fixed
            // speed is an explicit fan command, so switch the profile with it instead of
            // storing a target that the firmware will ignore.
            if (mode == BladeFanMode.Manual &&
                BladeModelCapabilities.FixedFanRequiresCustomMode(_blade._bladeProductId))
            {
                target.PerformanceMode = (byte)BladePerformanceMode.Custom;
                _setBladePerformanceMode(BladePerformanceMode.Custom);
            }

            var blade = _deviceDescriptors().FirstOrDefault(device =>
                DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) && device.Access == DeviceAccessState.Available);
            _diagnosticLog.TryWrite(
                "blade-fan",
                $"Fixed fan apply: mode={mode}, target={targetRpm?.ToString() ?? "automatic"}, " +
                $"power={_currentPowerSource()}, active={_isSelectedPerformancePowerActive()}, " +
                $"blade={blade?.Id ?? "none"}.");
            if (_isSelectedPerformancePowerActive())
            {
                var result = await ApplyProfileAsync(blade, _currentPowerSource(), cancellationToken);
                if (result.Error is not null)
                {
                    _setProfile(previous);
                    throw new InvalidOperationException(result.Error);
                }

                if (!await _saveProfileAsync(cancellationToken))
                {
                    _setProfile(previous);
                    await ApplyProfileAsync(blade, _currentPowerSource(), cancellationToken);
                    throw new InvalidOperationException(AppStrings.Text("Text_3D32CBBD"));
                }

                _refreshBladePerformanceEditor();
                return;
            }

            if (!await _saveProfileAsync(cancellationToken))
            {
                _setProfile(previous);
                throw new InvalidOperationException(AppStrings.Text("Text_CC12A6F5"));
            }

            _refreshBladePerformanceEditor();
        }, cancellationToken, null);
    }

    private async Task ObserveBladeFanControlAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            if (ReferenceEquals(_bladeFanControlCompletion, completion))
            {
                _diagnosticLog.TryWrite("blade-fan", $"Fan control failed: {exception}");
                _bladeFanControlFingerprint = string.Empty;
                _bladeFanControlCompletion = null;
                try
                {
                    await _bladeFanRuntime.StopAsync().ConfigureAwait(false);
                }
                catch (Exception restoreException) when (IsExpectedFanException(restoreException))
                {
                    _diagnosticLog.TryWrite("blade-fan", $"Fan control recovery failed: {restoreException}");
                }
                _requestDeviceRefresh();
            }
        }
    }

    /// <summary>Re-raises the radio bindings after a stored mode changes.</summary>
    private void RaiseBladeFanModeSelection()
    {
        OnPropertyChanged(nameof(BladeFanControlModeIndex));
        OnPropertyChanged(nameof(BladeFanAutomaticModeSelected));
        OnPropertyChanged(nameof(BladeFanFixedModeSelected));
        OnPropertyChanged(nameof(BladeFanCurveModeSelected));
        OnPropertyChanged(nameof(BladeFanFixedVisibility));
        OnPropertyChanged(nameof(BladeFanFixedRpmVisibility));
        OnPropertyChanged(nameof(BladeFanCurvePanelVisibility));
    }

    private int SnapBladeFanFixedRpm(int value)
    {
        var minimum = BladeFanFixedMinimumRpm;
        var clamped = Math.Clamp(value, minimum, BladeFanFixedMaximumRpm);
        return minimum + ((clamped - minimum) / BladeFanLimits.StepRpm) * BladeFanLimits.StepRpm;
    }

    private static BladeFanCurve? TryCreateCurve(BladeProfileSettings resolved)
    {
        if (resolved.FanCurve is not { } profile)
        {
            return null;
        }

        try
        {
            return profile.CreateCurve();
        }
        catch (ArgumentException)
        {
            // A hand-edited profile file can hold tiers the model rejects; fall back to the
            // default curve rather than blocking the card.
            return null;
        }
    }

    private static string CreateBladeFanFingerprint(
        BladeProfileSettings profile,
        string devicePath,
        PowerSourceKind powerState)
    {
        if (profile.FanCurve is { } curve)
        {
            var points = string.Join(",", curve.CpuPoints.Select(FormatPoint)) + "/" +
                string.Join(",", curve.GpuPoints.Select(FormatPoint));
            return $"curve\n{devicePath}\n{powerState}\n{curve.TemperatureMode}\n" +
                $"{curve.MinimumCpuTemperatureCelsius},{curve.MinimumGpuTemperatureCelsius}," +
                $"{curve.MinimumFanSpeedRpm}\n{points}";
        }

        return profile.FanMode is byte mode
            ? $"fixed\n{devicePath}\n{powerState}\n{mode}\n{profile.FanTargetRpm}"
            : string.Empty;

        static string FormatPoint(BladeFanCurvePoint point) =>
            $"{point.TemperatureCelsius}:{point.CpuFanSpeedRpm}:{point.GpuFanSpeedRpm}";
    }

    private static bool IsExpectedFanException(Exception exception) =>
        RuntimeErrors.IsExpected(exception) ||
        exception is ArgumentException or AggregateException or ObjectDisposedException;

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

internal readonly record struct BladeFanProfileApplyResult(string? Error, bool Changed);
