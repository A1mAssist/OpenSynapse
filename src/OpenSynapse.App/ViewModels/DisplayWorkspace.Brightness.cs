using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class DisplayWorkspace
{
    private Func<BladeProfileSettings>? _brightnessBladeProfile;

    internal void AttachBrightnessProfile(Func<BladeProfileSettings>? getProfile) =>
        _brightnessBladeProfile = getProfile;

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
                    var previousPercent = _confirmedInternalDisplayBrightnessPercent;
                    var applied = await _internalDisplayController!.SetBrightnessAsync(
                        requested, CancellationToken.None);
                    var appliedPercent = Math.Clamp(applied * 100, 0, 100);
                    await SaveBrightnessProfileAsync(
                        appliedPercent, previousPercent, CancellationToken.None);
                    lock (_internalDisplayBrightnessGate)
                    {
                        _internalDisplayBrightnessInFlight = null;
                        _confirmedInternalDisplayBrightnessPercent = appliedPercent;
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

    private async Task SaveBrightnessProfileAsync(
        double percent,
        double previousPercent,
        CancellationToken cancellationToken)
    {
        var getProfile = _brightnessBladeProfile;
        if (getProfile is null)
        {
            return;
        }

        var target = getProfile();
        var previous = target.DisplayBrightnessPercent;
        target.DisplayBrightnessPercent = (int)Math.Round(
            percent, MidpointRounding.AwayFromZero);
        try
        {
            if (await _saveProfileAsync(cancellationToken))
            {
                return;
            }
        }
        catch
        {
            target.DisplayBrightnessPercent = previous;
            await RestoreBrightnessAsync(previousPercent);
            throw;
        }

        target.DisplayBrightnessPercent = previous;
        await RestoreBrightnessAsync(previousPercent);
        throw new InvalidOperationException(
            AppStrings.FormatText("ProfileSaveError", AppStrings.Text("ScreenBrightnessLabel")));
    }

    private async Task RestoreBrightnessAsync(double percent)
    {
        if (_internalDisplayController is null)
        {
            return;
        }

        try
        {
            await _internalDisplayController.SetBrightnessAsync(
                Math.Clamp(percent / 100d, 0, 1), CancellationToken.None);
        }
        catch
        {
            // The save error is the failure the caller must report.
        }
    }
}
