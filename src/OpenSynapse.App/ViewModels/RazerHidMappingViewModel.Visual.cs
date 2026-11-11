namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    private void StartVisualLoad()
    {
        if (_visualLoadStarted || _productId == 0)
        {
            return;
        }

        _visualLoadStarted = true;
        _ = LoadVisualAsync();
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
                }
            }

            if (visual.Keyboard is { } keyboard)
            {
                _keyboardLayout = keyboard;
                OnPropertyChanged(nameof(KeyboardLayout));
                OnPropertyChanged(nameof(KeyboardStageVisibility));
                OnPropertyChanged(nameof(KeyboardWidth));
                OnPropertyChanged(nameof(KeyboardHeight));
                OnPropertyChanged(nameof(TableVisibility));
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
}
