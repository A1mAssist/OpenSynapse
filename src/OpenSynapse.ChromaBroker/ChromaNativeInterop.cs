using System.Runtime.InteropServices;

namespace OpenSynapse.ChromaBroker;

internal static class ChromaNativeMethods
{
    internal const uint SnapshotModules = 0x00000008;
    internal const uint SnapshotModules32 = 0x00000010;
    internal static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr Descriptor;
        internal int InheritHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFileMappingW(
        IntPtr file,
        ref SecurityAttributes attributes,
        uint protect,
        uint maximumSizeHigh,
        uint maximumSizeLow,
        string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr MapViewOfFile(
        IntPtr mapping,
        uint desiredAccess,
        uint offsetHigh,
        uint offsetLow,
        UIntPtr numberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UnmapViewOfFile(IntPtr address);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateEventW(
        ref SecurityAttributes attributes,
        bool manualReset,
        bool initialState,
        string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateMutexW(
        ref SecurityAttributes attributes,
        bool initialOwner,
        string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenEventW(uint desiredAccess, bool inheritHandle, string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenMutexW(uint desiredAccess, bool inheritHandle, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl,
        uint revision,
        out IntPtr descriptor,
        IntPtr descriptorSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool Module32FirstW(IntPtr snapshot, ref ModuleEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool Module32NextW(IntPtr snapshot, ref ModuleEntry32 entry);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ModuleEntry32
    {
        internal uint Size;
        internal uint ModuleId;
        internal uint ProcessId;
        internal uint GlobalUsage;
        internal uint ProcessUsage;
        internal IntPtr BaseAddress;
        internal uint BaseSize;
        internal IntPtr ModuleHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string ModuleName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string ExecutablePath;
    }
}

internal sealed class EveryoneSecurity : IDisposable
{
    private const string Sddl = "D:(A;;GA;;;WD)";
    private IntPtr _descriptor;

    private EveryoneSecurity(IntPtr descriptor)
    {
        _descriptor = descriptor;
        Attributes = new ChromaNativeMethods.SecurityAttributes
        {
            Length = Marshal.SizeOf<ChromaNativeMethods.SecurityAttributes>(),
            Descriptor = descriptor,
            InheritHandle = 0,
        };
    }

    internal ChromaNativeMethods.SecurityAttributes Attributes;

    internal static EveryoneSecurity Create()
    {
        if (!ChromaNativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                Sddl, 1, out var descriptor, IntPtr.Zero))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }
        return new EveryoneSecurity(descriptor);
    }

    public void Dispose()
    {
        if (_descriptor != IntPtr.Zero)
        {
            ChromaNativeMethods.LocalFree(_descriptor);
            _descriptor = IntPtr.Zero;
        }
    }
}

internal sealed class MappedSection : IDisposable
{
    private IntPtr _mapping;
    private IntPtr _view;

    internal MappedSection(string guid, int size, IntPtr mapping, IntPtr view)
    {
        Guid = guid;
        Size = size;
        _mapping = mapping;
        _view = view;
    }

    internal string Guid { get; }
    internal int Size { get; }
    internal IntPtr View => _view;

    public void Dispose()
    {
        if (_view != IntPtr.Zero)
        {
            ChromaNativeMethods.UnmapViewOfFile(_view);
            _view = IntPtr.Zero;
        }
        if (_mapping != IntPtr.Zero)
        {
            ChromaNativeMethods.CloseHandle(_mapping);
            _mapping = IntPtr.Zero;
        }
    }
}
