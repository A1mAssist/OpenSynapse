using System.ComponentModel;
using System.IO.Pipes;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Optional fast path for machine-wide installs. A missing service, a stopped
/// service, or a protocol mismatch returns null so the caller can use the
/// existing one-shot UAC helper.
/// </summary>
public static class DedicatedGpuModeServiceClient
{
    private static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(20);

    public static async Task<DedicatedGpuSwitchResult?> TrySetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var request = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetOperation,
            enabled);

        return await SendWithWakeupRetryAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the service to restart the discrete adapter. Only the machine-wide
    /// service can do this, because a device restart needs administrator rights.
    /// </summary>
    public static async Task<DedicatedGpuSwitchResult?> TryRestartDeviceAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.RestartOperation,
            Enabled: null);

        return await SendWithWakeupRetryAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DedicatedGpuSwitchResult?> SendWithWakeupRetryAsync(
        DedicatedGpuModeServiceRequest request,
        CancellationToken cancellationToken)
    {

        // The service is demand-started and stops itself after an idle window, so a
        // wake-up races the pipe creation. One retry covers that gap and keeps the
        // common case off the UAC helper.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!await TryEnsureServiceRunningAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response is not null &&
                response.Handled &&
                response.Version == DedicatedGpuModeServiceProtocol.Version)
            {
                return new DedicatedGpuSwitchResult(response.Outcome, response.ExitCode);
            }

            if (attempt == 0)
            {
                await Task.Delay(PipeConnectTimeout, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static Task<bool> TryEnsureServiceRunningAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            try
            {
                using var controller = new ServiceController(DedicatedGpuModeServiceProtocol.ServiceName);
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.Running)
                {
                    return true;
                }

                if (controller.Status == ServiceControllerStatus.StartPending)
                {
                    controller.WaitForStatus(
                        ServiceControllerStatus.Running,
                        ServiceStartTimeout);
                    return true;
                }

                if (controller.Status == ServiceControllerStatus.StopPending)
                {
                    controller.WaitForStatus(
                        ServiceControllerStatus.Stopped,
                        ServiceStartTimeout);
                    controller.Refresh();
                }

                controller.Start();
                controller.WaitForStatus(
                    ServiceControllerStatus.Running,
                    ServiceStartTimeout);
                return true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or Win32Exception or
                    System.ServiceProcess.TimeoutException or ObjectDisposedException)
            {
                return false;
            }
        }, cancellationToken);

    private static async Task<DedicatedGpuModeServiceResponse?> SendAsync(
        DedicatedGpuModeServiceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                DedicatedGpuModeServiceProtocol.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(
                (int)PipeConnectTimeout.TotalMilliseconds,
                cancellationToken).ConfigureAwait(false);

            using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(false),
                4096,
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: false,
                4096,
                leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            responseTimeout.CancelAfter(ResponseTimeout);
            var line = await reader.ReadLineAsync(responseTimeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) ||
                line.Length > DedicatedGpuModeServiceProtocol.MaximumMessageLength)
            {
                return null;
            }

            return JsonSerializer.Deserialize<DedicatedGpuModeServiceResponse>(line);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }
}
