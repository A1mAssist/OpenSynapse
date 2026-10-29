using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App.ViewModels;

// The GPU offset is driver state, not profile state. It outlives this process, so the
// card reads the driver value on load and only zeroes it when the user asks.
public sealed partial class MainViewModel
{
    private bool _gpuClockOffsetProbeStarted;
    private DateTimeOffset _gpuClockOffsetNextProbe;

    /// <summary>
    /// Hidden until the driver answers a Pstates20 read. A laptop driver that refuses
    /// the read refuses the write, so the card would only ever show a failing button.
    /// </summary>
    public Visibility BladeGpuClockOffsetVisibility => _blade._bladeGpuClockOffsetAvailable
        ? Visibility.Visible
        : Visibility.Collapsed;

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
            if (_blade._bladeGpuClockOffsetAvailable ||
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
                ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            _blade._bladeGpuClockOffsetAvailable = false;
        }

        OnPropertyChanged(nameof(BladeGpuClockOffsetVisibility));
    }

    public async Task ApplyBladeGpuClockOffsetAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade._bladeGpuClockOffsetAvailable)
        {
            return;
        }

        var core = (int)Math.Round(_blade._bladeCoreClockOffsetMhz);
        var memory = (int)Math.Round(_blade._bladeMemoryClockOffsetMhz);
        await RunDeviceOperationAsync(
            AppStrings.Text("BladeGpuClockOffsetTitle"),
            async () =>
            {
                if (!await DedicatedGpuModeServiceClient.TrySetGpuClockOffsetAsync(
                        core, memory, cancellationToken))
                {
                    throw new InvalidOperationException(AppStrings.Text("BladeGpuClockOffsetFailed"));
                }

                BladeGpuClockOffsetStatusText = AppStrings.Text("BladeGpuClockOffsetApplied");
            },
            cancellationToken);
    }

    /// <summary>Puts the driver back to stock clocks on the user's explicit request.</summary>
    public async Task ResetBladeGpuClockOffsetDefaultsAsync(CancellationToken cancellationToken = default)
    {
        BladeCoreClockOffsetMhz = 0;
        BladeMemoryClockOffsetMhz = 0;
        await ApplyBladeGpuClockOffsetAsync(cancellationToken);
    }
}
