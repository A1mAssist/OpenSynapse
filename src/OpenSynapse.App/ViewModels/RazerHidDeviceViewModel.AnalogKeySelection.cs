using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private void SelectAnalogKeyForDisplay()
    {
        if (_analogKeyLayout is null || _analogKeyKeys.Any(key => key.IsSelected))
        {
            return;
        }

        var display = AnalogKeyboardLayouts.ResolveInitialDisplayKey(
            _analogKeyLayout,
            RememberedAnalogKeyId());
        var key = display is null
            ? null
            : _analogKeyKeys.FirstOrDefault(candidate => candidate.KeyId == display.KeyId);
        if (key is not null)
        {
            key.IsSelected = true;
        }
    }

    private int? RememberedAnalogKeyId()
    {
        var settings = _getMachineSettings?.Invoke();
        if (settings is null)
        {
            return null;
        }

        var productId = Connection.Definition.ProductId.ToString("X4");
        return settings.RazerHidAnalogKeyLastModified.TryGetValue(productId, out var keyId)
            ? keyId
            : null;
    }

    private async Task RememberAnalogKeyAsync(byte keyId, CancellationToken cancellationToken)
    {
        var settings = _getMachineSettings?.Invoke();
        if (settings is null || _saveMachineSettingsAsync is null)
        {
            return;
        }

        var productId = Connection.Definition.ProductId.ToString("X4");
        var entries = settings.RazerHidAnalogKeyLastModified;
        var hadPrevious = entries.TryGetValue(productId, out var previous);
        if (hadPrevious && previous == keyId)
        {
            return;
        }

        entries[productId] = keyId;
        void Restore()
        {
            var current = settings.RazerHidAnalogKeyLastModified;
            if (hadPrevious)
            {
                current[productId] = previous;
            }
            else
            {
                current.Remove(productId);
            }
        }

        try
        {
            if (await _saveMachineSettingsAsync(cancellationToken))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            Restore();
            _log?.Invoke($"analog key selection save failed: {exception.Message}");
            return;
        }

        Restore();
        _log?.Invoke("analog key selection save failed");
    }
}
