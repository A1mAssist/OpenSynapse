using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.Storage.Pickers;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class HeadsetAudioPanel : UserControl
{
    public HeadsetAudioPanel()
    {
        InitializeComponent();
        InitializeAutoApply();
    }

    internal CancellationToken Lifetime { get; private set; } = CancellationToken.None;

    internal MainWindow Host { get; set; } = null!;

    private UsbAccessoriesWorkspace? Accessories { get; set; }

    private RazerHeadsetAudioViewModel? ViewModel => DataContext as RazerHeadsetAudioViewModel;

    internal async Task PollEqualizerAsync(Func<bool> isVisible, CancellationToken cancellationToken)
    {
        if (ViewModel is not { } viewModel || Accessories is null) return;
        bool IsCurrent() => isVisible() && Visibility == Visibility.Visible && ReferenceEquals(ViewModel, viewModel);
        await Accessories.RunHeadsetOperationAsync(
            viewModel, (vm, token) => vm.PollEqualizerAsync(IsCurrent, token), cancellationToken);
    }

    private async Task RunAsync(Func<RazerHeadsetAudioViewModel, CancellationToken, Task> action)
    {
        var target = ViewModel;
        if (target is null || Accessories is null) return;
        try { await Accessories.RunHeadsetOperationAsync(target, action, Lifetime); }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
    }

    internal void Attach(UsbAccessoriesWorkspace accessories, CancellationToken lifetime)
    {
        Accessories = accessories;
        Lifetime = lifetime;
    }

    private async void HeadsetAudioRefreshClick(object sender, RoutedEventArgs e) =>
        await (Accessories?.RefreshSelectedHeadsetAudioAsync(Lifetime) ?? Task.CompletedTask);

    private async void HeadsetAudioInstallThxClick(object sender, RoutedEventArgs e) =>
        await RunAsync((vm, token) => vm.InstallThxComponentsAsync(token));

    private async void HeadsetAudioLoadParametricEqClick(object sender, RoutedEventArgs e) =>
        await RunAsync((vm, token) => vm.LoadParametricEqAsync(token));

    private async void HeadsetAudioApplyMatrixBrightnessClick(object sender, RoutedEventArgs e) =>
        await RunAsync((vm, token) => vm.ApplyMatrixBrightnessAsync(token));

    private async void HeadsetAudioApplyMatrixLightingClick(object sender, RoutedEventArgs e) =>
        await RunAsync((vm, token) => vm.ApplyMatrixLightingAsync(token));

    private async void HeadsetAudioApplyLightingClick(object sender, RoutedEventArgs e) =>
        await RunAsync((vm, token) => vm.Lighting?.ApplyAsync(token) ?? Task.CompletedTask);

    private async void HeadsetAudioImportSynapseEqClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Host is null)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".synapse4");
        Host.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null || string.IsNullOrWhiteSpace(file.Path))
        {
            return;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(file.Path, Lifetime);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            json = string.Empty;
        }

        await RunAsync(async (vm, token) =>
        {
            if (vm.TryLoadSynapseEq(json)) await vm.ApplyThxBandsAsync(token);
        });
    }

    private async void HeadsetAudioApplyThxS3BaselineClick(object sender, RoutedEventArgs e) =>
        await RunAsync((vm, token) => vm.ApplyS3BaselineAsync(token));

    private async void Mixer053EMute1Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMute1Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute2Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMute2Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute3Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMute3Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMute4Click(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMute4Async(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EMicMuteClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ToggleMicMuteAsync(Lifetime) ?? Task.CompletedTask);

    private async void Mixer053EApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel?.Mixer053E?.ApplyBrightnessAsync(Lifetime) ?? Task.CompletedTask);
}
