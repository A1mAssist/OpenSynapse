using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task RunDeviceOperationAsync(
        string label,
        Func<Task> operation,
        CancellationToken cancellationToken,
        Action? restoreSelection = null,
        string? successVerb = null,
        string? failureVerb = null)
    {
        if (!await TryEnterOperationAsync(cancellationToken))
        {
            restoreSelection?.Invoke();
            return;
        }
        IsBusy = true;
        SetDeviceOperationError(string.Empty);
        successVerb ??= AppStrings.Text("Text_242C3A0C");
        failureVerb ??= AppStrings.Text("Text_CC3895A3");
        try
        {
            await operation();
            DeviceTelemetryTimeText = AppStrings.FormatText("DeviceOperationSucceeded",
                label,
                successVerb,
                DateTimeOffset.Now);
        }
        catch (OperationCanceledException exception)
        {
            restoreSelection?.Invoke();
            if (!cancellationToken.IsCancellationRequested)
            {
                SetDeviceOperationError(AppStrings.FormatText("DeviceOperationFailed",
                    label,
                    failureVerb,
                    FormatOperationException(exception)));
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or NotSupportedException or ArgumentOutOfRangeException or OverflowException or AggregateException or ObjectDisposedException)
        {
            restoreSelection?.Invoke();
            SetDeviceOperationError(AppStrings.FormatText("DeviceOperationFailed",
                label,
                failureVerb,
                FormatOperationException(exception)));
        }
        finally
        {
            IsBusy = false;
            _deviceOperationGate.Release();
        }
    }

    private async Task<bool> TryEnterOperationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _deviceOperationGate.WaitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }


}

