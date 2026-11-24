using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

public sealed partial class LightingStudioViewModel
{
    public async Task ImportPresetAsync(string path)
    {
        try
        {
            var preset = await Task.Run(() => StudioPresetCodec.Load(path));
            if (_project.SelectedProfile is not { } profile) return;

            var layer = StudioLayerItem.CreateLayer(preset.Name, StudioEffectKind.Preset);
            layer.Params.Preset = preset.Id;
            layer.Params.PresetDefinition = preset;
            profile.Items.Insert(0, layer);
            SyncLayersFromProfile();
            RebuildLayerRows();
            SelectedLayerRow = LayerRows.FirstOrDefault(row => row.Layer == layer);
            NotifyEdited();
            StatusText = AppStrings.FormatText("LightingStudioStatusImported", path);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = AppStrings.FormatText("LightingStudioStatusLoadFailed", exception.Message);
        }
    }
}
