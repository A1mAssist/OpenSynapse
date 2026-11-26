using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using OpenSynapse.App.Controls;

namespace OpenSynapse.App.Views;

/// <summary>Commit boundaries for device settings; the caller compares drafts to confirmed state.</summary>
internal sealed class SettingAutoApply
{
    private readonly StackPanel _group;
    private readonly SettingApplyQueue _queue;
    private readonly Func<object?> _getContext;
    private readonly Func<Func<Task<string?>>?> _prepareApply;
    private readonly HashSet<DependencyObject> _hooked = [];
    private readonly List<Action> _unhook = [];
    private readonly DispatcherTimer _delay = new();
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _retry = new() { Visibility = Visibility.Collapsed };
    private readonly Grid _feedback = new() { ColumnSpacing = 8, MinHeight = 18 };
    private object? _scheduledContext;
    private object? _retryContext;
    private Func<Task<string?>>? _retryApply;
    private bool _pointerDown;
    private bool _applying;
    private bool _scheduledUserEdit;
    private long _revision;
    private long _scheduleRevision;
    private (DependencyObject Input, object Value)[]? _submittedValues;

    internal static SettingAutoApply Attach(StackPanel group, SettingApplyQueue queue,
        Func<object?> getContext, Func<Func<Task<string?>>?> prepareApply) =>
        new(group, queue, getContext, prepareApply);

    internal void CommitEdit() => Schedule(TimeSpan.Zero, userEdit: true);

    private SettingAutoApply(StackPanel group, SettingApplyQueue queue,
        Func<object?> getContext, Func<Func<Task<string?>>?> prepareApply)
    {
        _group = group;
        _queue = queue;
        _getContext = getContext;
        _prepareApply = prepareApply;
        _feedback.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _feedback.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _status.Foreground = (Brush)Application.Current.Resources["MutedBrush"];
        _retry.Style = (Style)Application.Current.Resources["SecondaryButtonStyle"];
        _retry.Content = AppStrings.Text("SettingAutoApplyRetry");
        _retry.Padding = new Thickness(10, 3, 10, 3);
        Grid.SetColumn(_retry, 1);
        _feedback.Children.Add(_status);
        _feedback.Children.Add(_retry);
        group.Children.Add(_feedback);
        AutomationProperties.SetAutomationId(_status, group.Name + "ApplyStatus");
        AutomationProperties.SetAutomationId(_retry, group.Name + "ApplyRetry");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _delay.Tick += (_, _) => { _delay.Stop(); Commit(_scheduledContext, _scheduledUserEdit); };
        _dismiss.Tick += (_, _) => { _dismiss.Stop(); _status.Text = string.Empty; };
        _retry.Click += (_, _) =>
        {
            if (_retryApply is { } apply && Equals(_retryContext, _getContext()))
                Submit(_retryContext, apply);
        };
        group.Loaded += (_, _) => group.DispatcherQueue.TryEnqueue(() => Hook(group));
        group.Unloaded += (_, _) =>
        {
            _delay.Stop();
            _dismiss.Stop();
            _scheduleRevision++;
            _pointerDown = false;
            foreach (var unhook in _unhook) unhook();
            _unhook.Clear();
            _hooked.Clear();
        };
        group.DataContextChanged += (_, _) => Reset();
        group.LostFocus += (_, e) =>
        {
            if (FindInput(e.OriginalSource as DependencyObject) is NumberBox && !ContainsFocus())
                Commit(_getContext(), userEdit: true);
        };
        group.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == VirtualKey.Enter) Schedule(TimeSpan.Zero, userEdit: true);
            else if (e.Key is VirtualKey.Space or VirtualKey.Left or VirtualKey.Right or
                VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown)
            {
                if (FindInput(e.OriginalSource as DependencyObject) is Slider or ToggleSwitch or ComboBox or CheckBox)
                    Schedule(TimeSpan.FromMilliseconds(300), userEdit: true);
            }
        }), true);
        if (group.IsLoaded) Hook(group);
    }

    private void Reset()
    {
        _revision++;
        _scheduleRevision++;
        _delay.Stop();
        _dismiss.Stop();
        _pointerDown = false;
        _applying = false;
        _status.Text = string.Empty;
        _retryApply = null;
        _submittedValues = null;
        _retry.Visibility = Visibility.Collapsed;
    }

    private void Hook(DependencyObject element)
    {
        if (ReferenceEquals(element, _feedback)) return;
        if (_hooked.Add(element))
        {
            switch (element)
            {
                case EqualizerCurveEditor editor:
                    EventHandler rebuilt = (_, _) => _group.DispatcherQueue.TryEnqueue(() =>
                    {
                        foreach (var unhook in _unhook) unhook();
                        _unhook.Clear();
                        _hooked.Clear();
                        if (_group.IsLoaded) Hook(_group);
                    });
                    editor.ControlsRebuilt += rebuilt;
                    _unhook.Add(() => editor.ControlsRebuilt -= rebuilt);
                    break;
                case ToggleSwitch toggle:
                    RoutedEventHandler toggled = (_, _) => Schedule(TimeSpan.Zero);
                    PointerEventHandler toggledByPointer = (_, _) => Schedule(TimeSpan.Zero, userEdit: true);
                    toggle.Toggled += toggled;
                    toggle.AddHandler(UIElement.PointerReleasedEvent, toggledByPointer, true);
                    _unhook.Add(() => { toggle.Toggled -= toggled; toggle.RemoveHandler(UIElement.PointerReleasedEvent, toggledByPointer); });
                    return;
                case CheckBox check:
                    RoutedEventHandler checkedChanged = (_, _) => { if (check.IsLoaded) Schedule(TimeSpan.Zero); };
                    RoutedEventHandler checkedByUser = (_, _) => Schedule(TimeSpan.Zero, userEdit: true);
                    check.Checked += checkedChanged;
                    check.Unchecked += checkedChanged;
                    check.Click += checkedByUser;
                    _unhook.Add(() =>
                    {
                        check.Checked -= checkedChanged;
                        check.Unchecked -= checkedChanged;
                        check.Click -= checkedByUser;
                    });
                    return;
                case ComboBox combo:
                    SelectionChangedEventHandler selected = (_, _) =>
                    {
                        if (!combo.IsDropDownOpen) Schedule(TimeSpan.Zero);
                    };
                    EventHandler<object> closed = (_, _) => Schedule(TimeSpan.Zero, userEdit: true);
                    combo.SelectionChanged += selected;
                    combo.DropDownClosed += closed;
                    _unhook.Add(() => { combo.SelectionChanged -= selected; combo.DropDownClosed -= closed; });
                    return;
                case Slider slider:
                    PointerEventHandler pressed = (_, _) => { _pointerDown = true; _delay.Stop(); };
                    PointerEventHandler released = (_, _) =>
                    {
                        if (!_pointerDown) return;
                        _pointerDown = false;
                        Schedule(TimeSpan.Zero, userEdit: true);
                    };
                    Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventHandler changed = (_, _) =>
                    {
                        if (!_pointerDown) Schedule(TimeSpan.FromMilliseconds(300));
                    };
                    slider.AddHandler(UIElement.PointerPressedEvent, pressed, true);
                    slider.AddHandler(UIElement.PointerReleasedEvent, released, true);
                    slider.AddHandler(UIElement.PointerCaptureLostEvent, released, true);
                    slider.ValueChanged += changed;
                    _unhook.Add(() =>
                    {
                        slider.RemoveHandler(UIElement.PointerPressedEvent, pressed);
                        slider.RemoveHandler(UIElement.PointerReleasedEvent, released);
                        slider.RemoveHandler(UIElement.PointerCaptureLostEvent, released);
                        slider.ValueChanged -= changed;
                    });
                    return;
                case NumberBox:
                    return;
                case ItemsRepeater repeater:
                    global::Windows.Foundation.TypedEventHandler<ItemsRepeater, ItemsRepeaterElementPreparedEventArgs> prepared =
                        (_, args) => Hook(args.Element);
                    repeater.ElementPrepared += prepared;
                    _unhook.Add(() => repeater.ElementPrepared -= prepared);
                    break;
                case ItemsControl items:
                    // Keyboard rows and Snap Tap pairs can appear after the group was loaded.
                    var rowsChanged = true;
                    global::Windows.Foundation.Collections.VectorChangedEventHandler<object> collectionChanged =
                        (_, _) => rowsChanged = true;
                    EventHandler<object> layout = (_, _) =>
                    {
                        if (!rowsChanged) return;
                        rowsChanged = false;
                        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(items); i++)
                            Hook(VisualTreeHelper.GetChild(items, i));
                    };
                    items.Items.VectorChanged += collectionChanged;
                    items.LayoutUpdated += layout;
                    _unhook.Add(() =>
                    {
                        items.Items.VectorChanged -= collectionChanged;
                        items.LayoutUpdated -= layout;
                    });
                    break;
            }
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            Hook(VisualTreeHelper.GetChild(element, i));
    }

    private bool ContainsFocus()
    {
        if (_group.XamlRoot is null) return false;
        for (var element = FocusManager.GetFocusedElement(_group.XamlRoot) as DependencyObject;
             element is not null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, _group)) return true;
        return false;
    }

    private static bool IsVisible(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    private static DependencyObject? FindInput(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is Slider or NumberBox or ComboBox or ToggleSwitch or CheckBox) return element;
        return null;
    }

    private void Schedule(TimeSpan delay, bool userEdit = false)
    {
        if (!_group.IsLoaded || !IsVisible(_group) || (_applying && !userEdit) || _pointerDown) return;
        _scheduledContext = _getContext();
        _scheduledUserEdit = userEdit;
        var scheduleRevision = ++_scheduleRevision;
        _delay.Stop();
        if (delay == TimeSpan.Zero)
        {
            var context = _scheduledContext;
            _group.DispatcherQueue.TryEnqueue(() =>
            {
                if (scheduleRevision == _scheduleRevision) Commit(context, userEdit);
            });
        }
        else
        {
            _delay.Interval = delay;
            _delay.Start();
        }
    }

    private void Commit(object? context, bool userEdit = false)
    {
        if (!_group.IsLoaded || !IsVisible(_group) || (_applying && !userEdit) || context is null || !Equals(context, _getContext())) return;
        if (_hooked.OfType<ComboBox>().Any(combo => combo.IsDropDownOpen)) return;
        foreach (var number in _hooked.OfType<NumberBox>().Where(number => number.IsLoaded && number.IsEnabled && IsVisible(number)))
        {
            if (!double.IsFinite(number.Value) ||
                !double.TryParse(number.Text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var value) ||
                !double.IsFinite(value) || value < number.Minimum || value > number.Maximum)
            {
                Show(AppStrings.Text("SettingAutoApplyInvalid"), retry: false);
                return;
            }
        }
        var values = CaptureValues();
        if (_submittedValues is not null && _submittedValues.SequenceEqual(values)) return;
        if (_prepareApply() is { } apply) Submit(context, apply);
    }

    private (DependencyObject Input, object Value)[] CaptureValues() => _hooked
        .Where(input => input is FrameworkElement { IsLoaded: true } && IsVisible(input))
        .Select(input => (Input: input, Value: input switch
        {
            NumberBox number => (object)number.Value,
            Slider slider => slider.Value,
            ComboBox combo => combo.SelectedIndex,
            ToggleSwitch toggle => toggle.IsOn,
            CheckBox check => check.IsChecked,
            _ => null,
        }))
        .Where(item => item.Value is not null)
        .Select(item => (item.Input, item.Value!)).ToArray();

    private void Submit(object? context, Func<Task<string?>> apply)
    {
        var revision = ++_revision;
        _submittedValues = CaptureValues();
        Show(AppStrings.Text("SettingAutoApplyApplying"), retry: false);
        _queue.Enqueue(this, async () =>
        {
            if (revision != _revision) return;
            if (!Equals(context, _getContext()))
            {
                if (revision == _revision) Reset();
                return;
            }
            _applying = true;
            string? error;
            try { error = await apply(); }
            catch (OperationCanceledException) { error = AppStrings.Text("SettingAutoApplyCancelled"); }
            catch (Exception exception) { error = exception.Message; }
            finally { _applying = false; }
            if (revision != _revision) return;
            if (!Equals(context, _getContext())) { Reset(); return; }
            _submittedValues = null;
            _retryContext = context;
            _retryApply = string.IsNullOrEmpty(error) ? null : apply;
            Show(string.IsNullOrEmpty(error) ? AppStrings.Text("SettingAutoApplyApplied") : error,
                retry: !string.IsNullOrEmpty(error));
            if (string.IsNullOrEmpty(error)) _dismiss.Start();
        });
    }

    private void Show(string text, bool retry)
    {
        _dismiss.Stop();
        _status.Text = text;
        _retry.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
    }
}
