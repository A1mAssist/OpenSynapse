namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _peripheralLightingSuspended;

    internal Func<Task>? SuspendStudioLightingForDisplay { get; set; }
    internal Func<Task>? ResumeStudioLightingForDisplay { get; set; }

    internal async Task SuspendPeripheralLightingForDisplayAsync()
    {
        if (_peripheralLightingSuspended)
        {
            return;
        }

        _peripheralLightingSuspended = true;
        await RunDisplayLightingStepAsync("studio suspend", SuspendStudioLightingForDisplay)
            .ConfigureAwait(true);
        await _deviceOperationGate.WaitAsync();
        try
        {
            await RunDisplayLightingStepAsync("razorhid off", () => RazerHid.SetDisplayLightingAvailableAsync(false));
            await RunDisplayLightingStepAsync("chroma off", () => Accessories.SetDisplayLightingAvailableAsync(false));
        }
        finally { _deviceOperationGate.Release(); }
    }

    internal async Task ResumePeripheralLightingForDisplayAsync()
    {
        if (!_peripheralLightingSuspended)
        {
            return;
        }

        _peripheralLightingSuspended = false;
        await RunDisplayLightingStepAsync(
                "razorhid restore", () => RazerHid.SetDisplayLightingAvailableAsync(true))
            .ConfigureAwait(true);
        await RunDisplayLightingStepAsync(
                "chroma restore", () => Accessories.SetDisplayLightingAvailableAsync(true))
            .ConfigureAwait(true);
        await RunDisplayLightingStepAsync("studio resume", ResumeStudioLightingForDisplay)
            .ConfigureAwait(true);
    }

    private async Task RunDisplayLightingStepAsync(string step, Func<Task>? action)
    {
        if (action is null)
        {
            return;
        }

        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite(
                "lighting-display",
                $"{step} failed: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
