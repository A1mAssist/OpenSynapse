using System.ComponentModel;
using System.ServiceProcess;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Starts a kernel driver service. A non-elevated process cannot start one, so
/// the app calls this through the machine-wide OpenSynapseService.
/// </summary>
internal static class WindowsServiceControl
{
    internal static bool TryStart(string serviceName, out string? error)
    {
        error = null;
        try
        {
            using var controller = new ServiceController(serviceName);
            controller.Refresh();
            if (controller.Status == ServiceControllerStatus.Running)
            {
                return true;
            }

            if (controller.Status == ServiceControllerStatus.StartPending)
            {
                controller.WaitForStatus(
                    ServiceControllerStatus.Running,
                    TimeSpan.FromSeconds(5));
                return controller.Status == ServiceControllerStatus.Running;
            }

            if (controller.Status == ServiceControllerStatus.StopPending)
            {
                controller.WaitForStatus(
                    ServiceControllerStatus.Stopped,
                    TimeSpan.FromSeconds(5));
                controller.Refresh();
            }

            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                controller.Start();
            }

            controller.WaitForStatus(
                ServiceControllerStatus.Running,
                TimeSpan.FromSeconds(5));
            return controller.Status == ServiceControllerStatus.Running;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException or
                System.ServiceProcess.TimeoutException)
        {
            error = exception.Message;
            return false;
        }
    }
}
