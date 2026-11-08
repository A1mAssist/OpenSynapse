using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerChromaViewModel
{
    private string ProfileDeviceKey => string.IsNullOrWhiteSpace(_serial)
        ? $"1532:{_connection.ProductId:X4}"
        : _serial;

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            _connection.DevicePath,
            _connection.Name,
            0x1532,
            _connection.ProductId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "chroma",
            DeviceCategory.Lighting);
        var applier = new ChromaProfileApplier(_diagnostic);
        await applier.ApplyAsync(
            new DeviceProfileApplyContext(
                document,
                descriptor,
                powerSource,
                ProfileResolver.Resolve(document, descriptor, powerSource)),
            cancellationToken);
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string EncodeArgbFrame(IReadOnlyList<RazerHidColor> colors) =>
        Convert.ToBase64String(
            colors.SelectMany(color => new[] { color.Red, color.Green, color.Blue }).ToArray());

    private async Task RestoreArgbFrameAsync(
        RazerHidSpecialLightingConnection connection,
        byte port,
        string? previousFrame)
    {
        if (string.IsNullOrWhiteSpace(previousFrame) || _specialLighting is null)
        {
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(previousFrame);
            var colors = Enumerable.Range(0, bytes.Length / 3)
                .Select(index => new RazerHidColor(
                    bytes[index * 3],
                    bytes[index * 3 + 1],
                    bytes[index * 3 + 2]))
                .ToArray();
            await _specialLighting.SetArgbFrameAsync(
                connection, port, colors, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            // The save failure is the actionable error; restore is best effort.
        }
    }
}
