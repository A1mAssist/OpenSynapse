using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Internal display brightness and refresh rate, including power-source profiles.
/// </summary>
public sealed partial class DisplayWorkspace : INotifyPropertyChanged
{
    private readonly IInternalDisplayController? _internalDisplayController;
    private readonly Func<bool> _isSelectedRefreshRatePowerActive;
    private readonly Func<BladeProfileSettings> _editableRefreshRateBladeProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly Func<string, Func<Task>, CancellationToken, Action?, Task> _runDeviceOperation;
    private readonly Action<string> _setDisplayError;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _deviceDescriptors;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly Func<bool> _isDisposed;
    private string _internalDisplayResolutionText = "--";
    private string _internalDisplayRefreshRateText = "--";
    private IReadOnlyList<int> _internalDisplayRefreshRates = Array.Empty<int>();
    private int _internalDisplayRefreshRateHertz;
    private int _confirmedInternalDisplayRefreshRateHertz;
    private bool _canSetInternalDisplayRefreshRate;
    private readonly object _internalDisplayBrightnessGate = new();
    private Task _internalDisplayBrightnessWriter = Task.CompletedTask;
    private double? _desiredInternalDisplayBrightness;
    private double? _internalDisplayBrightnessInFlight;
    private bool _internalDisplayBrightnessWriterActive;
    private double _internalDisplayBrightnessPercent;
    private double _confirmedInternalDisplayBrightnessPercent;
    private bool _canSetInternalDisplayBrightness;

    public DisplayWorkspace(
        IInternalDisplayController? internalDisplayController,
        Func<bool> isSelectedRefreshRatePowerActive,
        Func<BladeProfileSettings> editableRefreshRateBladeProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        Func<string, Func<Task>, CancellationToken, Action?, Task> runDeviceOperation,
        Action<string> setDisplayError,
        Func<ProfileDocument> getProfile,
        Func<IReadOnlyList<DeviceDescriptor>> deviceDescriptors,
        LocalDiagnosticLog diagnosticLog,
        Func<bool> isDisposed)
    {
        _internalDisplayController = internalDisplayController;
        _isSelectedRefreshRatePowerActive = isSelectedRefreshRatePowerActive;
        _editableRefreshRateBladeProfile = editableRefreshRateBladeProfile;
        _saveProfileAsync = saveProfileAsync;
        _runDeviceOperation = runDeviceOperation;
        _setDisplayError = setDisplayError;
        _getProfile = getProfile;
        _deviceDescriptors = deviceDescriptors;
        _diagnosticLog = diagnosticLog;
        _isDisposed = isDisposed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InternalDisplayResolutionText { get => _internalDisplayResolutionText; private set => SetField(ref _internalDisplayResolutionText, value); }
    public string InternalDisplayRefreshRateText { get => _internalDisplayRefreshRateText; private set => SetField(ref _internalDisplayRefreshRateText, value); }

    public IReadOnlyList<int> InternalDisplayRefreshRates
    {
        get => _internalDisplayRefreshRates;
        private set
        {
            if (SetField(ref _internalDisplayRefreshRates, value))
            {
                OnPropertyChanged(nameof(InternalDisplayRefreshRateOptions));
                OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
            }
        }
    }

    public IReadOnlyList<string> InternalDisplayRefreshRateOptions => InternalDisplayRefreshRates
        .Select(rate => $"{rate} Hz")
        .ToArray();

    public int InternalDisplayRefreshRateHertz
    {
        get => _internalDisplayRefreshRateHertz;
        set
        {
            if (SetField(ref _internalDisplayRefreshRateHertz, value))
            {
                OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
            }
        }
    }

    public int InternalDisplayRefreshRateIndex
    {
        get => Array.IndexOf(InternalDisplayRefreshRates.ToArray(), InternalDisplayRefreshRateHertz);
        set
        {
            if (value >= 0 && value < InternalDisplayRefreshRates.Count)
            {
                InternalDisplayRefreshRateHertz = InternalDisplayRefreshRates[value];
            }
        }
    }

    public bool CanSetInternalDisplayRefreshRate { get => _canSetInternalDisplayRefreshRate; private set => SetField(ref _canSetInternalDisplayRefreshRate, value); }

    public double InternalDisplayBrightnessPercent
    {
        get => _internalDisplayBrightnessPercent;
        private set
        {
            if (SetField(ref _internalDisplayBrightnessPercent, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(InternalDisplayBrightnessText));
            }
        }
    }

    public string InternalDisplayBrightnessText => $"{_internalDisplayBrightnessPercent:0}%";
    public bool CanSetInternalDisplayBrightness { get => _canSetInternalDisplayBrightness; private set => SetField(ref _canSetInternalDisplayBrightness, value); }

    internal IInternalDisplayController? Controller => _internalDisplayController;
    internal int ConfirmedRefreshRateHertz => _confirmedInternalDisplayRefreshRateHertz;
    public async Task ApplyInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        if (!_canSetInternalDisplayRefreshRate || _internalDisplayController is null)
        {
            return;
        }
        if (!_isSelectedRefreshRatePowerActive())
        {
            var profile = _editableRefreshRateBladeProfile();
            var previous = profile.RefreshRateHertz;
            var saved = false;
            try
            {
                profile.RefreshRateHertz = InternalDisplayRefreshRateHertz;
                saved = await _saveProfileAsync(cancellationToken);
            }
            finally
            {
                if (!saved)
                {
                    profile.RefreshRateHertz = previous;
                    InternalDisplayRefreshRateHertz = previous ?? _confirmedInternalDisplayRefreshRateHertz;
                }
            }
            return;
        }

        await _runDeviceOperation(AppStrings.Text("Text_8423206D"), async () =>
        {
            var snapshot = _internalDisplayController.SetRefreshRate(InternalDisplayRefreshRateHertz);
            _ecoDisplayPolicy.ClearRestore();
            ApplyInternalDisplaySnapshot(snapshot);
            var profile = _editableRefreshRateBladeProfile();
            var previous = profile.RefreshRateHertz;
            var saved = false;
            try
            {
                profile.RefreshRateHertz = snapshot.RefreshRateHertz;
                saved = await _saveProfileAsync(cancellationToken);
            }
            finally
            {
                if (!saved) profile.RefreshRateHertz = previous;
            }
        }, cancellationToken, () =>
            InternalDisplayRefreshRateHertz = _confirmedInternalDisplayRefreshRateHertz);
    }

    internal Task QueueInternalDisplayBrightnessAsync(
        double percent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_canSetInternalDisplayBrightness || _internalDisplayController is null || !double.IsFinite(percent))
        {
            return Task.CompletedTask;
        }

        var value = Math.Clamp(percent, 0, 100);
        InternalDisplayBrightnessPercent = value;
        lock (_internalDisplayBrightnessGate)
        {
            var previous = _desiredInternalDisplayBrightness ?? _internalDisplayBrightnessInFlight ??
                _confirmedInternalDisplayBrightnessPercent / 100d;
            var requested = value / 100d;
            if (Math.Abs(requested - previous) < 0.005)
            {
                return Task.CompletedTask;
            }

            _desiredInternalDisplayBrightness = requested;
            if (!_internalDisplayBrightnessWriterActive)
            {
                _internalDisplayBrightnessWriterActive = true;
                _internalDisplayBrightnessWriter = WriteDesiredInternalDisplayBrightnessAsync();
            }
        }

        return Task.CompletedTask;
    }

    internal void RefreshInternalDisplay(PowerSourceKind powerState, bool applyProfile)
    {
        if (_internalDisplayController is null)
        {
            return;
        }

        try
        {
            var snapshot = _internalDisplayController.Read();
            if (applyProfile)
            {
                var blade = _deviceDescriptors().FirstOrDefault(
                    device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily)) ?? new DeviceDescriptor(
                        "internal-display",
                        "Windows internal display",
                        0,
                        0,
                        DeviceAccessState.Available,
                        DeviceCapabilityState.Unsupported,
                        0,
                        0,
                        0,
                        DeviceProtocolFamilies.Blade);
                var requested = ProfileResolver.Resolve(_getProfile(), blade, powerState).Blade.RefreshRateHertz;
                if (ResolveDisplayRate(snapshot, requested) is int hertz &&
                    hertz != snapshot.RefreshRateHertz)
                {
                    if (!snapshot.SupportedRefreshRates.Contains(hertz))
                    {
                        throw new InvalidOperationException(AppStrings.FormatText("UnsupportedDisplayRefreshRate",
                            hertz,
                            snapshot.Width,
                            snapshot.Height));
                    }

                    snapshot = _internalDisplayController.SetRefreshRate(hertz);
                }
                _ecoDisplayPolicy.ConfirmApplied(snapshot.RefreshRateHertz, requested);
            }

            ApplyInternalDisplaySnapshot(snapshot);
            _setDisplayError(string.Empty);
        }
        catch (NoActiveInternalDisplayException)
        {
            // External-display-only: nothing to drive, and nothing is wrong.
            SetInternalDisplayUnavailable();
            _setDisplayError(string.Empty);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentOutOfRangeException)
        {
            SetInternalDisplayUnavailable();
            _setDisplayError(AppStrings.FormatText("DisplayRefreshRateError", exception.Message));
        }
    }

    internal void ApplyInternalDisplaySnapshot(InternalDisplaySnapshot snapshot)
    {
        InternalDisplayResolutionText = $"{snapshot.Width} x {snapshot.Height}";
        InternalDisplayRefreshRateText = $"{snapshot.RefreshRateHertz} Hz";
        InternalDisplayRefreshRates = snapshot.SupportedRefreshRates;
        InternalDisplayRefreshRateHertz = snapshot.RefreshRateHertz;
        _confirmedInternalDisplayRefreshRateHertz = snapshot.RefreshRateHertz;
        CanSetInternalDisplayRefreshRate = snapshot.CanSetRefreshRate;
        ApplyInternalDisplayBrightness(snapshot.BrightnessLevel);
    }

    internal void RaiseRefreshRateIndexChanged() =>
        OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));

    private void SetInternalDisplayUnavailable()
    {
        CanSetInternalDisplayRefreshRate = false;
        CanSetInternalDisplayBrightness = false;
    }

    private void ApplyInternalDisplayBrightness(double? level)
    {
        if (level is not double value)
        {
            CanSetInternalDisplayBrightness = false;
            return;
        }

        lock (_internalDisplayBrightnessGate)
        {
            if (_desiredInternalDisplayBrightness is not null || _internalDisplayBrightnessWriterActive)
            {
                return;
            }

            _confirmedInternalDisplayBrightnessPercent = Math.Clamp(value * 100, 0, 100);
            InternalDisplayBrightnessPercent = _confirmedInternalDisplayBrightnessPercent;
        }
        CanSetInternalDisplayBrightness = true;
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
