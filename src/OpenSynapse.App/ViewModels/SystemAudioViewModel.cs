using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed record SystemAudioEndpointOption(
    string Id,
    string DisplayName,
    bool IsDefault)
{
    public override string ToString() => DisplayName;
}

public sealed class SystemAudioViewModel : INotifyPropertyChanged
{
    private readonly ISystemAudioController _controller;
    private SystemAudioEndpointOption? _selectedRender;
    private SystemAudioEndpointOption? _selectedCapture;
    private double _renderVolume = 50;
    private double _captureVolume = 50;
    private bool _renderMuted;
    private bool _captureMuted;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;

    public SystemAudioViewModel(ISystemAudioController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SystemAudioEndpointOption> RenderEndpoints { get; } = [];
    public ObservableCollection<SystemAudioEndpointOption> CaptureEndpoints { get; } = [];

    public SystemAudioEndpointOption? SelectedRender
    {
        get => _selectedRender;
        set
        {
            if (!SetField(ref _selectedRender, value)) return;
            OnPropertyChanged(nameof(CanEditRender));
            OnPropertyChanged(nameof(RenderSelectionText));
        }
    }

    public SystemAudioEndpointOption? SelectedCapture
    {
        get => _selectedCapture;
        set
        {
            if (!SetField(ref _selectedCapture, value)) return;
            OnPropertyChanged(nameof(CanEditCapture));
            OnPropertyChanged(nameof(CaptureSelectionText));
        }
    }

    public double RenderVolume
    {
        get => _renderVolume;
        set
        {
            if (!SetField(ref _renderVolume, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(RenderVolumeText));
        }
    }

    public double CaptureVolume
    {
        get => _captureVolume;
        set
        {
            if (!SetField(ref _captureVolume, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(CaptureVolumeText));
        }
    }

    public bool RenderMuted
    {
        get => _renderMuted;
        set => SetField(ref _renderMuted, value);
    }

    public bool CaptureMuted
    {
        get => _captureMuted;
        set => SetField(ref _captureMuted, value);
    }

    public string RenderVolumeText => $"{Math.Round(RenderVolume)}%";
    public string CaptureVolumeText => $"{Math.Round(CaptureVolume)}%";
    public string RenderSelectionText => SelectedRender?.DisplayName ?? "--";
    public string CaptureSelectionText => SelectedCapture?.DisplayName ?? "--";
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEditRender));
            OnPropertyChanged(nameof(CanEditCapture));
        }
    }
    public bool CanEditRender => !IsBusy && SelectedRender is not null;
    public bool CanEditCapture => !IsBusy && SelectedCapture is not null;
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (!SetField(ref _errorText, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(true);
            OperationStatusText = AppStrings.Text("SystemAudioRefreshed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorText = AppStrings.FormatText("SystemAudioLoadFailed", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ApplyRenderAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync(SystemAudioDirection.Render, cancellationToken);

    public Task ApplyCaptureAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync(SystemAudioDirection.Capture, cancellationToken);

    private async Task ApplyAsync(
        SystemAudioDirection direction,
        CancellationToken cancellationToken)
    {
        var selected = direction == SystemAudioDirection.Render ? SelectedRender : SelectedCapture;
        if (selected is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        OperationStatusText = string.Empty;
        try
        {
            var volume = checked((int)Math.Round(
                direction == SystemAudioDirection.Render ? RenderVolume : CaptureVolume,
                MidpointRounding.AwayFromZero));
            var muted = direction == SystemAudioDirection.Render ? RenderMuted : CaptureMuted;
            await _controller.SetVolumeAsync(selected.Id, volume, cancellationToken).ConfigureAwait(true);
            await _controller.SetMuteAsync(selected.Id, muted, cancellationToken).ConfigureAwait(true);
            await _controller.SetDefaultAsync(selected.Id, cancellationToken).ConfigureAwait(true);
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(true);
            OperationStatusText = AppStrings.Text(direction == SystemAudioDirection.Render
                ? "SystemAudioRenderApplied"
                : "SystemAudioCaptureApplied");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorText = AppStrings.FormatText("SystemAudioApplyFailed", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _controller.ReadAsync(cancellationToken).ConfigureAwait(true);
        var renderSelection = SelectedRender?.Id;
        var captureSelection = SelectedCapture?.Id;
        Replace(RenderEndpoints,
            snapshot.RenderEndpoints.Select(endpoint => new SystemAudioEndpointOption(
                endpoint.Id,
                endpoint.IsDefault
                    ? AppStrings.FormatText("SystemAudioDefaultEndpoint", endpoint.Name)
                    : endpoint.Name,
                endpoint.IsDefault)));
        Replace(CaptureEndpoints,
            snapshot.CaptureEndpoints.Select(endpoint => new SystemAudioEndpointOption(
                endpoint.Id,
                endpoint.IsDefault
                    ? AppStrings.FormatText("SystemAudioDefaultEndpoint", endpoint.Name)
                    : endpoint.Name,
                endpoint.IsDefault)));

        SelectedRender = RenderEndpoints.FirstOrDefault(item => item.Id == renderSelection) ??
            RenderEndpoints.FirstOrDefault(item => item.IsDefault) ??
            RenderEndpoints.FirstOrDefault();
        SelectedCapture = CaptureEndpoints.FirstOrDefault(item => item.Id == captureSelection) ??
            CaptureEndpoints.FirstOrDefault(item => item.IsDefault) ??
            CaptureEndpoints.FirstOrDefault();

        var render = snapshot.RenderEndpoints.FirstOrDefault(item => item.Id == SelectedRender?.Id);
        var capture = snapshot.CaptureEndpoints.FirstOrDefault(item => item.Id == SelectedCapture?.Id);
        if (render is not null)
        {
            RenderVolume = render.VolumePercent;
            RenderMuted = render.IsMuted;
        }
        if (capture is not null)
        {
            CaptureVolume = capture.VolumePercent;
            CaptureMuted = capture.IsMuted;
        }
        if (render is null || capture is null)
        {
            ErrorText = AppStrings.Text("SystemAudioMissingEndpoint");
        }
    }

    private static void Replace(
        ObservableCollection<SystemAudioEndpointOption> target,
        IEnumerable<SystemAudioEndpointOption> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
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
