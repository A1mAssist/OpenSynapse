using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerGamepadDeviceRowViewModel : INotifyPropertyChanged
{
    public RazerGamepadDeviceRowViewModel(RazerGamepadConnection connection)
    {
        Connection = connection;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerGamepadConnection Connection { get; }
    public DeviceCategory HardwareCategory => DeviceCategory.Gamepad;
    public string Name => Connection.Name;
    public string Category => AppStrings.Text("RazerGamepadCategory");
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => "\uE7FC";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public Brush StatusBrush { get; } = new SolidColorBrush(Color.FromArgb(255, 153, 221, 114));
    public string Error => string.Empty;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}

public sealed class RazerGamepadStickRowViewModel : INotifyPropertyChanged
{
    private double _sensitivity;
    private double _deadZone;
    private readonly ushort _softBumper;
    private readonly ushort _deltaThreshold;
    private bool _preventDoubleDeadZone;
    private int _selectedCircularityIndex;

    internal RazerGamepadStickRowViewModel(byte controlId, RazerGamepadStickState state)
    {
        ControlId = controlId;
        HasSensitivity = state.Sensitivity is not null;
        HasZones = state.Zones is not null;
        HasCircularity = state.Circularity is not null;
        _sensitivity = state.Sensitivity ?? 50;
        _deadZone = state.Zones?.DeadZone ?? 7;
        _softBumper = state.Zones?.SoftBumper ?? 0;
        _deltaThreshold = state.Zones?.DeltaThreshold ?? 0;
        _preventDoubleDeadZone = state.Zones?.PreventDoubleDeadZone ?? false;
        _selectedCircularityIndex = state.Circularity == RazerGamepadCircularity.Circular ? 1 : 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte ControlId { get; }
    public string Title => AppStrings.Text(ControlId == 1 ? "RazerGamepadLeftStick" : "RazerGamepadRightStick");
    public bool HasSensitivity { get; }
    public bool HasZones { get; }
    public bool HasCircularity { get; }
    internal ushort ZoneSoftBumper => _softBumper;
    internal ushort ZoneDeltaThreshold => _deltaThreshold;
    public bool IsAvailable => HasSensitivity || HasZones || HasCircularity;
    public Visibility Visibility => IsAvailable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SensitivityVisibility => HasSensitivity ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ZonesVisibility => HasZones ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CircularityVisibility => HasCircularity ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<string> CircularityOptions =>
    [
        AppStrings.Text("RazerGamepadCircularityStandard"),
        AppStrings.Text("RazerGamepadCircularityCircular"),
    ];

    public double Sensitivity
    {
        get => _sensitivity;
        set
        {
            if (!SetField(ref _sensitivity, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(SensitivityText));
        }
    }

    public string SensitivityText => $"{Math.Round(Sensitivity)}%";

    public double DeadZone
    {
        get => _deadZone;
        set
        {
            if (!SetField(ref _deadZone, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(DeadZoneText));
        }
    }

    public string DeadZoneText => $"{Math.Round(DeadZone)}%";

    public bool PreventDoubleDeadZone
    {
        get => _preventDoubleDeadZone;
        set => SetField(ref _preventDoubleDeadZone, value);
    }

    public int SelectedCircularityIndex
    {
        get => _selectedCircularityIndex;
        set => SetField(ref _selectedCircularityIndex, Math.Clamp(value, 0, 1));
    }

    internal Func<RazerGamepadStickRowViewModel, CancellationToken, Task>? ApplyHandler { get; set; }

    public Task ApplyAsync(CancellationToken cancellationToken = default) =>
        ApplyHandler?.Invoke(this, cancellationToken) ?? Task.CompletedTask;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}

public sealed class RazerGamepadTriggerRowViewModel : INotifyPropertyChanged
{
    private int _selectedModeIndex;
    private double _startRange;
    private double _endRange;
    private int _selectedActuationIndex;
    private bool _rapidTrigger;

    internal RazerGamepadTriggerRowViewModel(byte controlId, RazerGamepadTriggerState state)
    {
        ControlId = controlId;
        HasMode = state.Mode is not null;
        HasRange = state.StartRange is not null && state.EndRange is not null;
        HasDigital = state.Digital is not null;
        _selectedModeIndex = state.Mode == RazerGamepadTriggerMode.Digital ||
            (state.Mode is null && state.StartRange is null && state.Digital is not null)
                ? 1
                : 0;
        _startRange = state.StartRange ?? 0;
        _endRange = state.EndRange ?? 100;
        _selectedActuationIndex = Math.Clamp((int)(state.Digital?.ActuationPoint ?? 0), 0, 6);
        _rapidTrigger = state.Digital?.IsRapidTrigger ?? false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte ControlId { get; }
    public string Title => AppStrings.Text(ControlId == 1 ? "RazerGamepadLeftTrigger" : "RazerGamepadRightTrigger");
    public bool HasMode { get; }
    public bool HasRange { get; }
    public bool HasDigital { get; }
    public bool IsAvailable => HasMode || HasRange || HasDigital;
    public Visibility Visibility => IsAvailable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ModeVisibility => HasMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RangeVisibility =>
        HasRange && _selectedModeIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DigitalVisibility =>
        HasDigital && _selectedModeIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<string> ModeOptions =>
    [
        AppStrings.Text("RazerGamepadTriggerAnalog"),
        AppStrings.Text("RazerGamepadTriggerDigital"),
    ];
    public IReadOnlyList<string> ActuationOptions => ["0%", "17%", "33%", "50%", "67%", "83%", "100%"];

    public int SelectedModeIndex
    {
        get => _selectedModeIndex;
        set
        {
            if (!SetField(ref _selectedModeIndex, Math.Clamp(value, 0, 1))) return;
            OnPropertyChanged(nameof(RangeVisibility));
            OnPropertyChanged(nameof(DigitalVisibility));
        }
    }

    public double StartRange
    {
        get => _startRange;
        set
        {
            if (!SetField(ref _startRange, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(RangeText));
        }
    }

    public double EndRange
    {
        get => _endRange;
        set
        {
            if (!SetField(ref _endRange, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(RangeText));
        }
    }

    public string RangeText => $"{Math.Round(StartRange)}% - {Math.Round(EndRange)}%";

    public int SelectedActuationIndex
    {
        get => _selectedActuationIndex;
        set => SetField(ref _selectedActuationIndex, Math.Clamp(value, 0, 6));
    }

    public bool RapidTrigger
    {
        get => _rapidTrigger;
        set => SetField(ref _rapidTrigger, value);
    }

    internal Func<RazerGamepadTriggerRowViewModel, CancellationToken, Task>? ApplyHandler { get; set; }

    public Task ApplyAsync(CancellationToken cancellationToken = default) =>
        ApplyHandler?.Invoke(this, cancellationToken) ?? Task.CompletedTask;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}

public sealed class RazerGamepadViewModel : INotifyPropertyChanged
{
    private readonly RazerGamepadConnection _connection;
    private readonly RazerGamepadService _service;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private bool _ignoreDPadDiagonals;
    private int _socdIndex;
    private int _vibrationIndex;
    private string _adcMaskText = "--";
    private string _rawInputText = "--";
    private bool _hasDpad;
    private bool _hasIgnoreDPad;
    private bool _hasSocd;
    private bool _hasVibration;
    private bool _hasAdc;
    private bool _hasRaw;

    public RazerGamepadViewModel(
        RazerGamepadConnection connection,
        Action<string>? diagnostic = null)
    {
        _connection = connection;
        _service = new RazerGamepadService(
            new RazerFeatureTransport(),
            connection.DevicePath,
            TimeSpan.FromMilliseconds(20),
            diagnostic);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<RazerGamepadStickRowViewModel> Sticks { get; } = [];
    public ObservableCollection<RazerGamepadTriggerRowViewModel> Triggers { get; } = [];
    public string Name => _connection.Name;
    public string CategoryText => AppStrings.Text("RazerGamepadCategory");
    public string Identity => $"VID_1532 / PID_{_connection.ProductId:X4}";
    public string DevicePath => _connection.DevicePath;
    public string StatusText => AppStrings.Text("BlackSharkStatusConnected");

    public Visibility SticksVisibility =>
        Sticks.Any(row => row.IsAvailable) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TriggersVisibility =>
        Triggers.Any(row => row.IsAvailable) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DpadVisibility => _hasDpad ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IgnoreDPadVisibility => _hasIgnoreDPad ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SocdVisibility => _hasSocd ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VibrationVisibility => _hasVibration ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AdcVisibility => _hasAdc ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RawInputVisibility => _hasRaw || _hasAdc ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RawTextVisibility => _hasRaw ? Visibility.Visible : Visibility.Collapsed;

    public bool IgnoreDPadDiagonals
    {
        get => _ignoreDPadDiagonals;
        set => SetField(ref _ignoreDPadDiagonals, value);
    }

    public int SocdIndex
    {
        get => _socdIndex;
        set => SetField(ref _socdIndex, Math.Clamp(value, 0, 4));
    }

    public IReadOnlyList<string> SocdOptions =>
    [
        AppStrings.Text("RazerGamepadSocdNeutral"),
        AppStrings.Text("RazerGamepadSocdLast"),
        AppStrings.Text("RazerGamepadSocdFirst"),
        AppStrings.Text("RazerGamepadSocdNone"),
        AppStrings.Text("RazerGamepadSocdAbsoluteUp"),
    ];

    public int VibrationIndex
    {
        get => _vibrationIndex;
        set
        {
            if (!SetField(ref _vibrationIndex, Math.Clamp(value, 0, 6))) return;
            OnPropertyChanged(nameof(VibrationText));
        }
    }

    public IReadOnlyList<string> VibrationOptions => ["0%", "17%", "33%", "50%", "67%", "83%", "100%"];
    public string VibrationText => VibrationOptions[VibrationIndex];
    public string AdcMaskText { get => _adcMaskText; private set => SetField(ref _adcMaskText, value); }
    public string RawInputText { get => _rawInputText; private set => SetField(ref _rawInputText, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
        }
    }

    public bool CanEdit => !IsBusy;
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public string OperationStatusText { get => _operationStatusText; private set => SetField(ref _operationStatusText, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            var state = await _service.ReadStateAsync(cancellationToken);
            ApplyState(state);
            ErrorText = Sticks.All(row => !row.IsAvailable) &&
                Triggers.All(row => !row.IsAvailable) &&
                !_hasDpad && !_hasVibration && !_hasAdc && !_hasRaw
                    ? AppStrings.Text("RazerHeadsetReadFailed")
                    : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyStickAsync(
        RazerGamepadStickRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            if (row.HasSensitivity)
            {
                await _service.SetSensitivityAsync(
                    row.ControlId,
                    (byte)Math.Round(row.Sensitivity),
                    cancellationToken);
            }

            if (row.HasZones)
            {
                await _service.SetZonesAsync(
                    row.ControlId,
                    new RazerGamepadZones(
                        (ushort)Math.Round(row.DeadZone),
                        row.ZoneSoftBumper,
                        row.ZoneDeltaThreshold,
                        row.PreventDoubleDeadZone),
                    cancellationToken);
            }

            if (row.HasCircularity)
            {
                await _service.SetCircularityAsync(
                    row.ControlId,
                    row.SelectedCircularityIndex == 1
                        ? RazerGamepadCircularity.Circular
                        : RazerGamepadCircularity.Standard,
                    cancellationToken);
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyTriggerAsync(
        RazerGamepadTriggerRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            var mode = row.SelectedModeIndex == 1
                ? RazerGamepadTriggerMode.Digital
                : RazerGamepadTriggerMode.Analog;
            if (row.HasMode)
            {
                await _service.SetTriggerModeAsync(row.ControlId, mode, cancellationToken);
            }

            if (mode == RazerGamepadTriggerMode.Analog && row.HasRange)
            {
                await _service.SetTriggerRangeAsync(
                    row.ControlId,
                    (byte)Math.Round(row.StartRange),
                    (byte)Math.Round(row.EndRange),
                    cancellationToken);
            }
            else if (mode == RazerGamepadTriggerMode.Digital && row.HasDigital)
            {
                await _service.SetDigitalTriggerAsync(
                    row.ControlId,
                    new RazerGamepadDigitalTrigger(
                        (byte)row.SelectedActuationIndex,
                        row.RapidTrigger),
                    cancellationToken);
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyDpadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            if (_hasIgnoreDPad)
            {
                await _service.SetIgnoreDPadDiagonalsAsync(IgnoreDPadDiagonals, cancellationToken);
            }

            if (_hasSocd)
            {
                await _service.SetSocdAsync((RazerGamepadSocdMode)SocdIndex, cancellationToken);
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyVibrationAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            await _service.SetVibrationScaleAsync((byte)VibrationIndex, cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private void ApplyState(RazerGamepadState state)
    {
        Sticks.Clear();
        Triggers.Clear();
        foreach (var stick in state.Sticks)
        {
            var row = new RazerGamepadStickRowViewModel(stick.ControlId, stick);
            row.ApplyHandler = ApplyStickAsync;
            Sticks.Add(row);
        }

        foreach (var trigger in state.Triggers)
        {
            var row = new RazerGamepadTriggerRowViewModel(trigger.ControlId, trigger);
            row.ApplyHandler = ApplyTriggerAsync;
            Triggers.Add(row);
        }

        _hasIgnoreDPad = state.IgnoreDPadDiagonals is not null;
        _hasSocd = state.SocdMode is not null;
        _hasDpad = _hasIgnoreDPad || _hasSocd;
        _hasVibration = state.VibrationScale is not null;
        _hasAdc = state.AdcMask is not null;
        _hasRaw = state.RawInput is not null;
        IgnoreDPadDiagonals = state.IgnoreDPadDiagonals ?? false;
        SocdIndex = (int)(state.SocdMode ?? RazerGamepadSocdMode.Neutral);
        VibrationIndex = state.VibrationScale ?? 0;
        AdcMaskText = state.AdcMask is ushort mask ? $"0x{mask:X4}" : "--";
        RawInputText = state.RawInput is { } raw
            ? $"LX {raw.LeftX}  LY {raw.LeftY}  RX {raw.RightX}  RY {raw.RightY}  LT {raw.LeftTrigger}  RT {raw.RightTrigger}"
            : "--";
        OnPropertyChanged(nameof(SticksVisibility));
        OnPropertyChanged(nameof(TriggersVisibility));
        OnPropertyChanged(nameof(DpadVisibility));
        OnPropertyChanged(nameof(IgnoreDPadVisibility));
        OnPropertyChanged(nameof(SocdVisibility));
        OnPropertyChanged(nameof(VibrationVisibility));
        OnPropertyChanged(nameof(AdcVisibility));
        OnPropertyChanged(nameof(RawInputVisibility));
        OnPropertyChanged(nameof(RawTextVisibility));
        OnPropertyChanged(nameof(VibrationText));
    }

    private bool BeginOperation()
    {
        if (IsBusy) return false;
        IsBusy = true;
        OperationStatusText = string.Empty;
        return true;
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}
