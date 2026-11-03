using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    // The analog-key short commands address class id 1 on the Huntsman V3 Pro and Mako.
    private const byte AnalogKeyClassId = 0x01;
    private bool _isAnalogKeyBusy;
    private bool _analogKeyContinuousRapidTrigger;
    private byte _analogKeyBottomDeadZone;
    private byte _analogReportMode;

    public Visibility AnalogKeyVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.AnalogKeyRead, RazerHidBackendCapability.AnalogKeyWrite));
    public Visibility AnalogKeyWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.AnalogKeyWrite));
    public bool AnalogKeyContinuousRapidTrigger
    {
        get => _analogKeyContinuousRapidTrigger;
        set => SetField(ref _analogKeyContinuousRapidTrigger, value);
    }
    public byte AnalogKeyBottomDeadZone
    {
        get => _analogKeyBottomDeadZone;
        set => SetField(ref _analogKeyBottomDeadZone, value);
    }
    public byte AnalogReportMode
    {
        get => _analogReportMode;
        set => SetField(ref _analogReportMode, value);
    }
    public bool IsAnalogKeyBusy
    {
        get => _isAnalogKeyBusy;
        private set => SetBusy(ref _isAnalogKeyBusy, value, nameof(CanEditAnalogKey));
    }
    public bool CanEditAnalogKey => CanUse(RazerHidBackendCapability.AnalogKeyWrite) && !IsAnalogKeyBusy;

    public async Task LoadAnalogKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(RazerHidBackendCapability.AnalogKeyRead) || IsAnalogKeyBusy)
        {
            return;
        }

        IsAnalogKeyBusy = true;
        try
        {
            await RefreshAnalogKeyAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleOptionalFailure(exception, RazerHidBackendCapability.AnalogKeyRead);
        }
        finally
        {
            IsAnalogKeyBusy = false;
        }
    }

    public Task ApplyAnalogKeyAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.AnalogKeyWrite,
            () => IsAnalogKeyBusy, value => IsAnalogKeyBusy = value,
            async () =>
            {
                await _service.SetAnalogKeyContinuousRapidTriggerAsync(
                    Connection, AnalogKeyClassId, AnalogKeyContinuousRapidTrigger, cancellationToken);
                await _service.SetAnalogKeyBottomDeadZoneAsync(
                    Connection, AnalogKeyClassId, AnalogKeyBottomDeadZone, cancellationToken);
                await _service.SetAnalogReportModeAsync(Connection, AnalogReportMode, cancellationToken);
                if (Has(RazerHidBackendCapability.AnalogKeyRead))
                {
                    await RefreshAnalogKeyAsync(cancellationToken);
                }
            }, cancellationToken);

    private async Task RefreshAnalogKeyAsync(CancellationToken cancellationToken)
    {
        AnalogKeyContinuousRapidTrigger =
            (await _service.GetAnalogKeyContinuousRapidTriggerAsync(Connection, AnalogKeyClassId, cancellationToken)).Enabled;
        AnalogKeyBottomDeadZone =
            (await _service.GetAnalogKeyBottomDeadZoneAsync(Connection, AnalogKeyClassId, cancellationToken)).Value;
        AnalogReportMode = await _service.GetAnalogReportModeAsync(Connection, cancellationToken);
    }

}
