using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed record RazerPairingReceiverItem(RazerHidDeviceRowViewModel Device, string Title);

public sealed record RazerPairingCandidateItem(ushort ProductId, string Title, DeviceCategory Category);

public sealed class RazerPairingDialogViewModel : INotifyPropertyChanged
{
    private const int PairingPollAttempts = 30;
    private readonly RazerHidDeviceService _service;
    private RazerPairingReceiverItem? _selectedReceiver;
    private RazerPairingCandidateItem? _selectedCandidate;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private InfoBarSeverity _statusSeverity;

    public RazerPairingDialogViewModel(
        IReadOnlyList<RazerHidDeviceRowViewModel> devices,
        RazerHidDeviceService service)
    {
        _service = service;
        foreach (var device in devices.Where(device => device.CanPairNewDevice))
        {
            Receivers.Add(new RazerPairingReceiverItem(
                device,
                $"{device.Name} · PID {device.Connection.Definition.ProductId:X4}"));
        }

        if (Receivers.Count == 1)
        {
            SelectedReceiver = Receivers[0];
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RazerPairingReceiverItem> Receivers { get; } = new();

    public ObservableCollection<RazerPairingCandidateItem> Candidates { get; } = new();

    public RazerPairingReceiverItem? SelectedReceiver
    {
        get => _selectedReceiver;
        set
        {
            if (!SetField(ref _selectedReceiver, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanStart));
            _ = LoadCandidatesAsync();
        }
    }

    public RazerPairingCandidateItem? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetField(ref _selectedCandidate, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanPick));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(IsStatusOpen));
            }
        }
    }

    public InfoBarSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetField(ref _statusSeverity, value);
    }

    public bool IsStatusOpen => !string.IsNullOrEmpty(StatusText);

    public bool CanStart => !IsBusy && SelectedReceiver is not null && SelectedCandidate is not null;

    public bool CanPick => !IsBusy;

    public Visibility ReceiverPickerVisibility =>
        Receivers.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || SelectedReceiver is not { } receiver || SelectedCandidate is not { } candidate)
        {
            return false;
        }

        IsBusy = true;
        SetStatus(AppStrings.Text("RazerDualDonglePairing"), InfoBarSeverity.Informational);
        var connection = receiver.Device.Connection;
        var productId = candidate.ProductId;
        try
        {
            var connected = await TryReadConnectedAsync(connection, cancellationToken);
            if (connected?.Contains(productId) == true)
            {
                SetStatus(AppStrings.FormatText("RazerDualDongleSuccess", candidate.Title),
                    InfoBarSeverity.Success);
                return true;
            }

            await _service.PairWirelessCandidateAsync(connection, productId, cancellationToken);
            for (var attempt = 0; attempt < PairingPollAttempts; attempt++)
            {
                await Task.Delay(1000, cancellationToken);
                connected = await TryReadConnectedAsync(connection, cancellationToken);
                if (connected?.Contains(productId) == true)
                {
                    SetStatus(AppStrings.FormatText("RazerDualDongleSuccess", candidate.Title),
                        InfoBarSeverity.Success);
                    return true;
                }
            }

            SetStatus(AppStrings.Text("RazerDualDongleTimeout"), InfoBarSeverity.Warning);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            SetStatus(AppStrings.Text("RazerDualDongleInvalid"), InfoBarSeverity.Error);
            return false;
        }
        finally
        {
            try
            {
                await _service.SetDeviceScanAsync(
                    connection, RazerPairingProtocol.ScanCancel, CancellationToken.None);
            }
            catch (Exception)
            {
                // Best effort: the receiver stops scanning on its own timeout.
            }

            IsBusy = false;
        }
    }

    private async Task LoadCandidatesAsync()
    {
        var receiver = SelectedReceiver;
        Candidates.Clear();
        SelectedCandidate = null;
        StatusText = string.Empty;
        if (receiver is null)
        {
            return;
        }

        try
        {
            var hostProductId = receiver.Device.Connection.Definition.ProductId;
            IReadOnlyList<ushort> connected;
            var readSucceeded = true;
            try
            {
                var statuses = await _service.GetMultipleDeviceWirelessConnectionStatusV2Async(
                    receiver.Device.Connection);
                connected = statuses
                    .Where(status => status.ProductId != ushort.MaxValue)
                    .Select(status => status.ProductId)
                    .Distinct()
                    .ToArray();
            }
            catch (Exception)
            {
                readSucceeded = false;
                connected = Array.Empty<ushort>();
            }

            // A slower read for a receiver the user already left must not replace the list.
            if (!ReferenceEquals(SelectedReceiver, receiver))
            {
                return;
            }

            var culture = CultureInfo.GetCultureInfo(AppLanguageSettings.Effective);
            foreach (var candidate in RazerDualDongleCatalog.FilterCandidates(hostProductId, connected))
            {
                Candidates.Add(new RazerPairingCandidateItem(
                    candidate.DongleProductId,
                    RazerDualDongleCatalog.DisplayName(candidate.DongleProductId, culture),
                    candidate.Category));
            }

            if (!readSucceeded)
            {
                SetStatus(AppStrings.Text("RazerDualDongleStatusUnknown"), InfoBarSeverity.Warning);
            }
            else if (Candidates.Count == 0)
            {
                SetStatus(AppStrings.Text("RazerDualDongleNoCandidate"), InfoBarSeverity.Warning);
            }
        }
        catch (Exception)
        {
            if (ReferenceEquals(SelectedReceiver, receiver))
            {
                SetStatus(AppStrings.Text("RazerDualDongleStatusUnknown"), InfoBarSeverity.Warning);
            }
        }
    }

    private async Task<HashSet<ushort>?> TryReadConnectedAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            var statuses = await _service.GetMultipleDeviceWirelessConnectionStatusV2Async(
                connection, cancellationToken);
            return statuses
                .Where(status => status.ProductId != ushort.MaxValue)
                .Select(status => status.ProductId)
                .ToHashSet();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // One failed poll means "not seen yet", not a failed pairing.
            return null;
        }
    }

    private void SetStatus(string text, InfoBarSeverity severity)
    {
        StatusText = text;
        StatusSeverity = severity;
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
