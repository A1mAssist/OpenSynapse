using System.Runtime.InteropServices;
using static OpenSynapse.Windows.Lifecycle.TaskbarStatusNative;

namespace OpenSynapse.Windows.Lifecycle;

internal static class TaskbarStatusAutomation
{
    internal static Rect? ReadTrayBounds(IntPtr taskbar)
    {
        IUIAutomation? automation = null;
        IUIAutomationElement? root = null;
        IUIAutomationCondition? condition = null;
        IUIAutomationElementArray? elements = null;
        try
        {
            automation = (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"), throwOnError: true)!)!;
            automation.ElementFromHandle(taskbar, out root);
            // Win11's secondary tray is XAML and can have no clock HWND at all.
            automation.CreatePropertyCondition(30011, "SystemTrayIcon", out condition);
            root.FindAll(4, condition, out elements);
            elements.GetLength(out var count);
            Rect? bounds = null;
            for (var index = 0; index < count; index++)
            {
                IUIAutomationElement? element = null;
                try
                {
                    elements.GetElement(index, out element);
                    element.GetCurrentPropertyValue(30022, out var offscreen);
                    if (offscreen is true) continue;
                    element.GetCurrentPropertyValue(30001, out var value);
                    if (value is not double[] { Length: 4 } r || r[2] <= 0 || r[3] <= 0)
                        continue;
                    var rect = new Rect
                    {
                        Left = (int)Math.Floor(r[0]), Top = (int)Math.Floor(r[1]),
                        Right = (int)Math.Ceiling(r[0] + r[2]),
                        Bottom = (int)Math.Ceiling(r[1] + r[3]),
                    };
                    bounds = bounds is { } previous ? new Rect
                    {
                        Left = Math.Min(previous.Left, rect.Left),
                        Top = Math.Min(previous.Top, rect.Top),
                        Right = Math.Max(previous.Right, rect.Right),
                        Bottom = Math.Max(previous.Bottom, rect.Bottom),
                    } : rect;
                }
                finally { Release(element); }
            }
            return bounds;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            Release(elements);
            Release(condition);
            Release(root);
            Release(automation);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    // SDK UIAutomationClient.h order; unused slots preserve the native vtables.
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        void ElementFromHandle(IntPtr window, out IUIAutomationElement element);
        void ElementFromPoint();
        void GetFocusedElement();
        void GetRootElementBuildCache();
        void ElementFromHandleBuildCache();
        void ElementFromPointBuildCache();
        void GetFocusedElementBuildCache();
        void CreateTreeWalker();
        void GetControlViewWalker();
        void GetContentViewWalker();
        void GetRawViewWalker();
        void GetRawViewCondition();
        void GetControlViewCondition();
        void GetContentViewCondition();
        void CreateCacheRequest();
        void CreateTrueCondition();
        void CreateFalseCondition();
        void CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value,
            out IUIAutomationCondition condition);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll(int scope, IUIAutomationCondition condition, out IUIAutomationElementArray found);
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        void GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        void GetLength(out int length);
        void GetElement(int index, out IUIAutomationElement element);
    }

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition { }
}
