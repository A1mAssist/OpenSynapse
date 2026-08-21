using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public static class BladeRecoveryFilterRestorer
{
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;

    public static void DisableAndClear(string filterDevicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterDevicePath);
        using var handle = CreateFile(filterDevicePath, 0, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the Product 710 filter endpoint.");

        var errors = new List<Exception>();
        foreach (var (code, payload) in CreateRecoveryPlan())
            TryControl(handle, code, payload, errors);
        if (errors.Count != 0)
            throw new AggregateException("Product 710 filter recovery was incomplete.", errors);
    }

    internal static IReadOnlyList<(uint Code, byte[] Payload)> CreateRecoveryPlan()
    {
        var plan = new List<(uint, byte[])>(RazerFilterInputHost.OfficialProduct710Hooks.Count + 3)
        {
            (RazerFilterInputProtocol.EnableInputRedirect,
                RazerFilterInputProtocol.CreateKeyboardRedirect(false)),
        };
        plan.AddRange(RazerFilterInputHost.OfficialProduct710Hooks.Reverse().Select(static hook =>
            (RazerFilterInputProtocol.ClearInputHook,
                RazerFilterInputProtocol.CreateKeyboardClearKey(hook.ScanCode, hook.Flag))));
        plan.Add((RazerFilterInputHost.EnableInputNotify, BitConverter.GetBytes(0)));
        plan.Add((RazerFilterInputHost.EnableInputHooks, BitConverter.GetBytes(0)));
        return plan;
    }

    private static void TryControl(SafeFileHandle handle, uint code, byte[] input, ICollection<Exception> errors)
    {
        if (!DeviceIoControl(handle, code, input, input.Length, null, 0, out _, 0))
            errors.Add(new Win32Exception(Marshal.GetLastWin32Error(), $"Filter IOCTL 0x{code:X8} failed."));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, byte[] input,
        int inputLength, byte[]? output, int outputLength, out uint bytesReturned, nint overlapped);
}
