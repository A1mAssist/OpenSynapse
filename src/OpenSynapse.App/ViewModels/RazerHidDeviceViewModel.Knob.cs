using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private const byte KnobId = RazerKnobProtocol.KnobId;
    private bool _isKnobBusy;
    private RazerKnobStage? _knobCurrentStage;
    private RazerHidKnobStageRowViewModel? _selectedKnobStage;

    private RazerKnobModeTable? KnobModeTable =>
        RazerKnobProtocol.GetModeTable(Connection.Definition.ProductId);

    public ObservableCollection<RazerHidKnobStageRowViewModel> KnobStages { get; } = new();
    public Visibility KnobVisibility => VisibleWhen(KnobModeTable is not null && HasAny(
        RazerHidBackendCapability.KnobRead, RazerHidBackendCapability.KnobWrite));
    public Visibility KnobWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.KnobWrite));
    public RazerHidKnobStageRowViewModel? SelectedKnobStage
    {
        get => _selectedKnobStage;
        set => SetField(ref _selectedKnobStage, value);
    }
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

    [ProfileSetting("keyboard.knob", DeviceCategory.Keyboard)]
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

                var previousStages = KnobStages.Select(row => row.ToStage()).ToArray();
                var previousStage = _knobCurrentStage;
                var previousProfile = ReadProfile(settings => settings.Keyboard.KnobMode);
                var target = SelectedKnobStage;
                var stages = KnobStages.Select(row => row.ToStage()).ToArray();
                var table = KnobModeTable;
                if (table is null)
                {
                    return;
                }
                // Resolve once: the rollback must write back to the same slot even if
                // the device switched profiles while this apply was in flight.
                var profileId = await _service.ResolveActiveProfileIdAsync(Connection, cancellationToken);
                await _service.SetKnobConfigurationAsync(
                    Connection, profileId, KnobId, stages, cancellationToken);
                await ApplyKnobStateAsync(
                    profileId, table, stages,
                    target is { IsEnabled: true } ? target.ModeId : null,
                    cancellationToken);
                if (target is { IsEnabled: true } selected)
                {
                    await _service.SetKnobModeAsync(
                        Connection, profileId, KnobId, selected.ToStage(), cancellationToken);
                }
                if (Has(RazerHidBackendCapability.KnobRead))
                {
                    await RefreshKnobAsync(cancellationToken, profileId);
                }
                if (target is { } applied)
                {
                    await SaveProfileAsync(
                        settings => settings.Keyboard.KnobMode = applied.ModeId,
                        settings => settings.Keyboard.KnobMode = previousProfile,
                        null,
                        async token =>
                        {
                            await _service.SetKnobConfigurationAsync(
                                Connection, profileId, KnobId, previousStages, token);
                            await ApplyKnobStateAsync(
                                profileId, table, previousStages,
                                previousStage?.ModeId,
                                token);
                            if (previousStage is { } previous)
                            {
                                await _service.SetKnobModeAsync(
                                    Connection, profileId, KnobId, previous, token);
                            }
                        },
                        cancellationToken);
                }
            }, cancellationToken);

    private async Task RefreshKnobAsync(CancellationToken cancellationToken, byte? profileId = null)
    {
        var table = KnobModeTable;
        if (table is null)
        {
            return;
        }

        profileId ??= await _service.ResolveActiveProfileIdAsync(Connection, cancellationToken);
        var configuration = await _service.GetKnobConfigurationAsync(
            Connection, profileId.Value, KnobId, cancellationToken);
        KnobStages.Clear();
        foreach (var stage in configuration.Stages)
        {
            KnobStages.Add(new RazerHidKnobStageRowViewModel(stage, table));
        }
        OnPropertyChanged(nameof(CanApplyKnob));

        if (Has(RazerHidBackendCapability.OledRead))
        {
            var modeState = await _service.GetKnobModeStateAsync(
                Connection, profileId.Value, cancellationToken);
            foreach (var row in KnobStages)
            {
                row.IsEnabled = row.StateFlagIndex < modeState.Flags.Count &&
                    modeState.Flags[row.StateFlagIndex] == 1;
            }

            var active = await _service.GetKnobActiveModeAsync(
                Connection, profileId.Value, cancellationToken);
            SelectedKnobStage = KnobStages.FirstOrDefault(row =>
                row.ModeId == active.ModeId && row.IsEnabled)
                ?? KnobStages.FirstOrDefault(row => row.ModeId == active.ModeId)
                ?? KnobStages.FirstOrDefault(row => row.IsEnabled)
                ?? KnobStages.FirstOrDefault();
            _knobCurrentStage = SelectedKnobStage?.ToStage();
            return;
        }

        var mode = await _service.GetKnobModeAsync(
            Connection, profileId.Value, KnobId, cancellationToken);
        _knobCurrentStage = mode.Stage;
        SelectedKnobStage = KnobStages.FirstOrDefault(row => row.Stage == mode.Stage.Stage)
            ?? KnobStages.FirstOrDefault();
    }

    private async Task ApplyKnobStateAsync(
        byte profileId,
        RazerKnobModeTable table,
        IReadOnlyList<RazerKnobStage> stages,
        byte? activeModeId,
        CancellationToken cancellationToken)
    {
        if (!Has(RazerHidBackendCapability.OledWrite))
        {
            return;
        }

        var flags = RazerKnobProtocol.CreateModeStateFlags(table, stages);
        await _service.SetKnobModeStateAsync(
            Connection, profileId, flags, cancellationToken);
        if (activeModeId is not { } modeId ||
            RazerKnobProtocol.FindActiveMode(table, stages, modeId) is not { } active)
        {
            return;
        }

        await _service.SetKnobActiveModeAsync(
            Connection, profileId, active, cancellationToken);
    }

}

public sealed class RazerHidKnobStageRowViewModel : INotifyPropertyChanged
{
    private readonly RazerKnobModeTable _table;
    private string[] _modeOptions;
    private byte _modeId;

    public RazerHidKnobStageRowViewModel(RazerKnobStage stage, RazerKnobModeTable table)
    {
        _table = table;
        Disabled = stage.Disabled;
        Stage = stage.Stage;
        _modeId = stage.ModeId;
        Red = stage.Red;
        Green = stage.Green;
        Blue = stage.Blue;
        _modeOptions = BuildModeOptions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte Disabled { get; private set; }
    public bool IsEnabled
    {
        get => Disabled == 0;
        set
        {
            var disabled = value ? (byte)0 : (byte)1;
            if (Disabled == disabled) return;
            Disabled = disabled;
            PropertyChanged?.Invoke(this, new(nameof(IsEnabled)));
        }
    }
    public byte Stage { get; }
    public byte ModeId => _modeId;
    public byte StateFlagIndex => _table.Find(_modeId)?.StateFlagIndex ?? byte.MaxValue;
    public string ModeName => _table.Find(_modeId) is { } mode
        ? AppStrings.Text(mode.ResourceKey)
        : _modeId.ToString();
    public string DisplayName => $"{Stage} · {ModeName}";
    public IReadOnlyList<string> ModeOptions => _modeOptions;
    public int ModeIndex
    {
        get
        {
            for (var index = 0; index < _table.Modes.Count; index++)
            {
                if (_table.Modes[index].ModeId == _modeId)
                {
                    return index;
                }
            }

            return -1;
        }
        set
        {
            if (value >= 0 && value < _table.Modes.Count)
            {
                _modeId = _table.Modes[value].ModeId;
                PropertyChanged?.Invoke(this, new(nameof(ModeName)));
                PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
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

    public RazerKnobStage ToStage() => new(Disabled, Stage, _modeId, Red, Green, Blue);

    private string[] BuildModeOptions() => _table.Modes
        .Select(mode => AppStrings.Text(mode.ResourceKey))
        .ToArray();
}
