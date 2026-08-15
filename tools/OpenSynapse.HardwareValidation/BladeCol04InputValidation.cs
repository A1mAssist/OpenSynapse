using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class BladeCol04InputValidation
{
    private const string Col04CollectionId = "VID_1532&PID_02C6&MI_01&COL04";
    private const string Col05CollectionId = "VID_1532&PID_02C6&MI_01&COL05";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var holdSeconds = ReadHoldSeconds(args);
            var output = ReadOutput(args);
            var collectionId = args.Contains("--blade-col05-input", StringComparer.Ordinal)
                ? Col05CollectionId
                : Col04CollectionId;
            var snapshot = await WindowsHidDiscovery.DiscoverAllAsync();
            if (snapshot.ErrorMessage is not null)
            {
                throw new InvalidOperationException(snapshot.ErrorMessage);
            }

            var matches = snapshot.Devices
                .Where(device => device.Id.Contains(collectionId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"需要且只能有一个 Blade {collectionId} collection，当前为 {matches.Length}。可先运行设备发现确认路径。");
            }

            var useSoftwareMode = args.Contains("--software-mode", StringComparer.Ordinal);
            var controlDevice = snapshot.Devices.SingleOrDefault(device =>
                device.ProductId == 0x02C6 &&
                device.Access == OpenSynapse.Core.Devices.DeviceAccessState.Available &&
                device.UsagePage == 0x0001 &&
                device.Usage == 0x0002 &&
                device.FeatureReportByteLength == RazerFeatureReport.Length);
            if (useSoftwareMode && controlDevice is null)
            {
                throw new InvalidOperationException("找不到可写的 Blade feature collection，不能安全切换 Software 模式。");
            }
            var transport = new RazerFeatureTransport();
            var softwareModeApplied = false;

            var startedAt = DateTimeOffset.UtcNow;
            var observations = new List<Observation>();
            using var interrupted = new CancellationTokenSource();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(holdSeconds));
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(interrupted.Token, timeout.Token);
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                interrupted.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                if (useSoftwareMode)
                {
                    await SendAsync(
                        transport,
                        controlDevice!.Id,
                        BladeDeviceModeProtocol.CreateSetSoftwareRequest(),
                        CancellationToken.None);
                    softwareModeApplied = true;
                    Console.WriteLine("Blade 已切换到 Software/Driver 模式；结束时将恢复 Normal 模式。");
                }
                using var handle = File.OpenHandle(
                    matches[0].Id,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    FileOptions.Asynchronous);
                var inputReportByteLength = GetInputReportByteLength(handle);
                if (inputReportByteLength == 0)
                {
                    throw new InvalidOperationException("HID caps 返回 InputReportByteLength=0。");
                }

                await using var stream = new FileStream(handle, FileAccess.Read, inputReportByteLength, true);
                var buffer = new byte[inputReportByteLength];
                Console.WriteLine(
                    $"正在只读采集 {collectionId}，InputReportByteLength={inputReportByteLength}。请按 M3、M4、M5；{holdSeconds} 秒后自动结束。");

                try
                {
                    while (!stopped.IsCancellationRequested)
                    {
                        var count = await stream.ReadAsync(buffer, stopped.Token);
                        if (count == 0)
                        {
                            break;
                        }

                        var report = buffer.AsSpan(0, count).ToArray();
                        var observation = new Observation(
                            DateTimeOffset.UtcNow,
                            count,
                            report[0],
                            Convert.ToHexString(report));
                        observations.Add(observation);
                        Console.WriteLine($"{observation.At:O} {observation.Hex}");
                    }
                }
                catch (OperationCanceledException) when (stopped.IsCancellationRequested)
                {
                }

                var artifact = new Artifact(
                    startedAt,
                    DateTimeOffset.UtcNow,
                    matches[0].Id,
                    inputReportByteLength,
                    observations);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(
                    output,
                    JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"记录 {observations.Count} 条原始报告：{output}");
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                if (softwareModeApplied)
                {
                    await SendAsync(
                        transport,
                        controlDevice!.Id,
                        BladeDeviceModeProtocol.CreateSetNormalRequest(),
                        CancellationToken.None);
                    Console.WriteLine("Blade 已恢复 Normal 模式。");
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static Task<byte[]> SendAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        byte[] request,
        CancellationToken cancellationToken) =>
        transport.QueryAsync(
            devicePath,
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            TimeSpan.FromMilliseconds(2),
            cancellationToken);

    private static ushort GetInputReportByteLength(SafeFileHandle handle)
    {
        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsedData))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetPreparsedData failed.");
        }

        try
        {
            var status = NativeMethods.HidP_GetCaps(preparsedData, out var caps);
            if (status < 0)
            {
                throw new InvalidOperationException($"HidP_GetCaps failed: 0x{status:X8}.");
            }
            return caps.InputReportByteLength;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsedData);
        }
    }

    private static int ReadHoldSeconds(string[] args)
    {
        var index = Array.IndexOf(args, "--hold-seconds");
        if (index < 0)
        {
            return 60;
        }
        if (index + 1 >= args.Length ||
            !int.TryParse(args[index + 1], out var seconds) || seconds is < 30 or > 120)
        {
            throw new ArgumentException("--hold-seconds 必须为 30..120。");
        }
        return seconds;
    }

    private static string ReadOutput(string[] args)
    {
        var index = Array.IndexOf(args, "--output");
        if (index < 0 || index + 1 >= args.Length)
        {
            throw new ArgumentException("必须提供 --output <json>。");
        }
        var output = Path.GetFullPath(args[index + 1]);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(output), ".json") ||
            File.Exists(output))
        {
            throw new ArgumentException("--output 必须是尚不存在的 .json 文件。");
        }
        return output;
    }

    private sealed record Artifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string DevicePath,
        ushort InputReportByteLength,
        IReadOnlyList<Observation> Observations);

    private sealed record Observation(
        DateTimeOffset At,
        int ByteCount,
        byte ReportId,
        string Hex);

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct HidpCaps
        {
            internal ushort Usage;
            internal ushort UsagePage;
            internal ushort InputReportByteLength;
            internal ushort OutputReportByteLength;
            internal ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            internal ushort[] Reserved;
            internal ushort NumberLinkCollectionNodes;
            internal ushort NumberInputButtonCaps;
            internal ushort NumberInputValueCaps;
            internal ushort NumberInputDataIndices;
            internal ushort NumberOutputButtonCaps;
            internal ushort NumberOutputValueCaps;
            internal ushort NumberOutputDataIndices;
            internal ushort NumberFeatureButtonCaps;
            internal ushort NumberFeatureValueCaps;
            internal ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        internal static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);
    }
}
