using System.ComponentModel;
using System.IO.Pipes;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Optional fast path for machine-wide installs. A missing service, a stopped
/// service, or a confirmed protocol rejection returns null for the UAC helper.
/// A PnP write with no trustworthy terminal response is uncertain, never a fallback.
/// </summary>
public static class OpenSynapseServiceClient
{
    private static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ApplyUpdateResponseTimeout = TimeSpan.FromMinutes(2);

    public static async Task<DedicatedGpuSwitchResult?> TrySetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.SetOperation,
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
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.RestartOperation,
            Enabled: null);

        return await SendWithWakeupRetryAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the service to create the macro agent's scheduled task and start it.
    ///
    /// The task must run at the highest integrity level, so only the
    /// machine-wide service can create it. The agent then runs in the user's
    /// session without any further prompt.
    /// </summary>
    public static async Task<bool> TryEnsureMacroAgentAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.EnsureMacroAgentOperation,
            Enabled: null);

        var response = await SendWithWakeupRetryAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return response?.Outcome == DedicatedGpuSwitchOutcome.Success;
    }

    /// <summary>
    /// Asks the machine-wide service to start the Chroma broker as LocalSystem.
    /// The broker exits when the calling app process exits.
    /// </summary>
    public static async Task<bool> TryStartChromaBrokerAsync(
        int parentProcessId,
        CancellationToken cancellationToken = default)
    {
        if (parentProcessId <= 0)
        {
            return false;
        }

        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.StartChromaBrokerOperation,
            Enabled: null,
            ParentProcessId: parentProcessId);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.Handled == true &&
            response.Outcome == DedicatedGpuSwitchOutcome.Success;
    }

    /// <summary>
    /// Stops the Chroma broker that belongs to this installation's service.
    /// The broker also exits by itself when its parent app process goes away.
    /// </summary>
    public static async Task<bool> TryStopChromaBrokerAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsServiceRunning())
        {
            return true;
        }

        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.StopChromaBrokerOperation,
            Enabled: null);

        var response = await SendForResponseAsync(
                request,
                cancellationToken,
                _ => Task.FromResult(IsServiceRunning()),
                (value, token) => ExchangeAsync(value, token, ConnectAsync),
                Task.Delay)
            .ConfigureAwait(false);
        return response?.Handled == true &&
            response.Outcome == DedicatedGpuSwitchOutcome.Success;
    }

    /// <summary>
    /// Asks the machine-wide service to start a Razer driver service that is
    /// installed but stopped. False means the privileged host is unavailable,
    /// the installed host predates this operation, or the driver refused to start.
    /// </summary>
    public static async Task<bool> TryStartDriverServiceAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.StartDriverServiceOperation,
            Enabled: null,
            ServiceName: serviceName);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.Handled == true &&
            response.Outcome == DedicatedGpuSwitchOutcome.Success;
    }

    /// <summary>
    /// Asks the machine-wide service to attach the staged RzDev filter INF for one
    /// product. Null means the service is unavailable, so the caller keeps its UAC
    /// fallback. Applied is false when nothing was staged, which is not a failure.
    /// </summary>
    public static async Task<(bool Applied, string? Error)?> TryBindRazerFilterAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.BindFilterOperation,
            Enabled: null,
            ProductId: productId);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.Handled == true
            ? (response.Outcome == DedicatedGpuSwitchOutcome.Success &&
                response.ExitCode == 0, response.Error)
            : null;
    }

    /// <summary>
    /// Asks the machine-wide service to install the WHQL Razer Common driver.
    /// Null means the service is unavailable, so the caller keeps its UAC fallback.
    /// </summary>
    public static async Task<bool?> TryInstallRazerCommonAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.InstallRazerCommonOperation,
            Enabled: null);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.Handled == true
            ? response.Outcome == DedicatedGpuSwitchOutcome.Success
            : null;
    }

    /// <summary>
    /// Asks the machine-wide service to download and install the official Chroma
    /// DLL. Null means the service is unavailable, so the caller keeps its own
    /// elevated fallback; false means the service ran and the install failed.
    /// </summary>
    public static async Task<bool?> TryInstallChromaDllAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.InstallChromaDllOperation,
            Enabled: null);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.Handled == true
            ? response.Outcome == DedicatedGpuSwitchOutcome.Success
            : null;
    }

    /// <summary>Applies a downloaded Velopack package through the machine service.</summary>
    public static async Task<bool?> TryApplyUpdateAsync(
        string packageFileName,
        string packagePath,
        string packageVersion,
        int waitProcessId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageFileName) ||
            string.IsNullOrWhiteSpace(packagePath) ||
            string.IsNullOrWhiteSpace(packageVersion) ||
            waitProcessId <= 0)
        {
            return false;
        }

        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.ApplyUpdateOperation,
            Enabled: null,
            PackageFileName: packageFileName,
            WaitProcessId: waitProcessId,
            PackagePath: packagePath,
            PackageVersion: packageVersion);

        var response = await SendForResponseAsync(
                request,
                cancellationToken,
                ApplyUpdateResponseTimeout)
            .ConfigureAwait(false);
        return response?.Handled == true
            ? response.Outcome == DedicatedGpuSwitchOutcome.Success
            : null;
    }

    /// <summary>
    /// Asks the service whether the driver accepts GPU clock offsets. Null means the
    /// service is unavailable, so the caller keeps the offset controls hidden.
    /// </summary>
    public static async Task<DedicatedGpuClockOffsetState?> TryGetGpuClockOffsetAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.GpuClockOffsetStatusOperation,
            Enabled: null);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        if (response?.ClockOffsetAvailable is not { } available)
        {
            return null;
        }

        return new DedicatedGpuClockOffsetState(
            available,
            response.CoreOffsetMhz ?? 0,
            response.MemoryOffsetMhz ?? 0);
    }

    /// <summary>
    /// Applies the GPU core and memory clock offsets. Only the machine-wide service can do
    /// this, because the NVAPI write needs administrator rights.
    /// </summary>
    public static async Task<bool> TrySetGpuClockOffsetAsync(
        int coreOffsetMhz,
        int memoryOffsetMhz,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.SetGpuClockOffsetOperation,
            Enabled: null,
            CoreOffsetMhz: coreOffsetMhz,
            MemoryOffsetMhz: memoryOffsetMhz);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.Outcome == DedicatedGpuSwitchOutcome.Success;
    }

    /// <summary>
    /// Reads the driver's clock ceiling. Null means the installed service predates the
    /// clock-limit operation, so the caller hides the control instead of showing an error.
    /// </summary>
    public static async Task<DedicatedGpuClockLimitState?> TryGetGpuClockLimitAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.GpuClockLimitStatusOperation,
            Enabled: null);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        if (response?.ClockLimitAvailable is not { } available)
        {
            return null;
        }

        return new DedicatedGpuClockLimitState(
            available,
            response.ClockLimitMhz ?? 0);
    }

    /// <summary>Applies the GPU core clock ceiling through the machine-wide service.</summary>
    public static async Task<bool> TrySetGpuClockLimitAsync(
        int megahertz,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.SetGpuClockLimitOperation,
            Enabled: null,
            ClockLimitMhz: megahertz);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.Outcome == DedicatedGpuSwitchOutcome.Success;
    }

    /// <summary>
    /// Reads the CPU package power wall. Null means the installed service predates the
    /// operation or the pipe is unreachable.
    /// </summary>
    public static async Task<CpuPowerLimitReport?> TryGetCpuPowerLimitAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.CpuPowerLimitStatusOperation,
            Enabled: null);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.PowerLimit;
    }

    /// <summary>Applies both CPU power walls through the machine-wide service.</summary>
    public static async Task<CpuPowerLimitReport?> TrySetCpuPowerLimitAsync(
        double pl1Watts,
        double pl2Watts,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.SetCpuPowerLimitOperation,
            Enabled: null,
            Pl1Watts: pl1Watts,
            Pl2Watts: pl2Watts);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.PowerLimit;
    }

    /// <summary>
    /// Restores an explicit pair of walls, or the service's open-time firmware snapshot
    /// when both values are null.
    /// </summary>
    public static async Task<CpuPowerLimitReport?> TryRestoreCpuPowerLimitAsync(
        double? pl1Watts,
        double? pl2Watts,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.RestoreCpuPowerLimitOperation,
            Enabled: null,
            Pl1Watts: pl1Watts,
            Pl2Watts: pl2Watts);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.PowerLimit;
    }

    /// <summary>
    /// Reports whether the SMU accepts a Curve Optimizer write. Null means the installed
    /// service predates the operation or the pipe is unreachable.
    /// </summary>
    public static async Task<DedicatedGpuCurveOptimizerState?> TryGetCurveOptimizerAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.CurveOptimizerStatusOperation,
            Enabled: null);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response?.CurveOptimizerAvailable is { } available
            ? new DedicatedGpuCurveOptimizerState(available, response.Error)
            : null;
    }

    /// <summary>Applies an all-core Curve Optimizer offset through the machine service.</summary>
    public static async Task<DedicatedGpuCurveOptimizerResult?> TrySetCurveOptimizerAsync(
        int steps,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.SetCurveOptimizerOperation,
            Enabled: null,
            CurveSteps: steps);

        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response is null
            ? null
            : new DedicatedGpuCurveOptimizerResult(
                response.Outcome == DedicatedGpuSwitchOutcome.Success,
                response.Error);
    }

    private static async Task<DedicatedGpuSwitchResult?> SendWithWakeupRetryAsync(
        OpenSynapseServiceRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendForResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return response is null
            ? null
            : new DedicatedGpuSwitchResult(response.Outcome, response.ExitCode);
    }

    private static Task<OpenSynapseServiceResponse?> SendForResponseAsync(
        OpenSynapseServiceRequest request,
        CancellationToken cancellationToken,
        TimeSpan? responseTimeout = null) =>
        SendForResponseAsync(
            request,
            cancellationToken,
            TryEnsureServiceRunningAsync,
            (value, token) => ExchangeAsync(value, token, ConnectAsync, responseTimeout),
            Task.Delay);

    internal static async Task<OpenSynapseServiceResponse?> SendForResponseAsync(
        OpenSynapseServiceRequest request, CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>> ensureRunning,
        Func<OpenSynapseServiceRequest, CancellationToken, Task<OpenSynapseServiceResponse?>> send,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        // The service is demand-started and stops itself after an idle window, so a
        // wake-up races the pipe creation. One retry covers that gap and keeps the
        // common case off the UAC helper.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!await ensureRunning(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            // A dispatched write without a terminal response throws; never retry it or
            // fall back to UAC. A status read cannot prove the same service instance survived.
            var response = await send(request, cancellationToken).ConfigureAwait(false);
            if (response is not null &&
                response.Handled &&
                response.Version == OpenSynapseServiceProtocol.Version)
            {
                return response;
            }

            if (attempt == 0)
            {
                await delay(PipeConnectTimeout, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static Task<bool> TryEnsureServiceRunningAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            try
            {
                using var controller = new ServiceController(OpenSynapseServiceProtocol.ServiceName);
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

    private static bool IsServiceRunning()
    {
        try
        {
            using var controller = new ServiceController(
                OpenSynapseServiceProtocol.ServiceName);
            controller.Refresh();
            return controller.Status is ServiceControllerStatus.Running or
                ServiceControllerStatus.StartPending;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or ObjectDisposedException)
        {
            return false;
        }
    }

    private static async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(
            ".", OpenSynapseServiceProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)PipeConnectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    internal static async Task<OpenSynapseServiceResponse?> ExchangeAsync(
        OpenSynapseServiceRequest request, CancellationToken cancellationToken,
        Func<CancellationToken, Task<Stream>> connect, TimeSpan? responseTimeout = null)
    {
        var pnpWrite = request.Operation is OpenSynapseServiceProtocol.SetOperation or
            OpenSynapseServiceProtocol.RestartOperation or
            OpenSynapseServiceProtocol.ApplyUpdateOperation;
        var dispatched = false;
        try
        {
            using var pipe = await connect(cancellationToken).ConfigureAwait(false);

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

            var payload = JsonSerializer.Serialize(request);
            cancellationToken.ThrowIfCancellationRequested();
            dispatched = true;
            await writer.WriteLineAsync(payload).ConfigureAwait(false);
            await writer.FlushAsync(pnpWrite ? CancellationToken.None : cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                pnpWrite ? CancellationToken.None : cancellationToken);
            timeout.CancelAfter(responseTimeout ?? ResponseTimeout);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) ||
                line.Length > OpenSynapseServiceProtocol.MaximumMessageLength)
            {
                if (pnpWrite) throw new IOException("The OpenSynapse service did not return a complete response.");
                return null;
            }

            var response = JsonSerializer.Deserialize<OpenSynapseServiceResponse>(line);
            if (pnpWrite && (response is null ||
                response.Version != OpenSynapseServiceProtocol.Version || !Enum.IsDefined(response.Outcome)))
                throw new IOException("The OpenSynapse service returned an untrustworthy write response.");
            return response;
        }
        catch (Exception exception) when (pnpWrite && dispatched)
        {
            throw new DedicatedGpuOperationUncertainException(
                "The OpenSynapse service request may have executed; its terminal state is unknown.", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }
}
