using System.ComponentModel;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App;

internal static partial class OpenSynapseServiceHost
{
    private const int IdleStopSeconds = 5;
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 ||
            !StringComparer.Ordinal.Equals(
                args[0],
                OpenSynapseServiceProtocol.ServiceArgument))
        {
            return false;
        }

        SecurityIdentifier? authorizedUser = null;
        if (args.Length == 3 &&
            StringComparer.Ordinal.Equals(
                args[1],
                OpenSynapseServiceProtocol.ServiceUserArgument))
        {
            try
            {
                authorizedUser = new SecurityIdentifier(args[2]);
            }
            catch (ArgumentException)
            {
            }
        }

        Environment.CurrentDirectory = AppContext.BaseDirectory;
        ServiceBase.Run(new OpenSynapseWindowsService(authorizedUser));
        return true;
    }

    private sealed class OpenSynapseWindowsService : ServiceBase
    {
        private readonly SecurityIdentifier? _authorizedUser;
        private readonly CancellationTokenSource _stop = new();
        private Task? _worker;

        public OpenSynapseWindowsService(SecurityIdentifier? authorizedUser)
        {
            _authorizedUser = authorizedUser;
            ServiceName = OpenSynapseServiceProtocol.ServiceName;
            CanStop = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            if (_authorizedUser is null)
            {
                throw new InvalidOperationException(
                    "The OpenSynapse service is missing its authorized user SID.");
            }

            _worker = Task.Run(() => RunAsync(_stop.Token));
        }

        protected override void OnStop()
        {
            ChromaBrokerServiceChild.Stop();
            _stop.Cancel();
            try
            {
                _worker?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            base.OnStop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stop.Dispose();
            }

            base.Dispose(disposing);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var shouldStop = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var pipe = CreatePipe();
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    idle.CancelAfter(TimeSpan.FromSeconds(IdleStopSeconds));
                    await pipe.WaitForConnectionAsync(idle.Token).ConfigureAwait(false);
                    await HandleRequestAsync(pipe, _authorizedUser, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (ChromaBrokerServiceChild.IsRunning)
                    {
                        continue;
                    }

                    shouldStop = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidOperationException or
                        UnauthorizedAccessException or JsonException)
                {
                    // A single malformed or disconnected client must not kill the service.
                }
            }

            if (shouldStop)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        Stop();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                });
            }
        }

        private static async Task HandleRequestAsync(
            NamedPipeServerStream pipe,
            SecurityIdentifier? authorizedUser,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: false,
                4096,
                leaveOpen: true);
            using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(false),
                4096,
                leaveOpen: true)
            {
                AutoFlush = true,
            };

            string? line;
            try
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                readTimeout.CancelAfter(RequestReadTimeout);
                line = await reader.ReadLineAsync(readTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return;
            }

            OpenSynapseServiceRequest? request = null;
            if (!string.IsNullOrWhiteSpace(line) &&
                line.Length <= OpenSynapseServiceProtocol.MaximumMessageLength)
            {
                try
                {
                    request = JsonSerializer.Deserialize<OpenSynapseServiceRequest>(line);
                }
                catch (JsonException)
                {
                }
            }

            var response = await ExecuteAsync(request, authorizedUser).ConfigureAwait(false);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response)).ConfigureAwait(false);
        }

        private static async Task<OpenSynapseServiceResponse> ExecuteAsync(
            OpenSynapseServiceRequest? request,
            SecurityIdentifier? authorizedUser)
        {
            if (request is null ||
                request.Version != OpenSynapseServiceProtocol.Version)
            {
                return Invalid("Unsupported OpenSynapse service request.");
            }

            switch (OpenSynapseServiceRequestParser.Parse(request))
            {
                case OpenSynapseServiceAction.Status:
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        DedicatedGpuSwitchOutcome.Success,
                        ExitCode: 0,
                        Status: WindowsDedicatedGpuMode.GetStatus(),
                        Error: null);

                case OpenSynapseServiceAction.Restart:
                    var restart = WindowsDedicatedGpuMode.RestartFaultedDirect();
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        restart.Outcome,
                        ExitCode: restart.ExitCode,
                        Status: WindowsDedicatedGpuMode.GetStatus(),
                        Error: null);

                case OpenSynapseServiceAction.Set:
                    var result = WindowsDedicatedGpuMode.SetEnabledDirect(
                        request.Enabled!.Value,
                        validateDisplayPolicy: false);
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        result.Outcome,
                        ExitCode: result.ExitCode,
                        Status: WindowsDedicatedGpuMode.GetStatus(),
                        Error: null);

                case OpenSynapseServiceAction.EnsureMacroAgent:
                    return EnsureMacroAgent(authorizedUser);

                case OpenSynapseServiceAction.StartMacroAgent:
                    return StartMacroAgent();

                case OpenSynapseServiceAction.StartChromaBroker:
                    return StartChromaBroker(authorizedUser, request.ParentProcessId);

                case OpenSynapseServiceAction.StopChromaBroker:
                    return StopChromaBroker();

                case OpenSynapseServiceAction.InstallChromaDll:
                    var dllOutcome = await ChromaOfficialDllInstaller
                        .InstallAsServiceAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    var dllInstalled = dllOutcome == ChromaOfficialDllInstallOutcome.Installed;
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        dllInstalled
                            ? DedicatedGpuSwitchOutcome.Success
                            : DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: dllInstalled ? 0 : 1,
                        Status: null,
                        Error: dllInstalled ? null : dllOutcome.ToString());

                case OpenSynapseServiceAction.InstallRazerCommon:
                    var commonResult = await RazerCommonDriverInstaller
                        .InstallAsServiceAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        commonResult.Success
                            ? DedicatedGpuSwitchOutcome.Success
                            : DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: commonResult.Success ? 0 : 1,
                        Status: null,
                        Error: commonResult.Success ? null : commonResult.Failure.ToString());

                case OpenSynapseServiceAction.BindFilter:
                    var bindResult = await RazerFilterInfBinder
                        .ApplyDirectAsync((ushort)request.ProductId!.Value, CancellationToken.None)
                        .ConfigureAwait(false);
                    var bindAccepted = bindResult.Error is null;
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        bindAccepted
                            ? DedicatedGpuSwitchOutcome.Success
                            : DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: bindResult.Applied ? 0 : 2,
                        Status: null,
                        Error: bindResult.Error);

                case OpenSynapseServiceAction.StartDriverService:
                    var driverStarted = WindowsServiceControl.TryStart(
                        request.ServiceName!,
                        out var driverError);
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        driverStarted
                            ? DedicatedGpuSwitchOutcome.Success
                            : DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: driverStarted ? 0 : 1,
                        Status: null,
                        Error: driverError);

                case OpenSynapseServiceAction.ApplyUpdate:
                    return await ApplyUpdateAsync(request, authorizedUser, CancellationToken.None)
                        .ConfigureAwait(false);

                case OpenSynapseServiceAction.GpuClockOffsetStatus:
                    var clockRead = NvApiGpuClockOffset.TryRead(
                        out var clockCore, out var clockMemory, out var clockReadError);
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        DedicatedGpuSwitchOutcome.Success,
                        ExitCode: 0,
                        Status: null,
                        Error: clockReadError,
                        ClockOffsetAvailable: clockRead,
                        CoreOffsetMhz: clockRead ? clockCore : null,
                        MemoryOffsetMhz: clockRead ? clockMemory : null);

                case OpenSynapseServiceAction.SetGpuClockOffset:
                    var clockApplied = NvApiGpuClockOffset.TryApply(
                        request.CoreOffsetMhz!.Value,
                        request.MemoryOffsetMhz!.Value,
                        out var clockApplyError);
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        clockApplied ? DedicatedGpuSwitchOutcome.Success : DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: clockApplied ? 0 : 1,
                        Status: null,
                        Error: clockApplyError,
                        ClockOffsetAvailable: clockApplied);

                case OpenSynapseServiceAction.GpuClockLimitStatus:
                    var limitAvailable = NvApiGpuClockLimit.TryProbe(out var limitProbeError);
                    var limitMhz = 0;
                    string? limitReadError = null;
                    var limitRead = limitAvailable &&
                        NvApiGpuClockLimit.TryRead(out limitMhz, out limitReadError);
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        DedicatedGpuSwitchOutcome.Success,
                        ExitCode: 0,
                        Status: null,
                        Error: limitAvailable
                            ? (limitRead ? null : limitReadError)
                            : limitProbeError,
                        ClockLimitAvailable: limitAvailable && limitRead,
                        ClockLimitMhz: limitRead ? limitMhz : null);

                case OpenSynapseServiceAction.SetGpuClockLimit:
                    var limitApplied = NvApiGpuClockLimit.TryApply(
                        request.ClockLimitMhz!.Value,
                        out var limitApplyError);
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        limitApplied ? DedicatedGpuSwitchOutcome.Success : DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: limitApplied ? 0 : 1,
                        Status: null,
                        Error: limitApplyError,
                        ClockLimitAvailable: limitApplied);

                case OpenSynapseServiceAction.CpuPowerLimitStatus:
                    return WithPowerLimit(controller => controller.Read());

                case OpenSynapseServiceAction.SetCpuPowerLimit:
                    var powerLimit = new CpuPowerLimit(
                        request.Pl1Watts!.Value,
                        request.Pl2Watts!.Value,
                        0,
                        0);
                    return WithPowerLimit(controller => controller.Apply(powerLimit));

                case OpenSynapseServiceAction.RestoreCpuPowerLimit:
                    return WithPowerLimit(controller =>
                        request.Pl1Watts is { } pl1 && request.Pl2Watts is { } pl2
                            ? controller.RestoreTo(pl1, pl2)
                            : controller.RestoreFirmware());

                case OpenSynapseServiceAction.CurveOptimizerStatus:
                    return WithCurveOptimizer(_ => (true, null));

                case OpenSynapseServiceAction.SetCurveOptimizer:
                    var curveSteps = request.CurveSteps!.Value;
                    return WithCurveOptimizer(controller =>
                    {
                        var applied = controller.TryApply(new CpuCurveOptimizer(curveSteps), out var curveError);
                        return (applied, applied ? null : curveError);
                    });

                default:
                    return Invalid("Invalid OpenSynapse service command.");
            }
        }

        /// <summary>
        /// Creates the macro agent's scheduled task for the authorized user.
        ///
        /// A highest-run-level task can only be created by an administrator,
        /// which is what this service is. The task runs with the user's
        /// interactive token, so the agent reaches the user's desktop and no
        /// further UAC prompt is needed.
        /// </summary>
        private static OpenSynapseServiceResponse EnsureMacroAgent(
            SecurityIdentifier? authorizedUser)
        {
            if (authorizedUser is null)
            {
                return Invalid("The OpenSynapse service is missing its authorized user SID.");
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return Invalid("The OpenSynapse service cannot resolve its own path.");
            }

            if (!ElevatedMacroTaskInstaller.EnsureInstalled(
                    executablePath,
                    authorizedUser.Value))
            {
                return Invalid("The macro agent task could not be created.");
            }

            return StartMacroAgent();
        }

        private static OpenSynapseServiceResponse StartMacroAgent()
        {
            if (!ElevatedMacroTaskInstaller.TryStart())
            {
                return Invalid("The macro agent task could not be started.");
            }

            return new OpenSynapseServiceResponse(
                OpenSynapseServiceProtocol.Version,
                Handled: true,
                DedicatedGpuSwitchOutcome.Success,
                ExitCode: 0,
                Status: null,
                Error: null);
        }

        private static OpenSynapseServiceResponse StartChromaBroker(
            SecurityIdentifier? authorizedUser,
            int? parentProcessId)
        {
            if (authorizedUser is null)
            {
                return Invalid("The OpenSynapse service is missing its authorized user SID.");
            }

            if (parentProcessId is not > 0)
            {
                return Invalid("The Chroma broker request is missing its parent process.");
            }

            var error = ChromaBrokerServiceChild.Start(parentProcessId.Value, authorizedUser);
            return error is null
                ? new OpenSynapseServiceResponse(
                    OpenSynapseServiceProtocol.Version,
                    Handled: true,
                    DedicatedGpuSwitchOutcome.Success,
                    ExitCode: 0,
                    Status: null,
                    Error: null)
                : Invalid(error);
        }

        private static OpenSynapseServiceResponse StopChromaBroker()
        {
            ChromaBrokerServiceChild.Stop();
            return new OpenSynapseServiceResponse(
                OpenSynapseServiceProtocol.Version,
                Handled: true,
                DedicatedGpuSwitchOutcome.Success,
                ExitCode: 0,
                Status: null,
                Error: null);
        }

        /// <summary>
        /// Runs one CPU power operation with one controller. Failures become reports so a
        /// firmware lock or a broken PawnIO install cannot take the whole service down.
        /// </summary>
        private static OpenSynapseServiceResponse WithPowerLimit(
            Func<ICpuPowerLimitController, CpuPowerLimitReport> action)
        {
            ICpuPowerLimitController? controller = null;
            CpuPowerLimitReport report;
            try
            {
                var availability = CpuPowerLimitLocal.TryOpen(out controller, out var detail);
                report = controller is null
                    ? CpuPowerLimitReport.Unavailable(availability, detail)
                    : action(controller);
            }
            catch (Exception exception)
            {
                report = CpuPowerLimitReport.Unavailable(
                    CpuPowerLimitAvailability.Failed,
                    exception.Message);
            }
            finally
            {
                controller?.Dispose();
            }

            return new OpenSynapseServiceResponse(
                OpenSynapseServiceProtocol.Version,
                Handled: true,
                report.Availability == CpuPowerLimitAvailability.Available
                    ? DedicatedGpuSwitchOutcome.Success
                    : DedicatedGpuSwitchOutcome.Failed,
                ExitCode: report.Availability == CpuPowerLimitAvailability.Available ? 0 : 1,
                Status: null,
                Error: report.Detail,
                PowerLimit: report);
        }

        /// <summary>
        /// Runs one Curve Optimizer operation. The SMU cannot read the offset back, so a
        /// write is reported as accepted rather than verified.
        /// </summary>
        private static OpenSynapseServiceResponse WithCurveOptimizer(
            Func<ICurveOptimizerController, (bool Applied, string? Error)> action)
        {
            ICurveOptimizerController? controller = null;
            bool applied;
            string? error;
            try
            {
                RyzenSmuCurveOptimizer.TryOpen(out controller, out var detail);
                if (controller is null)
                {
                    return new OpenSynapseServiceResponse(
                        OpenSynapseServiceProtocol.Version,
                        Handled: true,
                        DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: 1,
                        Status: null,
                        Error: detail,
                        CurveOptimizerAvailable: false);
                }

                (applied, error) = action(controller);
            }
            catch (Exception exception)
            {
                applied = false;
                error = exception.Message;
            }
            finally
            {
                controller?.Dispose();
            }

            return new OpenSynapseServiceResponse(
                OpenSynapseServiceProtocol.Version,
                Handled: true,
                applied ? DedicatedGpuSwitchOutcome.Success : DedicatedGpuSwitchOutcome.Failed,
                ExitCode: applied ? 0 : 1,
                Status: null,
                Error: error,
                CurveOptimizerAvailable: applied);
        }

        private static OpenSynapseServiceResponse Invalid(string error) =>
            new(
                OpenSynapseServiceProtocol.Version,
                Handled: false,
                DedicatedGpuSwitchOutcome.Failed,
                ExitCode: 0,
                Status: null,
                Error: error);

        private NamedPipeServerStream CreatePipe()
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                _authorizedUser!,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Deny));

            return NamedPipeServerStreamAcl.Create(
                OpenSynapseServiceProtocol.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: security);
        }
    }
}
