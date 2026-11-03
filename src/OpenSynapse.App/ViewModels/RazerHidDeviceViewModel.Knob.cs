using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    // The keyboard middleware addresses the knob as LED class 3 / id 0x24.
    private const byte KnobLedClass = 0x03;
    private const byte KnobLedId = 0x24;
    private bool _isKnobBusy;
    private RazerKnobStage? _knobCurrentStage;

    public ObservableCollection<RazerHidKnobStageRowViewModel> KnobStages { get; } = new();
    public Visibility KnobVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.KnobRead, RazerHidBackendCapability.KnobWrite));
    public Visibility KnobWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.KnobWrite));
    public string KnobCurrentModeText =>
        _knobCurrentStage is { } stage ? FormatKnobMode(stage.Mode) : "--";
    public bool IsKnobBusy
    {
        get => _isKnobBusy;
        private set
        {
            if (!SetField(ref _isKnobBusy, value)) return;
            OnPropertyChanged(nameof(CanEditKnob));
            OnPropertyChanged(nameof(CanApplyKnob));
        }
    }
    public bool CanEditKnob => CanUse(RazerHidBackendCapability.KnobWrite) && !IsKnobBusy;
    public bool CanApplyKnob => CanEditKnob && KnobStages.Count > 0;

    public async Task LoadKnobAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(RazerHidBackendCapability.KnobRead) || IsKnobBusy)
        {
            return;
        }

        IsKnobBusy = true;
        try
        {
            await RefreshKnobAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleOptionalFailure(exception, RazerHidBackendCapability.KnobRead);
        }
        finally
        {
            IsKnobBusy = false;
        }
    }

    public Task ApplyKnobAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.KnobWrite,
            () => IsKnobBusy, value => IsKnobBusy = value,
            async () =>
            {
                // Without a successful read there are no stages to preserve; never overwrite
                // the device's existing configuration with an empty table.
                if (KnobStages.Count == 0)
                {
                    return;
                }

                var stages = KnobStages.Select(row => row.ToStage()).ToArray();
                await _service.SetKnobConfigurationAsync(
                    Connection, KnobLedClass, KnobLedId, stages, cancellationToken);
                if (_knobCurrentStage is { } currentStage)
                {
                    var activeRow = KnobStages.FirstOrDefault(row => row.Stage == currentStage.Stage);
                    if (activeRow is not null)
                    {
                        await _service.SetKnobModeAsync(
                            Connection, KnobLedClass, KnobLedId, activeRow.ToStage(), cancellationToken);
                    }
                }
                if (Has(RazerHidBackendCapability.KnobRead))
                {
                    await RefreshKnobAsync(cancellationToken);
                }
            }, cancellationToken);

    private async Task RefreshKnobAsync(CancellationToken cancellationToken)
    {
        var configuration = await _service.GetKnobConfigurationAsync(
            Connection, KnobLedClass, KnobLedId, cancellationToken);
        KnobStages.Clear();
        foreach (var stage in configuration.Stages)
        {
            KnobStages.Add(new RazerHidKnobStageRowViewModel(stage));
        }
        OnPropertyChanged(nameof(CanApplyKnob));

        var mode = await _service.GetKnobModeAsync(Connection, KnobLedClass, KnobLedId, cancellationToken);
        _knobCurrentStage = mode.Stage;
        OnPropertyChanged(nameof(KnobCurrentModeText));
    }

    private static string FormatKnobMode(RazerKnobMode mode)
    {
        var modes = Enum.GetValues<RazerKnobMode>();
        return modes.Contains(mode)
            ? AppStrings.Text($"RazerHidKnobMode{mode}")
            : ((byte)mode).ToString(CultureInfo.InvariantCulture);
    }
}

public sealed class RazerHidKnobStageRowViewModel : INotifyPropertyChanged
{
    private static readonly RazerKnobMode[] Modes = Enum.GetValues<RazerKnobMode>();
    private string[] _modeOptions = BuildModeOptions();
    private RazerKnobMode _mode;

    public RazerHidKnobStageRowViewModel(RazerKnobStage stage)
    {
        Disabled = stage.Disabled;
        Stage = stage.Stage;
        _mode = stage.Mode;
        Red = stage.Red;
        Green = stage.Green;
        Blue = stage.Blue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte Disabled { get; }
    public byte Stage { get; }
    public IReadOnlyList<string> ModeOptions => _modeOptions;
    public int ModeIndex
    {
        get => Array.IndexOf(Modes, _mode);
        set
        {
            if (value >= 0 && value < Modes.Length)
            {
                _mode = Modes[value];
            }
        }
    }
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }

    public void RefreshLocalization()
    {
        _modeOptions = BuildModeOptions();
        PropertyChanged?.Invoke(this, new(string.Empty));
    }

    public RazerKnobStage ToStage() => new(Disabled, Stage, _mode, Red, Green, Blue);

    private static string[] BuildModeOptions() => Modes
        .Select(mode => AppStrings.Text($"RazerHidKnobMode{mode}"))
        .ToArray();
}
