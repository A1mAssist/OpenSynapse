using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// One of the six fixed Hapticore scroll stages. The id is the protocol value (129-134),
/// not a position; the stage list order never changes.
/// </summary>
public sealed class OpenRazerHapticScrollStageRowViewModel(byte id, string name, bool isActive)
    : INotifyPropertyChanged
{
    private bool _isActive = isActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    public byte Id { get; } = id;
    public string Name { get; } = name;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
}

public sealed partial class OpenRazerDeviceViewModel
{
    // The middleware writes profile 1 by default; the UI never offers a profile picker.
    private const byte HapticScrollProfileId = 0x01;

    private bool _isHapticScrollBusy;
    private int _selectedHapticScrollStageIndex;

    public Visibility HapticScrollVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.HapticScrollRead, OpenRazerBackendCapability.HapticScrollWrite));
    public Visibility HapticScrollWriteVisibility =>
        VisibleWhen(Has(OpenRazerBackendCapability.HapticScrollWrite));

    public ObservableCollection<OpenRazerHapticScrollStageRowViewModel> HapticScrollStages { get; } =
        new(StageDefinitions());

    public IReadOnlyList<string> HapticScrollStageNames { get; } =
        StageDefinitions().Select(stage => stage.Name).ToArray();

    public int SelectedHapticScrollStageIndex
    {
        get => _selectedHapticScrollStageIndex;
        set => SetField(ref _selectedHapticScrollStageIndex, Math.Clamp(value, 0, 5));
    }

    public bool IsHapticScrollBusy
    {
        get => _isHapticScrollBusy;
        private set => SetBusy(ref _isHapticScrollBusy, value,
            nameof(CanEditHapticScroll), nameof(CanApplyHapticScroll));
    }

    public bool CanEditHapticScroll => CanUse(OpenRazerBackendCapability.HapticScrollWrite);
    public bool CanApplyHapticScroll =>
        CanUse(OpenRazerBackendCapability.HapticScrollWrite) && !IsHapticScrollBusy;

    public async Task LoadHapticScrollAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(OpenRazerBackendCapability.HapticScrollRead) || IsHapticScrollBusy)
        {
            return;
        }

        IsHapticScrollBusy = true;
        try
        {
            var state = await _service
                .GetHapticScrollStagesAsync(Connection, HapticScrollProfileId, cancellationToken)
                .ConfigureAwait(false);
            ApplyHapticScrollState(state);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, OpenRazerBackendCapability.HapticScrollRead);
        }
        finally
        {
            IsHapticScrollBusy = false;
        }
    }

    public Task ApplyHapticScrollAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.HapticScrollWrite,
            () => IsHapticScrollBusy, value => IsHapticScrollBusy = value,
            async () =>
            {
                var stageIds = HapticScrollStages
                    .Where(stage => stage.IsActive)
                    .Select(stage => stage.Id)
                    .ToArray();
                await _service.SetHapticScrollStagesAsync(
                    Connection,
                    HapticScrollProfileId,
                    (byte)(SelectedHapticScrollStageIndex + 1),
                    stageIds,
                    cancellationToken).ConfigureAwait(false);
                if (Has(OpenRazerBackendCapability.HapticScrollRead))
                {
                    ApplyHapticScrollState(await _service
                        .GetHapticScrollStagesAsync(Connection, HapticScrollProfileId, cancellationToken)
                        .ConfigureAwait(false));
                }
            }, cancellationToken);

    private void ApplyHapticScrollState(
        OpenSynapse.Windows.Protocols.RazerHapticoreScrollStageInformationState state)
    {
        // Unfilled slots come back as zero, so only the ids the device actually stored count
        // as enabled.
        var active = state.StageTypes.ToHashSet();
        foreach (var stage in HapticScrollStages)
        {
            stage.IsActive = active.Contains(
                (OpenSynapse.Windows.Protocols.RazerHapticoreScrollStageType)stage.Id);
        }

        // ActiveStage is a 1-based position in the full six-stage list.
        SelectedHapticScrollStageIndex = Math.Clamp(state.ActiveStage - 1, 0, 5);
    }

    private static OpenRazerHapticScrollStageRowViewModel[] StageDefinitions() =>
    [
        new(OpenRazerHapticScrollStageIds.Standard, AppStrings.Text("OpenRazerHapticScrollStandard"), true),
        new(OpenRazerHapticScrollStageIds.Distinct, AppStrings.Text("OpenRazerHapticScrollDistinct"), true),
        new(OpenRazerHapticScrollStageIds.UltraFine, AppStrings.Text("OpenRazerHapticScrollUltraFine"), true),
        new(OpenRazerHapticScrollStageIds.Adaptive, AppStrings.Text("OpenRazerHapticScrollAdaptive"), true),
        new(OpenRazerHapticScrollStageIds.SmoothScroll, AppStrings.Text("OpenRazerHapticScrollSmooth"), true),
        new(OpenRazerHapticScrollStageIds.Custom, AppStrings.Text("OpenRazerHapticScrollCustom"), false),
    ];
}
