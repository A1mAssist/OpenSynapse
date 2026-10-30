using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// 0F1B 8x8 matrix editor. The protocol supports arbitrary frames; the first
/// user-facing path is a full solid-color fill plus clear, both sent through
/// <see cref="RazerSeirenEmoteService"/>.
/// </summary>
public sealed class RazerSeirenEmoteViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<RazerSeirenEmoteService?>> _connect;
    private RazerSeirenEmoteService? _service;
    private bool _isBusy;
    private bool _isAvailable;
    private Color _frameColor = Color.FromArgb(255, 0, 255, 0);
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;

    public RazerSeirenEmoteViewModel(Action<string>? diagnostic = null)
        : this(cancellationToken => RazerSeirenEmoteService.ConnectAsync(diagnostic, cancellationToken))
    {
    }

    internal RazerSeirenEmoteViewModel(
        Func<CancellationToken, Task<RazerSeirenEmoteService?>> connect)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Color FrameColor
    {
        get => _frameColor;
        set => SetField(ref _frameColor, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (!SetField(ref _isAvailable, value)) return;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool CanApply => IsAvailable && !IsBusy;
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _service ??= await _connect(cancellationToken).ConfigureAwait(false);
            IsAvailable = _service is not null;
            if (IsAvailable)
            {
                StatusText = AppStrings.Text("SeirenEmoteStatusConnected");
                SetError(string.Empty);
            }
            else
            {
                SetError(AppStrings.Text("SeirenEmoteErrorEndpointMissing"));
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetError(AppStrings.FormatText("SeirenEmoteErrorConnect", exception.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return;
        await SendAsync(pixels => Fill(pixels, FrameColor), "SeirenEmoteStatusApplied", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return;
        await SendAsync(
            pixels => Array.Clear(pixels),
            "SeirenEmoteStatusCleared",
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_service is not null)
        {
            await _service.DisposeAsync().ConfigureAwait(false);
            _service = null;
        }
    }

    private async Task SendAsync(
        Action<RazerRgb[]> fill,
        string statusKey,
        CancellationToken cancellationToken)
    {
        if (_service is null)
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_service is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var pixels = new RazerRgb[RazerSeirenEmoteProtocol.PixelCount];
            fill(pixels);
            await _service.SendFrameAsync(pixels, cancellationToken).ConfigureAwait(false);
            StatusText = AppStrings.Text(statusKey);
            SetError(string.Empty);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetError(AppStrings.FormatText("SeirenEmoteErrorWrite", exception.Message));
            IsAvailable = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void Fill(RazerRgb[] pixels, Color color)
    {
        var rgb = new RazerRgb(color.R, color.G, color.B);
        Array.Fill(pixels, rgb);
    }

    private void SetError(string error)
    {
        ErrorText = error;
        OnPropertyChanged(nameof(HasError));
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or ArgumentException or
            BadImageFormatException or DllNotFoundException or EntryPointNotFoundException or
            Win32Exception;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
