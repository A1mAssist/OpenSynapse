using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App.ViewModels;

// The GPU offset is driver state, not profile state. It outlives this process, so the
// card reads the driver value on load and only zeroes it when the user asks.
public sealed partial class MainViewModel
{
    private bool _gpuClockOffsetProbeStarted;
    private DateTimeOffset _gpuClockOffsetNextProbe;
    private bool _bladeGpuClockLimitAvailable;
    private double _bladeGpuClockLimitMhz = NvApiGpuClockLimit.MaximumMhz;

    /// <summary>
    /// Hidden until the driver answers a Pstates20 read. A laptop driver that refuses
    /// the read refuses the write, so the card would only ever show a failing button.
    /// </summary>
    public Visibility BladeGpuClockOffsetVisibility => _blade._bladeGpuClockOffsetAvailable
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility BladeGpuClockLimitVisibility => _bladeGpuClockLimitAvailable
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double BladeGpuClockLimitMhz
    {
        get => _bladeGpuClockLimitMhz;
        set
        {
            var clamped = Math.Clamp(
                value,
                NvApiGpuClockLimit.MinimumMhz,
                NvApiGpuClockLimit.MaximumMhz);
            if (SetField(ref _bladeGpuClockLimitMhz, clamped))
            {
                OnPropertyChanged(nameof(BladeGpuClockLimitText));
            }
        }
    }

    public string BladeGpuClockLimitText => _bladeGpuClockLimitMhz >= NvApiGpuClockLimit.MaximumMhz
        ? AppStrings.Text("BladeGpuClockLimitDefault")
        : $"{_bladeGpuClockLimitMhz:0} MHz";

    public double BladeCoreClockOffsetMhz
    {
        get => _blade._bladeCoreClockOffsetMhz;
        set
        {
            if (SetField(ref _blade._bladeCoreClockOffsetMhz, value))
            {
                OnPropertyChanged(nameof(BladeCoreClockOffsetText));
            }
        }
    }

    public double BladeMemoryClockOffsetMhz
    {
        get => _blade._bladeMemoryClockOffsetMhz;
        set
        {
            if (SetField(ref _blade._bladeMemoryClockOffsetMhz, value))
            {
                OnPropertyChanged(nameof(BladeMemoryClockOffsetText));
            }
        }
    }

    public string BladeCoreClockOffsetText => FormatClockOffset(_blade._bladeCoreClockOffsetMhz);

    public string BladeMemoryClockOffsetText => FormatClockOffset(_blade._bladeMemoryClockOffsetMhz);

    public string BladeGpuClockOffsetStatusText
    {
        get => _blade._bladeGpuClockOffsetStatusText;
        private set => SetField(ref _blade._bladeGpuClockOffsetStatusText, value);
    }

    private static string FormatClockOffset(double megahertz) =>
        $"{(megahertz > 0 ? "+" : string.Empty)}{megahertz:0} MHz";

    /// <summary>Probes once per process; the answer only changes after a driver restart.</summary>
    internal void ProbeBladeGpuClockOffset()
    {
        if (_gpuClockOffsetProbeStarted)
        {
            // A probe fails while the installed service is still the older build, which
            // only matters until the service restarts on its own. Retry slowly instead of
            // hiding the card for the rest of the process.
            if ((_blade._bladeGpuClockOffsetAvailable && _bladeGpuClockLimitAvailable) ||
                DateTimeOffset.UtcNow < _gpuClockOffsetNextProbe)
            {
                return;
            }
        }

        _gpuClockOffsetProbeStarted = true;
        _gpuClockOffsetNextProbe = DateTimeOffset.UtcNow.AddMinutes(2);
        _ = ProbeBladeGpuClockOffsetCoreAsync();
    }

    private async Task ProbeBladeGpuClockOffsetCoreAsync()
    {
        try
        {
            var state = await DedicatedGpuModeServiceClient.TryGetGpuClockOffsetAsync();
            _blade._bladeGpuClockOffsetAvailable = state?.Available == true;
            if (state is { Available: true })
            {
                BladeCoreClockOffsetMhz = state.CoreMhz;
                BladeMemoryClockOffsetMhz = state.MemoryMhz;
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException or
                ObjectDisposedException or OperationCanceledException or
                System.ComponentModel.Win32Exception)
        {
            _blade._bladeGpuClockOffsetAvailable = false;
        }

        try
        {
            var limit = await DedicatedGpuModeServiceClient.TryGetGpuClockLimitAsync();
            _bladeGpuClockLimitAvailable = limit?.Available == true;
            if (limit is { Available: true })
            {
                BladeGpuClockLimitMhz =
                    limit.Megahertz is >= NvApiGpuClockLimit.MinimumMhz and < NvApiGpuClockLimit.MaximumMhz
                        ? limit.Megahertz
                        : NvApiGpuClockLimit.MaximumMhz;
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException or
                ObjectDisposedException or OperationCanceledException or
                System.ComponentModel.Win32Exception)
        {
            _bladeGpuClockLimitAvailable = false;
        }

        OnPropertyChanged(nameof(BladeGpuClockOffsetVisibility));
        OnPropertyChanged(nameof(BladeGpuClockLimitVisibility));
    }

    public async Task ApplyBladeGpuClockOffsetAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade._bladeGpuClockOffsetAvailable)
        {
            return;
        }

        var core = (int)Math.Round(_blade._bladeCoreClockOffsetMhz);
        var memory = (int)Math.Round(_blade._bladeMemoryClockOffsetMhz);
        var limit = (int)Math.Round(_bladeGpuClockLimitMhz);
        await RunDeviceOperationAsync(
            AppStrings.Text("BladeGpuClockOffsetTitle"),
            async () =>
            {
                if (!await DedicatedGpuModeServiceClient.TrySetGpuClockOffsetAsync(
                        core, memory, cancellationToken))
                {
                    throw new InvalidOperationException(AppStrings.Text("BladeGpuClockOffsetFailed"));
                }

                await ApplyBladeGpuClockLimitAsync(limit, cancellationToken);
                BladeGpuClockOffsetStatusText = AppStrings.Text("BladeGpuClockOffsetApplied");
            },
            cancellationToken);
    }

    /// <summary>An older service has no clock-limit operation, so the control stays hidden.</summary>
    private async Task ApplyBladeGpuClockLimitAsync(
        int megahertz,
        CancellationToken cancellationToken)
    {
        if (!_bladeGpuClockLimitAvailable)
        {
            return;
        }

        if (!await DedicatedGpuModeServiceClient.TrySetGpuClockLimitAsync(
                megahertz, cancellationToken))
        {
            throw new InvalidOperationException(AppStrings.Text("BladeGpuClockOffsetFailed"));
        }
    }

    /// <summary>Puts the driver back to stock clocks on the user's explicit request.</summary>
    public async Task ResetBladeGpuClockOffsetDefaultsAsync(CancellationToken cancellationToken = default)
    {
        BladeCoreClockOffsetMhz = 0;
        BladeMemoryClockOffsetMhz = 0;
        BladeGpuClockLimitMhz = NvApiGpuClockLimit.MaximumMhz;
        await ApplyBladeGpuClockOffsetAsync(cancellationToken);
    }
}
