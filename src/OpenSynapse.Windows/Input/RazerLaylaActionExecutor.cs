using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Input;

/// <summary>Pod gestures are host mappings. Media uses the existing input sink; macros use the existing player.</summary>
public sealed class RazerLaylaActionExecutor : IAsyncDisposable
{
    private readonly IMacroInputSink _media;
    private readonly MacroRuntime _macros;
    private readonly Action _cyclePlayback;
    private readonly Action<string>? _error;
    private readonly object _gate = new();
    private IReadOnlyDictionary<RazerLaylaInput, RazerLaylaBinding> _bindings = RazerLaylaMappings.Defaults();
    private IReadOnlyList<MacroDefinition> _definitions = [];
    private readonly List<Task> _playbacks = [];
    private bool _disposed;

    public RazerLaylaActionExecutor(Action<string>? error = null)
        : this(new WindowsMacroInputSink(), new WindowsMacroInputSink(), WindowsCoreAudioMuteEventSource.CyclePlaybackDevice, error) { }

    internal RazerLaylaActionExecutor(IMacroInputSink media, IMacroInputSink macros, Action cyclePlayback, Action<string>? error = null)
    {
        _media = media;
        _macros = new(sink: macros, installInputHooks: false);
        _cyclePlayback = cyclePlayback;
        _error = error;
    }

    public void Update(IReadOnlyDictionary<RazerLaylaInput, RazerLaylaBinding> bindings, IReadOnlyList<MacroDefinition> macros)
    {
        RazerLaylaMappings.Validate(bindings);
        foreach (var binding in bindings.Values.Where(value => value.Action == RazerLaylaAction.Macro))
            if (!macros.Any(macro => macro.Id == binding.MacroId && macro.Enabled))
                throw new InvalidDataException($"Control Pod macro '{binding.MacroId}' is missing or disabled.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _macros.StopPlayback();
            _bindings = new Dictionary<RazerLaylaInput, RazerLaylaBinding>(bindings);
            _definitions = macros.Select(MacroDocumentService.Clone).ToArray();
            _macros.UpdateMacros(_definitions);
        }
    }

    public async Task ExecuteAsync(RazerLaylaInput input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RazerLaylaBinding binding;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            binding = _bindings[input];
            if (binding.Action == RazerLaylaAction.Macro)
            {
                var macro = MacroDocumentService.Clone(_definitions.Single(item => item.Id == binding.MacroId));
                // These HID gestures have no key-up edge: each gesture invokes one playback.
                macro.PlaybackMode = MacroPlaybackMode.Once;
                _playbacks.RemoveAll(task => task.IsCompleted);
                _playbacks.Add(PlayAsync(macro, token));
                return;
            }
        }
        if (binding.Action == RazerLaylaAction.Disabled) return;
        if (binding.Action == RazerLaylaAction.CyclePlayback) { _cyclePlayback(); return; }
        var key = binding.Action switch
        {
            RazerLaylaAction.Mute => 0xAD, RazerLaylaAction.VolumeDown => 0xAE,
            RazerLaylaAction.VolumeUp => 0xAF, RazerLaylaAction.NextTrack => 0xB0,
            RazerLaylaAction.PreviousTrack => 0xB1, RazerLaylaAction.PlayPause => 0xB3,
            _ => throw new InvalidDataException("Unknown Control Pod action."),
        };
        var step = new MacroStep { Kind = MacroStepKind.Input, Input = new() { Kind = MacroInputKind.Keyboard, VirtualKey = key, Extended = true }, IsDown = true };
        try { await _media.SendAsync(step, token).ConfigureAwait(false); }
        finally { step.IsDown = false; await _media.SendAsync(step, CancellationToken.None).ConfigureAwait(false); }
    }

    public void InvalidateMacros()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _macros.StopPlayback();
            _definitions = [];
            _bindings = _bindings.ToDictionary(pair => pair.Key, pair => pair.Value.Action == RazerLaylaAction.Macro
                ? new RazerLaylaBinding(RazerLaylaAction.Disabled) : pair.Value);
            _macros.UpdateMacros([]);
        }
    }

    private async Task PlayAsync(MacroDefinition macro, CancellationToken token)
    {
        try { await _macros.PlayAsync(macro, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _error?.Invoke(exception.Message); }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _macros.StopPlayback();
            pending = _playbacks.ToArray();
        }
        await Task.WhenAll(pending).ConfigureAwait(false);
        _macros.Dispose();
    }
}
