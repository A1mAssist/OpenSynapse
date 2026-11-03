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

    private static Task RunAsync(CancellationToken cancellationToken)
    {
        var sink = new WindowsMacroInputSink();
        return RunWithCleanupAsync(sink, async pressed =>
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                        var response = await HandleAsync(pipe, sink, pressed, requestTimeout.Token).ConfigureAwait(false);
                        if (!response.Ok) await ReleasePressedAsync(sink, pressed).ConfigureAwait(false);
                        await WriteResponseAsync(pipe, response, requestTimeout.Token).ConfigureAwait(false);
                        pipe.Disconnect();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException or
                        UnauthorizedAccessException or JsonException or ObjectDisposedException or OperationCanceledException)
                    {
                        await ReleasePressedAsync(sink, pressed).ConfigureAwait(false);
                        pipe.Dispose();
                        pipe = CreatePipeWithRetry(cancellationToken);
                    }
                }
            }
            finally { pipe?.Dispose(); }
        });
    }

    internal static async Task RunWithCleanupAsync(IMacroInputSink sink, Func<List<MacroInput>, Task> run)
    {
        var pressed = new List<MacroInput>();
        try { await run(pressed).ConfigureAwait(false); }
        finally { await ReleasePressedAsync(sink, pressed).ConfigureAwait(false); }
    }

    internal static async Task<Exception?> ReleasePressedAsync(IMacroInputSink sink, List<MacroInput> pressed)
    {
        Exception? failure = null;
        foreach (var input in pressed.ToArray().Reverse())
        {
            try
            {
                await sink.SendAsync(new MacroStep { Kind = MacroStepKind.Input, Input = input, IsDown = false },
                    CancellationToken.None).ConfigureAwait(false);
                pressed.Remove(input);
            }
            catch (Exception exception) { failure ??= exception; }
        }
        return failure;
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
        IMacroInputSink sink,
        List<MacroInput> pressed,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            4096,
            leaveOpen: true);

        var line = await ReadRequestLineAsync(reader, cancellationToken).ConfigureAwait(false);
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

        return await ExecuteRequestAsync(request, sink, pressed, ProcessIntegrityLevel.Current()).ConfigureAwait(false);
    }

    internal static async Task<string?> ReadRequestLineAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character, cancellationToken).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n')
            {
                if (text.Length > 0 && text[^1] == '\r') text.Length--;
                if (text.Length > ElevatedMacroProtocol.MaximumMessageLength) throw new InvalidDataException("RequestTooLong");
                return text.ToString();
            }
            if (text.Length >= ElevatedMacroProtocol.MaximumMessageLength + 1) throw new InvalidDataException("RequestTooLong");
            text.Append(character[0]);
        }
        if (text.Length != 0) throw new InvalidDataException("IncompleteRequest");
        return null;
    }

    internal static async Task<ElevatedMacroResponse> ExecuteRequestAsync(
        ElevatedMacroRequest request, IMacroInputSink sink, List<MacroInput> pressed, int integrityLevel)
    {
        try
        {
            if (request.Version != ElevatedMacroProtocol.Version) throw new ArgumentException("UnsupportedVersion");
            return Execute(request, sink, pressed, integrityLevel);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or
            Win32Exception or OverflowException or IOException)
        {
            await ReleasePressedAsync(sink, pressed).ConfigureAwait(false);
            return new ElevatedMacroResponse { Ok = false, Error = exception.Message,
                ErrorCode = exception is Win32Exception win32 ? win32.NativeErrorCode : 0 };
        }
    }

    private static ElevatedMacroResponse Execute(
        ElevatedMacroRequest request,
        IMacroInputSink sink,
        List<MacroInput> pressed, int integrityLevel)
    {
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
                if (request.Input is not { } input || !input.IsValid(allowPrimaryMouseButton: true) ||
                    input.Kind is not (MacroInputKind.Keyboard or MacroInputKind.MouseButton or MacroInputKind.MouseWheel))
                {
                    throw new ArgumentException("InvalidInput");
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
                if (request.MouseMoveMode is not (MacroMouseMoveMode.Absolute or MacroMouseMoveMode.Relative or MacroMouseMoveMode.ForegroundRelative))
                    throw new ArgumentException("UnsupportedMouseMoveMode");
                sink.SendMouseMoveAsync(request.MouseMoveMode, request.X, request.Y)
                    .GetAwaiter().GetResult();
                return new ElevatedMacroResponse
                {
                    Ok = true,
                    IntegrityLevel = integrityLevel,
                };

            case ElevatedMacroCommandKind.Text:
                if (request.Text?.Length > ElevatedMacroProtocol.MaximumMessageLength) throw new ArgumentException("TextTooLong");
                sink.SendTextAsync(request.Text ?? string.Empty).GetAwaiter().GetResult();
                return new ElevatedMacroResponse
                {
                    Ok = true,
                    IntegrityLevel = integrityLevel,
                };

            case ElevatedMacroCommandKind.ReleaseAll:
                if (ReleasePressedAsync(sink, pressed).GetAwaiter().GetResult() is { } failure) throw failure;
                return new ElevatedMacroResponse
                {
                    Ok = true,
                    IntegrityLevel = integrityLevel,
                };

            default:
                throw new ArgumentException("UnsupportedCommand");
        }
    }

    private static void TrackPressed(
        MacroInput input,
        bool isDown,
        List<MacroInput> pressed)
    {
        if (input.Kind is not (MacroInputKind.Keyboard or MacroInputKind.MouseButton))
        {
            // These inputs cannot be replayed as keystrokes, so they never
            // leave a key stuck down.
            return;
        }

        pressed.RemoveAll(item => item.Matches(input));
        if (isDown)
        {
            pressed.Add(input.Clone());
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
