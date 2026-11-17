using Microsoft.UI.Dispatching;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private const ushort RiMouseLeftDown = 0x0001;
    private const ushort RiMouseLeftUp = 0x0002;
    private const ushort RiMouseRightDown = 0x0004;

    private MouseRotationCalibrator? _rotationCalibrator;
    private IDisposable? _rotationSubscription;
    private DispatcherQueue? _rotationDispatcher;
    private ushort _rotationProductId;

    /// <summary>
    /// Starts Razer's sensor-rotation measurement: hold the left button and sweep the
    /// mouse left/right, then press the right button. The measured angle is filled into
    /// the card, never written to the device on its own.
    /// </summary>
    [NotProfileScoped]
    public void BeginRotationCalibration()
    {
        if (!CanEditSensorRotation)
        {
            return;
        }

        // A rotated sensor already compensates the deltas we would measure.
        if (SensorRotationEnabled && Math.Abs(SensorRotationAngle) >= 0.5)
        {
            SensorRotationStatusText = AppStrings.Text("RazerHidSensorRotationCalibrateZeroFirst");
            return;
        }

        CancelRotationCalibration();
        _rotationProductId = Connection.Definition.ProductId;
        _rotationCalibrator = new MouseRotationCalibrator();
        _rotationDispatcher = DispatcherQueue.GetForCurrentThread();
        _rotationSubscription = RawInputRouter.Subscribe(ProcessRotationFrame, includeMouse: true);
        SensorRotationStatusText = AppStrings.Text("RazerHidSensorRotationCalibrateHint");
    }

    private void ProcessRotationFrame(RawInputFrame frame)
    {
        if (!frame.IsMouse || _rotationCalibrator is not { } calibrator)
        {
            return;
        }

        // Several mice can be attached; only the card's own model feeds the measurement.
        if (!DeviceIdParser.TryParse(frame.DevicePath, out var vendorId, out var productId) ||
            vendorId != RazerHidDeviceDefinition.VendorId ||
            productId != _rotationProductId)
        {
            return;
        }

        if ((frame.MouseFlags & RiMouseLeftDown) != 0)
        {
            calibrator.BeginStroke();
        }

        if (frame.DeltaX != 0 || frame.DeltaY != 0)
        {
            calibrator.AddDelta(frame.DeltaX, frame.DeltaY);
        }

        if ((frame.MouseFlags & RiMouseLeftUp) != 0)
        {
            calibrator.EndStroke();
        }

        if ((frame.MouseFlags & RiMouseRightDown) != 0)
        {
            var pending = calibrator;
            _rotationDispatcher?.TryEnqueue(() => FinishRotationCalibration(pending));
        }
    }

    private void FinishRotationCalibration(MouseRotationCalibrator calibrator)
    {
        // A restart clicked before this callback ran must not consume the old sweep.
        if (!ReferenceEquals(calibrator, _rotationCalibrator))
        {
            return;
        }

        CancelRotationCalibration();
        if (calibrator.AverageAngleDegrees is not { } angle)
        {
            SensorRotationStatusText = AppStrings.Text("RazerHidSensorRotationCalibrateTooShort");
            return;
        }

        var rounded = Math.Round(
            Math.Clamp(
                angle,
                RazerProximitySensorProtocol.MinimumSensorRotationAngle,
                RazerProximitySensorProtocol.MaximumSensorRotationAngle),
            1);
        SensorRotationAngle = rounded;
        SensorRotationStatusText = AppStrings.FormatText("RazerHidSensorRotationCalibrated", rounded);
    }

    private void CancelRotationCalibration()
    {
        _rotationSubscription?.Dispose();
        _rotationSubscription = null;
        _rotationCalibrator = null;
    }
}
