using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class KeypadMacroBindingTransaction : IAsyncDisposable
{
    private readonly Action<string> _capture;
    private readonly Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
        MacroSoftwareBinding?, CancellationToken, Task<string?>> _bind;
    private readonly Func<string, MacroInput, bool, CancellationToken, Task<string?>> _clear;
    private readonly Action _prepare;
    private readonly Action _commit;
    private readonly Action _rollback;
    private readonly Action _release;
    private bool _disposed;
    private bool _prepared;

    internal KeypadMacroBindingTransaction(
        Action<string> capture,
        Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
            MacroSoftwareBinding?, CancellationToken, Task<string?>> bind,
        Func<string, MacroInput, bool, CancellationToken, Task<string?>> clear,
        Action prepare, Action commit, Action rollback, Action release)
    {
        _capture = capture;
        _bind = bind;
        _clear = clear;
        _prepare = prepare;
        _commit = commit;
        _rollback = rollback;
        _release = release;
    }

    public bool IsCommitted { get; private set; }

    public Task<string?> BindAsync(
        string macroId, MacroInput input, MacroTriggerMode triggerMode, MacroPlaybackMode playbackMode,
        int repeatCount, bool hyperShiftLayer, MacroSoftwareBinding? binding, CancellationToken cancellationToken)
    {
        CheckInput(input);
        _capture(macroId);
        return _bind(macroId, input, triggerMode, playbackMode, repeatCount, hyperShiftLayer, binding, cancellationToken);
    }

    public Task<string?> ClearAsync(string macroId, MacroInput input, bool hyperShiftLayer, CancellationToken cancellationToken)
    {
        CheckInput(input);
        _capture(macroId);
        return _clear(macroId, input, hyperShiftLayer, cancellationToken);
    }

    public void PrepareSave()
    {
        CheckOpen();
        _prepare();
        _prepared = true;
    }

    public void Commit()
    {
        CheckOpen();
        if (!_prepared) throw new InvalidOperationException("The keypad binding transaction has not been prepared for saving.");
        IsCommitted = true;
        _commit();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        try
        {
            if (!IsCommitted) _rollback();
        }
        finally { _release(); }
        return ValueTask.CompletedTask;
    }

    private void CheckInput(MacroInput input)
    {
        CheckOpen();
        _prepared = false;
        if (input.Kind != MacroInputKind.RazerKey ||
            !RazerKeypadInputCatalog.UsesHostMapping(input.DeviceId) ||
            string.IsNullOrWhiteSpace(input.InstanceId) || input.RazerKeyCode is < 0 or > byte.MaxValue ||
            !RazerKeypadInputCatalog.GetButtonIds(input.DeviceId).Contains((byte)input.RazerKeyCode))
        {
            throw new InvalidOperationException("A keypad transaction requires an input from a specific keypad instance.");
        }
    }

    private void CheckOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsCommitted) throw new InvalidOperationException("The keypad binding transaction is already committed.");
    }
}
