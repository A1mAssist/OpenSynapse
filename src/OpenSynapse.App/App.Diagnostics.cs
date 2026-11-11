using OpenSynapse.Core.Diagnostics;

namespace OpenSynapse.App;

public partial class App
{
    private void ConfigureDiagnostics()
    {
        UnhandledException += (_, args) =>
        {
            _diagnosticLog.TryWrite(DiagnosticLogLevel.Error, "unhandled", args.Exception.ToString());
            CrashReportWriter.TryWrite("xaml-unhandled", args.Exception, _diagnosticLog);
            EmergencyStopBladeLighting();
            EmergencyStopBladeMapping();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                _diagnosticLog.TryWrite(DiagnosticLogLevel.Error, "unhandled", exception.ToString());
                CrashReportWriter.TryWrite("appdomain-unhandled", exception, _diagnosticLog);
            }

            EmergencyStopBladeLighting();
            EmergencyStopBladeMapping();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _diagnosticLog.TryWrite(
                DiagnosticLogLevel.Error,
                "unobserved-task",
                args.Exception.ToString());
            CrashReportWriter.TryWrite("unobserved-task", args.Exception, _diagnosticLog);
            args.SetObserved();
        };
    }
}
