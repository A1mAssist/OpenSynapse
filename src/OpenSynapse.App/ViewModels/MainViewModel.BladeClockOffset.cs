using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App.ViewModels;

// The GPU offset is driver state, not profile state, so it is never saved into a
// profile: leaving the performance mode or closing the app puts it back to zero.
public sealed partial class MainViewModel
{
    private bool _gpuClockOffsetProbeStarted;

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
            return;
        }

        _gpuClockOffsetProbeStarted = true;
        _ = ProbeBladeGpuClockOffsetCoreAsync();
    }

    private async Task ProbeBladeGpuClockOffsetCoreAsync()
    {
        try
        {
            var available = await DedicatedGpuModeServiceClient.TryGetGpuClockOffsetCapabilityAsync();
            _blade._bladeGpuClockOffsetAvailable = available == true;
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

    /// <summary>
    /// The driver keeps the offset after this process exits, so leaving the performance
    /// mode or closing the app has to write zero back. Skipped when nothing was applied.
    /// </summary>
    internal async Task ResetBladeGpuClockOffsetAsync(CancellationToken cancellationToken = default)
    {
        if (_blade._bladeCoreClockOffsetMhz == 0 && _blade._bladeMemoryClockOffsetMhz == 0)
        {
            return;
        }

        BladeCoreClockOffsetMhz = 0;
        BladeMemoryClockOffsetMhz = 0;
        BladeGpuClockOffsetStatusText = string.Empty;
        await DedicatedGpuModeServiceClient.TrySetGpuClockOffsetAsync(0, 0, cancellationToken);
    }

    /// <summary>
    /// The window-close path cannot await, so this blocks briefly. The pipe write is local
    /// and normally finishes in milliseconds; the timeout only stops a dead service from
    /// holding the app open.
    /// </summary>
    internal void ResetBladeGpuClockOffsetOnExit()
    {
        try
        {
            ResetBladeGpuClockOffsetAsync(CancellationToken.None)
                .Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The app is closing; a refused offset write is already logged by the service.
        }
    }
}
