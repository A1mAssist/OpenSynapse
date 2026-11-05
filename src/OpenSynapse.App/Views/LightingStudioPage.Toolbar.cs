using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

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
