using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace OpenSynapse.App;

public sealed class NavigationPaneResizeThumb : Control
{
    internal const double MinimumPaneWidth = 224;
    internal const double MaximumPaneWidth = 480;
    private Thumb? _dragThumb;
    private double _dragWidth;
    internal bool IsDragging => _dragThumb?.IsDragging == true;
    internal double Value { get; private set; } = MinimumPaneWidth;
    internal double Maximum { get; private set; } = MaximumPaneWidth;
    internal event Action<double>? WidthRequested;
    internal event EventHandler? ResizeCompleted;

    public NavigationPaneResizeThumb() =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    protected override void OnApplyTemplate()
    {
        if (_dragThumb is not null)
        {
            CancelDrag();
            _dragThumb.DragStarted -= OnDragStarted;
            _dragThumb.DragDelta -= OnDragDelta;
            _dragThumb.DragCompleted -= OnDragCompleted;
        }
        base.OnApplyTemplate();
        _dragThumb = GetTemplateChild("ResizeThumb") as Thumb;
        if (_dragThumb is null) return;
        _dragThumb.DragStarted += OnDragStarted;
        _dragThumb.DragDelta += OnDragDelta;
        _dragThumb.DragCompleted += OnDragCompleted;
    }

    internal void CancelDrag() => _dragThumb?.CancelDrag();

    private void OnDragStarted(object sender, DragStartedEventArgs args)
    {
        _dragWidth = Value;
        Focus(FocusState.Pointer);
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs args)
    {
        _dragWidth += args.HorizontalChange;
        RequestWidth(_dragWidth);
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs args) =>
        ResizeCompleted?.Invoke(this, EventArgs.Empty);

    internal void UpdateRange(double value, double maximum)
    {
        var oldValue = Value;
        var oldMaximum = Maximum;
        Value = value;
        Maximum = maximum;
        if (FrameworkElementAutomationPeer.FromElement(this) is not { } peer) return;
        if (oldValue != value)
        {
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, value);
            peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty,
                oldValue.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture));
        }
        if (oldMaximum != maximum)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MaximumProperty, oldMaximum, maximum);
    }

    private void RequestWidth(double value) =>
        WidthRequested?.Invoke(Math.Clamp(value, MinimumPaneWidth, Maximum));

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        var width = e.Key switch
        {
            VirtualKey.Left => Value - 8,
            VirtualKey.Right => Value + 8,
            VirtualKey.PageDown => Value - 32,
            VirtualKey.PageUp => Value + 32,
            VirtualKey.Home => MinimumPaneWidth,
            VirtualKey.End => Maximum,
            _ => double.NaN,
        };
        if (double.IsNaN(width)) { base.OnKeyDown(e); return; }
        RequestWidth(width);
        ResizeCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ResizeAutomationPeer(this);

    private sealed class ResizeAutomationPeer(NavigationPaneResizeThumb owner) :
        FrameworkElementAutomationPeer(owner), IRangeValueProvider, IValueProvider
    {
        protected override string GetClassNameCore() => nameof(NavigationPaneResizeThumb);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;

        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.RangeValue or PatternInterface.Value
                ? this : base.GetPatternCore(patternInterface);

        public bool IsReadOnly => !owner.IsEnabled || owner.Visibility != Visibility.Visible;
        public double LargeChange => 32;
        public double SmallChange => 8;
        public double Minimum => MinimumPaneWidth;
        public double Maximum => owner.Maximum;
        public double Value => owner.Value;
        string IValueProvider.Value => owner.Value.ToString(CultureInfo.InvariantCulture);

        public void SetValue(double value)
        {
            if (IsReadOnly) throw new InvalidOperationException("Navigation pane resizing is unavailable.");
            if (!double.IsFinite(value) || value < Minimum || value > Maximum)
                throw new ArgumentOutOfRangeException(nameof(value));
            owner.RequestWidth(value);
            owner.ResizeCompleted?.Invoke(owner, EventArgs.Empty);
        }

        void IValueProvider.SetValue(string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
                throw new ArgumentException("A numeric width is required.", nameof(value));
            SetValue(width);
        }
    }
}
