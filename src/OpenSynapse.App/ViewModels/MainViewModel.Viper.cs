using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task ApplyViperPollingRateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperPollingRate)
        {
            return;
        }

        var hertz = ViperPollingRateIndex switch
        {
            0 => 125,
            1 => 500,
            2 => 1000,
            _ => 0,
        };
        await RunDeviceOperationAsync(AppStrings.Text("Text_CAEC7015"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetViperPollingRateAsync(_deviceDescriptors, hertz, cancellationToken);
            ViperPollingRateText = $"{actual} Hz";
            ViperPollingRateIndex = actual switch { 125 => 0, 500 => 1, _ => 2 };
            _viper._confirmedViperPollingRateIndex = ViperPollingRateIndex;
            _profile.Global.Viper.PollingRateHertz = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () => ViperPollingRateIndex = _viper._confirmedViperPollingRateIndex);
    }

    public async Task ApplyViperDpiAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperDpi)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_25083B1F"), async () =>
        {
            var x = checked((int)Math.Round(ViperDpiXValue, MidpointRounding.AwayFromZero));
            var y = checked((int)Math.Round(ViperDpiYValue, MidpointRounding.AwayFromZero));
            var actual = await _deviceTelemetryReader.SetViperDpiAsync(_deviceDescriptors, x, y, cancellationToken);
            ViperDpiXValue = actual.X;
            ViperDpiYValue = actual.Y;
            _viper._confirmedViperDpiXValue = actual.X;
            _viper._confirmedViperDpiYValue = actual.Y;
            ViperDpiText = $"{actual.X} × {actual.Y}";
            _profile.Global.Viper.DpiX = actual.X;
            _profile.Global.Viper.DpiY = actual.Y;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
        {
            ViperDpiXValue = _viper._confirmedViperDpiXValue;
            ViperDpiYValue = _viper._confirmedViperDpiYValue;
        });
    }

    public async Task ApplyViperIdleAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperIdle)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_C41C6FC1"), async () =>
        {
            var minutes = checked((int)Math.Round(ViperIdleMinutesValue, MidpointRounding.AwayFromZero));
            var seconds = checked(minutes * 60);
            var actual = await _deviceTelemetryReader.SetViperIdleSecondsAsync(_deviceDescriptors, seconds, cancellationToken);
            ViperIdleMinutesValue = actual / 60d;
            _viper._confirmedViperIdleMinutesValue = ViperIdleMinutesValue;
            ViperIdleText = FormatDuration(actual);
            _profile.Global.Viper.IdleSeconds = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () => ViperIdleMinutesValue = _viper._confirmedViperIdleMinutesValue);
    }

    public async Task ApplyViperBatteryChemistryAsync(CancellationToken cancellationToken = default)
    {
        if (ViperBatteryChemistryIndex is < 0 or > 2)
        {
            return;
        }

        var chemistry = checked((byte)ViperBatteryChemistryIndex);
        await RunDeviceOperationAsync(AppStrings.Text("Text_94C31F83"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetViperBatteryChemistryAsync(
                _deviceDescriptors,
                chemistry,
                cancellationToken);
            ViperBatteryChemistryIndex = actual;
            _profile.Global.Viper.BatteryChemistry = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task ApplyViperDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperDpiStages || ViperDpiStages.Count is < 1 or > 5)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_7F983785"), async () =>
        {
            var previousProfile = _profile.Clone();
            var requested = new ViperDpiStagesTelemetry(
                checked((byte)ViperActiveDpiStage),
                ViperDpiStages.Select(row => new ViperDpiStageTelemetry(
                    checked((byte)row.Number), checked((int)row.X), checked((int)row.Y))).ToArray());
            var actual = await _deviceTelemetryReader.SetViperDpiStagesAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetViperDpiStages(actual);
            _profile.Global.Viper.DpiStages = new ViperDpiStagesProfile
            {
                ActiveStage = actual.ActiveStage,
                Stages = actual.Stages.Select(stage => new ViperDpiStageProfile
                {
                    Number = stage.Number,
                    X = stage.X,
                    Y = stage.Y,
                }).ToList(),
            };
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_86504D84"));
            }
        }, cancellationToken, RestoreViperDpiStages);
    }

    public async Task ReadViperButtonMappingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanReadViperButtonMappings)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_D9C12104"), async () =>
        {
            var previousProfileAssignments = _profile.Global.Viper.ButtonAssignments;
            var assignments = await _deviceTelemetryReader.ReadViperButtonAssignmentsAsync(
                _deviceDescriptors, cancellationToken);
            SetViperButtonAssignments(assignments);
            _profile.Global.Viper.ButtonAssignments = assignments
                .Select(ToProfileAssignment)
                .ToList();
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile.Global.Viper.ButtonAssignments = previousProfileAssignments;
                throw new InvalidOperationException(AppStrings.Text(
                    "ViperMappingReadProfileSaveFailed"));
            }
        }, cancellationToken, () =>
        {
            ViperButtonAssignments.Clear();
            OnPropertyChanged(nameof(VisibleViperButtonAssignments));
            _viper._canSetViperButtonMappings = false;
            ViperButtonMappingsText = AppStrings.Text("Text_6320369E");
            OnPropertyChanged(nameof(CanSetViperButtonMappings));
        });
    }

    public async Task ApplyViperButtonMappingAsync(
        ViperButtonAssignmentRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!CanSetViperButtonMappings || !row.CanApply)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_D9C12104"), async () =>
        {
            var previous = row.Assignment;
            var previousProfileAssignments = _profile.Global.Viper.ButtonAssignments;
            var actual = await _deviceTelemetryReader.SetViperButtonAssignmentAsync(
                _deviceDescriptors, row.CreateAssignment(), cancellationToken);
            row.Apply(actual);
            _profile.Global.Viper.ButtonAssignments = ViperButtonAssignments
                .Select(item => ToProfileAssignment(item.Assignment))
                .ToList();
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile.Global.Viper.ButtonAssignments = previousProfileAssignments;
                var restored = await _deviceTelemetryReader.SetViperButtonAssignmentAsync(
                    _deviceDescriptors, previous, CancellationToken.None);
                row.Apply(restored);
                throw new InvalidOperationException(AppStrings.Text(
                    "ViperMappingSaveFailedRestored"));
            }
        }, cancellationToken, row.RestoreSelection);
    }

    public async Task ApplyAllViperButtonMappingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperButtonMappings)
        {
            return;
        }

        if (!ViperButtonAssignments.Any(row => row.CanApply))
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_D9C12104"), async () =>
        {
            var previous = ViperButtonAssignments.Select(row => row.Assignment).ToArray();
            var previousProfileAssignments = _profile.Global.Viper.ButtonAssignments;
            var requested = ViperButtonAssignments.Select(row => row.CreateAssignment()).ToArray();
            var actual = await _deviceTelemetryReader.SetViperButtonAssignmentsAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetViperButtonAssignments(actual);
            _profile.Global.Viper.ButtonAssignments = actual
                .Select(ToProfileAssignment)
                .ToList();
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile.Global.Viper.ButtonAssignments = previousProfileAssignments;
                var restored = await _deviceTelemetryReader.SetViperButtonAssignmentsAsync(
                    _deviceDescriptors, previous, CancellationToken.None);
                SetViperButtonAssignments(restored);
                throw new InvalidOperationException(AppStrings.Text(
                    "ViperMappingBatchSaveFailedRestored"));
            }
        }, cancellationToken, () =>
        {
            foreach (var row in ViperButtonAssignments.Where(row => row.CanApply))
            {
                row.RestoreSelection();
            }
        });
    }

    private void SetViperButtonAssignments(IReadOnlyList<ViperButtonAssignment> assignments)
    {
        ViperButtonAssignments.Clear();
        foreach (var assignment in assignments
            .OrderBy(item => item.ButtonId)
            .ThenBy(item => item.Layer))
        {
            ViperButtonAssignments.Add(new(assignment));
        }
        OnPropertyChanged(nameof(VisibleViperButtonAssignments));
        _viper._canSetViperButtonMappings = assignments.Count == 16;
        ViperButtonMappingsText = _viper._canSetViperButtonMappings
            ? AppStrings.Text("Text_0913960E")
            : AppStrings.FormatText("MappingReadIncomplete", assignments.Count);
        OnPropertyChanged(nameof(CanSetViperButtonMappings));
    }

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

    public string ViperDeviceName { get => _viper._viperDeviceName; private set => SetField(ref _viper._viperDeviceName, value); }
    public Visibility ViperDeviceVisibility { get => _viper._viperDeviceVisibility; private set => SetField(ref _viper._viperDeviceVisibility, value); }
    public string ViperStatusText { get => _viper._viperStatusText; private set => SetField(ref _viper._viperStatusText, value); }
    public string ViperBatteryText { get => _viper._viperBatteryText; private set => SetField(ref _viper._viperBatteryText, value); }
    public int ViperBatteryChemistryIndex { get => _viper._viperBatteryChemistryIndex; set => SetField(ref _viper._viperBatteryChemistryIndex, value); }
    public IReadOnlyList<string> ViperBatteryChemistryOptions => AppStrings.Texts("Text_E43748D4", "Text_DC5115A1", "Text_54C45B90");
    public bool CanSetViperBatteryChemistry { get => _viper._canSetViperBatteryChemistry; private set => SetField(ref _viper._canSetViperBatteryChemistry, value); }
    public string ViperPollingRateText { get => _viper._viperPollingRateText; private set => SetField(ref _viper._viperPollingRateText, value); }
    public int ViperPollingRateIndex { get => _viper._viperPollingRateIndex; set => SetField(ref _viper._viperPollingRateIndex, value); }
    public bool CanSetViperPollingRate { get => _viper._canSetViperPollingRate; private set => SetField(ref _viper._canSetViperPollingRate, value); }
    public string ViperDpiText { get => _viper._viperDpiText; private set => SetField(ref _viper._viperDpiText, value); }
    public double ViperDpiXValue { get => _viper._viperDpiXValue; set => SetField(ref _viper._viperDpiXValue, value); }
    public double ViperDpiYValue { get => _viper._viperDpiYValue; set => SetField(ref _viper._viperDpiYValue, value); }
    public bool CanSetViperDpi { get => _viper._canSetViperDpi; private set => SetField(ref _viper._canSetViperDpi, value); }
    public string ViperIdleText { get => _viper._viperIdleText; private set => SetField(ref _viper._viperIdleText, value); }
    public string ViperDpiStagesText { get => _viper._viperDpiStagesText; private set => SetField(ref _viper._viperDpiStagesText, value); }
    public string ViperLowBatteryThresholdText { get => _viper._viperLowBatteryThresholdText; private set => SetField(ref _viper._viperLowBatteryThresholdText, value); }
    public double ViperIdleMinutesValue { get => _viper._viperIdleMinutesValue; set => SetField(ref _viper._viperIdleMinutesValue, value); }
    public bool CanSetViperIdle { get => _viper._canSetViperIdle; private set => SetField(ref _viper._canSetViperIdle, value); }
    public ObservableCollection<ViperDpiStageRowViewModel> ViperDpiStages => _viper.ViperDpiStages;
    public int ViperDpiStageCount
    {
        get => _viper._viperDpiStageCount;
        set => ResizeViperDpiStages(Math.Clamp(value, 1, 5));
    }
    public int ViperActiveDpiStage
    {
        get => _viper._viperActiveDpiStage;
        set => SetField(ref _viper._viperActiveDpiStage, Math.Clamp(value, 1, Math.Max(1, ViperDpiStages.Count)));
    }
    public bool CanSetViperDpiStages { get => _viper._canSetViperDpiStages; private set => SetField(ref _viper._canSetViperDpiStages, value); }
    public string ViperButtonMappingsText { get => _viper._viperButtonMappingsText; private set => SetField(ref _viper._viperButtonMappingsText, value); }
    public ObservableCollection<ViperButtonAssignmentRowViewModel> ViperButtonAssignments => _viper.ViperButtonAssignments;
    public IReadOnlyList<string> ViperButtonMappingLayerOptions => AppStrings.Texts("Text_5F541189", "Text_0670E591");
    public int ViperButtonMappingLayerIndex
    {
        get => _viper._viperButtonMappingLayerIndex;
        set
        {
            if (SetField(ref _viper._viperButtonMappingLayerIndex, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(VisibleViperButtonAssignments));
            }
        }
    }
    public IReadOnlyList<ViperButtonAssignmentRowViewModel> VisibleViperButtonAssignments =>
        ViperButtonAssignments
            .Where(row => (int)row.Assignment.Layer == ViperButtonMappingLayerIndex)
            .ToArray();
    public bool CanReadViperButtonMappings => _viper._canReadViperButtonMappings;
    public bool CanSetViperButtonMappings => _viper._canSetViperButtonMappings;
}
