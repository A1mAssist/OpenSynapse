using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class ViperWorkspace : INotifyPropertyChanged
{
    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Action<ProfileDocument> _setProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _deviceDescriptors;
    private readonly Func<PowerSourceKind> _currentPowerSource;
    private readonly Func<DeviceRowViewModel?> _deviceRow;
    private readonly Func<string, Func<Task>, CancellationToken, Action?, string?, string?, Task> _runDeviceOperation;
    private readonly Action<DeviceBatteryReading> _notifyBatteryObserved;
    private readonly Func<IReadOnlyList<MacroDefinition>>? _localMacros;
    private readonly Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
        MacroSoftwareBinding?, CancellationToken, Task<string?>>? _softwareMacroBinder;
    private readonly Func<string, CancellationToken, Task<string?>>? _softwareMacroClearer;
    private string _deviceName = "Razer Viper V3 HyperSpeed";
    private Visibility _deviceVisibility = Visibility.Collapsed;
    private string _statusText = AppStrings.Text("Text_DB0974DC");
    private string? _serial;
    private string _batteryText = "--";
    private int _batteryChemistryIndex = -1;
    private int _confirmedBatteryChemistryIndex = -1;
    private bool _canSetBatteryChemistry;
    private string _pollingRateText = "--";
    private int _pollingRateIndex = -1;
    private int _confirmedPollingRateIndex = -1;
    private bool _canSetPollingRate;
    private string _dpiText = "--";
    private double _dpiXValue;
    private double _dpiYValue;
    private double _confirmedDpiXValue;
    private double _confirmedDpiYValue;
    private bool _canSetDpi;
    private string _idleText = "--";
    private string _dpiStagesText = "--";
    private string _lowBatteryThresholdText = "--";
    private double _lowBatteryThresholdPercent;
    private double _confirmedLowBatteryThresholdPercent;
    private bool _canSetLowBatteryThreshold;
    private double _idleMinutesValue;
    private double _confirmedIdleMinutesValue;
    private bool _canSetIdle;
    private int _dpiStageCount;
    private int _activeDpiStage;
    private bool _canSetDpiStages;
    private ViperDpiStagesTelemetry? _confirmedDpiStages;
    private RazerHidMappingViewModel? _mapping;
    private string _mappingProfileFingerprint = string.Empty;

    internal ViperWorkspace(
        IRazerDeviceTelemetryReader deviceTelemetryReader,
        Func<ProfileDocument> getProfile,
        Action<ProfileDocument> setProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        Func<IReadOnlyList<DeviceDescriptor>> deviceDescriptors,
        Func<PowerSourceKind> currentPowerSource,
        Func<DeviceRowViewModel?> deviceRow,
        Func<string, Func<Task>, CancellationToken, Action?, string?, string?, Task> runDeviceOperation,
        Action<DeviceBatteryReading> notifyBatteryObserved,
        Func<IReadOnlyList<MacroDefinition>>? localMacros = null,
        Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
            MacroSoftwareBinding?, CancellationToken, Task<string?>>? softwareMacroBinder = null,
        Func<string, CancellationToken, Task<string?>>? softwareMacroClearer = null)
    {
        _deviceTelemetryReader = deviceTelemetryReader;
        _getProfile = getProfile;
        _setProfile = setProfile;
        _saveProfileAsync = saveProfileAsync;
        _deviceDescriptors = deviceDescriptors;
        _currentPowerSource = currentPowerSource;
        _deviceRow = deviceRow;
        _runDeviceOperation = runDeviceOperation;
        _notifyBatteryObserved = notifyBatteryObserved;
        _localMacros = localMacros;
        _softwareMacroBinder = softwareMacroBinder;
        _softwareMacroClearer = softwareMacroClearer;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DeviceName { get => _deviceName; private set => SetField(ref _deviceName, value); }
    public string ProtocolText => _deviceRow()?.Capability ?? StatusText;
    public string IdentityText => _deviceRow()?.Identity ?? "VID_1532 / PID_00B8";
    public Visibility DeviceVisibility { get => _deviceVisibility; private set => SetField(ref _deviceVisibility, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string? Serial { get => _serial; private set => SetField(ref _serial, value); }
    public Visibility SerialVisibility => string.IsNullOrWhiteSpace(Serial) ? Visibility.Collapsed : Visibility.Visible;
    public string BatteryText { get => _batteryText; private set => SetField(ref _batteryText, value); }
    public int BatteryChemistryIndex { get => _batteryChemistryIndex; set => SetField(ref _batteryChemistryIndex, value); }
    public IReadOnlyList<string> BatteryChemistryOptions => AppStrings.Texts("Text_E43748D4", "Text_DC5115A1", "Text_54C45B90");
    public string BatteryChemistryText => _confirmedBatteryChemistryIndex is >= 0 and < 3
        ? BatteryChemistryOptions[_confirmedBatteryChemistryIndex]
        : "--";
    public bool CanSetBatteryChemistry { get => _canSetBatteryChemistry; private set => SetField(ref _canSetBatteryChemistry, value); }
    public string PollingRateText { get => _pollingRateText; private set => SetField(ref _pollingRateText, value); }
    public int PollingRateIndex { get => _pollingRateIndex; set => SetField(ref _pollingRateIndex, value); }
    public bool CanSetPollingRate { get => _canSetPollingRate; private set => SetField(ref _canSetPollingRate, value); }
    public string DpiText { get => _dpiText; private set => SetField(ref _dpiText, value); }
    public double DpiXValue { get => _dpiXValue; set => SetField(ref _dpiXValue, value); }
    public double DpiYValue { get => _dpiYValue; set => SetField(ref _dpiYValue, value); }
    public bool CanSetDpi { get => _canSetDpi; private set => SetField(ref _canSetDpi, value); }
    public string IdleText { get => _idleText; private set => SetField(ref _idleText, value); }
    public string DpiStagesText { get => _dpiStagesText; private set => SetField(ref _dpiStagesText, value); }
    public string LowBatteryThresholdText { get => _lowBatteryThresholdText; private set => SetField(ref _lowBatteryThresholdText, value); }
    public double LowBatteryThresholdPercent { get => _lowBatteryThresholdPercent; set => SetField(ref _lowBatteryThresholdPercent, value); }
    public bool CanSetLowBatteryThreshold { get => _canSetLowBatteryThreshold; private set => SetField(ref _canSetLowBatteryThreshold, value); }
    public double IdleMinutesValue { get => _idleMinutesValue; set => SetField(ref _idleMinutesValue, value); }
    public bool CanSetIdle { get => _canSetIdle; private set => SetField(ref _canSetIdle, value); }
    public ObservableCollection<ViperDpiStageRowViewModel> DpiStages { get; } = new();
    public int DpiStageCount
    {
        get => _dpiStageCount;
        set => ResizeDpiStages(Math.Clamp(value, 1, 5));
    }
    public int ActiveDpiStage
    {
        get => _activeDpiStage;
        set => SetField(ref _activeDpiStage, Math.Clamp(value, 1, Math.Max(1, DpiStages.Count)));
    }
    public bool CanSetDpiStages { get => _canSetDpiStages; private set => SetField(ref _canSetDpiStages, value); }
    public RazerHidMappingViewModel Mapping =>
        _mapping ??= new RazerHidMappingViewModel(
            new RazerViperMappingTransport(_deviceTelemetryReader, _deviceDescriptors),
            canWrite: true,
            actionCatalog: RazerViperMappingActionCatalog.Instance,
            productId: 0x00B8,
            deviceKind: RazerMappingDeviceKind.Mouse,
            localMacros: _localMacros,
            softwareTriggerResolver: buttonId =>
                buttonId is 4 or 5
                    ? new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = buttonId }
                    : RazerPrivateKeyCatalog.IsPrivate(0x00B8, buttonId)
                        ? new MacroInput
                        {
                            Kind = MacroInputKind.RazerKey,
                            DeviceId = 0x00B8,
                            RazerKeyCode = buttonId,
                        }
                        : null,
            softwareTriggerBinder: _softwareMacroBinder,
            softwareTriggerClearer: _softwareMacroClearer,
            afterApplyAsync: PersistMappingProfileAsync);

    public async Task ApplyLowBatteryThresholdAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetLowBatteryThreshold)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_B98036FA"), async () =>
        {
            var settings = Profile.Global.Viper;
            var previous = settings.LowBatteryThresholdPercent;
            var percent = checked((int)Math.Round(
                LowBatteryThresholdPercent, MidpointRounding.AwayFromZero));
            var actual = await _deviceTelemetryReader.SetViperLowBatteryThresholdAsync(
                _deviceDescriptors(), percent, cancellationToken);
            LowBatteryThresholdPercent = actual;
            _confirmedLowBatteryThresholdPercent = actual;
            LowBatteryThresholdText = $"{actual}%";
            if (!ReferenceEquals(settings, Profile.Global.Viper)) throw ProfileSaveFailed();
            settings.LowBatteryThresholdPercent = actual;
            var saved = false;
            try { saved = await _saveProfileAsync(cancellationToken); }
            finally { if (!saved) settings.LowBatteryThresholdPercent = previous; }
            if (!saved) throw ProfileSaveFailed();
        }, cancellationToken, () =>
        {
            LowBatteryThresholdPercent = _confirmedLowBatteryThresholdPercent;
        });
    }

    public async Task ApplyDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetDpiStages || DpiStages.Count is < 1 or > 5)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_7F983785"), async () =>
        {
            var previousProfile = Profile.Clone();
            var requested = new ViperDpiStagesTelemetry(
                checked((byte)ActiveDpiStage),
                DpiStages.Select(row => new ViperDpiStageTelemetry(
                    checked((byte)row.Number), checked((int)row.X), checked((int)row.Y))).ToArray());
            var actual = await _deviceTelemetryReader.SetViperDpiStagesAsync(
                _deviceDescriptors(), requested, cancellationToken);
            SetDpiStages(actual);
            Profile.Global.Viper.DpiStages = new ViperDpiStagesProfile
            {
                ActiveStage = actual.ActiveStage,
                Stages = actual.Stages.Select(stage => new ViperDpiStageProfile
                {
                    Number = stage.Number,
                    X = stage.X,
                    Y = stage.Y,
                }).ToList(),
            };
            var activeStage = actual.Stages.FirstOrDefault(stage => stage.Number == actual.ActiveStage);
            if (activeStage is not null)
            {
                Profile.Global.Viper.DpiX = activeStage.X;
                Profile.Global.Viper.DpiY = activeStage.Y;
            }
            if (!await _saveProfileAsync(cancellationToken))
            {
                _setProfile(previousProfile);
                throw new InvalidOperationException(AppStrings.Text("Text_86504D84"));
            }
        }, cancellationToken, RestoreDpiStages);
    }

    internal void ApplyTelemetry(RazerDeviceTelemetry telemetry)
    {
        if (telemetry.ViperBatteryPercent is int battery)
        {
            BatteryText = $"{battery}%";
        }

        var viper = _deviceDescriptors().FirstOrDefault(device =>
            device.ProtocolFamily == DeviceProtocolFamilies.Viper &&
            device.Access == DeviceAccessState.Available);
        if (viper is not null)
        {
            if (telemetry.ViperBatteryPercent is not null)
            {
                _notifyBatteryObserved(new DeviceBatteryReading(
                    $"viper:{viper.Id}", viper.Name, telemetry.ViperBatteryPercent, null));
            }

            var chemistry = ProfileResolver.Resolve(
                Profile,
                viper,
                _currentPowerSource()).Viper.BatteryChemistry;
            BatteryChemistryIndex = chemistry is byte value ? value : -1;
            _confirmedBatteryChemistryIndex = BatteryChemistryIndex;
            OnPropertyChanged(nameof(BatteryChemistryText));
        }

        if (telemetry.ViperPollingRateHertz is int pollingRate)
        {
            PollingRateText = $"{pollingRate} Hz";
            PollingRateIndex = pollingRate switch { 125 => 0, 500 => 1, _ => 2 };
            _confirmedPollingRateIndex = PollingRateIndex;
            CanSetPollingRate = true;
        }

        if (telemetry.ViperDpiX is int dpiX && telemetry.ViperDpiY is int dpiY)
        {
            DpiText = $"{dpiX} × {dpiY}";
            DpiXValue = dpiX;
            DpiYValue = dpiY;
            _confirmedDpiXValue = dpiX;
            _confirmedDpiYValue = dpiY;
            CanSetDpi = true;
        }

        if (telemetry.ViperIdleSeconds is int idleSeconds)
        {
            IdleText = FormatDuration(idleSeconds);
            IdleMinutesValue = idleSeconds / 60d;
            _confirmedIdleMinutesValue = IdleMinutesValue;
            CanSetIdle = true;
        }

        if (telemetry.ViperDpiStages is { } stages)
        {
            SetDpiStages(stages);
            CanSetDpiStages = true;
        }

        if (telemetry.ViperLowBatteryThresholdRaw is byte raw)
        {
            LowBatteryThresholdPercent = ViperLowBatteryThresholdProtocol.ToPercent(raw);
            LowBatteryThresholdText = $"{LowBatteryThresholdPercent}%";
            _confirmedLowBatteryThresholdPercent = LowBatteryThresholdPercent;
            CanSetLowBatteryThreshold = true;
        }

        if (viper is not null)
        {
            CanSetBatteryChemistry = true;
            StatusText = telemetry.ViperBatteryPercent is not null ||
                         telemetry.ViperPollingRateHertz is not null ||
                         telemetry.ViperDpiX is not null ||
                         telemetry.ViperIdleSeconds is not null
                ? AppStrings.Text("Text_7EEF96A9")
                : AppStrings.Text("Text_2E25FD9A");
        }
    }

    internal async Task<string?> ApplyProfileMappingAsync(
        DeviceDescriptor? viper,
        PowerSourceKind powerState,
        CancellationToken cancellationToken)
    {
        if (viper is null || viper.Access != DeviceAccessState.Available)
        {
            _mappingProfileFingerprint = string.Empty;
            return null;
        }

        var profile = ProfileResolver.Resolve(Profile, viper, powerState).Viper;
        var fingerprint = CreateMappingFingerprint(viper.Id, profile.ButtonAssignments);
        if (StringComparer.Ordinal.Equals(_mappingProfileFingerprint, fingerprint))
        {
            return null;
        }

        try
        {
            IReadOnlyList<ViperButtonAssignment> actual;
            if (profile.ButtonAssignments is null)
            {
                actual = await _deviceTelemetryReader.ReadViperButtonAssignmentsAsync(
                    _deviceDescriptors(), cancellationToken);
                var previous = Profile.Global.Viper.ButtonAssignments;
                Profile.Global.Viper.ButtonAssignments = actual.Select(ToProfileAssignment).ToList();
                if (!await _saveProfileAsync(cancellationToken))
                {
                    Profile.Global.Viper.ButtonAssignments = previous;
                    return AppStrings.Text("ViperMappingReadProfileSaveFailed");
                }
                fingerprint = CreateMappingFingerprint(
                    viper.Id,
                    Profile.Global.Viper.ButtonAssignments);
            }
            else
            {
                actual = await _deviceTelemetryReader.SetViperButtonAssignmentsAsync(
                    _deviceDescriptors(),
                    profile.ButtonAssignments.Select(ToDeviceAssignment).ToArray(),
                    cancellationToken);
            }

            _mappingProfileFingerprint = fingerprint;
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _mappingProfileFingerprint = string.Empty;
            return AppStrings.FormatText("ViperMappingProfileError", exception.Message);
        }
    }

    private async Task PersistMappingProfileAsync(CancellationToken cancellationToken)
    {
        var previous = Profile.Global.Viper.ButtonAssignments;
        var actual = await _deviceTelemetryReader.ReadViperButtonAssignmentsAsync(
            _deviceDescriptors(), cancellationToken).ConfigureAwait(true);
        Profile.Global.Viper.ButtonAssignments = actual
            .Select(ToProfileAssignment)
            .ToList();
        if (!await _saveProfileAsync(cancellationToken).ConfigureAwait(true))
        {
            Profile.Global.Viper.ButtonAssignments = previous;
            throw new InvalidOperationException(AppStrings.Text("ViperMappingReadProfileSaveFailed"));
        }

        var viper = _deviceDescriptors().FirstOrDefault(device =>
            device.ProtocolFamily == DeviceProtocolFamilies.Viper);
        _mappingProfileFingerprint = CreateMappingFingerprint(
            viper?.Id ?? string.Empty,
            Profile.Global.Viper.ButtonAssignments);
    }

    internal void SetDevice(DeviceDescriptor? device, string? serial, string status)
    {
        DeviceName = device?.Name ?? "Razer Viper";
        Serial = serial;
        StatusText = status;
    }

    internal void SetVisibility(bool available) =>
        DeviceVisibility = available ? Visibility.Visible : Visibility.Collapsed;

    internal void SetUnavailable(bool hadViper)
    {
        Serial = null;
        DeviceVisibility = hadViper ? Visibility.Visible : Visibility.Collapsed;
        StatusText = hadViper ? AppStrings.Text("Text_A70924BE") : AppStrings.Text("Text_DB0974DC");
    }

    internal void ResetTelemetry()
    {
        _statusText = AppStrings.Text("Text_4626A505");
        _dpiStagesText = "--";
        _lowBatteryThresholdText = "--";
        _lowBatteryThresholdPercent = 0;
        _confirmedLowBatteryThresholdPercent = 0;
        _canSetLowBatteryThreshold = false;
        _batteryText = "--";
        _batteryChemistryIndex = -1;
        _confirmedBatteryChemistryIndex = -1;
        _pollingRateText = "--";
        _pollingRateIndex = -1;
        _confirmedPollingRateIndex = -1;
        _canSetPollingRate = false;
        _dpiText = "--";
        _dpiXValue = 0;
        _dpiYValue = 0;
        _confirmedDpiXValue = 0;
        _confirmedDpiYValue = 0;
        _canSetDpi = false;
        _idleText = "--";
        _idleMinutesValue = 0;
        _confirmedIdleMinutesValue = 0;
        _canSetIdle = false;
        DpiStages.Clear();
        _dpiStageCount = 0;
        _activeDpiStage = 0;
        _confirmedDpiStages = null;
        _canSetDpiStages = false;
        OnPropertyChanged(string.Empty);
    }

    internal void RefreshDeviceRow()
    {
        OnPropertyChanged(nameof(ProtocolText));
        OnPropertyChanged(nameof(IdentityText));
    }

    internal void RefreshLocalization()
    {
        OnPropertyChanged(string.Empty);
    }

    private ProfileDocument Profile => _getProfile();

    private Task RunDeviceOperationAsync(
        string label,
        Func<Task> operation,
        CancellationToken cancellationToken,
        Action? restoreSelection = null) =>
        _runDeviceOperation(label, operation, cancellationToken, restoreSelection, null, null);

    private void SetDpiStages(ViperDpiStagesTelemetry stages, bool confirm = true)
    {
        DpiStages.Clear();
        foreach (var stage in stages.Stages)
        {
            DpiStages.Add(new(stage.Number, stage.X, stage.Y));
        }

        _dpiStageCount = DpiStages.Count;
        _activeDpiStage = Math.Clamp(stages.ActiveStage, 1, Math.Max(1, DpiStages.Count));
        _dpiStagesText = AppStrings.FormatText(
            "DpiStageSummary",
            stages.ActiveStage,
            stages.Stages.Count,
            string.Join(", ", stages.Stages.Select(stage => $"{stage.X}x{stage.Y}")));
        if (confirm)
        {
            _confirmedDpiStages = CopyDpiStages(stages);
        }

        OnPropertyChanged(nameof(DpiStageCount));
        OnPropertyChanged(nameof(ActiveDpiStage));
        OnPropertyChanged(nameof(DpiStagesText));
    }

    private void ResizeDpiStages(int count)
    {
        if (DpiStages.Count == 0)
        {
            _dpiStageCount = 0;
            OnPropertyChanged(nameof(DpiStageCount));
            return;
        }

        while (DpiStages.Count > count)
        {
            DpiStages.RemoveAt(DpiStages.Count - 1);
        }
        while (DpiStages.Count < count)
        {
            var previous = DpiStages[^1];
            DpiStages.Add(new(
                DpiStages.Count + 1,
                checked((int)previous.X),
                checked((int)previous.Y)));
        }

        _dpiStageCount = count;
        _activeDpiStage = Math.Min(_activeDpiStage, count);
        OnPropertyChanged(nameof(DpiStageCount));
        OnPropertyChanged(nameof(ActiveDpiStage));
    }

    private void RestoreDpiStages()
    {
        if (_confirmedDpiStages is { } confirmed)
        {
            SetDpiStages(confirmed, confirm: false);
        }
    }

    private string CreateMappingFingerprint(
        string devicePath,
        IReadOnlyList<ViperButtonAssignmentProfile>? assignments) =>
        string.Join('|',
            Profile.ActiveProfileName,
            devicePath,
            assignments is null
                ? "unmanaged"
                : string.Join(';', assignments
                    .OrderBy(item => item.ButtonId)
                    .ThenBy(item => item.Layer)
                    .Select(item => $"{item.ProfileId:X2}:{item.ButtonId:X2}:{(byte)item.Layer:X2}:{(byte)item.Function:X2}:{Convert.ToHexString(item.FunctionData.ToArray())}")));

    private static ViperButtonAssignmentProfile ToProfileAssignment(ViperButtonAssignment assignment) => new()
    {
        ProfileId = assignment.ProfileId,
        ButtonId = assignment.ButtonId,
        Layer = assignment.Layer,
        Function = assignment.Function,
        FunctionData = assignment.FunctionData.ToList(),
    };

    private static ViperButtonAssignment ToDeviceAssignment(ViperButtonAssignmentProfile assignment) => new(
        assignment.ProfileId,
        assignment.ButtonId,
        assignment.Layer,
        assignment.Function,
        assignment.FunctionData.ToArray());

    private static ViperDpiStagesTelemetry CopyDpiStages(ViperDpiStagesTelemetry stages) =>
        new(stages.ActiveStage, stages.Stages.ToArray());

    private static string FormatDuration(int seconds) => seconds switch
    {
        < 60 => AppStrings.FormatText("DurationSeconds", seconds),
        _ when seconds % 60 == 0 => AppStrings.FormatText("DurationMinutes", seconds / 60),
        _ => AppStrings.FormatText("DurationMinutesSeconds", seconds / 60, seconds % 60),
    };

    private static bool IsExpectedRuntimeException(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException;

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
