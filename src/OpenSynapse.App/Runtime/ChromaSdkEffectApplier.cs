using OpenSynapse.App.ViewModels;
using OpenSynapse.ChromaSdk.Protocol;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Runtime;

internal sealed class ChromaSdkEffectApplier
{
    private const int ChromaRows = 6;
    private const int ChromaColumns = 22;
    private const uint KeyActiveMask = 0x01000000;
    private readonly MainViewModel _viewModel;
    private readonly IBladeLightingController? _lighting;
    private readonly ChromaExternalFrameSource _bladeFrame = new();

    internal ChromaSdkEffectApplier(MainViewModel viewModel, IBladeLightingController? lighting)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _lighting = lighting;
    }

    internal uint SupportedDevices
    {
        get
        {
            var mask = HasKeyboardTarget() ? ChromaSdkDeviceMask.Keyboard : 0u;
            if (HasHidTarget(DeviceCategory.Mouse)) mask |= ChromaSdkDeviceMask.Mouse;
            if (HasHidTarget(DeviceCategory.Headset)) mask |= ChromaSdkDeviceMask.Headset;
            if (HasHidTarget(DeviceCategory.MouseMat)) mask |= ChromaSdkDeviceMask.Mousepad;
            return mask;
        }
    }

    internal async Task<bool> ApplyAsync(ChromaSdkEffect effect, CancellationToken cancellationToken)
    {
        if (effect.DeviceType != ChromaSdkDeviceType.Keyboard)
        {
            var category = effect.DeviceType switch
            {
                ChromaSdkDeviceType.Mouse => DeviceCategory.Mouse,
                ChromaSdkDeviceType.Headset => DeviceCategory.Headset,
                ChromaSdkDeviceType.Mousepad => DeviceCategory.MouseMat,
                _ => (DeviceCategory?)null,
            };
            if (category is null)
            {
                // Keypad/ChromaLink have no OpenSynapse matrix target yet. The SDK
                // still accepts their effects, just as it does with no device attached.
                return true;
            }

            _ = await _viewModel.RazerHid.ApplyChromaFrameAsync(
                effect.Colors.Select(ChromaKeyboardFrameMapper.ToRgb).ToArray(),
                effect.Height,
                effect.Width,
                category.Value,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        var (bladeFrame, chromaFrame, sourceRows, sourceColumns) = effect.Kind switch
        {
            ChromaSdkEffectKind.None => (
                ChromaKeyboardFrameMapper.Static(default),
                new RazerRgb[ChromaRows * ChromaColumns],
                ChromaRows,
                ChromaColumns),
            ChromaSdkEffectKind.Static => (
                ChromaKeyboardFrameMapper.Static(ChromaKeyboardFrameMapper.ToRgb(effect.Colors[0])),
                Enumerable.Repeat(
                    ChromaKeyboardFrameMapper.ToRgb(effect.Colors[0]),
                    ChromaRows * ChromaColumns).ToArray(),
                ChromaRows,
                ChromaColumns),
            ChromaSdkEffectKind.Custom => (
                ChromaKeyboardFrameMapper.Custom(ToMatrix(effect.Colors)),
                effect.Colors.Select(ChromaKeyboardFrameMapper.ToRgb).ToArray(),
                ChromaRows,
                ChromaColumns),
            ChromaSdkEffectKind.CustomKey => (
                ChromaKeyboardFrameMapper.CustomKey(ToMatrix(effect.Colors), ToMatrix(effect.Keys!)),
                ApplySourceKeys(effect.Colors, effect.Keys),
                ChromaRows,
                ChromaColumns),
            ChromaSdkEffectKind.Custom2 => (
                ChromaKeyboardFrameMapper.Custom2(ToMatrix(effect.Colors, 8, 24)),
                effect.Colors.Select(ChromaKeyboardFrameMapper.ToRgb).ToArray(),
                8,
                24),
            _ => throw new InvalidDataException("Unsupported keyboard effect."),
        };

        if (_lighting is not null && HasBladeTarget() && _viewModel.Lighting.IsBladeChromaOverrideEnabled())
        {
            _bladeFrame.Publish(bladeFrame);
            try
            {
                await _lighting.ApplyExternalAsync(
                    _viewModel.CurrentDeviceDescriptors,
                    _bladeFrame,
                    restorePersistentEffect: false,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // RazerHid-only systems have no Blade target.
            }
        }

        _ = await _viewModel.RazerHid.ApplyChromaFrameAsync(
            chromaFrame, sourceRows, sourceColumns, DeviceCategory.Keyboard, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private bool HasKeyboardTarget() =>
        (_lighting is not null && HasBladeTarget()) || HasHidTarget(DeviceCategory.Keyboard);

    private bool HasHidTarget(DeviceCategory category) =>
        _viewModel.RazerHid.CurrentConnections.Any(connection =>
            connection.IsReady &&
            connection.Definition.Category == category &&
            connection.Definition.MatrixDimensions is not null &&
            connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite));

    private bool HasBladeTarget() => _viewModel.CurrentDeviceDescriptors.Any(device =>
        device.Access == DeviceAccessState.Available && IsBlade(device));

    private static bool IsBlade(DeviceDescriptor device) =>
        device.ProtocolFamily is "blade-710" or "blade-710-chroma";

    private static uint[][] ToMatrix(uint[] colors) => ToMatrix(colors, ChromaRows, ChromaColumns);

    private static uint[][] ToMatrix(uint[] colors, int rows, int columns)
    {
        var matrix = new uint[rows][];
        for (var row = 0; row < rows; row++)
        {
            matrix[row] = new uint[columns];
            Array.Copy(colors, row * columns, matrix[row], 0, columns);
        }
        return matrix;
    }

    private static RazerRgb[] ApplySourceKeys(uint[] colors, uint[]? keys)
    {
        var frame = colors.Select(ChromaKeyboardFrameMapper.ToRgb).ToArray();
        if (keys is null)
        {
            return frame;
        }
        for (var index = 0; index < frame.Length; index++)
        {
            if ((keys[index] & KeyActiveMask) != 0)
            {
                frame[index] = ChromaKeyboardFrameMapper.ToRgb((~keys[index]) & 0x00FFFFFFu);
            }
        }
        return frame;
    }
}
