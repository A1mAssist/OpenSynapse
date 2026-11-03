using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private bool _isOledBusy;
    private byte _oledBrightness;
    private byte _oledScreensaverMode;
    private byte _oledScreensaverItem;
    private byte _oledLanguage;
    private int _selectedOledLanguageIndex;

    // rzDevice25 OLED language enum: ENGLISH 0, SIMPLIFIED_CHINESE 1, JAPANESE 2,
    // GERMAN 3, KOREAN 4, FRENCH 5. Values 6, 80 and 255 are internal markers.
    private static readonly byte[] OledLanguageValues = [0, 1, 2, 3, 4, 5];

    public Visibility OledVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.OledRead, RazerHidBackendCapability.OledWrite));
    public Visibility OledWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.OledWrite));
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
    public bool CanEditOled => CanUse(RazerHidBackendCapability.OledWrite) && !IsOledBusy;

    public IReadOnlyList<string> OledLanguageOptions =>
    [
        AppStrings.Text("RazerHidOledLanguageEnglish"),
        AppStrings.Text("RazerHidOledLanguageChinese"),
        AppStrings.Text("RazerHidOledLanguageJapanese"),
        AppStrings.Text("RazerHidOledLanguageGerman"),
        AppStrings.Text("RazerHidOledLanguageKorean"),
        AppStrings.Text("RazerHidOledLanguageFrench"),
    ];

    public int SelectedOledLanguageIndex
    {
        get => _selectedOledLanguageIndex;
        set => SetField(ref _selectedOledLanguageIndex, Math.Clamp(value, 0, OledLanguageValues.Length - 1));
    }

    public async Task LoadOledAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(RazerHidBackendCapability.OledRead) || IsOledBusy)
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
            HandleOptionalFailure(exception, RazerHidBackendCapability.OledRead);
        }
        finally
        {
            IsOledBusy = false;
        }
    }

    public Task ApplyOledAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.OledWrite,
            () => IsOledBusy, value => IsOledBusy = value,
            async () =>
            {
                await _service.SetOledBrightnessAsync(Connection, OledBrightness, cancellationToken);
                await _service.SetOledLanguageAsync(
                    Connection, OledLanguageValues[SelectedOledLanguageIndex], cancellationToken);
                if (Has(RazerHidBackendCapability.OledRead))
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
        _oledLanguage = await _service.GetOledLanguageAsync(Connection, cancellationToken);
        var languageIndex = Array.IndexOf(OledLanguageValues, _oledLanguage);
        if (languageIndex >= 0)
        {
            SelectedOledLanguageIndex = languageIndex;
        }
    }
}
