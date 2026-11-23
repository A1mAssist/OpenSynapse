using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerDockEndpointAudioViewModel : INotifyPropertyChanged
{
    private readonly RazerDockEndpointAudioService _service;
    private readonly Action<string>? _diagnostic;
    private WindowsPlaybackAudioState? _state;
    private bool _isBusy;
    private double _volume;
    private bool _muted;
    private string _errorText = string.Empty;
    private string _statusText = string.Empty;

    internal RazerDockEndpointAudioViewModel(RazerDockEndpointAudioService service, Action<string>? diagnostic = null)
    {
        _service = service;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CanRefresh => !_isBusy;
    public bool CanEdit => _state is not null && !_isBusy;
    public bool CanApply => CanEdit && (_state!.VolumePercent != (int)Volume || _state.Muted != Muted);
    public bool HasError => ErrorText.Length != 0;
    public string ErrorText { get => _errorText; private set { _errorText = value; Notify(nameof(ErrorText)); Notify(nameof(HasError)); } }
    public string StatusText { get => _statusText; private set { _statusText = value; Notify(nameof(StatusText)); } }
    public string VolumeText => $"{Volume:0}%";
    public double Volume
    {
        get => _volume;
        set
        {
            if (!double.IsFinite(value)) return;
            _volume = Math.Round(Math.Clamp(value, 0, 100));
            Notify(nameof(Volume));
            Notify(nameof(VolumeText));
            Notify(nameof(CanApply));
        }
    }
    public bool Muted
    {
        get => _muted;
        set { _muted = value; Notify(nameof(Muted)); Notify(nameof(CanApply)); }
    }

    internal Task RefreshAsync(CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        ApplyState(await _service.ReadAsync(cancellationToken));
        StatusText = string.Empty;
    }, cancellationToken);

    // Windows owns these global values. Loading or replaying a device profile never writes them.
    [NotProfileScoped]
    internal Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return Task.CompletedTask;
        var previous = _state!;
        int? volume = (int)Volume != previous.VolumePercent ? (int)Volume : null;
        bool? muted = Muted != previous.Muted ? Muted : null;
        return RunAsync(async () =>
        {
            ApplyState(await _service.ApplyAsync(previous.EndpointId, volume, muted, cancellationToken));
            StatusText = AppStrings.Text("DockEndpointAudioApplied");
        }, cancellationToken);
    }

    private async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        if (_isBusy) return;
        _isBusy = true;
        NotifyControls();
        try
        {
            await operation();
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
            COMException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // A failed apply may have changed one field; require a fresh snapshot before another write.
            _state = null;
            StatusText = string.Empty;
            ErrorText = AppStrings.Text(exception is WindowsPlaybackEndpointChangedException
                ? "DockEndpointAudioDeviceChanged" : "DockEndpointAudioUnavailable");
            _diagnostic?.Invoke($"dock endpoint audio: {exception.Message}");
        }
        finally
        {
            _isBusy = false;
            NotifyControls();
        }
    }

    private void ApplyState(WindowsPlaybackAudioState state)
    {
        _state = state;
        Volume = state.VolumePercent;
        Muted = state.Muted;
    }

    private void NotifyControls()
    {
        Notify(nameof(CanRefresh));
        Notify(nameof(CanEdit));
        Notify(nameof(CanApply));
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
