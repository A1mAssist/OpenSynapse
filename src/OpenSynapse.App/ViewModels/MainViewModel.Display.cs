using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task ApplyInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        if (!_canSetInternalDisplayRefreshRate || _internalDisplayController is null)
        {
            return;
        }

        if (!IsSelectedRefreshRatePowerActive)
        {
            var previous = EditableRefreshRateBladeProfile.RefreshRateHertz;
            EditableRefreshRateBladeProfile.RefreshRateHertz = InternalDisplayRefreshRateHertz;
            if (!await SaveProfileAsync(cancellationToken))
            {
                EditableRefreshRateBladeProfile.RefreshRateHertz = previous;
            }
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_8423206D"), async () =>
        {
            var snapshot = _internalDisplayController.SetRefreshRate(InternalDisplayRefreshRateHertz);
            ApplyInternalDisplaySnapshot(snapshot);
            EditableRefreshRateBladeProfile.RefreshRateHertz = snapshot.RefreshRateHertz;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
            InternalDisplayRefreshRateHertz = _confirmedInternalDisplayRefreshRateHertz);
    }

    public string InternalDisplayResolutionText { get => _internalDisplayResolutionText; private set => SetField(ref _internalDisplayResolutionText, value); }
    public string InternalDisplayRefreshRateText { get => _internalDisplayRefreshRateText; private set => SetField(ref _internalDisplayRefreshRateText, value); }
    public IReadOnlyList<int> InternalDisplayRefreshRates
    {
        get => _internalDisplayRefreshRates;
        private set
        {
            if (SetField(ref _internalDisplayRefreshRates, value))
            {
                OnPropertyChanged(nameof(InternalDisplayRefreshRateOptions));
                OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
            }
        }
    }
    public IReadOnlyList<string> InternalDisplayRefreshRateOptions => InternalDisplayRefreshRates
        .Select(rate => $"{rate} Hz")
        .ToArray();
    public int InternalDisplayRefreshRateHertz
    {
        get => _internalDisplayRefreshRateHertz;
        set
        {
            if (SetField(ref _internalDisplayRefreshRateHertz, value))
            {
                OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
            }
        }
    }
    public int InternalDisplayRefreshRateIndex
    {
        get => Array.IndexOf(InternalDisplayRefreshRates.ToArray(), InternalDisplayRefreshRateHertz);
        set
        {
            if (value >= 0 && value < InternalDisplayRefreshRates.Count)
            {
                InternalDisplayRefreshRateHertz = InternalDisplayRefreshRates[value];
            }
        }
    }
    public bool CanSetInternalDisplayRefreshRate { get => _canSetInternalDisplayRefreshRate; private set => SetField(ref _canSetInternalDisplayRefreshRate, value); }
    public double InternalDisplayBrightnessPercent
    {
        get => _internalDisplayBrightnessPercent;
        private set
        {
            if (SetField(ref _internalDisplayBrightnessPercent, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(InternalDisplayBrightnessText));
            }
        }
    }
    public string InternalDisplayBrightnessText => $"{_internalDisplayBrightnessPercent:0}%";
    public bool CanSetInternalDisplayBrightness { get => _canSetInternalDisplayBrightness; private set => SetField(ref _canSetInternalDisplayBrightness, value); }

    private readonly object _internalDisplayBrightnessGate = new();
    private Task _internalDisplayBrightnessWriter = Task.CompletedTask;
    private double? _desiredInternalDisplayBrightness;
    private bool _internalDisplayBrightnessWriterActive;
    private double _internalDisplayBrightnessPercent;
    private double _confirmedInternalDisplayBrightnessPercent;
    private bool _canSetInternalDisplayBrightness;

    internal Task QueueInternalDisplayBrightnessAsync(
        double percent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_canSetInternalDisplayBrightness || _internalDisplayController is null || !double.IsFinite(percent))
        {
            return Task.CompletedTask;
        }

        var value = Math.Clamp(percent, 0, 100);
        InternalDisplayBrightnessPercent = value;
        lock (_internalDisplayBrightnessGate)
        {
            var previous = _desiredInternalDisplayBrightness ?? _confirmedInternalDisplayBrightnessPercent / 100d;
            var requested = value / 100d;
            if (Math.Abs(requested - previous) < 0.005)
            {
                return Task.CompletedTask;
            }

            _desiredInternalDisplayBrightness = requested;
            if (!_internalDisplayBrightnessWriterActive)
            {
                _internalDisplayBrightnessWriterActive = true;
                _internalDisplayBrightnessWriter = WriteDesiredInternalDisplayBrightnessAsync();
            }
        }

        return Task.CompletedTask;
    }

    private async Task WriteDesiredInternalDisplayBrightnessAsync()
    {
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
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
                }

                try
                {
                    var applied = await _internalDisplayController!.SetBrightnessAsync(
                        requested, CancellationToken.None);
                    lock (_internalDisplayBrightnessGate)
                    {
                        _confirmedInternalDisplayBrightnessPercent = Math.Clamp(applied * 100, 0, 100);
                    }
                    InternalDisplayBrightnessPercent = _confirmedInternalDisplayBrightnessPercent;
                    SetDisplayError(string.Empty);
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception))
                {
                    _diagnosticLog.TryWrite("display-brightness", exception.ToString());
                    SetDisplayError(AppStrings.FormatText("ScreenBrightnessError", exception.Message));
                    InternalDisplayBrightnessPercent = _confirmedInternalDisplayBrightnessPercent;
                    lock (_internalDisplayBrightnessGate)
                    {
                        _desiredInternalDisplayBrightness = null;
                    }
                    break;
                }
            }
        }
        finally
        {
            lock (_internalDisplayBrightnessGate)
            {
                _internalDisplayBrightnessWriterActive = false;
                if (_desiredInternalDisplayBrightness is not null && Volatile.Read(ref _disposed) == 0)
                {
                    _internalDisplayBrightnessWriterActive = true;
                    _internalDisplayBrightnessWriter = WriteDesiredInternalDisplayBrightnessAsync();
                }
            }
        }
    }

    private void ApplyInternalDisplayBrightness(double? level)
    {
        if (level is not double value)
        {
            CanSetInternalDisplayBrightness = false;
            return;
        }

        lock (_internalDisplayBrightnessGate)
        {
            if (_desiredInternalDisplayBrightness is not null || _internalDisplayBrightnessWriterActive)
            {
                return;
            }

            _confirmedInternalDisplayBrightnessPercent = Math.Clamp(value * 100, 0, 100);
            InternalDisplayBrightnessPercent = _confirmedInternalDisplayBrightnessPercent;
        }
        CanSetInternalDisplayBrightness = true;
    }
}
