using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class FirmwareWorkspace : INotifyPropertyChanged
{
    private readonly RazerFirmwareUpdateClient _firmwareUpdateClient;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _deviceDescriptors;
    private FirmwareVersions _firmwareVersions = FirmwareVersions.Empty;
    private string _biosText = "--";
    private string _ecText = "--";
    private string _mcuText = "--";
    private string _vbiosText = "--";
    private string _updateStatusText = string.Empty;
    private string _downloadUrl = string.Empty;
    private bool _isCheckingUpdate;
    private bool _hasUpdate;

    internal FirmwareWorkspace(
        RazerFirmwareUpdateClient firmwareUpdateClient,
        Func<IReadOnlyList<DeviceDescriptor>> deviceDescriptors)
    {
        _firmwareUpdateClient = firmwareUpdateClient;
        _deviceDescriptors = deviceDescriptors;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BiosText { get => _biosText; private set => SetField(ref _biosText, value); }
    public string EcText { get => _ecText; private set => SetField(ref _ecText, value); }
    public string McuText { get => _mcuText; private set => SetField(ref _mcuText, value); }
    public string VbiosText { get => _vbiosText; private set => SetField(ref _vbiosText, value); }
    public string UpdateStatusText { get => _updateStatusText; private set => SetField(ref _updateStatusText, value); }
    public string DownloadUrl
    {
        get => _downloadUrl;
        private set
        {
            if (SetField(ref _downloadUrl, value))
            {
                OnPropertyChanged(nameof(DownloadAvailableVisibility));
            }
        }
    }
    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (SetField(ref _hasUpdate, value))
            {
                OnPropertyChanged(nameof(UpdateAvailableVisibility));
                OnPropertyChanged(nameof(DownloadAvailableVisibility));
            }
        }
    }
    public Visibility UpdateAvailableVisibility =>
        HasUpdate ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DownloadAvailableVisibility =>
        HasUpdate && !string.IsNullOrWhiteSpace(DownloadUrl)
            ? Visibility.Visible
            : Visibility.Collapsed;
    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set
        {
            if (SetField(ref _isCheckingUpdate, value))
            {
                OnPropertyChanged(nameof(CanCheckUpdate));
            }
        }
    }
    public bool CanCheckUpdate =>
        !IsCheckingUpdate &&
        _deviceDescriptors().Any(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
            device.Access == DeviceAccessState.Available);

    internal void RefreshVersions()
    {
        var (bios, ec, vbios) = WindowsFirmwareVersionReader.Read();
        _firmwareVersions = _firmwareVersions with { Bios = bios, Ec = ec, Vbios = vbios };
        ApplyTexts();
        OnPropertyChanged(nameof(CanCheckUpdate));
    }

    internal void ApplyMcuVersion(string? mcuVersion)
    {
        if (!string.IsNullOrWhiteSpace(mcuVersion))
        {
            _firmwareVersions = _firmwareVersions with { Mcu = mcuVersion };
        }

        ApplyTexts();
    }

    internal void ResetMcu()
    {
        _firmwareVersions = _firmwareVersions with { Mcu = "--" };
        ApplyTexts();
    }

    internal void NotifyDeviceStateChanged() => OnPropertyChanged(nameof(CanCheckUpdate));

    public async Task CheckUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanCheckUpdate)
        {
            return;
        }

        IsCheckingUpdate = true;
        HasUpdate = false;
        UpdateStatusText = AppStrings.Text("FirmwareUpdateChecking");
        try
        {
            var result = await _firmwareUpdateClient.CheckAsync(_firmwareVersions, _deviceDescriptors(), cancellationToken);
            DownloadUrl = result.DownloadUrl ?? string.Empty;
            HasUpdate = result.HasUpdate;
            UpdateStatusText = result.HasUpdate
                ? AppStrings.FormatText("FirmwareUpdateAvailable", result.DeviceName is { Length: > 0 } name ? $"{name}: {string.Join(", ", result.NewerComponents)}" : string.Join(", ", result.NewerComponents))
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
            UpdateStatusText = exception is RazerSupportPageNotFoundException
                ? AppStrings.Text("FirmwareUpdateSupportPageUnavailable")
                : AppStrings.FormatText("FirmwareUpdateFailed", exception.Message);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private void ApplyTexts()
    {
        BiosText = _firmwareVersions.Bios;
        EcText = _firmwareVersions.Ec;
        McuText = _firmwareVersions.Mcu;
        VbiosText = _firmwareVersions.Vbios;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
