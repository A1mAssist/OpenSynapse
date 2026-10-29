using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Classification and text for exceptions the device paths expect to hit: a missing HID
/// device, a driver refusing a report, or a closed handle. Shared by the shell and the
/// extracted workspaces so both report the same way.
/// </summary>
internal static class RuntimeErrors
{
    public static bool IsExpected(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException;

    public static string Format(Exception exception)
    {
        var exceptions = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [exception];
        return string.Join(
            "; ",
            exceptions
                .Select(error => error.Message)
                .Where(message => !string.IsNullOrWhiteSpace(message)));
    }
}
