using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private const string JolietCustomPresetKey = "RazerHeadsetPresetCustom";

    // 1453 product bundle, audioEqualizer preset curves (31 Hz..16 kHz).
    private static readonly IReadOnlyDictionary<string, double[]> JolietPresetCurves =
        new Dictionary<string, double[]>(StringComparer.Ordinal)
        {
            ["RazerHeadsetPresetDefault"] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            ["RazerHeadsetPresetGame"] = [-2, -2, -1, -2, 1, 1, 1, 0, 3, -3],
            ["RazerHeadsetPresetMusic"] = [2, 4, 1, 0, -1, -1, 3, 3, 0, 2],
            ["RazerHeadsetPresetMovie"] = [2, 4, 2, -2, -2, -2, -2, 2, 5, 2],
        };

    private double[] _jolietCustomBands = new double[10];
    private bool _hasJolietCustomBands;

    private void ApplyJolietEqState(byte? preset, IReadOnlyList<byte>? bands)
    {
        if (preset is byte value)
        {
            var index = JolietPresetIndex(value);
            if (index >= 0) SelectedEqPresetIndex = index;
        }

        // The device only exposes its live curve, not one curve per preset.
        // Keep the first read as the Custom curve, then use the local preset
        // tables for the rest of the session.
        if (bands is { Count: 10 } &&
            (!_hasJolietCustomBands || ActivePresets[SelectedEqPresetIndex].Key == JolietCustomPresetKey))
        {
            _jolietCustomBands = bands.Select(FromFirmwareBand).ToArray();
            _hasJolietCustomBands = true;
        }

        ApplyJolietCurve(CurrentJolietCurve());
    }

    private void ApplyJolietCurve(IReadOnlyList<double> levels) =>
        ApplyEqBands(levels.Select(ToFirmwareBand).ToArray());

    private double[] CurrentJolietCurve() =>
        SelectedEqPresetIndex >= 0 && SelectedEqPresetIndex < ActivePresets.Length
            ? JolietCurve(ActivePresets[SelectedEqPresetIndex].Key)
            : _jolietCustomBands;

    private double[] JolietCurveForPreset(byte preset)
    {
        var index = JolietPresetIndex(preset);
        return index >= 0 ? JolietCurve(ActivePresets[index].Key) : _jolietCustomBands;
    }

    private double[] JolietCurve(string key) =>
        key == JolietCustomPresetKey
            ? _jolietCustomBands
            : JolietPresetCurves.TryGetValue(key, out var curve) ? curve : _jolietCustomBands;

    private int JolietPresetIndex(byte preset) =>
        Array.FindIndex(ActivePresets, item => item.Value == preset);

    private byte JolietCustomPreset()
    {
        foreach (var preset in ActivePresets)
        {
            if (preset.Key == JolietCustomPresetKey) return preset.Value;
        }

        return 0x10;
    }

    private async Task ApplyPersistedJolietEqAsync(
        HeadsetProfileSettings profile,
        CancellationToken cancellationToken)
    {
        var custom = ReadBandLevels(profile.EqBands);
        if (custom is not null)
        {
            _jolietCustomBands = custom;
            _hasJolietCustomBands = true;
        }

        var preset = TryParseHexByte(profile.EqPreset);
        if (preset is null && custom is null)
        {
            return;
        }

        var target = preset is byte value && JolietPresetIndex(value) >= 0
            ? value
            : JolietCustomPreset();
        var index = JolietPresetIndex(target);
        if (index < 0)
        {
            return;
        }

        SelectedEqPresetIndex = index;
        ApplyJolietCurve(JolietCurve(ActivePresets[index].Key));
        try
        {
            await WriteCustomEqHardwareAsync(
                target,
                JolietCurve(ActivePresets[index].Key).Select(ToFirmwareBand).ToArray(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"profile headset.eq: {exception.Message}");
        }
    }
}
