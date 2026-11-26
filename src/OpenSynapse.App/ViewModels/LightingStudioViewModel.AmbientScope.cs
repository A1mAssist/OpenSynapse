using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

public enum StudioAmbientLayerScope { All, Current, Custom }

public sealed partial class LightingStudioViewModel
{
    private StudioDeviceVisual? CurrentAmbientVisual => DeviceVisuals.FirstOrDefault(visual =>
        StudioLedSelection.SameInstance(visual.InstanceId, SelectedDevice?.EffectiveInstanceId));

    public bool CanUseCurrentAmbientDevice =>
        SelectedDevice is { HasNoPixelOutput: false, EffectiveProductId: not 0 } &&
        CurrentAmbientVisual is { Leds.Count: > 0 };

    public StudioAmbientLayerScope AmbientLayerScope
    {
        get
        {
            if (_selectedLayer is not { Regions.Count: > 0 } layer) return StudioAmbientLayerScope.All;
            if (layer.Regions.Count == 1 && layer.Regions[0].Devices.Count == 1 &&
                CurrentAmbientVisual is { } visual)
            {
                var target = layer.Regions[0].Devices[0];
                if (StudioLedSelection.SameInstance(target.InstanceId, visual.InstanceId) &&
                    StudioLedSelection.SameSelection(target.Leds, StudioLedSelection.DeviceCells(visual)))
                    return StudioAmbientLayerScope.Current;
            }
            return StudioAmbientLayerScope.Custom;
        }
    }

    public void SetAmbientLayerScope(StudioAmbientLayerScope scope)
    {
        if (scope == StudioAmbientLayerScope.Custom)
        {
            StatusText = AppStrings.Text("LightingStudioAmbientCustomScopeHint");
            return;
        }
        if (_selectedLayer is not { IsGroup: false } layer ||
            SelectedParams is not { Effect: StudioEffectKind.Ambient } parameters) return;
        StudioRegion? region = null;
        if (scope == StudioAmbientLayerScope.Current)
        {
            if (!CanUseCurrentAmbientDevice || CurrentAmbientVisual is not { } visual) return;
            region = StudioRegionFactory.FromLeds(parameters, SelectedDevice!.EffectiveProductId,
                visual.InstanceId, StudioLedSelection.DeviceCells(visual));
        }
        else if (scope != StudioAmbientLayerScope.All) return;

        // A scope change replaces this layer's mask; its active effect follows the new mask.
        layer.Params = parameters.Clone();
        layer.Regions.Clear();
        if (region is not null) layer.Regions.Add(region);
        _selectedRegion = region;
        RefreshRegionChoices();
        layer.NotifyEffectChanged();
        OnPropertyChanged(nameof(SelectedEffectChoice));
        NotifyEdited();
    }
}
