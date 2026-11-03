using System.Collections.ObjectModel;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>One inspector target: the whole layer or one of its Chroma regions.</summary>
public sealed record StudioRegionChoice(int Index, StudioRegion? Region, string Label)
{
    public bool IsLayer => Region is null;
}

public sealed partial class LightingStudioViewModel
{
    private StudioRegion? _selectedRegion;

    public ObservableCollection<StudioRegionChoice> RegionChoices { get; } = new();

    public StudioRegionChoice? SelectedRegionChoice
    {
        get => RegionChoices.FirstOrDefault(choice => ReferenceEquals(choice.Region, _selectedRegion));
        set
        {
            var region = value?.Region;
            if (ReferenceEquals(_selectedRegion, region)) return;
            _selectedRegion = region;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedParams));
            OnPropertyChanged(nameof(SelectedEffectChoice));
            OnPropertyChanged(nameof(SelectedBlendChoice));
            OnPropertyChanged(nameof(SpeedMinimum));
            OnPropertyChanged(nameof(SpeedMaximum));
            _ = EnsureMediaSamplerAsync();
            _ = EnsureInputAsync();
        }
    }

    /// <summary>Restricts the selected layer to the LEDs picked on the canvas.</summary>
    public void ApplySelectionToLayer(IReadOnlyList<StudioLedRef> leds)
    {
        if (_selectedLayer is null)
        {
            StatusText = AppStrings.Text("LightingStudioStatusSelectLayer");
            return;
        }

        if (leds.Count == 0)
        {
            StatusText = AppStrings.Text("LightingStudioStatusSelectLeds");
            return;
        }

        var device = SelectedDevice;
        if (device is null || device.EffectiveProductId == 0)
        {
            StatusText = AppStrings.Text("LightingStudioStatusNoDevice");
            return;
        }

        var replacement = StudioRegionFactory.FromLeds(
            _selectedLayer.Params, device.EffectiveProductId, device.EffectiveInstanceId, leds);
        var region = _selectedRegion;
        if (region is null)
        {
            region = replacement;
            _selectedLayer.Regions.Add(region);
        }
        else
        {
            region.Devices.Clear();
            foreach (var regionDevice in replacement.Devices) region.Devices.Add(regionDevice);
        }

        _selectedRegion = region;
        RefreshRegionChoices();
        SelectedRegionChoice = RegionChoices.FirstOrDefault(choice => ReferenceEquals(choice.Region, region));
        StatusText = AppStrings.FormatText("LightingStudioStatusRegionApplied", leds.Count);
        NotifyDocumentChanged();
    }

    /// <summary>Rebuilds the layer/region selector after a project or selection change.</summary>
    private void RefreshRegionChoices()
    {
        var previous = _selectedRegion;
        RegionChoices.Clear();
        if (_selectedLayer is { } layer)
        {
            RegionChoices.Add(new StudioRegionChoice(
                -1, null, AppStrings.Text("LightingStudioAllRegions")));
            for (var index = 0; index < layer.Regions.Count; index++)
            {
                var region = layer.Regions[index];
                var ledCount = region.Devices.Sum(device => device.Leds.Count);
                var label = AppStrings.FormatText("LightingStudioRegionName", index + 1);
                RegionChoices.Add(new StudioRegionChoice(
                    index,
                    region,
                    ledCount > 0 ? $"{label} ({ledCount})" : label));
            }
        }

        _selectedRegion = previous is null
            ? null
            : RegionChoices.FirstOrDefault(choice => ReferenceEquals(choice.Region, previous))?.Region;
        OnPropertyChanged(nameof(RegionChoices));
        OnPropertyChanged(nameof(SelectedRegionChoice));
        OnPropertyChanged(nameof(SelectedParams));
    }
}
