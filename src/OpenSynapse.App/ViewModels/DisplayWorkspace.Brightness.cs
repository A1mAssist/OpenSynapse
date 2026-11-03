namespace OpenSynapse.App.ViewModels;

public sealed partial class DisplayWorkspace
{
    private async Task WriteDesiredInternalDisplayBrightnessAsync()
    {
        try
        {
            while (!_isDisposed())
            {
                double requested;
                lock (_internalDisplayBrightnessGate)
                {
                    if (_desiredInternalDisplayBrightness is not double desired)
                    {
                        break;
                    }
                    requested = desired;
                    _desiredInternalDisplayBrightness = null;
                    _internalDisplayBrightnessInFlight = requested;
                }

                try
                {
                    var applied = await _internalDisplayController!.SetBrightnessAsync(
                        requested, CancellationToken.None);
                    lock (_internalDisplayBrightnessGate)
                    {
                        _internalDisplayBrightnessInFlight = null;
                        _confirmedInternalDisplayBrightnessPercent = Math.Clamp(applied * 100, 0, 100);
                        // Publishing an older reply also fires the Slider's ValueChanged.
                        if (_desiredInternalDisplayBrightness is null)
                        {
                            InternalDisplayBrightnessPercent = _confirmedInternalDisplayBrightnessPercent;
                        }
                    }
                    _setDisplayError(string.Empty);
                }
                catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
                {
                    _diagnosticLog.TryWrite("display-brightness", exception.ToString());
                    _setDisplayError(AppStrings.FormatText("ScreenBrightnessError", exception.Message));
                    lock (_internalDisplayBrightnessGate)
                    {
                        _internalDisplayBrightnessInFlight = null;
                        if (_desiredInternalDisplayBrightness is not null)
                        {
                            continue;
                        }
                        InternalDisplayBrightnessPercent = _confirmedInternalDisplayBrightnessPercent;
                    }
                    break;
                }
            }
        }
        finally
        {
            lock (_internalDisplayBrightnessGate)
            {
                _internalDisplayBrightnessInFlight = null;
                _internalDisplayBrightnessWriterActive = false;
                if (_desiredInternalDisplayBrightness is not null && !_isDisposed())
                {
                    _internalDisplayBrightnessWriterActive = true;
                    _internalDisplayBrightnessWriter = WriteDesiredInternalDisplayBrightnessAsync();
                }
            }
        }
    }
}
