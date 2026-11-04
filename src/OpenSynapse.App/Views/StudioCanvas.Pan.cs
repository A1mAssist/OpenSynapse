using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace OpenSynapse.App.Views;

public sealed partial class StudioCanvas
{
    private void CanvasKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space) return;
#if DEBUG
        PointerQaLog($"space-down focus={FocusState}");
#endif
        _spaceHeld = true;
        e.Handled = true;
        UpdateCursor();
    }

    private void CanvasKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space) return;
#if DEBUG
        PointerQaLog("space-up");
#endif
        _spaceHeld = false;
        e.Handled = true;
        UpdateCursor();
    }

    private bool TryBeginPan(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Scroller);
#if DEBUG
        PointerQaLog($"pan-probe space={_spaceHeld} middle={point.Properties.IsMiddleButtonPressed} tool={_tool}");
#endif
        if (!_spaceHeld && !point.Properties.IsMiddleButtonPressed) return false;
        BeginPan(point.Position, e);
        return true;
    }

    private void BeginPan(Point position, PointerRoutedEventArgs e)
    {
        _panActive = true;
        _panStart = position;
        _panOffsetX = Scroller.HorizontalOffset;
        _panOffsetY = Scroller.VerticalOffset;
#if DEBUG
        PointerQaLog($"pan-begin captured={Surface.CapturePointer(e.Pointer)}");
#else
        Surface.CapturePointer(e.Pointer);
#endif
        e.Handled = true;
        UpdateCursor();
    }

    private void PanTo(Point position)
    {
        Scroller.ChangeView(
            Math.Max(0, _panOffsetX - (position.X - _panStart.X)),
            Math.Max(0, _panOffsetY - (position.Y - _panStart.Y)),
            null,
            true);
    }

    private void EndPan(PointerRoutedEventArgs e)
    {
        _panActive = false;
        Surface.ReleasePointerCapture(e.Pointer);
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        if (!IsLoaded) return;
        ProtectedCursor = InputSystemCursor.Create(
            _panActive ? InputSystemCursorShape.SizeAll :
            _spaceHeld ? InputSystemCursorShape.Hand :
            _tool switch
            {
                StudioCanvasTool.Pen => InputSystemCursorShape.Cross,
                StudioCanvasTool.Bucket => InputSystemCursorShape.Hand,
                StudioCanvasTool.Move => InputSystemCursorShape.SizeAll,
                _ => InputSystemCursorShape.Arrow,
            });
    }
}
