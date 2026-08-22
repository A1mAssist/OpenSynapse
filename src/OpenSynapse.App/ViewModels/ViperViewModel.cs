using System.Collections.ObjectModel;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

// Owns Viper-facing UI state; MainViewModel remains the hardware/profile coordinator.
internal sealed class ViperViewModel
{
    internal string _viperDeviceName = "Razer Viper V3 HyperSpeed";
    internal Microsoft.UI.Xaml.Visibility _viperDeviceVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    internal string _viperStatusText = "未发现";
    internal string _viperBatteryText = "--";
    internal string _viperPollingRateText = "--";
    internal int _viperPollingRateIndex = -1;
    internal int _confirmedViperPollingRateIndex = -1;
    internal bool _canSetViperPollingRate;
    internal string _viperDpiText = "--";
    internal double _viperDpiXValue;
    internal double _viperDpiYValue;
    internal double _confirmedViperDpiXValue;
    internal double _confirmedViperDpiYValue;
    internal bool _canSetViperDpi;
    internal string _viperIdleText = "--";
    internal string _viperDpiStagesText = "--";
    internal string _viperLowBatteryThresholdText = "--";
    internal double _viperIdleMinutesValue;
    internal double _confirmedViperIdleMinutesValue;
    internal bool _canSetViperIdle;
    internal int _viperDpiStageCount;
    internal int _viperActiveDpiStage;
    internal bool _canSetViperDpiStages;
    internal ViperDpiStagesTelemetry? _confirmedViperDpiStages;
    internal string _viperButtonMappingsText = "未读取";
    internal int _viperButtonMappingLayerIndex;
    internal bool _canReadViperButtonMappings;
    internal bool _canSetViperButtonMappings;
    internal string _viperMappingProfileFingerprint = string.Empty;
    internal ObservableCollection<ViperDpiStageRowViewModel> ViperDpiStages { get; } = new();
    internal ObservableCollection<ViperButtonAssignmentRowViewModel> ViperButtonAssignments { get; } = new();

    internal void SetDpiStages(ViperDpiStagesTelemetry stages, bool confirm = true)
    {
        ViperDpiStages.Clear();
        foreach (var stage in stages.Stages)
        {
            ViperDpiStages.Add(new(stage.Number, stage.X, stage.Y));
        }

        _viperDpiStageCount = ViperDpiStages.Count;
        _viperActiveDpiStage = Math.Clamp(stages.ActiveStage, 1, Math.Max(1, ViperDpiStages.Count));
        _viperDpiStagesText = AppStrings.FormatText(
            "DpiStageSummary",
            stages.ActiveStage,
            stages.Stages.Count,
            string.Join(", ", stages.Stages.Select(stage => $"{stage.X}x{stage.Y}")));
        if (confirm)
        {
            _confirmedViperDpiStages = CopyDpiStages(stages);
        }
    }

    internal void ResizeDpiStages(int count)
    {
        if (ViperDpiStages.Count == 0)
        {
            _viperDpiStageCount = 0;
            return;
        }

        while (ViperDpiStages.Count > count)
        {
            ViperDpiStages.RemoveAt(ViperDpiStages.Count - 1);
        }
        while (ViperDpiStages.Count < count)
        {
            var previous = ViperDpiStages[^1];
            ViperDpiStages.Add(new(
                ViperDpiStages.Count + 1,
                checked((int)previous.X),
                checked((int)previous.Y)));
        }

        _viperDpiStageCount = count;
        _viperActiveDpiStage = Math.Min(_viperActiveDpiStage, count);
    }

    internal void RestoreDpiStages()
    {
        if (_confirmedViperDpiStages is { } confirmed)
        {
            SetDpiStages(confirmed, confirm: false);
        }
    }

    internal void Reset()
    {
        _viperStatusText = "探测中";
        _viperDpiStagesText = "--";
        _viperLowBatteryThresholdText = "--";
        _viperBatteryText = "--";
        _viperPollingRateText = "--";
        _viperPollingRateIndex = -1;
        _confirmedViperPollingRateIndex = -1;
        _canSetViperPollingRate = false;
        _viperDpiText = "--";
        _viperDpiXValue = 0;
        _viperDpiYValue = 0;
        _confirmedViperDpiXValue = 0;
        _confirmedViperDpiYValue = 0;
        _canSetViperDpi = false;
        _viperIdleText = "--";
        _viperIdleMinutesValue = 0;
        _confirmedViperIdleMinutesValue = 0;
        _canSetViperIdle = false;
        ViperDpiStages.Clear();
        _viperDpiStageCount = 0;
        _viperActiveDpiStage = 0;
        _confirmedViperDpiStages = null;
        _canSetViperDpiStages = false;
        ViperButtonAssignments.Clear();
        _viperButtonMappingsText = "未读取";
        _canReadViperButtonMappings = false;
        _canSetViperButtonMappings = false;
    }

    private static ViperDpiStagesTelemetry CopyDpiStages(ViperDpiStagesTelemetry stages) =>
        new(stages.ActiveStage, stages.Stages.ToArray());
}
