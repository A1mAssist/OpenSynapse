using OpenSynapse.Core.Lighting;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class StudioWorldRenderer
{
    /// <summary>One timeline/capture per role; device masks never share world-grid identities.</summary>
    private sealed class LayerPlayback
    {
        private readonly StudioDeviceFrameSource _timeline;
        private readonly Dictionary<string, StudioPlaybackTrigger> _triggers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StudioKeyHeat> _keys = new(StringComparer.OrdinalIgnoreCase);
        private StudioWorldCanvas _canvas = StudioWorldCanvas.Empty;
        private double _lastKeySeconds;
        private double _lastRenderSeconds;

        public LayerPlayback(StudioDeviceFrameSource timeline) => _timeline = timeline;

        public void Update(StudioWorldCanvas canvas)
        {
            _canvas = canvas;
            var activeDevices = canvas.Devices.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in _keys.Keys.Where(id => !activeDevices.Contains(id)).ToArray()) _keys.Remove(id);
            foreach (var id in _triggers.Keys.Where(id => !activeDevices.Contains(id)).ToArray()) _triggers.Remove(id);
            foreach (var layer in canvas.MappedLayers)
                _timeline.EffectStates.For(layer.Render.Id).RetainScopes(activeDevices);
            foreach (var device in canvas.Devices)
            {
                if (!_keys.TryGetValue(device.InstanceId, out var keys) ||
                    keys.Rows != device.MatrixRows || keys.Columns != device.MatrixColumns)
                    _keys[device.InstanceId] = new(device.MatrixRows, device.MatrixColumns);
                if (!_triggers.TryGetValue(device.InstanceId, out var trigger))
                    _triggers[device.InstanceId] = trigger = new StudioPlaybackTrigger(_timeline.GetClock);
                var layers = canvas.MappedLayers.SelectMany(layer => layer.Targets
                    .Where(target => target.Device == device)
                    .Select(target => layer.Render with { Mask = target.Mask }));
                trigger.Update(layers, device.MatrixRows, device.MatrixColumns);
            }
        }

        public IReadOnlyDictionary<string, StudioDeviceFrame> Render(double now, StudioEffectInput input)
        {
            if (now < _lastRenderSeconds) _lastKeySeconds = now;
            _lastRenderSeconds = now;
            AdvanceKeys(now);
            var timed = new (StudioLayerRender, double)[_canvas.MappedLayers.Length];
            for (var index = 0; index < timed.Length; index++)
            {
                var plan = _canvas.MappedLayers[index];
                var layer = plan.Render;
                var device = plan.Targets[0].Device;
                var rows = _canvas.UsesLegacyWorld ? _canvas.Rows : device.MatrixRows;
                var columns = _canvas.UsesLegacyWorld ? _canvas.Columns : device.MatrixColumns;
                var state = _timeline.GetClock(layer.Id).Sample(now, layer.Params, rows, columns);
                timed[index] = (layer with
                {
                    Opacity = state.Running ? layer.Opacity : 0,
                    State = _timeline.EffectStates.For(layer.Id),
                }, state.LocalSeconds);
            }
            var keys = _canvas.Devices.ToDictionary(device => device.InstanceId,
                device => _keys[device.InstanceId].Snapshot(), StringComparer.OrdinalIgnoreCase);
            return _canvas.RenderTimedDeviceFrames(timed, input with { Keys = _timeline.SnapshotKeys(now) }, keys);
        }

        public int HandleInput(string? instanceId, StudioTriggerInput input, double now, int rowOffset,
            bool playbackOnly = false)
        {
            if (_canvas.FindDevice(instanceId) is not { } device) return 0;
            input = input with { Row = input.Row + rowOffset };
            if ((uint)input.Row >= (uint)device.MatrixRows || (uint)input.Column >= (uint)device.MatrixColumns)
                return 0;
            if (!playbackOnly)
            {
                AdvanceKeys(now);
                ApplyKey(_keys[device.InstanceId], input);
                var worldIndex = device.WorldIndex(input.Row, input.Column);
                _timeline.RecordInput(input with
                {
                    Row = worldIndex / _canvas.Columns,
                    Column = worldIndex % _canvas.Columns,
                }, now);
            }
            return _triggers[device.InstanceId].Handle(input, now);
        }

        public void RestoreHeldKeys(string? instanceId, IEnumerable<(int Row, int Column)> keys, int rowOffset)
        {
            if (_canvas.FindDevice(instanceId) is not { } device) return;
            foreach (var (row, column) in keys)
            {
                _keys[device.InstanceId].Press(row + rowOffset, column);
                var worldIndex = device.WorldIndex(row + rowOffset, column);
                if (worldIndex >= 0)
                    _timeline.RestoreHeldKeys([(worldIndex / _canvas.Columns, worldIndex % _canvas.Columns)]);
            }
        }

        private static void ApplyKey(StudioKeyHeat keys, StudioTriggerInput input)
        {
            if (input.IsDown) keys.Press(input.Row, input.Column);
            else keys.Release(input.Row, input.Column);
        }

        private void AdvanceKeys(double now)
        {
            if (!double.IsFinite(now) || now <= _lastKeySeconds) return;
            foreach (var keys in _keys.Values) keys.Decay(now - _lastKeySeconds);
            _lastKeySeconds = now;
        }
    }
}
