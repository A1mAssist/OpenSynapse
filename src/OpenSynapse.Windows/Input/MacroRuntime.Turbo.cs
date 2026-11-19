using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

internal sealed partial class MacroRuntime
{
    private const int TurboHoldMilliseconds = 15;
    private readonly object _turboGate = new();
    private readonly Dictionary<string, CancellationTokenSource> _turboRepeats =
        new(StringComparer.Ordinal);

    internal Func<MacroInput, bool, RazerTurboBinding?>? TurboResolver { get; set; }

    private void StartTurbo(MacroInput trigger, RazerTurboBinding binding)
    {
        var key = TurboKey(trigger);
        CancellationTokenSource? previous;
        CancellationTokenSource? fresh = null;
        lock (_turboGate)
        {
            if (_turboRepeats.TryGetValue(key, out previous))
            {
                _turboRepeats.Remove(key);
            }
            else
            {
                previous = null;
                fresh = new CancellationTokenSource();
                _turboRepeats[key] = fresh;
            }
        }

        if (previous is not null)
        {
            // A private key that reports only presses, or whose release was lost
            // with the device, would otherwise repeat until the app restarts.
            // The next press is the stop control.
            previous.Cancel();
            _log?.Invoke($"turbo stopped by a second press: {key}");
            return;
        }

        _log?.Invoke(
            $"turbo started: {key} button={binding.MouseButton} interval={binding.IntervalMilliseconds}ms");
        _ = Task.Run(() => RunTurboAsync(key, binding, fresh!));
    }

    private void StopTurbo(MacroInput trigger) => StopTurbo(TurboKey(trigger));

    private void StopTurbo(string key)
    {
        CancellationTokenSource? cancellation;
        lock (_turboGate)
        {
            if (!_turboRepeats.Remove(key, out cancellation))
            {
                return;
            }
        }

        _log?.Invoke($"turbo stopped: {key}");
        cancellation.Cancel();
    }

    internal void StopAllTurbo()
    {
        CancellationTokenSource[] active;
        lock (_turboGate)
        {
            active = _turboRepeats.Values.ToArray();
            _turboRepeats.Clear();
        }

        foreach (var cancellation in active)
        {
            cancellation.Cancel();
        }
    }

    private async Task RunTurboAsync(
        string key,
        RazerTurboBinding binding,
        CancellationTokenSource cancellation)
    {
        var target = new MacroInput
        {
            Kind = MacroInputKind.MouseButton,
            MouseButton = binding.MouseButton,
        };
        var token = cancellation.Token;
        var held = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await SendInputAsync(target, isDown: true, token).ConfigureAwait(false);
                held = true;
                await Task.Delay(TurboHoldMilliseconds, token).ConfigureAwait(false);
                await SendInputAsync(target, isDown: false, token).ConfigureAwait(false);
                held = false;
                await Task.Delay(
                        Math.Max(1, binding.IntervalMilliseconds - TurboHoldMilliseconds),
                        token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _log?.Invoke($"turbo repeat failed: {exception.Message}");
        }
        finally
        {
            if (held)
            {
                try
                {
                    await SendInputAsync(target, isDown: false, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _log?.Invoke($"turbo release failed: {exception.Message}");
                    // A rejected key-up leaves the synthetic button held for the
                    // whole desktop, so clear the sink's pressed set as a last resort.
                    try
                    {
                        await _sink.ReleaseAllAsync().ConfigureAwait(false);
                    }
                    catch (Exception fallback)
                    {
                        _log?.Invoke($"turbo release-all failed: {fallback.Message}");
                    }
                }
            }

            lock (_turboGate)
            {
                if (_turboRepeats.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, cancellation))
                {
                    _turboRepeats.Remove(key);
                }
            }

            cancellation.Dispose();
        }
    }

    private static string TurboKey(MacroInput input) =>
        $"{input.DeviceId:X4}:{input.RazerKeyCode:X2}";
}
