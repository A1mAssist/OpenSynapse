using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Body controls for the standalone accessories that previously stopped at lighting.
/// The Chroma page owns discovery and selection; this view model adds the device-specific
/// fan, haptic, audio, dial or dock sections under the lighting card.
/// </summary>
public sealed class RazerAccessoryBodyViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ushort _productId;
    private readonly Action<string>? _diagnostic;
    private bool _loaded;
    private bool _isBusy;
    private string _errorText = string.Empty;

    private RazerAccessoryBodyViewModel(ushort productId, Action<string>? diagnostic)
    {
        _productId = productId;
        _diagnostic = diagnostic;
        Thermal = productId is 0x0F35 or 0x0F3C
            ? new RazerThermalAccessoryViewModel(productId, diagnostic)
            : null;
        Haptics = productId is 0x0F45 or 0x0F6D
            ? new RazerHapticsAccessoryViewModel(productId, diagnostic)
            : null;
        Speaker = productId is 0x0517 or 0x0518 or 0x0566
            ? new RazerSpeakerAccessoryViewModel(productId, diagnostic)
            : null;
        Dock = productId is 0x00F1
            ? new RazerDockAccessoryViewModel(productId, diagnostic)
            : null;
        ControlPod = productId is 0x0566
            ? new RazerControlPodAccessoryViewModel(productId, diagnostic)
            : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ushort ProductId => _productId;
    public RazerThermalAccessoryViewModel? Thermal { get; }
    public RazerHapticsAccessoryViewModel? Haptics { get; }
    public RazerSpeakerAccessoryViewModel? Speaker { get; }
    public RazerDockAccessoryViewModel? Dock { get; }
    public RazerControlPodAccessoryViewModel? ControlPod { get; }
    public bool HasThermal => Thermal is not null;
    public bool HasHaptics => Haptics is not null;
    public bool HasSpeaker => Speaker is not null;
    public bool HasDock => Dock is not null;
    public bool HasControlPod => ControlPod is not null;
    public Visibility ThermalVisibility => HasThermal ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HapticsVisibility => HasHaptics ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SpeakerVisibility => HasSpeaker ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DockVisibility => HasDock ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ControlPodVisibility =>
        HasControlPod ? Visibility.Visible : Visibility.Collapsed;
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (!SetField(ref _errorText, value)) return;
            PropertyChanged?.Invoke(this, new(nameof(HasError)));
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }

    internal static RazerAccessoryBodyViewModel? Create(
        RazerChromaDeviceConnection connection,
        Action<string>? diagnostic = null)
    {
        var body = new RazerAccessoryBodyViewModel(connection.ProductId, diagnostic);
        return body.Thermal is null && body.Haptics is null && body.Speaker is null &&
            body.Dock is null && body.ControlPod is null
            ? null
            : body;
    }

    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded || IsBusy) return;
        IsBusy = true;
        try
        {
            if (Thermal is { } thermal)
            {
                await thermal.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            if (Haptics is { } haptics)
            {
                await haptics.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            if (Speaker is { } speaker)
            {
                await speaker.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            if (Dock is { } dock)
            {
                await dock.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            if (ControlPod is { } controlPod)
            {
                await controlPod.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            _loaded = true;
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Thermal is { } thermal)
        {
            await thermal.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        if (Haptics is { } haptics)
        {
            await haptics.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        if (Speaker is { } speaker)
        {
            await speaker.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        if (Dock is { } dock)
        {
            await dock.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        if (ControlPod is { } controlPod)
        {
            await controlPod.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Thermal is { } thermal)
        {
            await thermal.DisposeAsync().ConfigureAwait(false);
        }
        if (Haptics is { } haptics)
        {
            await haptics.DisposeAsync().ConfigureAwait(false);
        }
        if (Speaker is { } speaker)
        {
            await speaker.DisposeAsync().ConfigureAwait(false);
        }
        if (Dock is { } dock)
        {
            await dock.DisposeAsync().ConfigureAwait(false);
        }
        if (ControlPod is { } controlPod)
        {
            await controlPod.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
        return true;
    }
}
