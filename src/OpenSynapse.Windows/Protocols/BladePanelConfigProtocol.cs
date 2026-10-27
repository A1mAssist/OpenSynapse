using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Blade panel and SKU reports from rzDevice25 module 94735. The JS defines
/// P=[2,13,9] "Set VGA Configure Setting", L=[2,13,137] "Get VGA Configure Setting",
/// b=[2,13,138] "Get VGA Hardware Information", x=[2,13,139] "Get Synapse UI Setting",
/// H=[1,13,142] "Get Mini LED Panel Resolution", K=[1,13,14] "Set Mini LED Panel Resolution",
/// and Y=[1,13,143] "Get SKU hardware configuration".
/// </summary>
public static class BladePanelConfigProtocol
{
    public const byte TransactionId = 0x1F;
    public const byte CommandClass = 0x0D;

    public const byte MiniLedPanelResolutionDataSize = 0x01;
    public const byte GetMiniLedPanelResolutionCommandId = 0x8E;
    public const byte SetMiniLedPanelResolutionCommandId = 0x0E;

    public const byte VgaConfigureDataSize = 0x02;
    public const byte GetVgaConfigureCommandId = 0x89;
    public const byte SetVgaConfigureCommandId = 0x09;

    public const byte VgaHardwareDataSize = 0x02;
    public const byte GetVgaHardwareCommandId = 0x8A;

    public const byte SynapseUiDataSize = 0x02;
    public const byte GetSynapseUiCommandId = 0x8B;

    public const byte SkuHardwareDataSize = 0x01;
    public const byte GetSkuHardwareCommandId = 0x8F;

    public static byte[] CreateGetMiniLedPanelResolutionRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            MiniLedPanelResolutionDataSize,
            CommandClass,
            GetMiniLedPanelResolutionCommandId,
            new byte[] { 0x00 });

    public static byte[] CreateSetMiniLedPanelResolutionRequest(BladeNativeDisplayMode resolution)
    {
        if (!Enum.IsDefined(resolution))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        return RazerFeatureReport.CreateRequest(
            TransactionId,
            MiniLedPanelResolutionDataSize,
            CommandClass,
            SetMiniLedPanelResolutionCommandId,
            new[] { (byte)resolution });
    }

    public static BladeNativeDisplayMode ParseMiniLedPanelResolution(ReadOnlySpan<byte> response)
    {
        var request = CreateGetMiniLedPanelResolutionRequest();
        Validate(response, request, MiniLedPanelResolutionDataSize, "Blade Mini LED panel resolution");

        var raw = response[RazerFeatureReport.ArgumentsOffset];
        return raw switch
        {
            0x00 => BladeNativeDisplayMode.Uhd,
            0x01 => BladeNativeDisplayMode.Fhd,
            _ => throw new InvalidOperationException(
                $"Blade returned an unknown Mini LED panel resolution: 0x{raw:X2}."),
        };
    }

    public static byte[] CreateGetVgaConfigureSettingRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            VgaConfigureDataSize,
            CommandClass,
            GetVgaConfigureCommandId,
            new byte[] { 0x00, 0x00 });

    /// <summary>
    /// JS: <c>e[0]=0,e[1]=n</c>. Module 13546 names the second byte
    /// <c>vgaConfigureValue</c> but defines no enum, so it is exposed unchanged.
    /// </summary>
    public static byte[] CreateSetVgaConfigureSettingRequest(byte value) =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            VgaConfigureDataSize,
            CommandClass,
            SetVgaConfigureCommandId,
            new[] { (byte)0x00, value });

    public static BladeVgaConfigureSetting ParseVgaConfigureSetting(ReadOnlySpan<byte> response)
    {
        var request = CreateGetVgaConfigureSettingRequest();
        Validate(response, request, VgaConfigureDataSize, "Blade VGA configure setting");

        var offset = RazerFeatureReport.ArgumentsOffset;
        return new BladeVgaConfigureSetting(response[offset], response[offset + 1]);
    }

    public static byte[] CreateGetVgaHardwareInformationRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            VgaHardwareDataSize,
            CommandClass,
            GetVgaHardwareCommandId,
            new byte[] { 0x00, 0x00 });

    public static BladeVgaHardwareInformation ParseVgaHardwareInformation(ReadOnlySpan<byte> response)
    {
        var request = CreateGetVgaHardwareInformationRequest();
        Validate(response, request, VgaHardwareDataSize, "Blade VGA hardware information");

        var offset = RazerFeatureReport.ArgumentsOffset;
        var raw = response[offset + 1];
        return new BladeVgaHardwareInformation(
            raw,
            (raw & 0x01) != 0,
            (raw & 0x02) != 0,
            (raw & 0x04) != 0,
            (raw & 0x08) != 0);
    }

    public static byte[] CreateGetSynapseUiSettingRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            SynapseUiDataSize,
            CommandClass,
            GetSynapseUiCommandId,
            new byte[] { 0x00, 0x00 });

    public static BladeSynapseUiSetting ParseSynapseUiSetting(ReadOnlySpan<byte> response)
    {
        var request = CreateGetSynapseUiSettingRequest();
        Validate(response, request, SynapseUiDataSize, "Blade Synapse UI setting");

        var offset = RazerFeatureReport.ArgumentsOffset;
        var raw = response[offset + 1];
        return new BladeSynapseUiSetting(response[offset], raw, (byte)(raw & 0x0F));
    }

    public static byte[] CreateGetSkuHardwareConfigurationRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            SkuHardwareDataSize,
            CommandClass,
            GetSkuHardwareCommandId,
            new byte[] { 0x00 });

    public static BladeSkuHardwareConfiguration ParseSkuHardwareConfiguration(
        ReadOnlySpan<byte> response)
    {
        var request = CreateGetSkuHardwareConfigurationRequest();
        Validate(response, request, SkuHardwareDataSize, "Blade SKU hardware configuration");

        var raw = response[RazerFeatureReport.ArgumentsOffset];
        return new BladeSkuHardwareConfiguration(
            (raw & 0x01) != 0,
            (raw & 0x02) != 0,
            (raw & 0x04) != 0,
            raw);
    }

    private static void Validate(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments,
        string feature)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments))
        {
            throw new InvalidOperationException(
                $"{feature} returned an invalid or out-of-order feature report.");
        }
    }
}

public readonly record struct BladeVgaConfigureSetting(
    byte Total,
    byte Value);

public readonly record struct BladeVgaHardwareInformation(
    byte Raw,
    bool MicrosoftHybrid,
    bool DedicatedGpuOnly,
    bool DynamicDisplaySwitch,
    bool DedicatedIntegratedGpuOnly);

public readonly record struct BladeSynapseUiSetting(
    byte Total,
    byte Raw,
    byte SwitchInterface);
