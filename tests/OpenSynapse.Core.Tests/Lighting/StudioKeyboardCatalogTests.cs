using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Keyboards that only exist in Razer's engine manifests. Their 0x0F:0x03 frame
/// command matches the OpenRazer keyboard payload, so the studio catalog reuses a
/// sibling's transaction set; these rows pin identity, matrix size and frame builders.
/// </summary>
public sealed class StudioKeyboardCatalogTests
{
    [Theory]
    [InlineData(0x023E, "Razer Turret Keyboard Xbox One Edition", 6, 18)]
    [InlineData(0x0A23, "Razer Turret Keyboard Gears of War 5 Edition", 6, 18)]
    [InlineData(0x02B1, "Razer Huntsman V3 X Tenkeyless", 6, 18)]
    [InlineData(0x02B3, "Razer BlackWidow V4 Pro 75%", 6, 18)]
    [InlineData(0x02C4, "Razer Pro Type Ergo", 1, 19)]
    [InlineData(0x02CC, "Razer BlackWidow V4 Low-profile HyperSpeed", 7, 22)]
    [InlineData(0x02D0, "Razer Huntsman V3 Pro Tenkeyless 8KHz", 6, 22)]
    [InlineData(0x02D1, "Razer Huntsman V3 Pro Mini 8KHz", 5, 15)]
    [InlineData(0x02D4, "Razer BlackWidow V4 Low-Profile Tenkeyless HyperSpeed", 6, 18)]
    [InlineData(0x02D8, "Razer Huntsman Signature Edition", 6, 22)]
    [InlineData(0x02E3, "Razer Reclusa X Mini 65%", 5, 16)]
    [InlineData(0x02E4, "Razer Huntsman V3 HE Magnetic Mini 65% 8KHz", 5, 16)]
    [InlineData(0x02E5, "Razer Huntsman V3 Tenkeyless 8KHz", 6, 22)]
    [InlineData(0x02E6, "Razer Huntsman V3 Pro Low-Profile Tenkeyless 8KHz", 6, 22)]
    [InlineData(0x02EA, "Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz", 6, 18)]
    [InlineData(0x02F0, "Razer BlackWidow V4 75% XBOX 25 Anniversary Edition", 7, 18)]
    [InlineData(0x02F4, "Razer Reclusa Mini 65%", 5, 16)]
    public void StudioKeyboardEntriesExposeTheirManifestMatrix(ushort productId, string name, byte rows, byte columns)
    {
        Assert.True(StudioKeyboardCatalog.TryGet(productId, out var device));
        Assert.Equal(name, device.DisplayName);
        Assert.Equal(DeviceCategory.Keyboard, device.Category);
        Assert.Equal(new RazerHidMatrixDimensions(rows, columns), device.MatrixDimensions);
        Assert.True(device.HasSourceCapability("set_key_row"));
        Assert.Contains(device.Transactions.Keys, builder =>
            builder is "razer_chroma_standard_matrix_set_custom_frame" or
                "razer_chroma_extended_matrix_set_custom_frame" or
                "razer_chroma_extended_matrix_set_custom_frame2");
        Assert.Contains(RazerHidBackendCapability.MatrixFrameWrite, RazerHidDeviceService.GetCapabilities(device));
    }

    [Fact]
    public void KeyboardPageConnectionJoinsTheCanvasWithFrameWrite()
    {
        var keyboard = new RazerKeyboardConnection(@"\\?\hid#vid_1532&pid_02d0", 0x02D0, "test", 91, false);

        var connection = StudioKeyboardCatalog.CreateConnection(keyboard);

        Assert.NotNull(connection);
        Assert.True(connection!.IsReady);
        Assert.Contains(RazerHidBackendCapability.MatrixFrameWrite, connection.Capabilities);
        Assert.Equal(new RazerHidMatrixDimensions(6, 22), connection.Definition.MatrixDimensions);
    }
}
