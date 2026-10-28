using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class OpenRazerDeviceViewModel
{
    private bool _isWirelessBusy;
    private OpenRazerWirelessDeviceRowViewModel? _selectedWirelessDevice;

    public ObservableCollection<OpenRazerWirelessDeviceRowViewModel> WirelessDevices { get; } = new();
    public Visibility WirelessVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.WirelessConnectionStateRead,
        OpenRazerBackendCapability.WirelessPairingWrite));
    public Visibility WirelessWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.WirelessPairingWrite));
    public Visibility WirelessEmptyVisibility => VisibleWhen(WirelessDevices.Count == 0);
    public OpenRazerWirelessDeviceRowViewModel? SelectedWirelessDevice
    {
        get => _selectedWirelessDevice;
        set
        {
            if (SetField(ref _selectedWirelessDevice, value))
                OnPropertyChanged(nameof(CanUnpairWireless));
        }
    }
    public bool IsWirelessBusy
    {
        get => _isWirelessBusy;
        private set
        {
            if (!SetField(ref _isWirelessBusy, value)) return;
            OnPropertyChanged(nameof(CanEditWireless));
            OnPropertyChanged(nameof(CanRefreshWireless));
            OnPropertyChanged(nameof(CanUnpairWireless));
        }
    }
    public bool CanEditWireless => CanUse(OpenRazerBackendCapability.WirelessPairingWrite) && !IsWirelessBusy;
    public bool CanRefreshWireless =>
        CanUse(OpenRazerBackendCapability.WirelessConnectionStateRead) && !IsWirelessBusy;
    public bool CanUnpairWireless => CanEditWireless && SelectedWirelessDevice is not null;

    public async Task LoadWirelessAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(OpenRazerBackendCapability.WirelessConnectionStateRead) || IsWirelessBusy)
        {
            return;
        }

        IsWirelessBusy = true;
        try
        {
            await RefreshWirelessAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, OpenRazerBackendCapability.WirelessConnectionStateRead);
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    public Task UnpairWirelessAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedWirelessDevice is not { } device)
        {
            return Task.CompletedTask;
        }

        var productId = device.ProductId;
        return RunWriteAsync(OpenRazerBackendCapability.WirelessPairingWrite,
            () => IsWirelessBusy, value => IsWirelessBusy = value,
            async () =>
            {
                await _service.UnpairWirelessDeviceAsync(Connection, productId, cancellationToken);
                await RefreshWirelessAsync(cancellationToken);
            }, cancellationToken);
    }

    private async Task RefreshWirelessAsync(CancellationToken cancellationToken)
    {
        var statuses = await _service.GetMultipleDeviceWirelessConnectionStatusV2Async(
            Connection, cancellationToken);
        SelectedWirelessDevice = null;
        WirelessDevices.Clear();
        foreach (var status in statuses)
        {
            var name = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, status.ProductId)?.DisplayName;
            WirelessDevices.Add(new OpenRazerWirelessDeviceRowViewModel(status.ProductId, status.Status, name));
        }
        OnPropertyChanged(nameof(WirelessEmptyVisibility));
    }
}

public sealed class OpenRazerWirelessDeviceRowViewModel : INotifyPropertyChanged
{
    public OpenRazerWirelessDeviceRowViewModel(ushort productId, byte status, string? name)
    {
        ProductId = productId;
        Status = status;
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ushort ProductId { get; }
    public byte Status { get; }
    public string? Name { get; }
    public string Title => Name ?? $"PID {ProductId:X4}";
    public string Detail => Name is null
        ? AppStrings.FormatText("OpenRazerWirelessStatus", Status)
        : $"PID {ProductId:X4} · {AppStrings.FormatText("OpenRazerWirelessStatus", Status)}";

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
