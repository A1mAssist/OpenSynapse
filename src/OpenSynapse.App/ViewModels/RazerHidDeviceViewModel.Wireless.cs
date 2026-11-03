using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private bool _isWirelessBusy;
    private RazerHidWirelessDeviceRowViewModel? _selectedWirelessDevice;

    public ObservableCollection<RazerHidWirelessDeviceRowViewModel> WirelessDevices { get; } = new();
    public Visibility WirelessVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.WirelessConnectionStateRead,
        RazerHidBackendCapability.WirelessPairingWrite));
    public Visibility WirelessWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.WirelessPairingWrite));
    public Visibility WirelessEmptyVisibility => VisibleWhen(WirelessDevices.Count == 0);
    public RazerHidWirelessDeviceRowViewModel? SelectedWirelessDevice
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
    public bool CanEditWireless => CanUse(RazerHidBackendCapability.WirelessPairingWrite) && !IsWirelessBusy;
    public bool CanRefreshWireless =>
        CanUse(RazerHidBackendCapability.WirelessConnectionStateRead) && !IsWirelessBusy;
    public bool CanUnpairWireless => CanEditWireless && SelectedWirelessDevice is not null;

    public async Task LoadWirelessAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(RazerHidBackendCapability.WirelessConnectionStateRead) || IsWirelessBusy)
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
            HandleOptionalFailure(exception, RazerHidBackendCapability.WirelessConnectionStateRead);
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
        return RunWriteAsync(RazerHidBackendCapability.WirelessPairingWrite,
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
            var name = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, status.ProductId)?.DisplayName;
            WirelessDevices.Add(new RazerHidWirelessDeviceRowViewModel(status.ProductId, status.Status, name));
        }
        OnPropertyChanged(nameof(WirelessEmptyVisibility));
    }
}

public sealed class RazerHidWirelessDeviceRowViewModel : INotifyPropertyChanged
{
    public RazerHidWirelessDeviceRowViewModel(ushort productId, byte status, string? name)
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
        ? AppStrings.FormatText("RazerHidWirelessStatus", Status)
        : $"PID {ProductId:X4} · {AppStrings.FormatText("RazerHidWirelessStatus", Status)}";

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
