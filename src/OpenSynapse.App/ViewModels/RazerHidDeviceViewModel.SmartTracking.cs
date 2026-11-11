using Microsoft.UI.Xaml;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private int _smartTrackingModeIndex;
    private double _smartTrackingDistance = 2;
    private int _smartTrackingLiftOffDistance = 13;
    private int _smartTrackingLandingDistance = 12;
    private bool _isSmartTrackingBusy;
    private string _smartTrackingStatusText = string.Empty;

    public IReadOnlyList<string> SmartTrackingModeOptions => AppStrings.Texts(
        "RazerHidSmartTrackingModeAdaptive",
        "RazerHidSmartTrackingModeAdjustable");

    public int SmartTrackingModeIndex
    {
        get => _smartTrackingModeIndex;
        set
        {
            if (SetField(ref _smartTrackingModeIndex, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(SmartTrackingAdaptiveVisibility));
                OnPropertyChanged(nameof(SmartTrackingAdjustableVisibility));
            }
        }
    }

    public double SmartTrackingDistance
    {
        get => _smartTrackingDistance;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 1, 3);
            if (SetField(ref _smartTrackingDistance, clamped))
            {
                OnPropertyChanged(nameof(SmartTrackingDistanceText));
            }
        }
    }

    public int SmartTrackingLiftOffDistance
    {
        get => _smartTrackingLiftOffDistance;
        set => SetField(ref _smartTrackingLiftOffDistance, Math.Clamp(value, 1, 256));
    }

    public int SmartTrackingLandingDistance
    {
        get => _smartTrackingLandingDistance;
        set => SetField(ref _smartTrackingLandingDistance, Math.Clamp(value, 1, 256));
    }

    public string SmartTrackingDistanceText => AppStrings.Text(_smartTrackingDistance switch
    {
        <= 1 => "RazerHidSmartTrackingLow",
        >= 3 => "RazerHidSmartTrackingHigh",
        _ => "RazerHidSmartTrackingMedium",
    });

    public string SmartTrackingStatusText
    {
        get => _smartTrackingStatusText;
        private set => SetField(ref _smartTrackingStatusText, value);
    }

    public Visibility SmartTrackingVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.ProximitySensorWrite) &&
            RazerProximitySensorProfiles.SupportsSmartTrackingUi(Connection.Definition.ProductId));

    public Visibility SmartTrackingAdaptiveVisibility =>
        VisibleWhen(SmartTrackingModeIndex == 0);

    public Visibility SmartTrackingAdjustableVisibility =>
        VisibleWhen(SmartTrackingModeIndex == 1);

    public bool IsSmartTrackingBusy
    {
        get => _isSmartTrackingBusy;
        private set => SetBusy(
            ref _isSmartTrackingBusy, value,
            nameof(CanApplySmartTracking),
            nameof(CanEditSmartTracking));
    }

    public bool CanEditSmartTracking =>
        CanWrite && Has(RazerHidBackendCapability.ProximitySensorWrite) &&
        RazerProximitySensorProfiles.SupportsSmartTrackingUi(Connection.Definition.ProductId) &&
        !IsSmartTrackingBusy;

    public bool CanApplySmartTracking => CanEditSmartTracking;

    [NotProfileScoped]
    public Task ApplySmartTrackingAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            RazerHidBackendCapability.ProximitySensorWrite,
            () => IsSmartTrackingBusy,
            value => IsSmartTrackingBusy = value,
            async () =>
            {
                var mode = SmartTrackingModeIndex == 1
                    ? RazerSmartTrackingMode.Adjustable
                    : RazerSmartTrackingMode.Adaptive;
                var distance = (int)Math.Round(SmartTrackingDistance);
                var liftOff = SmartTrackingLiftOffDistance;
                var landing = SmartTrackingLandingDistance;
                await _service.SetSmartTrackingAsync(
                    Connection,
                    mode,
                    distance,
                    liftOff,
                    landing,
                    cancellationToken).ConfigureAwait(false);

                try
                {
                    var actual = await _service.GetSmartTrackingAsync(
                        Connection, cancellationToken).ConfigureAwait(false);
                    ApplySmartTracking(actual);
                    SmartTrackingStatusText = AppStrings.Text(MatchesRequested(
                        mode, distance, liftOff, landing, actual)
                        ? "RazerHidSmartTrackingApplied"
                        : "RazerHidSmartTrackingReadbackMismatch");
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // The write landed; a missing read-back must not be reported as a write failure.
                    SmartTrackingStatusText = AppStrings.Text("RazerHidSmartTrackingReadbackUnavailable");
                }
            },
            cancellationToken);

    private void ApplySmartTracking(RazerSmartTrackingState state)
    {
        SmartTrackingModeIndex = state.Mode == RazerSmartTrackingMode.Adjustable ? 1 : 0;
        if (state.Mode == RazerSmartTrackingMode.Adjustable)
        {
            SmartTrackingLiftOffDistance = state.LiftOffDistance;
            SmartTrackingLandingDistance = state.LandingDistance;
        }
        else
        {
            SmartTrackingDistance = state.TrackingDistance;
        }
    }

    private static bool MatchesRequested(
        RazerSmartTrackingMode mode,
        int distance,
        int liftOff,
        int landing,
        RazerSmartTrackingState actual) =>
        mode == RazerSmartTrackingMode.Adjustable
            ? actual.Mode == RazerSmartTrackingMode.Adjustable &&
              actual.LiftOffDistance == liftOff &&
              actual.LandingDistance == landing
            : actual.Mode == RazerSmartTrackingMode.Adaptive &&
              actual.TrackingDistance == distance;

    private void NotifySmartTrackingChanged()
    {
        OnPropertyChanged(nameof(SmartTrackingVisibility));
        OnPropertyChanged(nameof(SmartTrackingAdaptiveVisibility));
        OnPropertyChanged(nameof(SmartTrackingAdjustableVisibility));
        OnPropertyChanged(nameof(SmartTrackingModeOptions));
        OnPropertyChanged(nameof(SmartTrackingModeIndex));
        OnPropertyChanged(nameof(SmartTrackingDistance));
        OnPropertyChanged(nameof(SmartTrackingDistanceText));
        OnPropertyChanged(nameof(SmartTrackingLiftOffDistance));
        OnPropertyChanged(nameof(SmartTrackingLandingDistance));
        OnPropertyChanged(nameof(SmartTrackingStatusText));
        OnPropertyChanged(nameof(CanEditSmartTracking));
        OnPropertyChanged(nameof(CanApplySmartTracking));
    }
}
