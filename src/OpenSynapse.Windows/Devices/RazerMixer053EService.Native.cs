namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerMixer053EService
{
    /// <summary>
    /// True when the optional Razer native DLL was loaded. This only means the
    /// provider is present; HID remains the default path until a native probe is
    /// requested.
    /// </summary>
    public bool NativeAvailable => _native?.IsAvailable == true;

    /// <summary>
    /// Read-only native probe: enumerate render/capture devices and read the raw
    /// peak property. Returns null when the DLL is absent or initialization fails;
    /// this method never writes a mixer property and never touches the HID path.
    /// </summary>
    public async Task<RazerMixer053ENativeProbe?> ProbeNativeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_native is null ||
            !await _native.InitializeAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var capture = _native.EnumerateDevices(1);
        var render = _native.EnumerateDevices(0);
        var peaks = new List<RazerMixer053ENativeDevicePeak>();
        foreach (var device in capture.Concat(render))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_native.ReadPeak(device) is { } peak)
            {
                peaks.Add(new RazerMixer053ENativeDevicePeak(device, peak));
            }
        }

        return new RazerMixer053ENativeProbe(capture, render, peaks);
    }
}
