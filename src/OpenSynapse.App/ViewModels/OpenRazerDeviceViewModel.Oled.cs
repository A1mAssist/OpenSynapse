using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class OpenRazerDeviceViewModel
{
    private bool _isOledBusy;
    private byte _oledBrightness;
    private byte _oledScreensaverMode;
    private byte _oledScreensaverItem;

    public Visibility OledVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.OledRead, OpenRazerBackendCapability.OledWrite));
    public Visibility OledWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.OledWrite));
    public byte OledBrightness
    {
        get => _oledBrightness;
        set => SetField(ref _oledBrightness, value);
    }
    public byte OledScreensaverMode
    {
        get => _oledScreensaverMode;
        private set => SetField(ref _oledScreensaverMode, value);
    }
    public byte OledScreensaverItem
    {
        get => _oledScreensaverItem;
        private set => SetField(ref _oledScreensaverItem, value);
    }
    public bool IsOledBusy { get => _isOledBusy; private set => SetBusy(ref _isOledBusy, value, nameof(CanEditOled)); }
    public bool CanEditOled => CanUse(OpenRazerBackendCapability.OledWrite) && !IsOledBusy;

    public async Task LoadOledAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(OpenRazerBackendCapability.OledRead) || IsOledBusy)
        {
            return;
        }

        IsOledBusy = true;
        try
        {
            await RefreshOledAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, OpenRazerBackendCapability.OledRead);
        }
        finally
        {
            IsOledBusy = false;
        }
    }

    public Task ApplyOledAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.OledWrite,
            () => IsOledBusy, value => IsOledBusy = value,
            async () =>
            {
                await _service.SetOledBrightnessAsync(Connection, OledBrightness, cancellationToken);
                if (Has(OpenRazerBackendCapability.OledRead))
                {
                    await RefreshOledAsync(cancellationToken);
                }
            }, cancellationToken);

    private async Task RefreshOledAsync(CancellationToken cancellationToken)
    {
        OledBrightness = await _service.GetOledBrightnessAsync(Connection, cancellationToken);
        var screensaver = await _service.GetOledScreensaverOptionAsync(Connection, cancellationToken);
        OledScreensaverMode = screensaver.Mode;
        OledScreensaverItem = screensaver.Item;
    }
}
