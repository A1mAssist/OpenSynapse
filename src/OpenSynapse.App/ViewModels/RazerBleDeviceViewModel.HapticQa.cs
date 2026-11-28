#if DEBUG
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerBleDeviceViewModel
{
    internal static RazerBleDeviceViewModel CreateHapticQa(RazerBleDeviceService service,
        ProfileDocument document, Func<CancellationToken, Task<bool>> save) => new(
            new RazerBleDevice("qa-naga-ble", "Naga V2 Pro · BLE QA", 0x00A9),
            getProfile: () => document, saveProfile: save)
        {
            _service = service, _profileIds = new byte[] { 4 },
        };
}
#endif
