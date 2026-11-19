namespace OpenSynapse.App.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.Runtime;

public sealed partial class RazerHidMappingViewModel
{
    private bool _visualCounted;
    private ImageSource? _keyboardBackgroundImage;
    private ImageSource? _keyboardScrollImage;
    private ImageSource? _keyboardDpadImage;

    public ImageSource? KeyboardBackgroundImage
    {
        get => _keyboardBackgroundImage;
        private set
        {
            if (ReferenceEquals(_keyboardBackgroundImage, value)) return;
            _keyboardBackgroundImage = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? KeyboardScrollImage
    {
        get => _keyboardScrollImage;
        private set
        {
            if (ReferenceEquals(_keyboardScrollImage, value)) return;
            _keyboardScrollImage = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? KeyboardDpadImage
    {
        get => _keyboardDpadImage;
        private set
        {
            if (ReferenceEquals(_keyboardDpadImage, value)) return;
            _keyboardDpadImage = value;
            OnPropertyChanged();
        }
    }

    public Visibility KeypadWidgetsVisibility =>
        _keyboardScrollImage is not null && _keyboardDpadImage is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    internal void StartVisualLoad()
    {
        if (_visualLoadStarted || _productId == 0)
        {
            return;
        }

        _visualLoadStarted = true;
        _ = LoadVisualAsync();
    }

    internal void ReleaseVisual()
    {
        if (_visualCounted)
        {
            _visualCounted = false;
            MemoryTrace.MappingVisualReleased();
        }

        _layout = null;
        _keyboardLayout = null;
        LayoutImage = null;
        KeyboardBackgroundImage = null;
        KeyboardScrollImage = null;
        KeyboardDpadImage = null;
        PopulateFallbackRows();
        OnPropertyChanged(nameof(KeypadWidgetsVisibility));
        _visualLoadStarted = false;
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(StageVisibility));
        OnPropertyChanged(nameof(TableVisibility));
        OnPropertyChanged(nameof(KeyboardLayout));
        OnPropertyChanged(nameof(KeyboardStageVisibility));
        OnPropertyChanged(nameof(KeyboardWidth));
        OnPropertyChanged(nameof(KeyboardHeight));
    }

    private async Task LoadVisualAsync()
    {
        try
        {
            var visual = await _assetService
                .GetAsync(_productId, _deviceKind, await ReadEditionIdAsync().ConfigureAwait(true))
                .ConfigureAwait(true);
            if (visual.Mouse is { } mouse)
            {
                var image = await RazerMappingImageLoader.LoadAsync(mouse.ImageUri).ConfigureAwait(true);
                if (image is null)
                {
                    _log?.Invoke($"mapping image decode failed: {mouse.ImageUri}");
                }
                else
                {
                    _layout = mouse;
                    LayoutImage = image;
                    OnPropertyChanged(nameof(Layout));
                    OnPropertyChanged(nameof(StageVisibility));
                    OnPropertyChanged(nameof(TableVisibility));
                    RazerHidMappingRowViewModel.ApplyButtonNames(Rows, mouse.ButtonNames, _productId);
                    PopulateStageRows();
                    MarkVisualLoaded();
                }
            }

            if (visual.Keyboard is { } keyboard)
            {
                if (keyboard.BackgroundImageData is { Length: > 0 } embedded)
                {
                    KeyboardBackgroundImage = await RazerMappingImageLoader
                        .LoadEmbeddedAsync(embedded, (int)Math.Ceiling(keyboard.Visual.Width))
                        .ConfigureAwait(true);
                }
                else if (keyboard.BackgroundImage is { } background)
                {
                    KeyboardBackgroundImage = await RazerMappingImageLoader
                        .LoadAsync(background, (int)Math.Ceiling(keyboard.Visual.Width))
                        .ConfigureAwait(true);
                }
                if (keyboard.ScrollImageData is { Length: > 0 } scroll)
                {
                    KeyboardScrollImage = await RazerMappingImageLoader
                        .LoadEmbeddedAsync(scroll, 26)
                        .ConfigureAwait(true);
                }
                if (keyboard.DpadImageData is { Length: > 0 } dpad)
                {
                    KeyboardDpadImage = await RazerMappingImageLoader
                        .LoadEmbeddedAsync(dpad, 50)
                        .ConfigureAwait(true);
                }
                OnPropertyChanged(nameof(KeypadWidgetsVisibility));

                _keyboardLayout = keyboard;
                OnPropertyChanged(nameof(KeyboardLayout));
                OnPropertyChanged(nameof(KeyboardStageVisibility));
                OnPropertyChanged(nameof(KeyboardWidth));
                OnPropertyChanged(nameof(KeyboardHeight));
                PopulateStageRows();
                MarkVisualLoaded();
            }

            if (!string.IsNullOrWhiteSpace(visual.Error))
            {
                _log?.Invoke($"mapping visual unavailable: {visual.Error}");
            }
        }
        catch (Exception exception) when (IsExpected(exception) || exception is ArgumentException)
        {
            _log?.Invoke($"mapping visual failed: {exception.Message}");
        }
    }

    /// <summary>The device is the only source of its edition; unsupported firmware means standard.</summary>
    private async Task<int> ReadEditionIdAsync()
    {
        try
        {
            var edition = await _transport.ReadEditionAsync().ConfigureAwait(true);
            if (edition is not null)
            {
                _log?.Invoke($"mapping edition pid=0x{_productId:X4} edition={edition}");
            }

            return edition ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void MarkVisualLoaded()
    {
        if (_visualCounted)
        {
            return;
        }

        _visualCounted = true;
        MemoryTrace.MappingVisualLoaded();
    }
}
