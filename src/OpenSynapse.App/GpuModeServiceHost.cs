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

namespace OpenSynapse.App;

internal static class GpuModeServiceHost
{
    private const int IdleStopSeconds = 5;
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 ||
            !StringComparer.Ordinal.Equals(
                args[0],
                DedicatedGpuModeServiceProtocol.ServiceArgument))
        {
            return false;
        }

        SecurityIdentifier? authorizedUser = null;
        if (args.Length == 3 &&
            StringComparer.Ordinal.Equals(
                args[1],
                DedicatedGpuModeServiceProtocol.ServiceUserArgument))
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
        ServiceBase.Run(new DedicatedGpuModeWindowsService(authorizedUser));
        return true;
    }

    private sealed class DedicatedGpuModeWindowsService : ServiceBase
    {
        private readonly SecurityIdentifier? _authorizedUser;
        private readonly CancellationTokenSource _stop = new();
        private Task? _worker;

        public DedicatedGpuModeWindowsService(SecurityIdentifier? authorizedUser)
        {
            _authorizedUser = authorizedUser;
            ServiceName = DedicatedGpuModeServiceProtocol.ServiceName;
            CanStop = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            if (_authorizedUser is null)
            {
                throw new InvalidOperationException(
                    "The GPU service is missing its authorized user SID.");
            }

            _worker = Task.Run(() => RunAsync(_stop.Token));
        }

        protected override void OnStop()
        {
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

            DedicatedGpuModeServiceRequest? request = null;
            if (!string.IsNullOrWhiteSpace(line) &&
                line.Length <= DedicatedGpuModeServiceProtocol.MaximumMessageLength)
            {
                try
                {
                    request = JsonSerializer.Deserialize<DedicatedGpuModeServiceRequest>(line);
                }
                catch (JsonException)
                {
                }
            }

            var response = Execute(request, authorizedUser);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response)).ConfigureAwait(false);
        }

        private static DedicatedGpuModeServiceResponse Execute(
            DedicatedGpuModeServiceRequest? request,
            SecurityIdentifier? authorizedUser)
        {
            if (request is null ||
                request.Version != DedicatedGpuModeServiceProtocol.Version)
            {
                return Invalid("Unsupported GPU service request.");
            }

            switch (DedicatedGpuModeServiceRequestParser.Parse(request))
            {
                case DedicatedGpuModeServiceAction.Status:
                    return new DedicatedGpuModeServiceResponse(
                        DedicatedGpuModeServiceProtocol.Version,
                        Handled: true,
                        DedicatedGpuSwitchOutcome.Success,
                        ExitCode: 0,
                        Status: WindowsDedicatedGpuMode.GetStatus(),
                        Error: null);

                case DedicatedGpuModeServiceAction.Restart:
                    var restart = WindowsDedicatedGpuMode.RestartFaultedDirect();
                    return new DedicatedGpuModeServiceResponse(
                        DedicatedGpuModeServiceProtocol.Version,
                        Handled: true,
                        restart.Outcome,
                        ExitCode: restart.ExitCode,
                        Status: WindowsDedicatedGpuMode.GetStatus(),
                        Error: null);

                case DedicatedGpuModeServiceAction.Set:
                    var result = WindowsDedicatedGpuMode.SetEnabledDirect(
                        request.Enabled!.Value,
                        validateDisplayPolicy: false);
                    return new DedicatedGpuModeServiceResponse(
                        DedicatedGpuModeServiceProtocol.Version,
                        Handled: true,
                        result.Outcome,
                        ExitCode: result.ExitCode,
                        Status: WindowsDedicatedGpuMode.GetStatus(),
                        Error: null);

                case DedicatedGpuModeServiceAction.EnsureMacroAgent:
                    return EnsureMacroAgent(authorizedUser);

                case DedicatedGpuModeServiceAction.StartMacroAgent:
                    return StartMacroAgent();

                case DedicatedGpuModeServiceAction.GpuClockOffsetStatus:
                    var clockAvailable = NvApiGpuClockOffset.TryProbe(out var clockProbeError);
                    var clockCore = 0;
                    var clockMemory = 0;
                    var clockRead = clockAvailable &&
                        NvApiGpuClockOffset.TryRead(out clockCore, out clockMemory, out _);
                    return new DedicatedGpuModeServiceResponse(
                        DedicatedGpuModeServiceProtocol.Version,
                        Handled: true,
                        DedicatedGpuSwitchOutcome.Success,
                        ExitCode: 0,
                        Status: null,
                        Error: clockAvailable ? null : clockProbeError,
                        ClockOffsetAvailable: clockAvailable,
                        CoreOffsetMhz: clockRead ? clockCore : null,
                        MemoryOffsetMhz: clockRead ? clockMemory : null);

                case DedicatedGpuModeServiceAction.SetGpuClockOffset:
                    var clockApplied = NvApiGpuClockOffset.TryApply(
                        request.CoreOffsetMhz!.Value,
                        request.MemoryOffsetMhz!.Value,
                        out var clockApplyError);
                    return new DedicatedGpuModeServiceResponse(
                        DedicatedGpuModeServiceProtocol.Version,
                        Handled: true,
                        clockApplied ? DedicatedGpuSwitchOutcome.Success : DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: clockApplied ? 0 : 1,
                        Status: null,
                        Error: clockApplyError,
                        ClockOffsetAvailable: clockApplied);

                case DedicatedGpuModeServiceAction.GpuClockLimitStatus:
                    var limitAvailable = NvApiGpuClockLimit.TryProbe(out var limitProbeError);
                    var limitMhz = 0;
                    string? limitReadError = null;
                    var limitRead = limitAvailable &&
                        NvApiGpuClockLimit.TryRead(out limitMhz, out limitReadError);
                    return new DedicatedGpuModeServiceResponse(
                        DedicatedGpuModeServiceProtocol.Version,
                        Handled: true,
                        DedicatedGpuSwitchOutcome.Success,
                        ExitCode: 0,
                        Status: null,
                        Error: limitAvailable
                            ? (limitRead ? null : limitReadError)
                            : limitProbeError,
                        ClockLimitAvailable: limitAvailable && limitRead,
                        ClockLimitMhz: limitRead ? limitMhz : null);

                case DedicatedGpuModeServiceAction.SetGpuClockLimit:
                    var limitApplied = NvApiGpuClockLimit.TryApply(
                        request.ClockLimitMhz!.Value,
                        out var limitApplyError);
                    return new DedicatedGpuModeServiceResponse(
                        DedicatedGpuModeServiceProtocol.Version,
                        Handled: true,
                        limitApplied ? DedicatedGpuSwitchOutcome.Success : DedicatedGpuSwitchOutcome.Failed,
                        ExitCode: limitApplied ? 0 : 1,
                        Status: null,
                        Error: limitApplyError,
                        ClockLimitAvailable: limitApplied);

                case DedicatedGpuModeServiceAction.CpuPowerLimitStatus:
                    return WithPowerLimit(controller => controller.Read());

                case DedicatedGpuModeServiceAction.SetCpuPowerLimit:
                    var powerLimit = new CpuPowerLimit(
                        request.Pl1Watts!.Value,
                        request.Pl2Watts!.Value,
                        0,
                        0);
                    return WithPowerLimit(controller => controller.Apply(powerLimit));

                case DedicatedGpuModeServiceAction.RestoreCpuPowerLimit:
                    return WithPowerLimit(controller =>
                        request.Pl1Watts is { } pl1 && request.Pl2Watts is { } pl2
                            ? controller.RestoreTo(pl1, pl2)
                            : controller.RestoreFirmware());

                default:
                    return Invalid("Invalid GPU service command.");
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
        private static DedicatedGpuModeServiceResponse EnsureMacroAgent(
            SecurityIdentifier? authorizedUser)
        {
            if (authorizedUser is null)
            {
                return Invalid("The GPU service is missing its authorized user SID.");
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return Invalid("The GPU service cannot resolve its own path.");
            }

            if (!ElevatedMacroTaskInstaller.EnsureInstalled(
                    executablePath,
                    authorizedUser.Value))
            {
                return Invalid("The macro agent task could not be created.");
            }

            return StartMacroAgent();
        }

        private static DedicatedGpuModeServiceResponse StartMacroAgent()
        {
            if (!ElevatedMacroTaskInstaller.TryStart())
            {
                return Invalid("The macro agent task could not be started.");
            }

            return new DedicatedGpuModeServiceResponse(
                DedicatedGpuModeServiceProtocol.Version,
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
        private static DedicatedGpuModeServiceResponse WithPowerLimit(
            Func<CpuPowerLimitController, CpuPowerLimitReport> action)
        {
            CpuPowerLimitController? controller = null;
            CpuPowerLimitReport report;
            try
            {
                var availability = CpuPowerLimitController.TryOpen(out controller, out var detail);
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

            return new DedicatedGpuModeServiceResponse(
                DedicatedGpuModeServiceProtocol.Version,
                Handled: true,
                report.Availability == CpuPowerLimitAvailability.Available
                    ? DedicatedGpuSwitchOutcome.Success
                    : DedicatedGpuSwitchOutcome.Failed,
                ExitCode: report.Availability == CpuPowerLimitAvailability.Available ? 0 : 1,
                Status: null,
                Error: report.Detail,
                PowerLimit: report);
        }

        private static DedicatedGpuModeServiceResponse Invalid(string error) =>
            new(
                DedicatedGpuModeServiceProtocol.Version,
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
                DedicatedGpuModeServiceProtocol.PipeName,
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
