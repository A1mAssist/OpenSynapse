using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

public sealed partial class LightingStudioViewModel
{
    public StudioLayerItem? AddPresetLayer(StudioPresetConfig preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (_project.SelectedProfile is not { } profile) return null;
        var layer = StudioLayerItem.CreateLayer(preset.Name, StudioEffectKind.Preset);
        layer.Params.Preset = preset.Id;
        layer.Params.PresetDefinition = preset;
        profile.Items.Insert(0, layer);
        _history?.Capture();
        SyncLayersFromProfile();
        RebuildLayerRows();
        SelectedLayer = layer;
        SelectedLayerRow = LayerRows.FirstOrDefault(row => row.Layer == layer);
        NotifyEdited();
        return layer;
    }

    public async Task ImportPresetAsync(string path)
    {
        try
        {
            var preset = await Task.Run(() => StudioPresetCodec.Load(path));
            if (AddPresetLayer(preset) is null) return;
            StatusText = AppStrings.FormatText("LightingStudioStatusImported", path);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = AppStrings.FormatText("LightingStudioStatusLoadFailed", exception.Message);
        }
    }
}
