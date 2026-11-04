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

    /// <summary>
    /// Official bucket rule: the clicked device's cells inside the region that owns the
    /// cell, otherwise that device's cells not covered by any region.
    /// </summary>
    public IReadOnlyList<StudioLedRef> BucketSelection(string instanceId, int row, int column)
    {
        if (_selectedLayer is not { } layer) return Array.Empty<StudioLedRef>();
        var visual = DeviceVisuals.FirstOrDefault(item =>
            StudioLedSelection.SameInstance(item.InstanceId, instanceId));
        return visual is null
            ? Array.Empty<StudioLedRef>()
            : StudioLedSelection.BucketCells(layer.Regions, visual, row, column);
    }

    /// <summary>Shift-click maps one LED to the Tidal centre in its 0..10000 parameter space.</summary>
    public void SetTidalCenter(int row, int column)
    {
        if (SelectedParams is not { Effect: StudioEffectKind.Tidal } parameters) return;
        var (rows, columns) = SelectedPreviewSize();
        if (rows <= 0 || columns <= 0) return;
        parameters.CenterPointX = columns <= 1
            ? 5000
            : (int)Math.Round(column * 10000d / (columns - 1), MidpointRounding.AwayFromZero);
        parameters.CenterPointY = rows <= 1
            ? 5000
            : (int)Math.Round(row * 10000d / (rows - 1), MidpointRounding.AwayFromZero);
        NotifyEdited();
        StatusText = AppStrings.Text("LightingStudioStatusTidalCenterSet");
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

        var replacement = StudioRegionFactory.FromLedsByInstance(
            _selectedLayer.Params,
            leds,
            instanceId => StudioLedSelection.SameInstance(device.EffectiveInstanceId, instanceId)
                ? device.EffectiveProductId
                : Devices.FirstOrDefault(option =>
                    StudioLedSelection.SameInstance(option.EffectiveInstanceId, instanceId))?.EffectiveProductId,
            device.EffectiveInstanceId);
        if (replacement.Devices.Count == 0)
        {
            StatusText = AppStrings.Text("LightingStudioStatusNoDevice");
            return;
        }

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
        StatusText = AppStrings.FormatText(
            "LightingStudioStatusRegionApplied", replacement.Devices.Sum(item => item.Leds.Count));
        _history?.Capture();
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
