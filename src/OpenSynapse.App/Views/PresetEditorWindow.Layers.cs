using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;
using Windows.System;

namespace OpenSynapse.App.Views;

public sealed partial class PresetEditorWindow
{
    private void LayerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi) return;
        _editor.SelectLayer(LayerList.SelectedItem as PresetEditorLayerItem);
        BuildParameterEditor();
        BuildPaletteEditor();
        UpdateLayerControls();
        RefreshPreviewDefinition();
    }

    private void AddLayerClick(object sender, RoutedEventArgs e)
    {
        if (!_editor.AddLayer()) return;
        SyncSelectedLayerPanels();
    }

    private void RemoveLayerClick(object sender, RoutedEventArgs e)
    {
        if (!_editor.RemoveSelectedLayer()) return;
        SyncSelectedLayerPanels();
    }

    private void DuplicateLayerClick(object sender, RoutedEventArgs e)
    {
        if (!_editor.DuplicateSelectedLayer()) return;
        SyncSelectedLayerPanels();
    }

    private void MoveLayerUpClick(object sender, RoutedEventArgs e)
    {
        if (!_editor.MoveSelectedLayer(-1)) return;
        SyncSelectedLayerPanels();
    }

    private void MoveLayerDownClick(object sender, RoutedEventArgs e)
    {
        if (!_editor.MoveSelectedLayer(1)) return;
        SyncSelectedLayerPanels();
    }

    private void LayerListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete || IsTextEditingFocus()) return;
        if (!_editor.RemoveSelectedLayer()) return;
        e.Handled = true;
        SyncSelectedLayerPanels();
    }

    private void IssueClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PresetEditorIssueItem item) return;
        var index = item.Issue.LayerIndex;
        if (index < 0 || index >= _editor.Layers.Count) return;
        var layer = _editor.Layers[index];
        _editor.SelectLayer(layer);
        _syncingUi = true;
        try
        {
            LayerList.SelectedItem = layer;
        }
        finally
        {
            _syncingUi = false;
        }

        BuildParameterEditor();
        BuildPaletteEditor();
        UpdateLayerControls();
        UpdateUiState();
    }

    private void SyncSelectedLayerPanels()
    {
        _syncingUi = true;
        try
        {
            LayerList.SelectedItem = _editor.SelectedLayer;
        }
        finally
        {
            _syncingUi = false;
        }

        BuildParameterEditor();
        BuildPaletteEditor();
        UpdateLayerControls();
        UpdateSimulationUi();
        UpdateUiState();
        RefreshPreviewDefinition(rebuildWorld: true);
    }

    private void UpdateLayerControls()
    {
        var item = _editor.SelectedLayer;
        _syncingLayerControls = true;
        try
        {
            if (item is null)
            {
                BlendPicker.SelectedValue = StudioBlendMode.Normal;
                OpacitySlider.Value = 1;
                OpacityBox.Value = 1;
                SpeedSlider.Value = 1;
                SpeedBox.Value = 1;
                return;
            }

            BlendPicker.SelectedValue = item.Layer.Blend;
            OpacitySlider.Value = item.Layer.Opacity;
            OpacityBox.Value = item.Layer.Opacity;
            SpeedSlider.Value = item.Layer.Speed;
            SpeedBox.Value = item.Layer.Speed;
        }
        finally
        {
            _syncingLayerControls = false;
        }
    }

    private void BlendChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || _syncingLayerControls || _editor.SelectedLayer is not { } item) return;
        if (BlendPicker.SelectedValue is not StudioBlendMode blend) return;
        _editor.SetLayerBlend(item, blend);
    }

    private void OpacitySliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi || _syncingLayerControls || _editor.SelectedLayer is not { } item) return;
        _syncingLayerControls = true;
        try
        {
            OpacityBox.Value = e.NewValue;
        }
        finally
        {
            _syncingLayerControls = false;
        }

        _editor.SetLayerOpacity(item, e.NewValue);
    }

    private void OpacityBoxChanged(object sender, NumberBoxValueChangedEventArgs e)
    {
        if (_syncingUi || _syncingLayerControls || _editor.SelectedLayer is not { } item) return;
        if (!double.IsFinite(e.NewValue)) return;
        _syncingLayerControls = true;
        try
        {
            OpacitySlider.Value = e.NewValue;
        }
        finally
        {
            _syncingLayerControls = false;
        }

        _editor.SetLayerOpacity(item, e.NewValue);
    }

    private void SpeedSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingUi || _syncingLayerControls || _editor.SelectedLayer is not { } item) return;
        _syncingLayerControls = true;
        try
        {
            SpeedBox.Value = e.NewValue;
        }
        finally
        {
            _syncingLayerControls = false;
        }

        _editor.SetLayerSpeed(item, e.NewValue);
    }

    private void SpeedBoxChanged(object sender, NumberBoxValueChangedEventArgs e)
    {
        if (_syncingUi || _syncingLayerControls || _editor.SelectedLayer is not { } item) return;
        if (!double.IsFinite(e.NewValue)) return;
        _syncingLayerControls = true;
        try
        {
            SpeedSlider.Value = e.NewValue;
        }
        finally
        {
            _syncingLayerControls = false;
        }

        _editor.SetLayerSpeed(item, e.NewValue);
    }
}
