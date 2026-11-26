using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    internal async Task RestoreLightingAfterExternalAsync(
        PowerSourceKind powerSource, CancellationToken cancellationToken)
    {
        var document = _getProfile?.Invoke() ??
            throw new InvalidOperationException("The keyboard lighting profile is unavailable.");
        var descriptor = new DeviceDescriptor(
            ProfileDeviceKey, _connection.Name, 0x1532, _connection.ProductId,
            DeviceAccessState.Available, DeviceCapabilityState.PendingValidation,
            0, 0, 0, "keyboard", DeviceCategory.Keyboard);
        var settings = ProfileResolver.ResolveWithDeviceKey(
            document, descriptor, powerSource, ProfileDeviceKey).Chroma;
        if (!Enum.TryParse<RazerChromaEffect>(settings.Effect, true, out var effect) ||
            !Effects.Contains(effect) || effect == RazerChromaEffect.CustomFrame ||
            !TryBuildColors(effect, settings, out var colors))
            throw new InvalidOperationException("The keyboard has no restorable saved lighting effect.");

        var service = OpenChromaService();
        // Studio overwrote the hardware, so the card's last-applied cache cannot skip this write.
        if (settings.BrightnessPercent is int brightness)
            await service.SetBrightnessAsync((byte)Math.Clamp(brightness, 0, 100), cancellationToken);
        await service.SetEffectAsync(effect, colors, cancellationToken);
    }
}
