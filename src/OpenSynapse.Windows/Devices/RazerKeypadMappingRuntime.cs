using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>Device-scoped mappings for normal-mode keypad filter input.</summary>
public sealed partial class RazerKeypadMappingRuntime : IAsyncDisposable
{
    private readonly string _instanceId;
    public ushort ProductId { get; }
    private readonly IMacroInputSink _sink;
    private readonly Action<MacroInput, bool, bool>? _inputCallback;
    private readonly Action<IReadOnlyCollection<MacroInput>>? _heldOutputsChanged;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<byte, Press> _pressed = [];
    private readonly Dictionary<OutputKey, HeldOutput> _outputs = [];
    private volatile Dictionary<(byte Button, byte Mode), RazerHidButtonAssignment> _assignments = [];
    private volatile bool _hyperShiftHeld;
    private int _hyperShiftOwners;
    private bool _releasePending;
    private bool _disposed;

    public RazerKeypadMappingRuntime(
        string instanceId,
        IMacroInputSink sink,
        Action<MacroInput, bool, bool>? inputCallback = null,
        Action<IReadOnlyCollection<MacroInput>>? heldOutputsChanged = null,
        ushort productId = RazerKeypadInputCatalog.ProductId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (!RazerKeypadInputCatalog.UsesHostMapping(productId))
            throw new ArgumentOutOfRangeException(nameof(productId), productId, "This keypad uses a different mapping protocol.");
        ProductId = productId;
        _instanceId = instanceId;
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _inputCallback = inputCallback;
        _heldOutputsChanged = heldOutputsChanged;
    }

    public bool IsHyperShiftHeld => _hyperShiftHeld;

    public RazerHidButtonAssignment GetAssignment(byte buttonId, byte mode = 0)
    {
        RazerKeypadInputCatalog.ValidateInput(buttonId, mode, ProductId);
        return _assignments.TryGetValue((buttonId, mode), out var assignment)
            ? Clone(assignment)
            : RazerKeypadInputCatalog.GetDefaultAssignment(buttonId, mode, ProductId);
    }

    public async Task SetAssignmentAsync(
        RazerHidButtonAssignment assignment,
        CancellationToken cancellationToken = default)
    {
        var copy = CloneValidated(assignment);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _assignments = new(_assignments) { [(copy.ButtonId, copy.Mode)] = copy };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Replaces all overrides; omitted inputs regain their defaults.</summary>
    public async Task ReplaceAssignmentsAsync(
        IEnumerable<RazerHidButtonAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var replacement = new Dictionary<(byte Button, byte Mode), RazerHidButtonAssignment>();
        foreach (var assignment in assignments)
        {
            var copy = CloneValidated(assignment);
            if (!replacement.TryAdd((copy.ButtonId, copy.Mode), copy))
            {
                throw new ArgumentException("A keypad input has more than one assignment in the same layer.", nameof(assignments));
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await ReleaseAllCoreAsync().ConfigureAwait(false);
            _assignments = replacement;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ProcessInputAsync(
        BladeMappingInputEvent input,
        CancellationToken cancellationToken = default)
    {
        if (!RazerKeypadInputCatalog.TryResolveInput(input, out var buttonId, ProductId))
        {
            return false;
        }

        var count = input.Kind == BladeMappingInputKind.MouseWheel
            ? Math.Max(1, Math.Abs((long)input.Code) / 120)
            : 1;
        for (long tick = 0; tick < count; tick++)
        {
            await ProcessButtonAsync(buttonId, input.IsDown, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    public async Task ProcessButtonAsync(
        byte buttonId,
        bool isDown,
        CancellationToken cancellationToken = default)
    {
        RazerKeypadInputCatalog.ValidateInput(buttonId, 0, ProductId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                if (_releasePending)
                {
                    await ReleaseAllCoreAsync().ConfigureAwait(false);
                }
                if (isDown)
                {
                    await PressAsync(buttonId, cancellationToken).ConfigureAwait(false);
                    if (RazerKeypadInputCatalog.IsWheelInput(buttonId, ProductId))
                    {
                        await ReleaseAsync(buttonId).ConfigureAwait(false);
                    }
                }
                else
                {
                    await ReleaseAsync(buttonId).ConfigureAwait(false);
                }
            }
            catch (Exception inputError)
            {
                try
                {
                    await ReleaseAllCoreAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(inputError, cleanupError);
                }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReleaseAllCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            // Failed releases remain tracked, so disposal can be retried.
            await ReleaseAllCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PressAsync(byte buttonId, CancellationToken cancellationToken)
    {
        if (_pressed.TryGetValue(buttonId, out var previous))
        {
            foreach (var key in previous.Outputs)
            {
                var output = _outputs[key].Input;
                if (output.Kind == MacroInputKind.Keyboard && !output.IsModifierKey)
                {
                    await SendAsync(output, true, cancellationToken).ConfigureAwait(false);
                }
            }
            return;
        }

        var layer = _hyperShiftHeld;
        var assignment = GetAssignment(buttonId, layer ? (byte)1 : (byte)0);
        var press = new Press(new MacroInput
        {
            Kind = MacroInputKind.RazerKey,
            DeviceId = ProductId,
            InstanceId = _instanceId,
            RazerKeyCode = buttonId,
        }, layer, assignment.FunctionId == RazerHidMappingFunctions.ModeButtonkey);
        _pressed.Add(buttonId, press);
        if (press.HyperShift)
        {
            _hyperShiftOwners++;
            _hyperShiftHeld = true;
        }

        // Mark before invoking: a callback can perform its action and then throw.
        press.CallbackDown = true;
        _inputCallback?.Invoke(press.Trigger.Clone(), true, layer);
        if (assignment.FunctionId == RazerHidMappingFunctions.DoubleClick)
        {
            for (var click = 0; click < 2; click++)
            {
                await HoldAsync(press, new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = 1 }, cancellationToken)
                    .ConfigureAwait(false);
                await ReleaseOutputsAsync(press).ConfigureAwait(false);
            }
            return;
        }

        foreach (var output in CreateOutputs(assignment))
        {
            if (output.Kind == MacroInputKind.MouseWheel)
            {
                await SendAsync(output, false, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await HoldAsync(press, output, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HoldAsync(Press press, MacroInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = OutputKey.From(input);
        if (press.Outputs.Contains(key))
        {
            return;
        }
        if (_outputs.TryGetValue(key, out var held))
        {
            held.Owners++;
        }
        else
        {
            held = new HeldOutput(input);
            _outputs.Add(key, held);
            press.Outputs.Add(key);
            // Journal the candidate before injection so a crash cannot strand it.
            PublishHeldOutputs();
            // A sink can accept the input before reporting a failure.
            held.Injected = true;
            await SendAsync(input, true, cancellationToken).ConfigureAwait(false);
            return;
        }
        press.Outputs.Add(key);
    }

    private async Task ReleaseAsync(byte buttonId)
    {
        if (!_pressed.TryGetValue(buttonId, out var press))
        {
            return;
        }
        if (press.HyperShift)
        {
            press.HyperShift = false;
            _hyperShiftHeld = --_hyperShiftOwners != 0;
        }
        if (press.CallbackDown)
        {
            _inputCallback?.Invoke(press.Trigger.Clone(), false, press.Layer);
            press.CallbackDown = false;
        }
        await ReleaseOutputsAsync(press).ConfigureAwait(false);
        _pressed.Remove(buttonId);
    }

    private async Task ReleaseOutputsAsync(Press press)
    {
        for (var index = press.Outputs.Count - 1; index >= 0; index--)
        {
            var key = press.Outputs[index];
            var held = _outputs[key];
            if (held.Owners == 1)
            {
                await SendAsync(held.Input, false, CancellationToken.None).ConfigureAwait(false);
                _outputs.Remove(key);
                press.Outputs.RemoveAt(index);
                PublishHeldOutputs();
            }
            else
            {
                held.Owners--;
                press.Outputs.RemoveAt(index);
            }
        }
    }

    private async Task ReleaseAllCoreAsync()
    {
        List<Exception>? errors = null;
        _releasePending = true;
        _hyperShiftOwners = 0;
        _hyperShiftHeld = false;
        foreach (var (buttonId, press) in _pressed.ToArray())
        {
            press.HyperShift = false;
            try
            {
                if (press.CallbackDown)
                {
                    _inputCallback?.Invoke(press.Trigger.Clone(), false, press.Layer);
                    press.CallbackDown = false;
                }
                _pressed.Remove(buttonId);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
        foreach (var (key, held) in _outputs.Reverse().ToArray())
        {
            try
            {
                if (held.Injected)
                {
                    await SendAsync(held.Input, false, CancellationToken.None).ConfigureAwait(false);
                }
                _outputs.Remove(key);
                PublishHeldOutputs();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }
        try
        {
            PublishHeldOutputs();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        if (errors is not null)
        {
            throw new AggregateException("Some keypad inputs could not be released.", errors);
        }
        _releasePending = false;
    }

    private void PublishHeldOutputs() =>
        _heldOutputsChanged?.Invoke(_outputs.Values.Select(held => held.Input.Clone()).ToArray());

    private Task SendAsync(MacroInput input, bool isDown, CancellationToken cancellationToken) =>
        _sink.SendAsync(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = input.Clone(),
            IsDown = isDown,
            AutoRelease = false,
        }, cancellationToken);

    private RazerHidButtonAssignment CloneValidated(RazerHidButtonAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(assignment.Data);
        var copy = Clone(assignment);
        RazerKeypadInputCatalog.ValidateAssignment(copy, ProductId);
        return copy;
    }

    private static RazerHidButtonAssignment Clone(RazerHidButtonAssignment assignment) =>
        assignment with { Data = assignment.Data.ToArray() };

    private sealed class Press(MacroInput trigger, bool layer, bool hyperShift)
    {
        public MacroInput Trigger { get; } = trigger;
        public bool Layer { get; } = layer;
        public bool HyperShift { get; set; } = hyperShift;
        public bool CallbackDown { get; set; }
        public List<OutputKey> Outputs { get; } = [];
    }

    private sealed class HeldOutput(MacroInput input)
    {
        public MacroInput Input { get; } = input;
        public int Owners { get; set; } = 1;
        public bool Injected { get; set; }
    }

    private readonly record struct OutputKey(MacroInputKind Kind, int Code, bool Extended)
    {
        public static OutputKey From(MacroInput input) => new(
            input.Kind,
            input.Kind == MacroInputKind.MouseButton ? input.MouseButton :
                input.ScanCode == 0 ? input.VirtualKey : input.ScanCode | 0x10000,
            input.Extended);
    }
}
