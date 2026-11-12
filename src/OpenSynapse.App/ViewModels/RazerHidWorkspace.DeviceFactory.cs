using System.Globalization;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    private RazerHidDeviceViewModel CreateDeviceViewModel(
        RazerHidDeviceConnection connection)
    {
        RazerHidDeviceViewModel device = new(
            _deviceService ?? throw new InvalidOperationException("RazerHid service is unavailable."),
            connection,
            _currentPowerSource,
            (powerState, zone) => ProfileResolver.ResolveRazerHidLighting(
                _getProfile(),
                connection.ToDescriptor(),
                powerState,
                ((byte)zone).ToString(CultureInfo.InvariantCulture),
                ((byte)connection.Definition.DefaultLedZone).ToString(CultureInfo.InvariantCulture)),
            (powerState, zone, profile, token) => SaveLightingAsync(
                connection,
                powerState,
                zone,
                profile,
                token),
            () => IsLightingEnabled(connection),
            () => IsChromaOverrideEnabled(connection),
            (lightingEnabled, chromaOverrideEnabled, token) => SaveLightingSettingsAsync(
                connection,
                lightingEnabled,
                chromaOverrideEnabled,
                token),
            _chromaIntegrationEnabled(),
            _localMacros,
            _getActiveProfile,
            _saveProfileAsync,
            _getMachineSettings,
            _saveMachineSettingsAsync,
            _getProfile,
            message => _diagnosticLog.TryWrite("mapping", message));
        device.AttachMatrixLayout(_matrixLayoutLoader.LoadLayoutAsync, _dispatcherQueue);
        device.Onboard.LocalMacroProvider = _localMacros;
        device.SoftwareMacroBinder = _softwareMacroBinder;
        device.SoftwareMacroClearer = _softwareMacroClearer;
        return device;
    }
}
