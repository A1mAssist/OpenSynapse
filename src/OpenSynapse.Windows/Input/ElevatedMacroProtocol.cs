using System.Text.Json.Serialization;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// The wire format shared by the app and the elevated macro agent.
///
/// The agent runs in the user session at high integrity so its SendInput calls
/// reach windows a medium-integrity process cannot write to. It is a pure
/// input injector: macro definitions, playback state and recording all stay in
/// the app, so the pipe only ever carries individual input events.
/// </summary>
public static class ElevatedMacroProtocol
{
    public const string AgentArgument = "--macro-agent";
    public const string InstallTaskArgument = "--install-macro-agent-task";
    public const string RemoveTaskArgument = "--remove-macro-agent-task";
    public const string TaskName = "OpenSynapseMacroAgent";
    public const string PipeName = "OpenSynapse.MacroAgent.v1";
    public const int Version = 1;
    public const int MaximumMessageLength = 8192;
}

public enum ElevatedMacroCommandKind
{
    Ping = 0,
    Input = 1,
    MouseMove = 2,
    Text = 3,
    ReleaseAll = 4,
}

/// <summary>
/// One injection request. The payload reuses the core macro model so the
/// agent never has to understand macros: it only forwards input events.
/// </summary>
public sealed record ElevatedMacroRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ElevatedMacroCommandKind Kind { get; init; } = ElevatedMacroCommandKind.Ping;

    public int Version { get; init; } = ElevatedMacroProtocol.Version;

    /// <summary>The key or mouse event to inject.</summary>
    public MacroInput? Input { get; init; }

    /// <summary>True for a press, false for a release.</summary>
    public bool IsDown { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MacroMouseMoveMode MouseMoveMode { get; init; }

    /// <summary>Target X for a mouse move.</summary>
    public int X { get; init; }

    /// <summary>Target Y for a mouse move.</summary>
    public int Y { get; init; }

    /// <summary>Text payload for the text command.</summary>
    public string? Text { get; init; }
}

public sealed record ElevatedMacroResponse
{
    public int Version { get; init; } = ElevatedMacroProtocol.Version;

    public bool Ok { get; init; }

    /// <summary>
    /// The integrity level the agent runs at: 0x2000 medium, 0x3000 high.
    /// The app checks this so it never routes macro output to an agent that
    /// would fail the same way its own SendInput call would.
    /// </summary>
    public int IntegrityLevel { get; init; }

    /// <summary>Win32 error code when the injection failed.</summary>
    public int ErrorCode { get; init; }

    public string? Error { get; init; }
}
