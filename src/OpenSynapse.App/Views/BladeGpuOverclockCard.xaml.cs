using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class BladeGpuOverclockCard : UserControl
{
    private bool _ready;
    private bool _applyInFlight;

    public BladeGpuOverclockCard()
    {
        InitializeComponent();
        Loaded += (_, _) => _ready = true;
        Unloaded += (_, _) => _ready = false;
    }

    private async void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _applyInFlight)
        {
            return;
        }

        await ApplyIfDirtyAsync();
    }

    private async void NumberLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_ready || _applyInFlight)
        {
            return;
        }

        await ApplyIfDirtyAsync();
    }

    private async Task ApplyIfDirtyAsync()
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.Blade.GpuOverclockDirty)
        {
            return;
        }

        _applyInFlight = true;
        try
        {
            await viewModel.Blade.ApplyGpuClockOffsetAsync();
        }
        finally
        {
            _applyInFlight = false;
        }
    }

    private async void ResetClick(object sender, RoutedEventArgs e)
    {
        if (!_ready || _applyInFlight || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _applyInFlight = true;
        try
        {
            await viewModel.Blade.ResetGpuClockOffsetDefaultsAsync();
        }
        finally
        {
            _applyInFlight = false;
        }
    }
}