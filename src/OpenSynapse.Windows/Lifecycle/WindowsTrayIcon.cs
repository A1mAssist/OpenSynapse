using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Lifecycle;

public sealed class WindowsTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private const uint IconId = 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint WmNull = 0x0000;
    private const uint WmContextMenu = 0x007B;
    private const uint WmTimer = 0x0113;
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmLeftButtonDoubleClick = 0x0203;
    private const uint WmRightButtonUp = 0x0205;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const int IdiApplication = 32512;
    private const uint ShowCommand = 1;
    private const uint ExitCommand = 2;
    private const uint RetryTimerId = 2;
    private const int MaxReAddAttempts = 3;

    private readonly IntPtr _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _iconHandle;
    private bool _ownsIcon;
    private int _reAddAttempts;
    private bool _unavailable;
    private bool _disposed;

    public WindowsTrayIcon(IntPtr windowHandle, string toolTip, string? iconPath = null)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("托盘图标需要有效的窗口句柄。", nameof(windowHandle));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(toolTip);
        _windowHandle = windowHandle;
        ToolTip = toolTip.Length > 127 ? toolTip[..127] : toolTip;
        _subclassProcedure = WindowProcedure;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册 Explorer 重启通知。");
        }

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            _iconHandle = LoadImageW(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
            _ownsIcon = _iconHandle != IntPtr.Zero;
        }

        if (_iconHandle == IntPtr.Zero)
        {
            _iconHandle = LoadIconW(IntPtr.Zero, new IntPtr(IdiApplication));
        }

        if (_iconHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载托盘图标。");
        }

        if (!SetWindowSubclass(_windowHandle, _subclassProcedure, IconId, UIntPtr.Zero))
        {
            ReleaseOwnedIcon();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册托盘窗口消息。");
        }

        if (!AddIcon())
        {
            RemoveWindowSubclass(_windowHandle, _subclassProcedure, IconId);
            ReleaseOwnedIcon();
            throw new InvalidOperationException("Windows 未能创建 OpenSynapse 托盘图标。");
        }
    }

    public event Action? ShowRequested;

    public event Action? ExitRequested;

    public event Action? Unavailable;

    private string ToolTip { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        KillTimer(_windowHandle, new UIntPtr(RetryTimerId));
        var data = CreateIconData();
        ShellNotifyIconW(NimDelete, ref data);

        RemoveWindowSubclass(_windowHandle, _subclassProcedure, IconId);
        ReleaseOwnedIcon();
        GC.SuppressFinalize(this);
    }

    private bool AddIcon()
    {
        var data = CreateIconData();
        return ShellNotifyIconW(NimAdd, ref data);
    }

    private NotifyIconData CreateIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _windowHandle,
        Id = IconId,
        Flags = NifMessage | NifIcon | NifTip,
        CallbackMessage = CallbackMessage,
        IconHandle = _iconHandle,
        Tip = ToolTip,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        if (message == _taskbarCreatedMessage)
        {
            ReAddIcon();
        }
        else if (message == WmTimer && wParam.ToUInt64() == RetryTimerId)
        {
            RetryAddIcon();
        }
        else if (message == CallbackMessage)
        {
            var notification = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
            if (notification is WmLeftButtonUp or WmLeftButtonDoubleClick)
            {
                ShowRequested?.Invoke();
            }
            else if (notification is WmRightButtonUp or WmContextMenu)
            {
                ShowMenu();
            }
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private void ReAddIcon()
    {
        if (_disposed || _unavailable)
        {
            return;
        }

        KillTimer(_windowHandle, new UIntPtr(RetryTimerId));
        _reAddAttempts = 1;
        if (AddIcon())
        {
            return;
        }

        if (SetTimer(_windowHandle, new UIntPtr(RetryTimerId), 500, IntPtr.Zero) == UIntPtr.Zero)
        {
            MarkUnavailable();
        }
    }

    private void RetryAddIcon()
    {
        if (AddIcon())
        {
            KillTimer(_windowHandle, new UIntPtr(RetryTimerId));
            return;
        }

        _reAddAttempts++;
        if (_reAddAttempts >= MaxReAddAttempts)
        {
            MarkUnavailable();
        }
    }

    private void MarkUnavailable()
    {
        KillTimer(_windowHandle, new UIntPtr(RetryTimerId));
        _unavailable = true;
        Unavailable?.Invoke();
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenuW(menu, MfString, ShowCommand, "显示 OpenSynapse");
            AppendMenuW(menu, MfSeparator, 0, null);
            AppendMenuW(menu, MfString, ExitCommand, "退出");
            GetCursorPos(out var cursor);
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(
                menu,
                TpmRightButton | TpmReturnCommand,
                cursor.X,
                cursor.Y,
                0,
                _windowHandle,
                IntPtr.Zero);
            PostMessageW(_windowHandle, WmNull, UIntPtr.Zero, IntPtr.Zero);

            if (command == ShowCommand)
            {
                ShowRequested?.Invoke();
            }
            else if (command == ExitCommand)
            {
                ExitRequested?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void ReleaseOwnedIcon()
    {
        if (_ownsIcon && _iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }

        _iconHandle = IntPtr.Zero;
        _ownsIcon = false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIconHandle;
    }

    private delegate IntPtr SubclassProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProcedure subclassProcedure,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr windowHandle,
        SubclassProcedure subclassProcedure,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(
        IntPtr instance,
        string name,
        uint type,
        int desiredWidth,
        int desiredHeight,
        uint loadFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr windowHandle,
        IntPtr rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern UIntPtr SetTimer(
        IntPtr windowHandle,
        UIntPtr timerId,
        uint intervalMilliseconds,
        IntPtr timerProcedure);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(IntPtr windowHandle, UIntPtr timerId);
}
