using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    private void OutputScopeMenuOpening(object sender, object e)
    {
        var choices = _viewModel?.GetOutputChoices() ?? [];
        ApplyAllMenuItem.IsEnabled = _viewModel?.IsBusy == false && choices.Any(choice => choice.IsOnline);
        ApplyCurrentMenuItem.IsEnabled = _viewModel?.IsBusy == false && choices.Any(choice => choice.IsOnline &&
            StudioLedSelection.SameInstance(choice.InstanceId, _viewModel?.SelectedDevice?.EffectiveInstanceId));
    }

    private async void ApplyAllClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not { IsBusy: false } viewModel) return;
        _historyTimer.Stop();
        viewModel.CommitHistory();
        await viewModel.ApplyAllAsync();
    }

    private async void ApplyCurrentClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not { IsBusy: false } viewModel) return;
        _historyTimer.Stop();
        viewModel.CommitHistory();
        await viewModel.ApplyCurrentAsync();
    }

    private async void SelectOutputDevicesClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not { IsBusy: false } viewModel || XamlRoot is null) return;
        var profile = viewModel.Profile;
        var panel = new StackPanel { Spacing = 8, MinWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = AppStrings.Text("LightingStudioOutputSelectionHint"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        });
        var boxes = viewModel.GetOutputChoices().Select(choice =>
        {
            var label = choice.IsOnline ? choice.Name : AppStrings.FormatText("LightingStudioOutputOffline", choice.Name);
            var box = new CheckBox
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
                IsChecked = choice.IsSelected,
                Tag = choice,
            };
            AutomationProperties.SetName(box, label);
            AutomationProperties.SetAutomationId(box, $"StudioOutputDevice:{choice.InstanceId}");
            return box;
        }).ToArray();
        foreach (var box in boxes) panel.Children.Add(box);
        if (boxes.Length == 0)
            panel.Children.Add(new TextBlock { Text = AppStrings.Text("LightingStudioStatusNoDevice") });

        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("LightingStudioSelectOutputDevices"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 360 },
            PrimaryButtonText = AppStrings.Text("LightingStudioConfirmOutputSelection"),
            SecondaryButtonText = AppStrings.Text("LightingStudioPreviewOutputSelection"),
            CloseButtonText = AppStrings.Text("LightingStudioCancelOutputSelection"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        AutomationProperties.SetAutomationId(dialog, "StudioOutputDeviceDialog");
        bool CanApplySelection() => !viewModel.IsBusy && viewModel.GetOutputChoices().Any(choice =>
            choice.IsOnline && boxes.Any(box => box.IsChecked == true &&
                StudioLedSelection.SameInstance(((StudioOutputChoice)box.Tag).InstanceId, choice.InstanceId)));
        void UpdateApplyEnabled() => dialog.IsPrimaryButtonEnabled = CanApplySelection();
        void OutputStateChanged(object? source, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(LightingStudioViewModel.IsBusy) or nameof(LightingStudioViewModel.CanApply))
                UpdateApplyEnabled();
        }
        foreach (var box in boxes)
        {
            box.Checked += (_, _) => UpdateApplyEnabled();
            box.Unchecked += (_, _) => UpdateApplyEnabled();
        }
        UpdateApplyEnabled();
        viewModel.PropertyChanged += OutputStateChanged;
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            viewModel.PropertyChanged -= OutputStateChanged;
        }
        if (result == ContentDialogResult.None ||
            !ReferenceEquals(_viewModel, viewModel) || !ReferenceEquals(viewModel.Profile, profile)) return;
        if (result == ContentDialogResult.Primary && !CanApplySelection()) return;

        var scope = new StudioOutputScope { Mode = StudioOutputScopeMode.Selected };
        foreach (var box in boxes.Where(box => box.IsChecked == true))
        {
            var choice = (StudioOutputChoice)box.Tag;
            scope.Targets.Add(new StudioOutputTarget(choice.InstanceId, choice.Name));
        }
        _historyTimer.Stop();
        viewModel.CommitHistory();
        if (result == ContentDialogResult.Primary)
            await viewModel.ApplySelectedAsync(scope.Targets.Select(target => target.InstanceId));
        else
            viewModel.SetOutputScope(scope);
    }

    private void AmbientScopeRequested(object? sender, StudioAmbientLayerScope scope)
    {
        if (_viewModel is not { } viewModel) return;
        _historyTimer.Stop();
        viewModel.CommitHistory();
        if (scope == StudioAmbientLayerScope.Custom)
        {
            ToolPicker.SelectedIndex = 0;
            ApplyRegionHighlight();
            DeviceCanvas.Focus(FocusState.Programmatic);
            viewModel.SetAmbientLayerScope(scope);
            return;
        }
        viewModel.SetAmbientLayerScope(scope);
        BindInspector();
        ApplyRegionHighlight();
    }
}
