using Microsoft.UI.Xaml;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private bool _sensorRotationKnown;
    private bool _sensorRotationEnabled;
    private double _sensorRotationAngle;
    private bool _isSensorRotationBusy;
    private string _sensorRotationStatusText = string.Empty;

    public bool SensorRotationEnabled
    {
        get => _sensorRotationEnabled;
        set => SetField(ref _sensorRotationEnabled, value);
    }

    public double SensorRotationAngle
    {
        get => _sensorRotationAngle;
        set => SetField(ref _sensorRotationAngle, Math.Clamp(
            value,
            RazerProximitySensorProtocol.MinimumSensorRotationAngle,
            RazerProximitySensorProtocol.MaximumSensorRotationAngle));
    }

    public string SensorRotationStatusText
    {
        get => _sensorRotationStatusText;
        private set => SetField(ref _sensorRotationStatusText, value);
    }

    public Visibility SensorRotationVisibility =>
        VisibleWhen(_sensorRotationKnown && Has(RazerHidBackendCapability.SensorRotationRead));

    public Visibility SensorRotationWriteVisibility =>
        VisibleWhen(_sensorRotationKnown && Has(RazerHidBackendCapability.SensorRotationWrite));

    public bool IsSensorRotationBusy
    {
        get => _isSensorRotationBusy;
        private set => SetBusy(
            ref _isSensorRotationBusy, value,
            nameof(CanApplySensorRotation),
            nameof(CanEditSensorRotation));
    }

    public bool CanWriteSensorRotation =>
        CanWrite && Has(RazerHidBackendCapability.SensorRotationWrite) && !IsSensorRotationBusy;

    public bool CanEditSensorRotation => _sensorRotationKnown && CanWriteSensorRotation;

    public bool CanApplySensorRotation => CanEditSensorRotation;

    private void ApplySensorRotation(RazerHidSensorRotation rotation)
    {
        _sensorRotationKnown = true;
        SensorRotationEnabled = rotation.Enabled;
        SensorRotationAngle = rotation.Angle;
        if (!rotation.IsAngleValid)
        {
            SensorRotationStatusText = string.Format(
                AppStrings.Text("RazerHidSensorRotationInvalid"), rotation.RawAngle);
        }
        else
        {
            SensorRotationStatusText = string.Empty;
        }
        // Low-frequency device state: refresh the visibility/editability derived
        // from _sensorRotationKnown in one go.
        NotifyStateChanged();
    }

    private void ClearSensorRotation()
    {
        _sensorRotationKnown = false;
        SensorRotationStatusText = string.Empty;
        NotifyStateChanged();
    }

    [NotProfileScoped]
    public Task ApplySensorRotationAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            RazerHidBackendCapability.SensorRotationWrite,
            () => IsSensorRotationBusy,
            value => IsSensorRotationBusy = value,
            async () =>
            {
                var angle = (int)Math.Round(SensorRotationAngle);
                // Called from a click handler; the read-back and status text below are
                // bound to the page and must raise PropertyChanged on the UI thread.
                await _service.SetSensorRotationAsync(
                    Connection, SensorRotationEnabled, angle, cancellationToken).ConfigureAwait(true);
                try
                {
                    ApplySensorRotation(await _service.GetSensorRotationAsync(
                        Connection, cancellationToken).ConfigureAwait(true));
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // The write landed; a slow read-back must not turn it into a failure.
                    ApplySensorRotation(new RazerHidSensorRotation(
                        RazerProximitySensorProtocol.DefaultAngleTuneProfileId,
                        SensorRotationEnabled,
                        angle,
                        unchecked((byte)angle)));
                }
                SensorRotationStatusText = AppStrings.Text("RazerHidSensorRotationApplied");
            },
            cancellationToken);

    private void NotifySensorRotationChanged()
    {
        OnPropertyChanged(nameof(SensorRotationVisibility));
        OnPropertyChanged(nameof(SensorRotationWriteVisibility));
        OnPropertyChanged(nameof(SensorRotationEnabled));
        OnPropertyChanged(nameof(SensorRotationAngle));
        OnPropertyChanged(nameof(SensorRotationStatusText));
        OnPropertyChanged(nameof(CanWriteSensorRotation));
        OnPropertyChanged(nameof(CanEditSensorRotation));
        OnPropertyChanged(nameof(CanApplySensorRotation));
    }
}
