using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    private readonly DispatcherTimer _equalizerPollTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _equalizerPolling;

    private void InitializeEqualizerPolling()
    {
        _equalizerPollTimer.Tick += EqualizerPollTick;
        Loaded += (_, _) => _equalizerPollTimer.Start();
        Unloaded += (_, _) => _equalizerPollTimer.Stop();
    }

    private bool CanPollEqualizer() => IsLoaded && Visibility == Visibility.Visible &&
        Host is not null && Host.AppWindow.IsVisible &&
        Host.AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized } &&
        DataContext is MainViewModel && !Lifetime.IsCancellationRequested;

    private async void EqualizerPollTick(object? sender, object args)
    {
        if (_equalizerPolling || !CanPollEqualizer()) return;
        _equalizerPolling = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Lifetime);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            if (HeadsetAudioPanel.Visibility == Visibility.Visible)
                await HeadsetAudioPanel.PollEqualizerAsync(CanPollEqualizer, timeout.Token);
            else if (AwBleHeadsetPanel.Visibility == Visibility.Visible && ViewModel.SelectedAwBleHeadset is { } aw)
                await aw.PollEqualizerAsync(() => CanPollEqualizer() &&
                    AwBleHeadsetPanel.Visibility == Visibility.Visible && ReferenceEquals(ViewModel.SelectedAwBleHeadset, aw), timeout.Token);
            else if (ApBleHeadsetPanel.Visibility == Visibility.Visible && ViewModel.SelectedApBleHeadset is { } ap)
                await ap.PollEqualizerAsync(() => CanPollEqualizer() &&
                    ApBleHeadsetPanel.Visibility == Visibility.Visible && ReferenceEquals(ViewModel.SelectedApBleHeadset, ap), timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        finally { _equalizerPolling = false; }
    }
}
