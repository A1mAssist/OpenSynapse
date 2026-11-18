using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerMappingAssetService
{
    private static bool IsExpectedBundle(ushort productId, RazerMappingBundle bundle)
    {
        var expected = RazerHidDeviceCatalog.BuiltIn
            .Find(RazerHidDeviceDefinition.VendorId, productId)?.DisplayName;
        if (expected is null || bundle.DeviceName is not { Length: > 0 } actual)
        {
            return true;
        }

        if (RazerMappingDeviceIdentity.Matches(expected, actual))
        {
            return true;
        }

        Log.TryWrite(
            "mapping-catalog",
            $"pid={productId:X4} bundle-mismatch expected='{expected}' actual='{actual}'");
        return false;
    }
}
