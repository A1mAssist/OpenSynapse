using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    private static readonly int[] GpuCoreOffsetPresets = [0, -300, -250, -100, 100, 200];
    private static readonly int[] GpuMemoryOffsetPresets = [0, -500, 200, 500, 800, 1000];
    private static readonly int[] GpuCoreLimitPresets = [0, 1000, 1500, 2000, 2500, 3000];

    private bool _gpuClockSnapshotLoaded;
    private bool _gpuClockApplying;
    private int _gpuCoreOffsetIndex = -1;
    private int _gpuMemoryOffsetIndex = -1;
    private int _gpuCoreLimitIndex = -1;
    private int _gpuClockAppliedCoreMhz;
    private int _gpuClockAppliedMemoryMhz;
    private int _gpuClockAppliedLimitMhz;

    /// <summary>
    /// The card stays visible when the service cannot answer, so a stale machine service
    /// explains itself instead of silently removing the feature.
    /// </summary>
    public bool CanEditGpuClockOffset =>
        _blade._bladeGpuClockOffsetAvailable && _gpuClockSnapshotLoaded && !_gpuClockApplying;

    public IReadOnlyList<string> GpuCoreOffsetOptions =>
        BuildOffsetOptions(GpuCoreOffsetPresets, AppStrings.Text("BladeGpuClockLimitDefault"));

    public IReadOnlyList<string> GpuMemoryOffsetOptions =>
        BuildOffsetOptions(GpuMemoryOffsetPresets, AppStrings.Text("BladeGpuClockLimitDefault"));

    public IReadOnlyList<string> GpuCoreLimitOptions =>
        BuildLimitOptions(GpuCoreLimitPresets, AppStrings.Text("BladeGpuClockLimitNoLimit"));

    public Visibility GpuClockLimitVisibility => _bladeGpuClockLimitAvailable
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool CanEditGpuClockLimit => CanEditGpuClockOffset && _bladeGpuClockLimitAvailable;

    public double GpuClockLimitMhz
    {
        get => _bladeGpuClockLimitMhz;
        set
        {
            if (!double.IsFinite(value)) return;
            var clamped = value <= 0
                ? 0
                : Math.Clamp(value, NvApiGpuClockLimit.MinimumMhz, NvApiGpuClockLimit.MaximumMhz);
            if (SetField(ref _bladeGpuClockLimitMhz, clamped))
            {
                _gpuCoreLimitIndex = IndexOfPreset(GpuCoreLimitPresets, (int)Math.Round(clamped));
                OnPropertyChanged(nameof(GpuCoreLimitIndex));
                OnPropertyChanged(nameof(GpuClockLimitText));
                NotifyGpuClockDirty();
            }
        }
    }

    public string GpuClockLimitText => _bladeGpuClockLimitMhz <= 0
        ? AppStrings.Text("BladeGpuClockLimitNoLimit")
        : $"{_bladeGpuClockLimitMhz:0} MHz";

    public double CoreClockOffsetMhz
    {
        get => _blade._bladeCoreClockOffsetMhz;
        set
        {
            if (!double.IsFinite(value)) return;
            var clamped = Math.Clamp(
                value,
                NvApiGpuClockOffset.LaptopCoreMinimumMhz,
                NvApiGpuClockOffset.LaptopCoreMaximumMhz);
            if (SetField(ref _blade._bladeCoreClockOffsetMhz, clamped))
            {
                _gpuCoreOffsetIndex = IndexOfPreset(GpuCoreOffsetPresets, (int)Math.Round(clamped));
                OnPropertyChanged(nameof(GpuCoreOffsetIndex));
                OnPropertyChanged(nameof(CoreClockOffsetText));
                NotifyGpuClockDirty();
            }
        }
    }

    public double MemoryClockOffsetMhz
    {
        get => _blade._bladeMemoryClockOffsetMhz;
        set
        {
            if (!double.IsFinite(value)) return;
            var clamped = Math.Clamp(
                value,
                NvApiGpuClockOffset.MemoryMinimumMhz,
                NvApiGpuClockOffset.MemoryMaximumMhz);
            if (SetField(ref _blade._bladeMemoryClockOffsetMhz, clamped))
            {
                _gpuMemoryOffsetIndex = IndexOfPreset(GpuMemoryOffsetPresets, (int)Math.Round(clamped));
                OnPropertyChanged(nameof(GpuMemoryOffsetIndex));
                OnPropertyChanged(nameof(MemoryClockOffsetText));
                NotifyGpuClockDirty();
            }
        }
    }

    public int GpuCoreOffsetIndex
    {
        get => _gpuCoreOffsetIndex;
        set
        {
            if (SetField(ref _gpuCoreOffsetIndex, value) && TryPreset(GpuCoreOffsetPresets, value, out var mhz))
            {
                CoreClockOffsetMhz = mhz;
            }
        }
    }

    public int GpuMemoryOffsetIndex
    {
        get => _gpuMemoryOffsetIndex;
        set
        {
            if (SetField(ref _gpuMemoryOffsetIndex, value) && TryPreset(GpuMemoryOffsetPresets, value, out var mhz))
            {
                MemoryClockOffsetMhz = mhz;
            }
        }
    }

    public int GpuCoreLimitIndex
    {
        get => _gpuCoreLimitIndex;
        set
        {
            if (SetField(ref _gpuCoreLimitIndex, value) && TryPreset(GpuCoreLimitPresets, value, out var mhz))
            {
                GpuClockLimitMhz = mhz;
            }
        }
    }

    public string CoreClockOffsetText => FormatClockOffset(_blade._bladeCoreClockOffsetMhz);

    public string MemoryClockOffsetText => FormatClockOffset(_blade._bladeMemoryClockOffsetMhz);

    public string GpuClockOffsetStatusText
    {
        get => string.IsNullOrWhiteSpace(_blade._bladeGpuClockOffsetStatusText)
            ? AppStrings.Text("BladeGpuClockOffsetReading")
            : _blade._bladeGpuClockOffsetStatusText;
        private set => SetField(ref _blade._bladeGpuClockOffsetStatusText, value);
    }

    public bool GpuOverclockDirty =>
        _gpuClockSnapshotLoaded &&
        (Math.Round(CoreClockOffsetMhz) != _gpuClockAppliedCoreMhz ||
         Math.Round(MemoryClockOffsetMhz) != _gpuClockAppliedMemoryMhz ||
         Math.Round(_bladeGpuClockLimitMhz) != _gpuClockAppliedLimitMhz);

    public bool CanResetGpuClockOffset =>
        CanEditGpuClockOffset &&
        (CoreClockOffsetMhz != 0 || MemoryClockOffsetMhz != 0 || GpuClockLimitMhz != 0);

    private static string FormatClockOffset(double megahertz) =>
        $"{(megahertz > 0 ? "+" : string.Empty)}{megahertz:0} MHz";

    private static IReadOnlyList<string> BuildOffsetOptions(int[] presets, string defaultValue) =>
        presets.Select((mhz, index) => index == 0 ? defaultValue : FormatClockOffset(mhz)).ToArray();

    private static IReadOnlyList<string> BuildLimitOptions(int[] presets, string defaultValue) =>
        presets.Select((mhz, index) => index == 0 ? defaultValue : $"{mhz} MHz").ToArray();

    private static bool TryPreset(int[] presets, int index, out int value)
    {
        if (index >= 0 && index < presets.Length)
        {
            value = presets[index];
            return true;
        }

        value = 0;
        return false;
    }

    private static int IndexOfPreset(int[] presets, int value) =>
        Array.FindIndex(presets, preset => preset == value);

    /// <summary>Reads the driver snapshot; retry unavailable service operations slowly.</summary>
    internal void ProbeGpuClockOffset()
    {
        if (_gpuClockApplying || GpuOverclockDirty) return;
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
        _ = ProbeGpuClockOffsetCoreAsync();
    }

    private async Task ProbeGpuClockOffsetCoreAsync()
    {
        // Binding selection events during initialization must never write clocks.
        _gpuClockSnapshotLoaded = false;
        OnPropertyChanged(nameof(CanEditGpuClockOffset));
        OnPropertyChanged(nameof(CanEditGpuClockLimit));
        try
        {
            var state = await OpenSynapseServiceClient.TryGetGpuClockOffsetAsync();
            _blade._bladeGpuClockOffsetAvailable = state?.Available == true;
            if (state is { Available: true })
            {
                _gpuClockAppliedCoreMhz = state.CoreMhz;
                _gpuClockAppliedMemoryMhz = state.MemoryMhz;
                CoreClockOffsetMhz = state.CoreMhz;
                MemoryClockOffsetMhz = state.MemoryMhz;
                GpuClockOffsetStatusText = AppStrings.Text("BladeGpuClockOffsetHint");
            }
            else
            {
                GpuClockOffsetStatusText = AppStrings.Text(state is null
                    ? "BladeGpuClockOffsetServiceMissing"
                    : "BladeGpuClockOffsetUnsupported");
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException or
                ObjectDisposedException or OperationCanceledException or
                System.ComponentModel.Win32Exception)
        {
            _blade._bladeGpuClockOffsetAvailable = false;
            GpuClockOffsetStatusText = AppStrings.Text("BladeGpuClockOffsetServiceMissing");
        }

        try
        {
            var limit = await OpenSynapseServiceClient.TryGetGpuClockLimitAsync();
            _bladeGpuClockLimitAvailable = limit?.Available == true;
            if (limit is { Available: true })
            {
                _gpuClockAppliedLimitMhz =
                    limit.Megahertz is >= NvApiGpuClockLimit.MinimumMhz and < NvApiGpuClockLimit.MaximumMhz
                        ? limit.Megahertz
                        : 0;
                GpuClockLimitMhz = _gpuClockAppliedLimitMhz;
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException or
                ObjectDisposedException or OperationCanceledException or
                System.ComponentModel.Win32Exception)
        {
            _bladeGpuClockLimitAvailable = false;
        }

        _gpuClockSnapshotLoaded = _blade._bladeGpuClockOffsetAvailable;
        OnPropertyChanged(nameof(CanEditGpuClockOffset));
        OnPropertyChanged(nameof(CanEditGpuClockLimit));
        OnPropertyChanged(nameof(GpuClockLimitVisibility));
        NotifyGpuClockDirty();
    }

    [ProfileSetting("blade.gpu-clock-offset", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyGpuClockOffsetAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEditGpuClockOffset)
        {
            return;
        }

        var core = (int)Math.Round(_blade._bladeCoreClockOffsetMhz);
        var memory = (int)Math.Round(_blade._bladeMemoryClockOffsetMhz);
        var limit = (int)Math.Round(_bladeGpuClockLimitMhz);
        var target = EditablePlatformProfile;
        var previousProfileCore = target.GpuCoreClockOffsetMhz;
        var previousProfileMemory = target.GpuMemoryClockOffsetMhz;
        var previousProfileLimit = target.GpuCoreClockLimitMhz;
        var previousAppliedCore = _gpuClockAppliedCoreMhz;
        var previousAppliedMemory = _gpuClockAppliedMemoryMhz;
        var previousAppliedLimit = _gpuClockAppliedLimitMhz;
        _gpuClockApplying = true;
        NotifyGpuClockDirty();
        try
        {
            await RunDeviceOperationAsync(
                AppStrings.Text("BladeGpuClockOffsetTitle"),
                async () =>
                {
                    try
                    {
                        if (!await OpenSynapseServiceClient.TrySetGpuClockOffsetAsync(
                                core, memory, cancellationToken))
                        {
                            throw new InvalidOperationException(AppStrings.Text("BladeGpuClockOffsetFailed"));
                        }

                        await ApplyGpuClockLimitAsync(limit, cancellationToken);
                        target.GpuCoreClockOffsetMhz = core;
                        target.GpuMemoryClockOffsetMhz = memory;
                        target.GpuCoreClockLimitMhz = limit;
                        if (!await SaveProfileAsync(cancellationToken))
                        {
                            target.GpuCoreClockOffsetMhz = previousProfileCore;
                            target.GpuMemoryClockOffsetMhz = previousProfileMemory;
                            target.GpuCoreClockLimitMhz = previousProfileLimit;
                            await RestoreGpuClockHardwareAsync(
                                previousAppliedCore,
                                previousAppliedMemory,
                                previousAppliedLimit,
                                CancellationToken.None);
                            throw new InvalidOperationException(
                                AppStrings.Text("BladeGpuClockOffsetFailed"));
                        }
                        _gpuClockAppliedCoreMhz = core;
                        _gpuClockAppliedMemoryMhz = memory;
                        _gpuClockAppliedLimitMhz = limit;
                        _gpuClockSnapshotLoaded = true;
                        GpuClockOffsetStatusText = AppStrings.Text("BladeGpuClockOffsetApplied");
                        NotifyGpuClockDirty();
                    }
                    catch
                    {
                        await ProbeGpuClockOffsetCoreAsync();
                        GpuClockOffsetStatusText = AppStrings.Text("BladeGpuClockOffsetFailed");
                        throw;
                    }
                },
                cancellationToken);
        }
        finally
        {
            _gpuClockApplying = false;
            NotifyGpuClockDirty();
        }
    }

    /// <summary>An older service has no clock-limit operation, so its control is disabled.</summary>
    private async Task ApplyGpuClockLimitAsync(
        int megahertz,
        CancellationToken cancellationToken)
    {
        if (!_bladeGpuClockLimitAvailable)
        {
            return;
        }

        var serviceValue = megahertz <= 0 ? NvApiGpuClockLimit.MaximumMhz : megahertz;
        if (!await OpenSynapseServiceClient.TrySetGpuClockLimitAsync(
                serviceValue, cancellationToken))
        {
            throw new InvalidOperationException(AppStrings.Text("BladeGpuClockOffsetFailed"));
        }
    }

    private async Task RestoreGpuClockHardwareAsync(
        int core,
        int memory,
        int limit,
        CancellationToken cancellationToken)
    {
        await OpenSynapseServiceClient.TrySetGpuClockOffsetAsync(
            core, memory, cancellationToken);
        if (_bladeGpuClockLimitAvailable)
        {
            var serviceValue = limit <= 0 ? NvApiGpuClockLimit.MaximumMhz : limit;
            await OpenSynapseServiceClient.TrySetGpuClockLimitAsync(
                serviceValue, cancellationToken);
        }
    }

    /// <summary>Puts the driver back to stock clocks on the user's explicit request.</summary>
    [NotProfileScoped]
    public async Task ResetGpuClockOffsetDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEditGpuClockOffset) return;
        CoreClockOffsetMhz = 0;
        MemoryClockOffsetMhz = 0;
        GpuClockLimitMhz = 0;
        await ApplyGpuClockOffsetAsync(cancellationToken);
    }

    private void NotifyGpuClockDirty()
    {
        OnPropertyChanged(nameof(CanEditGpuClockOffset));
        OnPropertyChanged(nameof(CanEditGpuClockLimit));
        OnPropertyChanged(nameof(GpuOverclockDirty));
        OnPropertyChanged(nameof(CanResetGpuClockOffset));
    }
}
