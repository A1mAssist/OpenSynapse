using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>Builds the shared spatial canvas from the current devices and project.</summary>
public sealed partial class LightingStudioViewModel
{
    private bool UsesWorld(string? instanceId, bool output = false)
    {
        var world = output ? _outputWorld : _world;
        return world.Active && world.Canvas?.HasDevice(instanceId) == true;
    }

    private void InvalidateWorld()
    {
        _worldDirty = true;
        _outputWorldDirty = true;
        // Placements may have moved; rebuild the scene copy instead of reusing stale geometry.
        _sceneProfile = null;
    }

    private void RefreshBladeWorldSource()
    {
        if (!IsApplied || _source is null || _appliedBlade is null) return;
        EnsureWorld(output: true);
        _source.Update(BuildLayersFor(_appliedBlade, output: true));
    }

    private void EnsureWorld(bool output = false)
    {
        if (output)
        {
            if (!_outputWorldDirty) return;
            _outputWorldDirty = false;
        }
        else
        {
            if (!_worldDirty && _worldInitialized) return;
            _worldDirty = false;
            _worldInitialized = true;
        }
        var world = output ? _outputWorld : _world;
        var scope = output ? _appliedScope : PendingScope;

        var profile = CurrentProfile;
        if (profile is null)
        {
            world.Update(null);
            _layerCache.Clear();
            return;
        }

        var devices = new List<StudioWorldCanvasDevice>();
        foreach (var option in Devices)
        {
            var instanceId = option.EffectiveInstanceId;
            if (!option.HasMatrix || !option.IsOutputAvailable ||
                option.HasNoPixelOutput ||
                option.EffectiveProductId == 0 ||
                string.IsNullOrWhiteSpace(instanceId) || !ScopeIncludes(scope, option))
            {
                continue;
            }

            var visual = DeviceVisuals.FirstOrDefault(item =>
                StudioLedSelection.SameInstance(item.InstanceId, instanceId)) ?? GridVisual(option);
            if (visual is null) continue;

            var layout = visual.Layout;
            // Blade's source owns the logical size; the manifest layout can be the
            // device geometry instead of the 7 x 16 canvas.
            var matrixRows = option.IsBlade
                ? option.Rows
                : layout is { MatrixRows: > 0, MatrixColumns: > 0 } ? layout.MatrixRows : option.Rows;
            var matrixColumns = option.IsBlade
                ? option.Columns
                : layout is { MatrixRows: > 0, MatrixColumns: > 0 } ? layout.MatrixColumns : option.Columns;
            var deviceRows = option.IsBlade
                ? option.Rows
                : layout is { DeviceRows: > 0, DeviceColumns: > 0 } ? layout.DeviceRows : option.Rows;
            var deviceColumns = option.IsBlade
                ? option.Columns
                : layout is { DeviceRows: > 0, DeviceColumns: > 0 } ? layout.DeviceColumns : option.Columns;
            if (matrixRows <= 0 || matrixColumns <= 0 || deviceRows <= 0 || deviceColumns <= 0)
            {
                continue;
            }

            var placement = profile.Devices.FirstOrDefault(item =>
                StudioLedSelection.SameInstance(item.InstanceId, instanceId));
            double? x = placement is { } placedX && double.IsFinite(placedX.X) ? placedX.X : null;
            double? y = placement is { } placedY && double.IsFinite(placedY.Y) ? placedY.Y : null;
            devices.Add(new StudioWorldCanvasDevice(
                instanceId,
                option.EffectiveProductId,
                visual,
                x,
                y,
                placement?.Rotation ?? 0,
                matrixRows,
                matrixColumns,
                deviceRows,
                deviceColumns,
                placement is { Width: > 0 } savedWidth ? savedWidth.Width : null,
                placement is { Height: > 0 } savedHeight ? savedHeight.Height : null));
        }

        world.Update(StudioWorldCanvas.Build(devices, profile));
        _layerCache.Clear();
    }
}
