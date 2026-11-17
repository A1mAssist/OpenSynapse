namespace OpenSynapse.App.ViewModels;

using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.Runtime;

public sealed partial class RazerHidMappingViewModel
{
    private bool _visualCounted;
    private ImageSource? _keyboardBackgroundImage;

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
            var visual = await _assetService.GetAsync(_productId, _deviceKind).ConfigureAwait(true);
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
                if (keyboard.BackgroundImage is { } background)
                {
                    KeyboardBackgroundImage = await RazerMappingImageLoader
                        .LoadAsync(background, (int)Math.Ceiling(keyboard.Visual.Width))
                        .ConfigureAwait(true);
                }

                _keyboardLayout = keyboard;
                OnPropertyChanged(nameof(KeyboardLayout));
                OnPropertyChanged(nameof(KeyboardStageVisibility));
                OnPropertyChanged(nameof(KeyboardWidth));
                OnPropertyChanged(nameof(KeyboardHeight));
                OnPropertyChanged(nameof(TableVisibility));
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
