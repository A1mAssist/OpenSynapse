using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.App.ViewModels;

internal static class AccessoryProfileReplay
{
    [NotProfileScoped]
    public static async Task ApplyAsync(
        MainViewModel viewModel,
        LocalDiagnosticLog diagnosticLog,
        PowerSourceKind powerSource,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        Add(viewModel.CoolingPad.IsConnected, () =>
            viewModel.CoolingPad.ApplyPersistedProfileAsync(powerSource, cancellationToken));
        Add(viewModel.Kiyo.IsConnected, () =>
            viewModel.Kiyo.ApplyPersistedProfileAsync(powerSource, cancellationToken));
        Add(viewModel.Accessories.SelectedHeadsetAudio is not null, () =>
            viewModel.Accessories.SelectedHeadsetAudio!.ApplyPersistedProfileAsync(powerSource, cancellationToken));
        Add(viewModel.SelectedAwBleHeadset is not null, () =>
            viewModel.SelectedAwBleHeadset!.ApplyPersistedProfileAsync(powerSource, cancellationToken));
        Add(viewModel.SelectedBlackShark is not null, () =>
            viewModel.SelectedBlackShark!.ApplyPersistedProfileAsync(powerSource, cancellationToken));
        Add(viewModel.Accessories.SelectedGamepad is not null, () =>
            viewModel.Accessories.SelectedGamepad!.ApplyPersistedProfileAsync(powerSource, cancellationToken));
        Add(viewModel.Accessories.SelectedChroma is not null, () =>
            viewModel.Accessories.SelectedChroma!.ApplyPersistedProfileAsync(powerSource, cancellationToken));
        Add(viewModel.RazerHid.KrakenDevices.Count > 0, () =>
            viewModel.RazerHid.ApplyKrakenProfilesAsync(powerSource, cancellationToken));
        Add(viewModel.RazerHid.SelectedDevice?.Soundbar is not null, () =>
            viewModel.RazerHid.SelectedDevice!.Soundbar!.ApplyPersistedProfileAsync(powerSource, cancellationToken));

        await Task.WhenAll(tasks);

        void Add(bool condition, Func<Task> apply)
        {
            if (condition)
            {
                tasks.Add(ApplySafelyAsync(apply));
            }
        }

        async Task ApplySafelyAsync(Func<Task> apply)
        {
            try
            {
                await apply();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnosticLog.TryWrite("profile", $"accessory replay failed: {exception.Message}");
            }
        }
    }
}
