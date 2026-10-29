using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class OpenRazerDeviceService
{
    private async Task<OpenRazerDeviceDefinition> ResolveLeviathanDefinitionAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        if (definition.ProductId != LeviathanV2XProtocol.ProductId)
        {
            return definition;
        }

        try
        {
            var request = OpenRazerStandardProtocol.Create(definition,
                "razer_standard_get_edition_info", 0x03, 0x00, 0x86, []);
            var response = await _transport.QueryPreparedAsync(
                devicePath, request.Report, definition.DeviceWait, cancellationToken).ConfigureAwait(false);
            var edition = LeviathanV2XProtocol.ReadEditionInfo(response).Edition;
            return definition.WithDisplayName(LeviathanV2XProtocol.GetEditionDisplayName(edition));
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or UnauthorizedAccessException)
        {
            WriteDiagnostic(definition.ProductId,
                $"optional edition read failed: {exception.GetType().Name}: {exception.Message}");
            return definition;
        }
    }
}
