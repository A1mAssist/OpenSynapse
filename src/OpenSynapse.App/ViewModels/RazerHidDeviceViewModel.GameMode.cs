using Microsoft.UI.Xaml;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private bool _isGameModeBusy;
    private bool _gameModeEnabled;
    private bool _gameModeKnown;

    public Visibility GameModeVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.GameModeRead));
    public Visibility GameModeWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.GameModeRead) &&
            Has(RazerHidBackendCapability.GameModeWrite));
    public bool IsGameModeBusy
    {
        get => _isGameModeBusy;
        private set
        {
            if (!SetField(ref _isGameModeBusy, value)) return;
            OnPropertyChanged(nameof(CanEditGameMode));
        }
    }
    public bool GameModeEnabled
    {
        get => _gameModeEnabled;
        set
        {
            if (!SetField(ref _gameModeEnabled, value)) return;
            OnPropertyChanged(nameof(GameModeText));
        }
    }
    public bool CanEditGameMode => CanUse(RazerHidBackendCapability.GameModeWrite) &&
        _gameModeKnown && !IsGameModeBusy;
    public string GameModeText => !_gameModeKnown
        ? "--"
        : AppStrings.Text(GameModeEnabled ? "Text_RazerHidAutoSleepOn" : "Text_RazerHidAutoSleepOff");

    public async Task LoadGameModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(RazerHidBackendCapability.GameModeRead) || IsGameModeBusy)
        {
            return;
        }

        IsGameModeBusy = true;
        try
        {
            await ReadGameModeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // A device that rejects the read must not keep a writable toggle.
            _unsupported.Add(RazerHidBackendCapability.GameModeWrite);
            HandleOptionalFailure(exception, RazerHidBackendCapability.GameModeRead);
        }
        finally
        {
            IsGameModeBusy = false;
        }
    }

    [NotProfileScoped]
    public Task ApplyGameModeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.GameModeWrite,
            () => IsGameModeBusy, value => IsGameModeBusy = value,
            async () =>
            {
                if (!_gameModeKnown) return;
                var requested = GameModeEnabled;
                var previous = _gameModeEnabled;
                try
                {
                    await _service.SetGameModeAsync(Connection, requested, cancellationToken);
                    if (Has(RazerHidBackendCapability.GameModeRead))
                    {
                        await ReadGameModeAsync(cancellationToken);
                        if (GameModeEnabled != requested)
                        {
                            throw new InvalidOperationException(
                                "The game-mode readback did not match the requested state.");
                        }
                    }
                }
                catch (Exception updateException)
                {
                    try
                    {
                        await _service.SetGameModeAsync(Connection, previous, cancellationToken);
                        if (Has(RazerHidBackendCapability.GameModeRead))
                        {
                            await ReadGameModeAsync(cancellationToken);
                        }
                    }
                    catch (Exception restoreException)
                    {
                        throw new InvalidOperationException(
                            "Game-mode update failed and the previous state could not be restored.",
                            new AggregateException(updateException, restoreException));
                    }

                    throw;
                }
            }, cancellationToken);

    private async Task ReadGameModeAsync(CancellationToken cancellationToken)
    {
        var state = await _service.GetGameModeAsync(Connection, cancellationToken);
        _gameModeEnabled = state.Enabled;
        _gameModeKnown = true;
        OnPropertyChanged(nameof(GameModeEnabled));
        OnPropertyChanged(nameof(GameModeText));
        OnPropertyChanged(nameof(CanEditGameMode));
    }
}
