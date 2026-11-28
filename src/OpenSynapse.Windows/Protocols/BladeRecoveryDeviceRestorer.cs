using OpenSynapse.Windows.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.Windows.Protocols;

public static class BladeRecoveryDeviceRestorer
{
    public static async Task RestoreNormalModeAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        await using var session = await transport.OpenSessionAsync(devicePath, cancellationToken).ConfigureAwait(false);
        await BladeAudioMuteRuntime.InitializeSessionAsync(session, cancellationToken).ConfigureAwait(false);
        await BladeAudioMuteRuntime.SetDeviceModeAsync(session, softwareMode: false, cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<BladeMappingOutputEvent> CreateReleaseEvents(
        IEnumerable<BladeRecoverySyntheticKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Distinct()
            .Where(static key => !key.IsMouseButton && key.VirtualKey == 0)
            .OrderBy(static key => key.ScanCode)
            .ThenBy(static key => key.Extended)
            .Select(static key => new BladeMappingOutputEvent(key.ScanCode, false, key.Extended))
            .ToArray();
    }

    internal static IReadOnlyList<MacroStep> CreateMacroReleaseSteps(IEnumerable<BladeRecoverySyntheticKey> keys) =>
        keys.Where(static key => key.IsMouseButton || key.VirtualKey != 0).Distinct()
            .Select(static key => new MacroStep
            {
                Kind = MacroStepKind.Input,
                Input = key.IsMouseButton
                    ? new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = key.ScanCode }
                    : new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = key.VirtualKey, Extended = key.Extended },
                IsDown = false,
            }).ToArray();

    public static void ReleaseSyntheticKeys(IEnumerable<BladeRecoverySyntheticKey> keys, string? keypadFilterPath = null)
    {
        var snapshot = keys.ToArray();
        if (keypadFilterPath is not null)
        {
            ReleaseKeypadKeysAsync(snapshot, keypadFilterPath).GetAwaiter().GetResult();
            return;
        }
        var errors = new List<Exception>();
        try { new WindowsKeyboardInputSink().Send(CreateReleaseEvents(snapshot)); }
        catch (Exception exception) { errors.Add(exception); }
        var macroSink = new WindowsMacroInputSink();
        foreach (var step in CreateMacroReleaseSteps(snapshot))
        {
            try { macroSink.SendAsync(step).GetAwaiter().GetResult(); }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (errors.Count > 0) throw new AggregateException("Synthetic input recovery was incomplete.", errors);
    }

    private static async Task ReleaseKeypadKeysAsync(BladeRecoverySyntheticKey[] keys, string filterPath)
    {
        await using var sink = RazerFilterMacroInputSink.CreateForKeypad(filterPath);
        var steps = CreateReleaseEvents(keys).Select(key => new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = new MacroInput { Kind = MacroInputKind.Keyboard, ScanCode = key.ScanCode, Extended = key.Extended },
            IsDown = false,
        }).Concat(CreateMacroReleaseSteps(keys));
        var errors = new List<Exception>();
        foreach (var step in steps)
        {
            try { await sink.SendAsync(step).ConfigureAwait(false); }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (errors.Count > 0) throw new AggregateException("Keypad input recovery was incomplete.", errors);
    }
}
