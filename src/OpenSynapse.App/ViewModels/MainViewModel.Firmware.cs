using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private FirmwareVersions _firmwareVersions = FirmwareVersions.Empty;
    private string _firmwareBiosText = "--";
    private string _firmwareEcText = "--";
    private string _firmwareMcuText = "--";
    private string _firmwareVbiosText = "--";
    private string _firmwareUpdateStatusText = string.Empty;
    private string _firmwareDownloadUrl = RazerFirmwareUpdateClient.Rz090528BiosUpdater;
    private bool _isCheckingFirmwareUpdate;
    private bool _hasFirmwareUpdate;

    public string FirmwareBiosText { get => _firmwareBiosText; private set => SetField(ref _firmwareBiosText, value); }
    public string FirmwareEcText { get => _firmwareEcText; private set => SetField(ref _firmwareEcText, value); }
    public string FirmwareMcuText { get => _firmwareMcuText; private set => SetField(ref _firmwareMcuText, value); }
    public string FirmwareVbiosText { get => _firmwareVbiosText; private set => SetField(ref _firmwareVbiosText, value); }
    public string FirmwareUpdateStatusText { get => _firmwareUpdateStatusText; private set => SetField(ref _firmwareUpdateStatusText, value); }
    public string FirmwareDownloadUrl { get => _firmwareDownloadUrl; private set => SetField(ref _firmwareDownloadUrl, value); }
    public bool HasFirmwareUpdate
    {
        get => _hasFirmwareUpdate;
        private set
        {
            if (SetField(ref _hasFirmwareUpdate, value))
            {
                OnPropertyChanged(nameof(FirmwareUpdateAvailableVisibility));
            }
        }
    }
    public Visibility FirmwareUpdateAvailableVisibility =>
        HasFirmwareUpdate ? Visibility.Visible : Visibility.Collapsed;
    public bool IsCheckingFirmwareUpdate
    {
        get => _isCheckingFirmwareUpdate;
        private set
        {
            if (SetField(ref _isCheckingFirmwareUpdate, value))
            {
                OnPropertyChanged(nameof(CanCheckFirmwareUpdate));
            }
        }
    }

    public bool CanCheckFirmwareUpdate =>
        !IsCheckingFirmwareUpdate &&
        _deviceDescriptors.Any(device =>
            device.ProtocolFamily == DeviceProtocolFamilies.Blade &&
            device.Access == DeviceAccessState.Available);

    private void RefreshFirmwareVersions()
    {
        var (bios, ec, vbios) = WindowsFirmwareVersionReader.Read();
        _firmwareVersions = _firmwareVersions with { Bios = bios, Ec = ec, Vbios = vbios };
        ApplyFirmwareTexts();
        OnPropertyChanged(nameof(CanCheckFirmwareUpdate));
    }

    private void ApplyFirmwareMcuVersion(string? mcuVersion)
    {
        if (!string.IsNullOrWhiteSpace(mcuVersion))
        {
            _firmwareVersions = _firmwareVersions with { Mcu = mcuVersion };
        }

        ApplyFirmwareTexts();
    }

    private void ApplyFirmwareTexts()
    {
        FirmwareBiosText = _firmwareVersions.Bios;
        FirmwareEcText = _firmwareVersions.Ec;
        FirmwareMcuText = _firmwareVersions.Mcu;
        FirmwareVbiosText = _firmwareVersions.Vbios;
    }

    public async Task CheckFirmwareUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanCheckFirmwareUpdate)
        {
            return;
        }

        IsCheckingFirmwareUpdate = true;
        HasFirmwareUpdate = false;
        FirmwareUpdateStatusText = AppStrings.Text("FirmwareUpdateChecking");
        try
        {
            var result = await _firmwareUpdateClient.CheckAsync(_firmwareVersions, cancellationToken);
            HasFirmwareUpdate = result.HasUpdate;
            FirmwareDownloadUrl = result.NewerComponents.FirstOrDefault() switch
            {
                "EC" => RazerFirmwareUpdateClient.Rz090528EcUpdater,
                "MCU" => RazerFirmwareUpdateClient.Rz090528McuUpdater,
                _ => RazerFirmwareUpdateClient.Rz090528BiosUpdater,
            };
            FirmwareUpdateStatusText = result.HasUpdate
                ? AppStrings.FormatText("FirmwareUpdateAvailable", string.Join(", ", result.NewerComponents))
                : AppStrings.Text("FirmwareUpdateCurrent");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidOperationException or
            UriFormatException or TaskCanceledException)
        {
            FirmwareUpdateStatusText = AppStrings.FormatText(
                "FirmwareUpdateFailed",
                exception.Message);
        }
        finally
        {
            IsCheckingFirmwareUpdate = false;
        }
    }
}
