using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>Device list, per-device matrix dimensions, and canvas visuals.</summary>
public sealed partial class LightingStudioViewModel
{
    public ObservableCollection<StudioDeviceVisual> DeviceVisuals { get; } = new();
    private CancellationTokenSource? _imagesCancellation;

    public Visibility NoPixelOutputVisibility =>
        _selectedDevice?.HasNoPixelOutput == true ? Visibility.Visible : Visibility.Collapsed;
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
            foreach (var descriptor in _devices())
            {
                if (StudioBladeDevices.Resolve(descriptor) is not { } profile) continue;
                Devices.Add(new StudioDeviceOption(
                    descriptor.Name,
                    descriptor,
                    descriptor.Id,
                    descriptor.ProductId,
                    profile.Rows,
                    profile.Columns,
                    IsBlade: true,
                    BladeFrameMode: profile.Mode));
            }
        }

        if (Devices.Count == 0)
        {
            Devices.Add(new StudioDeviceOption(AppStrings.Text("LightingStudioNoDevice"), null));
        }

        SelectedDevice = Devices.FirstOrDefault(option =>
            string.Equals(option.EffectiveInstanceId, previous, StringComparison.OrdinalIgnoreCase)) ?? Devices[0];
        var live = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in Devices)
        {
            if (option.EffectiveInstanceId is { } instanceId)
            {
                live[instanceId] = option.EffectiveProductId;
            }
        }
        for (var index = DeviceVisuals.Count - 1; index >= 0; index--)
        {
            var visual = DeviceVisuals[index];
            if (!live.TryGetValue(visual.InstanceId, out var productId) ||
                productId != visual.ProductId)
            {
                _hidRouter?.SetLayout(visual.InstanceId, null);
                DeviceVisuals.RemoveAt(index);
            }
        }

        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(PreviewRows));
        OnPropertyChanged(nameof(PreviewColumns));
        OnPropertyChanged(nameof(DeviceVisuals));
        _ = LoadAllVisualsAsync();
        _ = LoadDeviceImagesAsync();
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
            if (device.IsSingleZone)
            {
                if (GridVisual(device) is { } zoneVisual) AddVisual(zoneVisual);
                continue;
            }

            if (device.IsArgb && device.Layout is { } argbLayout)
            {
                AddVisual(StudioDeviceVisualBuilder.BuildGrid(
                    device.EffectiveProductId, instanceId, device.Name, argbLayout));
                continue;
            }

            // The manifest fetch can take seconds; paint the packaged layout (or an editable
            // grid) now and replace it when the live manifest lands.
            if ((_visualLoader.LoadOfflineVisual(
                    device.EffectiveProductId,
                    instanceId,
                    device.Name) ?? GridVisual(device)) is { } placeholder)
            {
                AddVisual(placeholder);
            }
            try
            {
                var visual = device.Descriptor is { } descriptor
                    ? await _visualLoader.LoadAsync(descriptor, cancellation.Token)
                    : await _visualLoader.LoadAsync(
                        device.EffectiveProductId, instanceId, device.Name, cancellation.Token);
                if ((visual ?? GridVisual(device)) is { } fallback) AddVisual(fallback);
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
        _hidRouter?.SetLayout(visual.InstanceId, visual.Layout);
        if (IsApplied && _hidRouter is not null) RefreshHidSnapshots();
        if (string.Equals(visual.InstanceId, _selectedDevice?.EffectiveInstanceId,
                StringComparison.OrdinalIgnoreCase))
        {
            OnPropertyChanged(nameof(PreviewRows));
            OnPropertyChanged(nameof(PreviewColumns));
        }

        OnPropertyChanged(nameof(DeviceVisuals));
    }

    /// <summary>Turns the selected device 90° on the canvas; the placement is what the project stores.</summary>
    /// <param name="x">Current canvas position; defaults would move an unplaced device.</param>
    public void RotateSelectedDevice(double x, double y)
    {
        if (_project.SelectedProfile is not { } profile) return;
        var instanceId = _selectedDevice?.EffectiveInstanceId;
        if (string.IsNullOrEmpty(instanceId)) return;
        var placement = profile.Devices.FirstOrDefault(item =>
            StudioLedSelection.SameInstance(item.InstanceId, instanceId));
        if (placement is null)
        {
            placement = NewPlacement(instanceId, x, y);
            profile.Devices.Add(placement);
        }

        placement.Rotation = (placement.Rotation + 90) % 360;
        _history?.Capture();
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        NotifyDocumentChanged();
    }

    /// <summary>Fills old placements that were saved before device identity was stored.</summary>
    private void EnsurePlacementIdentity()
    {
        foreach (var profile in _project.Profiles)
        foreach (var placement in profile.Devices)
        {
            var option = Devices.FirstOrDefault(device =>
                StudioLedSelection.SameInstance(device.EffectiveInstanceId, placement.InstanceId));
            if (option is not null)
            {
                if (placement.ProductId == 0) placement.ProductId = option.EffectiveProductId;
                if (string.IsNullOrWhiteSpace(placement.Category)) placement.Category = option.Category;
                if (string.IsNullOrWhiteSpace(placement.ProductName)) placement.ProductName = option.Name;
            }

            var visual = DeviceVisuals.FirstOrDefault(item =>
                StudioLedSelection.SameInstance(item.InstanceId, placement.InstanceId));
            if (visual is { UsesSvg: true })
            {
                if (placement.Width <= 0 && visual.Width > 0) placement.Width = visual.Width;
                if (placement.Height <= 0 && visual.Height > 0) placement.Height = visual.Height;
            }

            if (string.IsNullOrWhiteSpace(placement.LedRefJson) && visual?.Layout is { } layout)
            {
                placement.LedRefJson = StudioLedRefJson.Serialize(layout);
                if (string.IsNullOrWhiteSpace(placement.LedsJson)) placement.LedsJson = placement.LedRefJson;
            }
        }
    }

    private StudioDevicePlacement NewPlacement(string instanceId, double x = 5000, double y = 5000)
    {
        var option = Devices.FirstOrDefault(device =>
            StudioLedSelection.SameInstance(device.EffectiveInstanceId, instanceId));
        return new StudioDevicePlacement
        {
            InstanceId = instanceId,
            X = x,
            Y = y,
            ProductId = option?.EffectiveProductId ?? 0,
            Category = option?.Category,
            ProductName = option?.Name,
        };
    }

    private static StudioDeviceVisual? GridVisual(StudioDeviceOption option)
    {
        if (option.EffectiveProductId == 0 || option.Rows <= 0 || option.Columns <= 0) return null;
        var leds = new List<StudioLedMap>(option.Rows * option.Columns);
        for (var row = 0; row < option.Rows; row++)
        {
            for (var column = 0; column < option.Columns; column++)
            {
                leds.Add(new StudioLedMap(row, column, row, column));
            }
        }

        var layout = StudioLedLayout.Create(
            option.Rows, option.Columns, option.Rows, option.Columns, leds);
        return StudioDeviceVisualBuilder.BuildGrid(
            option.EffectiveProductId,
            option.EffectiveInstanceId ?? string.Empty,
            option.Name,
            layout);
    }

    private StudioLedLayout? FindLayout(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return null;
        var layout = DeviceVisuals.FirstOrDefault(visual =>
            string.Equals(visual.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))?.Layout;
        return layout is { Leds.Count: > 0 } ? layout : null;
    }

    private static (int Rows, int Columns) MatrixSize(
        StudioLedLayout? layout,
        int fallbackRows,
        int fallbackColumns) =>
        layout is { MatrixRows: > 0, MatrixColumns: > 0 }
            ? (layout.MatrixRows, layout.MatrixColumns)
            : (fallbackRows, fallbackColumns);

    private (int Rows, int Columns) MatrixSize(StudioDeviceOption device) =>
        MatrixSize(FindLayout(device.EffectiveInstanceId), device.Rows, device.Columns);

    private (int Rows, int Columns) FrameSize(StudioDeviceOption device) =>
        device.IsBlade ? (device.Rows, device.Columns) : MatrixSize(device);

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

        if (option.IsSingleZone)
        {
            SetDeviceVisual(GridVisual(option));
            StatusText = AppStrings.Text("LightingStudioStatusHint");
            return;
        }

        if (option.IsArgb && option.Layout is { } argbLayout)
        {
            SetDeviceVisual(StudioDeviceVisualBuilder.BuildGrid(
                option.EffectiveProductId,
                option.EffectiveInstanceId ?? string.Empty,
                option.Name,
                argbLayout));
            StatusText = AppStrings.Text("LightingStudioStatusHint");
            return;
        }

        var offline = _visualLoader.LoadOfflineVisual(
            option.EffectiveProductId,
            option.EffectiveInstanceId ?? string.Empty,
            option.Name);
        var placeholder = offline ?? GridVisual(option);
        if (placeholder is not null)
        {
            // The manifest request can take seconds; the packaged layout (or editable grid)
            // must be visible now.
            SetDeviceVisual(placeholder);
        }

        // The network/manifest path is bounded below; do not leave the page on a loading
        // status if the request hangs or the CDN is unreachable.
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _visualCancellation = cancellation;
        StatusText = AppStrings.Text(offline is null
            ? "LightingStudioStatusLoadingLayout"
            : "LightingStudioStatusHint");
        try
        {
            var visual = option.Descriptor is { } descriptor
                ? await _visualLoader.LoadAsync(descriptor, cancellation.Token)
                : await _visualLoader.LoadAsync(
                    option.EffectiveProductId,
                    option.EffectiveInstanceId ?? string.Empty,
                    option.Name,
                    cancellation.Token);
            if (ReferenceEquals(_visualCancellation, cancellation) && !cancellation.IsCancellationRequested)
            {
                SetDeviceVisual(visual ?? placeholder);
                StatusText = AppStrings.Text("LightingStudioStatusHint");
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_visualCancellation, cancellation))
            {
                SetDeviceVisual(placeholder);
                StatusText = AppStrings.Text("LightingStudioStatusLayoutFallback");
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (!ReferenceEquals(_visualCancellation, cancellation)) return;

            // No manifest (offline, unknown product) still gets an editable grid from the
            // matrix dimensions the device already reports.
            var fallback = GridVisual(option);
            SetDeviceVisual(fallback);
            StatusText = fallback is null
                ? AppStrings.FormatText("LightingStudioStatusLayoutFailed", exception.Message)
                : AppStrings.Text("LightingStudioStatusLayoutFallback");
        }
    }

    private async Task LoadDeviceImagesAsync()
    {
        _imagesCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _imagesCancellation = cancellation;
        var tasks = Devices
            .Where(device => device.EffectiveProductId != 0)
            .Select(device => LoadDeviceImageAsync(device, cancellation.Token))
            .ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadDeviceImageAsync(
        StudioDeviceOption device,
        CancellationToken cancellationToken)
    {
        if (device.ImageState == StudioProductImageState.Loaded) return;
        try
        {
            var image = await _visualLoader.LoadProductImageAsync(
                device.EffectiveProductId, device.Name, cancellationToken);
            if (!cancellationToken.IsCancellationRequested) ApplyProductImage(device, image);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
        }
    }

    private void ApplyProductImage(StudioDeviceOption device, StudioProductImage image)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            device.ApplyProductImage(image);
            return;
        }

        _dispatcher.TryEnqueue(() => device.ApplyProductImage(image));
    }

    private void SetDeviceVisual(StudioDeviceVisual? visual)
    {
        _deviceVisual = visual;
        if (visual is not null) AddVisual(visual);
        OnPropertyChanged(nameof(DeviceVisual));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }
}
