using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerBleDeviceViewModel
{
    public Visibility CalibrationConnectionVisibility => IsLayla ? Visibility.Collapsed : Visibility.Visible;

    public string CalibrationConnectionText => AppStrings.Text(
        RazerProximitySensorProfiles.GetConnectionSupport(_device.ProductId, RazerCalibrationConnection.Ble) ==
            RazerCalibrationSupport.Unsupported ? "SurfaceCalibrationUseUsb" : "SurfaceCalibrationConnectionUnknown");
}
