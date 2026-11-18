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
        CancellationTokenSource cancellation;
        lock (_turboGate)
        {
            if (_turboRepeats.ContainsKey(key))
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            _turboRepeats[key] = cancellation;
        }

        _ = Task.Run(() => RunTurboAsync(key, binding, cancellation));
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
                held = true;
                await SendInputAsync(target, isDown: true, token).ConfigureAwait(false);
                await Task.Delay(TurboHoldMilliseconds, token).ConfigureAwait(false);
                held = false;
                await SendInputAsync(target, isDown: false, token).ConfigureAwait(false);
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
