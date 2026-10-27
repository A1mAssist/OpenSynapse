using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// The output side of a macro playback.
///
/// The app implements this with the elevated agent when one is available and
/// falls back to a direct SendInput otherwise, so playback code never has to
/// know which path carries the events.
/// </summary>
public interface IMacroInputSink
{
    /// <summary>
    /// Injects a key or mouse button event.
    /// </summary>
    Task SendAsync(MacroStep step, CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects a text string.
    /// </summary>
    Task SendTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the mouse.
    /// </summary>
    Task SendMouseMoveAsync(
        MacroMouseMoveMode mode,
        int x,
        int y,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases every key this sink still holds down.
    ///
    /// A sink that owns keys across a playback has to release them when the
    /// playback ends, otherwise an interrupted macro leaves a key stuck.
    /// </summary>
    Task ReleaseAllAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
