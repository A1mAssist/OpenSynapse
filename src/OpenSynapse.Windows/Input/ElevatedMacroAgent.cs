using System.IO.Pipes;
using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// The elevated macro agent.
///
/// It runs in the user session at high integrity, so its SendInput calls
/// reach windows that a medium-integrity app cannot write to. The agent only
/// injects individual input events; macros, playback and recording stay in the
/// app, and the agent holds no state beyond the keys it has pressed down.
/// </summary>
public static class ElevatedMacroAgent
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Runs the agent until the process is stopped. Returns false when the
    /// arguments are not the agent command.
    /// </summary>
    public static bool TryRun(IReadOnlyList<string> args)
    {
        if (args.Count == 0 ||
            !StringComparer.Ordinal.Equals(args[0], ElevatedMacroProtocol.AgentArgument))
        {
            return false;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        RunAsync(cancellation.Token).GetAwaiter().GetResult();
        return true;
    }

    private static async Task RunAsync(CancellationToken cancellationToken)
    {
        var sink = new WindowsMacroInputSink();
        var pressed = new List<MacroInput>();

        // The pipe instance is reused across requests. Recreating it per
        // request would leave a window where the app's connect attempt lands
        // on a pipe nobody is serving.
        var pipe = CreatePipe();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var response = await HandleAsync(pipe, sink, pressed, cancellationToken)
                    .ConfigureAwait(false);
                await WriteResponseAsync(pipe, response, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or
                    UnauthorizedAccessException or JsonException or ObjectDisposedException)
            {
                // A malformed or disconnected client must not stop the agent.
                // A broken pipe cannot serve another client, so replace it.
                // The replacement can still fail while the old instance is
                // being torn down, so it retries until it succeeds.
                var broken = pipe;
                try
                {
                    broken.Dispose();
                }
                catch (IOException)
                {
                }

                pipe = CreatePipeWithRetry(cancellationToken);
            }
        }

        pipe.Dispose();

        // Never leave a key pressed on the desktop when the agent stops.
        foreach (var input in pressed)
        {
            try
            {
                sink.SendAsync(new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = input,
                    IsDown = false,
                }).GetAwaiter().GetResult();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or Win32Exception)
            {
            }
        }
    }

    /// <summary>
    /// Creates the pipe, waiting out the brief window where a previous
    /// instance still holds the name.
    /// </summary>
    private static NamedPipeServerStream CreatePipeWithRetry(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return CreatePipe();
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static async Task<ElevatedMacroResponse> HandleAsync(
        NamedPipeServerStream pipe,
        WindowsMacroInputSink sink,
        List<MacroInput> pressed,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            4096,
            leaveOpen: true);

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line) ||
            line.Length > ElevatedMacroProtocol.MaximumMessageLength)
        {
            return new ElevatedMacroResponse { Ok = false, Error = "InvalidRequest" };
        }

        ElevatedMacroRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ElevatedMacroRequest>(line, SerializerOptions);
        }
        catch (JsonException)
        {
            return new ElevatedMacroResponse { Ok = false, Error = "InvalidRequest" };
        }

        if (request is null || request.Version != ElevatedMacroProtocol.Version)
        {
            return new ElevatedMacroResponse { Ok = false, Error = "UnsupportedVersion" };
        }

        try
        {
            return Execute(request, sink, pressed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return new ElevatedMacroResponse
            {
                Ok = false,
                Error = exception.Message,
            };
        }
    }

    private static ElevatedMacroResponse Execute(
        ElevatedMacroRequest request,
        WindowsMacroInputSink sink,
        List<MacroInput> pressed)
    {
        var integrityLevel = ProcessIntegrityLevel.Current();
        switch (request.Kind)
        {
            case ElevatedMacroCommandKind.Ping:
                return new ElevatedMacroResponse
                {
                    Ok = true,
                    IntegrityLevel = integrityLevel,
                };

            case ElevatedMacroCommandKind.Input:
            {
                if (request.Input is not { } input)
                {
                    return new ElevatedMacroResponse { Ok = false, Error = "MissingInput" };
                }

                sink.SendAsync(new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = input,
                    IsDown = request.IsDown,
                }).GetAwaiter().GetResult();
                TrackPressed(input, request.IsDown, pressed);
                return new ElevatedMacroResponse
                {
                    Ok = true,
                    IntegrityLevel = integrityLevel,
                };
            }

            case ElevatedMacroCommandKind.MouseMove:
                sink.SendMouseMoveAsync(request.MouseMoveMode, request.X, request.Y)
                    .GetAwaiter().GetResult();
                return new ElevatedMacroResponse
                {
                    Ok = true,
                    IntegrityLevel = integrityLevel,
                };

            case ElevatedMacroCommandKind.Text:
                sink.SendTextAsync(request.Text ?? string.Empty).GetAwaiter().GetResult();
                return new ElevatedMacroResponse
                {
                    Ok = true,
                    IntegrityLevel = integrityLevel,
                };

            case ElevatedMacroCommandKind.ReleaseAll:
                foreach (var input in pressed.ToArray())
                {
                    sink.SendAsync(new MacroStep
                    {
                        Kind = MacroStepKind.Input,
                        Input = input,
                        IsDown = false,
                    }).GetAwaiter().GetResult();
                }

                pressed.Clear();
                return new ElevatedMacroResponse
                {
                    Ok = true,
                    IntegrityLevel = integrityLevel,
                };

            default:
                return new ElevatedMacroResponse { Ok = false, Error = "UnsupportedCommand" };
        }
    }

    private static void TrackPressed(
        MacroInput input,
        bool isDown,
        List<MacroInput> pressed)
    {
        if (input.Kind is MacroInputKind.NagaSideButton or MacroInputKind.DeviceButton or
            MacroInputKind.RazerKey)
        {
            // These inputs cannot be replayed as keystrokes, so they never
            // leave a key stuck down.
            return;
        }

        pressed.RemoveAll(item => item.Matches(input));
        if (isDown)
        {
            pressed.Add(input);
        }
    }

    private static async Task WriteResponseAsync(
        NamedPipeServerStream pipe,
        ElevatedMacroResponse response,
        CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(false),
            4096,
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        await writer.WriteLineAsync(
            JsonSerializer.Serialize(response, SerializerOptions).AsMemory(),
            cancellationToken).ConfigureAwait(false);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // The agent belongs to one logged-in user, so only that account may
        // drive it. The owner is resolved at run time because the task runs as
        // the interactive user of the session.
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } user)
        {
            security.AddAccessRule(new PipeAccessRule(
                user,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            ElevatedMacroProtocol.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            security);
    }
}
