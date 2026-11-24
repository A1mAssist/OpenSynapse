using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    private void SetIconButtonLabels()
    {
        SetButtonLabel(ZoomOutButton, "LightingStudioZoomOut", tooltip: true);
        SetButtonLabel(ZoomInButton, "LightingStudioZoomIn", tooltip: true);
        SetButtonLabel(RotateDeviceButton, "LightingStudioRotateDeviceLabel", tooltip: true);
        SetButtonLabel(InspectorToggleButton, "LightingStudioLayerProperties", tooltip: true);
        SetButtonLabel(UndoButton, "LightingStudioUndo", tooltip: true);
        SetButtonLabel(RedoButton, "LightingStudioRedo", tooltip: true);

        SetButtonLabel(ApplyButton, "LightingStudioApply");
        SetButtonLabel(StopButton, "LightingStudioStop");
        SetButtonLabel(AddLayerButton, "LightingStudioAdd");
        SetButtonLabel(DuplicateLayerButton, "LightingStudioDuplicate");
        SetButtonLabel(RemoveLayerButton, "LightingStudioRemove");
        SetButtonLabel(GroupLayerButton, "LightingStudioGroup");
        SetButtonLabel(DocumentMenuButton, "LightingStudioDocument");
        SetButtonLabel(SelectAllButton, "LightingStudioSelectAll");
        SetButtonLabel(ApplySelectionButton, "LightingStudioApplySelection");
        SetButtonLabel(ClearSelectionButton, "LightingStudioClearSelection");
        AutomationProperties.SetName(ToolPicker, AppStrings.Text("LightingStudioToolLabel"));
        AutomationProperties.SetName(ZoomPicker, AppStrings.Text("LightingStudioZoomLabel"));
        var regionHint = AppStrings.Text("LightingStudioRegionHint");
        AutomationProperties.SetName(RegionPicker, AppStrings.Text("LightingStudioRegionLabel"));
        ToolTipService.SetToolTip(RegionPicker, regionHint);
        AutomationProperties.SetName(ProjectExpander, AppStrings.Text("LightingStudioProject"));
    }

    private static void SetButtonLabel(Button button, string key, bool tooltip = false)
    {
        var label = AppStrings.Text(key);
        AutomationProperties.SetName(button, label);
        if (tooltip) ToolTipService.SetToolTip(button, label);
    }

    private void ZoomInClick(object sender, RoutedEventArgs e) => DeviceCanvas.ZoomStepUp();

    private void RotateDeviceClick(object sender, RoutedEventArgs e)
    {
        var instanceId = _viewModel?.SelectedDevice?.EffectiveInstanceId;
        if (string.IsNullOrEmpty(instanceId)) return;
        var placed = DeviceCanvas.TryGetPlacement(instanceId, out var left, out var top);
        _viewModel?.RotateSelectedDevice(placed ? left : 5000, placed ? top : 5000);
    }

    private void ZoomOutClick(object sender, RoutedEventArgs e) => DeviceCanvas.ZoomStepDown();

    private void ToggleEditClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleEditing();
        ApplyEditingState();
        RenderPreview();
    }

    private void SceneCardClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not FrameworkElement { DataContext: StudioSceneCard card }) return;
        _viewModel.SelectScene(card.Scene);
        if (_viewModel.IsPlaying != true)
        {
            _viewModel.TogglePlayback();
            UpdatePlayButton();
        }

        _clock.Restart();
        _timer.Start();
        RenderPreview();
    }

    /// <summary>Browse shows devices, canvas, and scenes; editing restores the full editor.</summary>
    internal void ApplyEditingState()
    {
        var editing = _viewModel?.IsEditing == true;
        var visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ProjectExpander.Visibility = visibility;
        LayerPanel.Visibility = visibility;
        DocumentPanel.Visibility = visibility;
        ToolPicker.Visibility = visibility;
        RotateDeviceButton.Visibility = visibility;
        RotateSeparator.Visibility = visibility;
        InspectorToggleButton.Visibility = visibility;
        InspectorSeparator.Visibility = visibility;
        SelectionActions.Visibility = visibility;
        InspectorPanel.Visibility = visibility;
        ScenePanel.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(
            EditButton,
            _viewModel?.EditButtonText ?? AppStrings.Text("LightingStudioEdit"));
    }

    private void TogglePlayClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.TogglePlayback();
        UpdatePlayButton();
        if (_viewModel.IsPlaying)
        {
            _clock.Restart();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            RenderPreview();
        }
    }

    private void UpdatePlayButton()
    {
        var playing = _viewModel?.IsPlaying == true;
        var label = AppStrings.Text(playing ? "LightingStudioPause" : "LightingStudioPlay");
        PlayButtonIcon.Glyph = playing ? "\uE103" : "\uE102";
        PlayButtonLabel.Text = label;
        AutomationProperties.SetName(PlayButton, label);
        PlayButton.HorizontalContentAlignment = HorizontalAlignment.Center;
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _historyTimer.Stop();
        _viewModel.CommitHistory();
        await _viewModel.ApplyAsync();
    }

    private async void StopClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _timer.Stop();
        await _viewModel.StopAsync();
        UpdatePlayButton();
        RenderPreview();
    }


}
