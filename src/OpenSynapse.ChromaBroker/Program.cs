// Chroma shared-memory protocol reference:
// worflor/neuron crates/neuron-host/src/adapters/chroma_shm.rs (GPL-3.0-or-later).
// This C# port is part of OpenSynapse and distributed under AGPL-3.0.
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.ChromaBroker;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const int PollMilliseconds = 16;

    private static int Main(string[] args)
    {
        BrokerLog.Initialize();
        if (!TryParse(args, out var options, out var error))
        {
            BrokerLog.Error(error);
            return 2;
        }

        if (options.SelfTest)
        {
            return RunSelfTest() ? 0 : 1;
        }

        BrokerLog.Info(
            $"starting: pipe={options.PipeName}, parent={options.ParentPid?.ToString(CultureInfo.InvariantCulture) ?? "none"}, " +
            $"user={options.User?.Value ?? "current"}");

        ChromaSharedMemoryServer? server = null;
        ChromaBrokerPipeClient? pipe = null;
        ChromaAppPipeHost? appPipe = null;
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };

        try
        {
            server = ChromaSharedMemoryServer.Create();
            appPipe = new ChromaAppPipeHost((pid, _) => server.NoteRegisteredClient(pid), options.User);
            pipe = new ChromaBrokerPipeClient(options.PipeName);
            var nextParentCheck = Environment.TickCount64;

            while (!stop.IsCancellationRequested)
            {
                var now = Environment.TickCount64;
                if (options.ParentPid is { } parentPid && now >= nextParentCheck)
                {
                    nextParentCheck = now + 2_000;
                    if (!ChromaSharedMemoryServer.IsProcessAlive(parentPid))
                    {
                        BrokerLog.Info($"parent process {parentPid} exited; shutting down.");
                        break;
                    }
                }

                if (server.UpdateClient(now))
                {
                    pipe.SendByeAndDisconnect();
                    continue;
                }

                if (pipe.TryConnect())
                {
                    foreach (var effect in server.GetLatestEffects(unchecked((ulong)now)))
                    {
                        pipe.TrySend(new ChromaSdkSetEffectMessage(effect));
                    }
                }

                foreach (var effect in server.PollFrames(unchecked((ulong)now)))
                {
                    pipe.TrySend(new ChromaSdkSetEffectMessage(effect));
                }

                Thread.Sleep(PollMilliseconds);
            }
        }
        catch (ChromaAlreadyServingException)
        {
            BrokerLog.Info("another Chroma server owns the arbitration mask; standing down.");
            return 0;
        }
        catch (Exception exception)
        {
            BrokerLog.Error($"fatal: {exception}");
            return 1;
        }
        finally
        {
            appPipe?.Dispose();
            pipe?.Dispose();
            server?.Dispose();
        }

        return 0;
    }

    private static bool TryParse(string[] args, out Options options, out string error)
    {
        var pipeName = ChromaBrokerPipeClient.DefaultPipeName;
        uint? parentPid = null;
        SecurityIdentifier? user = null;
        var selfTest = false;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--self-test":
                    selfTest = true;
                    break;
                case "--pipe" when index + 1 < args.Length &&
                    !string.IsNullOrWhiteSpace(args[index + 1]) &&
                    !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                    pipeName = args[++index];
                    break;
                case "--parent" when index + 1 < args.Length &&
                    uint.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) &&
                    pid != 0:
                    parentPid = pid;
                    index++;
                    break;
                case "--user" when index + 1 < args.Length:
                    try
                    {
                        user = new SecurityIdentifier(args[++index]);
                    }
                    catch (ArgumentException)
                    {
                        error = "invalid value for --user";
                        options = default!;
                        return false;
                    }
                    break;
                case "--pipe":
                case "--parent":
                case "--user":
                    error = "missing or invalid value for " + args[index];
                    options = default!;
                    return false;
                default:
                    error = "unknown argument: " + args[index];
                    options = default!;
                    return false;
            }
        }

        options = new Options(pipeName, parentPid, selfTest, user);
        return true;
    }

    private static bool RunSelfTest()
    {
        void Report(string message)
        {
            BrokerLog.Info(message);
            Console.WriteLine(message);
        }

        try
        {
            var objects = ChromaObjectCatalog.Objects;
            Assert(objects.Length == 35, "object catalog must contain 35 entries");
            Assert(objects.Count(item => item.Origin == ChromaObjectOrigin.ServerCreated) == 22,
                "server-created object count");
            Assert(objects.Count(item => item.Origin == ChromaObjectOrigin.ServerCreated &&
                item.Kind == ChromaObjectKind.Section) == 14, "server section count");
            Assert(objects.Count(item => item.Origin == ChromaObjectOrigin.ServerCreated &&
                item.Kind == ChromaObjectKind.Event) == 6, "server event count");
            Assert(objects.Count(item => item.Origin == ChromaObjectOrigin.ServerCreated &&
                item.Kind == ChromaObjectKind.Mutex) == 2, "server mutex count");
            Assert(ChromaFrameDecoder.Keystream.Length == 512, "keystream size");

            foreach (var device in ChromaBrokerDeviceClasses.All)
            {
                var layout = ChromaFrameDecoder.GetLayout(device);
                var section = objects.First(item => item.Guid == device.SectionGuid());
                Assert(section.Size >= ChromaFrameDecoder.Record0 +
                    ChromaFrameDecoder.RingDepth * layout.Stride, $"{device} ring fits its section");
            }

            var keyboard = ChromaFrameDecoder.GetLayout(ChromaBrokerDeviceClass.Keyboard);

            var staticSection = Synth(ChromaBrokerDeviceClass.Keyboard, 6);
            WriteXorRgb(staticSection, ChromaFrameDecoder.Record0 + keyboard.StaticColorOffset,
                new ChromaRgb(10, 20, 30), 0);
            var staticEffect = ToEffect(staticSection, ChromaBrokerDeviceClass.Keyboard, 0);
            Assert(staticEffect.Kind == ChromaSdkEffectKind.Static, "static kind");
            Assert(staticEffect.Width == 22 && staticEffect.Height == 6, "static dimensions");
            Assert(staticEffect.Colors.All(color => color == 0x001E140A), "static colour");
            Report("PASS static");

            var customSection = Synth(ChromaBrokerDeviceClass.Keyboard, 7);
            WriteXorRgb(customSection, ChromaFrameDecoder.Record0 + keyboard.GridOffset,
                new ChromaRgb(1, 2, 3), 0);
            WriteXorRgb(customSection, ChromaFrameDecoder.Record0 + keyboard.GridOffset + 4 * 131,
                new ChromaRgb(4, 5, 6), 0);
            var customEffect = ToEffect(customSection, ChromaBrokerDeviceClass.Keyboard, 0);
            Assert(customEffect.Kind == ChromaSdkEffectKind.Custom, "custom kind");
            Assert(customEffect.Width == 22 && customEffect.Height == 6, "custom dimensions");
            Assert(customEffect.Colors[0] == 0x00030201 && customEffect.Colors[131] == 0x00060504,
                "custom row-major colours");
            Report("PASS custom");

            var customKeySection = Synth(ChromaBrokerDeviceClass.Keyboard, 8);
            var baseOffset = ChromaFrameDecoder.Record0 + keyboard.KeyColorOffset!.Value;
            var keyOffset = ChromaFrameDecoder.Record0 + keyboard.KeyOverrideOffset!.Value;
            WriteXorRgb(customKeySection, baseOffset, new ChromaRgb(10, 20, 30), 0);
            WriteXorRgb(customKeySection, baseOffset + 4, new ChromaRgb(11, 21, 31), 0);
            WriteXorRgb(customKeySection, keyOffset + 4, new ChromaRgb(200, 0, 0), 0);
            WriteXorFlag(customKeySection, keyOffset + 3, 0, 0);
            WriteXorFlag(customKeySection, keyOffset + 7, 1, 0);
            var customKeyEffect = ToEffect(customKeySection, ChromaBrokerDeviceClass.Keyboard, 0);
            Assert(customKeyEffect.Kind == ChromaSdkEffectKind.Custom, "custom-key expands to custom");
            Assert(customKeyEffect.Colors[0] == 0x001E140A, "custom-key base colour");
            Assert(customKeyEffect.Colors[1] == 0x000000C8, "custom-key override colour");
            Report("PASS custom-key");

            var breathingSection = Synth(
                ChromaBrokerDeviceClass.Keyboard,
                3,
                (keyboard.BreathingTypeOffset!.Value, 2u),
                (keyboard.Color1Offset!.Value, 0x000000FFu),
                (keyboard.Color2Offset!.Value, 0x00FF0000u));
            var breathingFrame = ChromaFrameDecoder.Decode(breathingSection, ChromaBrokerDeviceClass.Keyboard) ??
                throw new InvalidOperationException("breathing frame did not decode");
            var breathingStart = ChromaFrameDecoder.ToEffect(breathingFrame, 0);
            var breathingFirst = ChromaFrameDecoder.ToEffect(breathingFrame, 2_000);
            var breathingSecond = ChromaFrameDecoder.ToEffect(breathingFrame, 6_000);
            Assert(breathingStart.Colors.All(color => color == 0), "breathing starts dark");
            Assert(breathingFirst.Colors.All(color => color == 0x000000FF), "breathing first colour");
            Assert(breathingSecond.Colors.All(color => color == 0x00FF0000), "breathing second colour");
            Report("PASS breathing");

            var waveSection = Synth(ChromaBrokerDeviceClass.Keyboard, 1, (keyboard.WaveDirectionOffset!.Value, 2u));
            var waveEffect = ToEffect(waveSection, ChromaBrokerDeviceClass.Keyboard, 0);
            Assert(waveEffect.Kind == ChromaSdkEffectKind.Custom, "wave renders as custom");
            Assert(waveEffect.Colors[0] == 0x000000FF && waveEffect.Colors[11] == 0x00FFFF00,
                "wave columns use expected hues");
            Assert(waveEffect.Colors[0] == waveEffect.Colors[22], "wave is constant down a column");
            Report("PASS wave");

            foreach (var device in ChromaBrokerDeviceClasses.All)
            {
                var size = ChromaFrameDecoder.Record0 +
                    ChromaFrameDecoder.RingDepth * ChromaFrameDecoder.GetLayout(device).Stride;
                var random = new Random((int)device);
                for (var seed = 0; seed < 8; seed++)
                {
                    var noisy = new byte[size];
                    random.NextBytes(noisy);
                    BinaryPrimitives.WriteUInt32LittleEndian(noisy, (uint)seed);
                    if (ChromaFrameDecoder.Decode(noisy, device) is { } frame)
                    {
                        _ = ChromaFrameDecoder.ToEffect(frame, 1_234);
                    }
                }
            }
            Report("PASS torn-read");

            Report("PASS Chroma broker decoder self-test");
            return true;
        }
        catch (Exception exception)
        {
            Report("FAIL Chroma broker decoder self-test: " + exception.Message);
            return false;
        }
    }

    private static ChromaSdkEffect ToEffect(
        byte[] section,
        ChromaBrokerDeviceClass device,
        ulong nowMs)
    {
        var frame = ChromaFrameDecoder.Decode(section, device) ??
            throw new InvalidOperationException($"{device} frame did not decode");
        return ChromaFrameDecoder.ToEffect(frame, nowMs);
    }

    private static byte[] Synth(
        ChromaBrokerDeviceClass device,
        uint effect,
        params (int Offset, uint Value)[] words)
    {
        var layout = ChromaFrameDecoder.GetLayout(device);
        var section = new byte[ChromaFrameDecoder.Record0 + ChromaFrameDecoder.RingDepth * layout.Stride];
        BinaryPrimitives.WriteUInt32LittleEndian(section, 1);
        var record = ChromaFrameDecoder.Record0;
        BinaryPrimitives.WriteUInt32LittleEndian(
            section.AsSpan(record),
            ((uint)device.Bit() << 16) | 0xFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(record + layout.EffectOffset), effect);
        foreach (var (offset, value) in words)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(record + offset), value);
        }
        return section;
    }

    private static void WriteXorRgb(byte[] section, int offset, ChromaRgb colour, int phase)
    {
        var keyStream = ChromaFrameDecoder.Keystream;
        section[offset] = (byte)(colour.R ^ keyStream[phase]);
        section[offset + 1] = (byte)(colour.G ^ keyStream[phase + ChromaFrameDecoder.KeyStreamChannelStride]);
        section[offset + 2] = (byte)(colour.B ^ keyStream[phase + 2 * ChromaFrameDecoder.KeyStreamChannelStride]);
    }

    private static void WriteXorFlag(byte[] section, int offset, byte value, int phase)
    {
        section[offset] = (byte)(value ^ ChromaFrameDecoder.Keystream[
            phase + 3 * ChromaFrameDecoder.KeyStreamChannelStride]);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record Options(
        string PipeName,
        uint? ParentPid,
        bool SelfTest,
        SecurityIdentifier? User);
}

internal static class BrokerLog
{
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OpenSynapse",
        "ChromaBroker.log");

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        Initialize();
        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:O} {level} {message}{Environment.NewLine}",
                    Utf8);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
