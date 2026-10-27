using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Routes macro output to the elevated agent when it is available, and to a
/// direct SendInput otherwise.
///
/// The elevated path is what lets a macro reach a window that runs at a higher
/// integrity level. The direct path stays as the fallback so a machine
/// without the scheduled task still plays macros normally.
/// </summary>
public sealed class ElevatedMacroInputSink : IMacroInputSink
{
    private readonly WindowsMacroInputSink _direct = new();
    private readonly ElevatedMacroClient _client;
    private bool _useElevated;

    public ElevatedMacroInputSink(ElevatedMacroClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// True when events are currently routed through the elevated agent.
    /// </summary>
    public bool IsElevated => _useElevated;

    /// <summary>
    /// Probes the agent once. A failed probe keeps the direct path, so the
    /// app never blocks on a missing agent.
    /// </summary>
    public async Task<bool> TryEnableAsync(CancellationToken cancellationToken = default)
    {
        _useElevated = await _client.IsElevatedAsync(cancellationToken).ConfigureAwait(false);
        return _useElevated;
    }

    public async Task SendAsync(MacroStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Kind == MacroStepKind.Delay)
        {
            return;
        }

        if (_useElevated && step.Input is { } input)
        {
            if (input.Kind is MacroInputKind.NagaSideButton or MacroInputKind.DeviceButton or
                MacroInputKind.RazerKey)
            {
                // Device-scoped events have no keyboard representation, so
                // they cannot be replayed through the agent.
                await _direct.SendAsync(step, cancellationToken).ConfigureAwait(false);
                return;
            }

            var response = await _client.SendInputAsync(input, step.IsDown, cancellationToken)
                .ConfigureAwait(false);
            if (response?.Ok == true)
            {
                return;
            }

            // The agent stopped answering. Fall back instead of dropping the
            // rest of the macro.
            _useElevated = false;
        }

        await _direct.SendAsync(step, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_useElevated)
        {
            var response = await _client.SendTextAsync(text, cancellationToken).ConfigureAwait(false);
            if (response?.Ok == true)
            {
                return;
            }

            _useElevated = false;
        }

        await _direct.SendTextAsync(text, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendMouseMoveAsync(
        MacroMouseMoveMode mode,
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        if (_useElevated)
        {
            var response = await _client.SendMouseMoveAsync(mode, x, y, cancellationToken)
                .ConfigureAwait(false);
            if (response?.Ok == true)
            {
                return;
            }

            _useElevated = false;
        }

        await _direct.SendMouseMoveAsync(mode, x, y, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases every key the agent still holds down.
    ///
    /// The app releases its own pressed keys when a macro stops, but the
    /// agent outlives a single playback. A macro that was interrupted while a
    /// key was down would otherwise leave that key stuck on the desktop.
    /// </summary>
    public async Task ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_useElevated)
        {
            return;
        }

        var response = await _client.ReleaseAllAsync(cancellationToken).ConfigureAwait(false);
        if (response?.Ok != true)
        {
            _useElevated = false;
        }
    }
}
