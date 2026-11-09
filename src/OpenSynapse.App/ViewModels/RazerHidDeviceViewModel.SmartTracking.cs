using Microsoft.UI.Xaml;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private bool _smartTrackingAsymmetric;
    private double _smartTrackingDistance = 1;
    private bool _isSmartTrackingBusy;
    private string _smartTrackingStatusText = string.Empty;

    public bool SmartTrackingAsymmetric
    {
        get => _smartTrackingAsymmetric;
        set => SetField(ref _smartTrackingAsymmetric, value);
    }

    public double SmartTrackingDistance
    {
        get => _smartTrackingDistance;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 0, 2);
            if (SetField(ref _smartTrackingDistance, clamped))
            {
                OnPropertyChanged(nameof(SmartTrackingDistanceText));
            }
        }
    }

    public string SmartTrackingDistanceText => AppStrings.Text(_smartTrackingDistance switch
    {
        <= 0 => "RazerHidSmartTrackingLow",
        >= 2 => "RazerHidSmartTrackingHigh",
        _ => "RazerHidSmartTrackingMedium",
    });

    public string SmartTrackingStatusText
    {
        get => _smartTrackingStatusText;
        private set => SetField(ref _smartTrackingStatusText, value);
    }

    public Visibility SmartTrackingVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.ProximitySensorWrite));

    public bool IsSmartTrackingBusy
    {
        get => _isSmartTrackingBusy;
        private set => SetBusy(
            ref _isSmartTrackingBusy, value,
            nameof(CanApplySmartTracking),
            nameof(CanEditSmartTracking));
    }

    public bool CanEditSmartTracking =>
        CanWrite && Has(RazerHidBackendCapability.ProximitySensorWrite) && !IsSmartTrackingBusy;

    public bool CanApplySmartTracking => CanEditSmartTracking;

    [NotProfileScoped]
    public Task ApplySmartTrackingAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            RazerHidBackendCapability.ProximitySensorWrite,
            () => IsSmartTrackingBusy,
            value => IsSmartTrackingBusy = value,
            async () =>
            {
                await _service.SetSmartTrackingAsync(
                    Connection,
                    SmartTrackingAsymmetric,
                    (int)Math.Round(SmartTrackingDistance) + 1,
                    cancellationToken).ConfigureAwait(false);
                SmartTrackingStatusText = AppStrings.Text("RazerHidSmartTrackingApplied");
            },
            cancellationToken);

    private void NotifySmartTrackingChanged()
    {
        OnPropertyChanged(nameof(SmartTrackingVisibility));
        OnPropertyChanged(nameof(SmartTrackingAsymmetric));
        OnPropertyChanged(nameof(SmartTrackingDistance));
        OnPropertyChanged(nameof(SmartTrackingDistanceText));
        OnPropertyChanged(nameof(SmartTrackingStatusText));
        OnPropertyChanged(nameof(CanEditSmartTracking));
        OnPropertyChanged(nameof(CanApplySmartTracking));
    }
}
