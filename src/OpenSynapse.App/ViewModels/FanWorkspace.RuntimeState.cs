using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class FanWorkspace
{
    private string _bladeFanRuntimeError = string.Empty;

    public string BladeFanRuntimeError
    {
        get => _bladeFanRuntimeError;
        private set
        {
            if (SetField(ref _bladeFanRuntimeError, value))
            {
                OnPropertyChanged(nameof(HasBladeFanRuntimeError));
            }
        }
    }

    public bool HasBladeFanRuntimeError => !string.IsNullOrWhiteSpace(_bladeFanRuntimeError);

    internal void SetBladeFanRuntimeError(string? error)
    {
        var value = error ?? string.Empty;
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            BladeFanRuntimeError = value;
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => BladeFanRuntimeError = value);
        }
    }

    /// <summary>Prefer the range the EC publishes for its current mode over the static contract.</summary>
    private BladeFanTargetRange CurrentBladeFanCurveRange =>
        _blade._bladeFanRange ??
        BladeModelCapabilities.FanCurveTargetRangeFor(_blade._bladeProductId, CurrentBladePerformanceMode);
}
