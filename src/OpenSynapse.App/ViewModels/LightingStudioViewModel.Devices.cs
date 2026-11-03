using System.Collections.ObjectModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>Device list, per-device matrix dimensions, and canvas visuals.</summary>
public sealed partial class LightingStudioViewModel
{
    public ObservableCollection<StudioDeviceVisual> DeviceVisuals { get; } = new();
    public void RefreshDevices()
    {
        var previous = _selectedDevice?.EffectiveInstanceId;
        Devices.Clear();
        var matrixDevices = _matrixDevices?.Invoke();
        if (matrixDevices is { Count: > 0 })
        {
            foreach (var device in matrixDevices) Devices.Add(device);
        }
        else
        {
            var blades = _devices()
                .Where(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                    device.Access == DeviceAccessState.Available)
                .ToArray();
            foreach (var blade in blades)
            {
                Devices.Add(new StudioDeviceOption(
                    blade.Name,
                    blade,
                    blade.Id,
                    blade.ProductId,
                    BladeLightingLayout.LogicalRows,
                    BladeLightingLayout.LogicalColumns,
                    IsBlade: true));
            }
        }

        if (Devices.Count == 0)
        {
            Devices.Add(new StudioDeviceOption(AppStrings.Text("LightingStudioNoDevice"), null));
        }

        SelectedDevice = Devices.FirstOrDefault(option =>
            string.Equals(option.EffectiveInstanceId, previous, StringComparison.OrdinalIgnoreCase)) ?? Devices[0];
        var liveIds = Devices
            .Select(option => option.EffectiveInstanceId ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = DeviceVisuals.Count - 1; index >= 0; index--)
        {
            if (!liveIds.Contains(DeviceVisuals[index].InstanceId)) DeviceVisuals.RemoveAt(index);
        }

        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(PreviewRows));
        OnPropertyChanged(nameof(PreviewColumns));
        OnPropertyChanged(nameof(DeviceVisuals));
        _ = LoadAllVisualsAsync();
    }

    private async Task LoadAllVisualsAsync()
    {
        _visualsCancellation?.Cancel();
        _visualsCancellation?.Dispose();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _visualsCancellation = cancellation;
        foreach (var device in Devices.ToArray())
        {
            if (cancellation.IsCancellationRequested) return;
            if (device.EffectiveProductId == 0) continue;
            var instanceId = device.EffectiveInstanceId ?? string.Empty;
            if (DeviceVisuals.Any(visual =>
                string.Equals(visual.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var visual = device.Descriptor is { } descriptor
                    ? await _visualLoader.LoadAsync(descriptor, cancellation.Token)
                    : await _visualLoader.LoadAsync(
                        device.EffectiveProductId, instanceId, device.Name, cancellation.Token);
                if (visual is not null) AddVisual(visual);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
            }
        }
    }

    private void AddVisual(StudioDeviceVisual visual)
    {
        var existing = DeviceVisuals.FirstOrDefault(item =>
            string.Equals(item.InstanceId, visual.InstanceId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) DeviceVisuals.Remove(existing);
        DeviceVisuals.Add(visual);
        OnPropertyChanged(nameof(DeviceVisuals));
    }

    private async Task LoadVisualAsync(StudioDeviceOption? option)
    {
        _visualCancellation?.Cancel();
        _visualCancellation?.Dispose();
        _visualCancellation = null;
        if (option is null || option.EffectiveProductId == 0)
        {
            SetDeviceVisual(null);
            return;
        }

        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _visualCancellation = cancellation;
        StatusText = AppStrings.Text("LightingStudioStatusLoadingLayout");
        try
        {
            var visual = option.Descriptor is { } descriptor
                ? await _visualLoader.LoadAsync(descriptor, cancellation.Token)
                : await _visualLoader.LoadAsync(
                    option.EffectiveProductId,
                    option.EffectiveInstanceId ?? string.Empty,
                    option.Name,
                    cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                SetDeviceVisual(visual);
                StatusText = AppStrings.Text("LightingStudioStatusHint");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetDeviceVisual(null);
            StatusText = AppStrings.FormatText("LightingStudioStatusLayoutFailed", exception.Message);
        }
    }

    private void SetDeviceVisual(StudioDeviceVisual? visual)
    {
        _deviceVisual = visual;
        if (visual is not null) AddVisual(visual);
        OnPropertyChanged(nameof(DeviceVisual));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }
}
