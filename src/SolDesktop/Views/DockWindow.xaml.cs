using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SolDesktop.Interop;
using SolDesktop.Services;
using SolDesktop.ViewModels;

namespace SolDesktop.Views;

public partial class DockWindow : Window
{
    private const double ItemGap = 6;
    private const double SeparatorSlot = 13;
    private const double DragThreshold = 6;
    private const double MagnifyReach = 2.6;

    private readonly DockViewModel _vm;
    private readonly WindowPreviewWindow _preview = new();
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _previewTracker;
    private readonly Dictionary<ContentPresenter, double> _scales = new();

    private bool _animating;
    private double? _mouseX;
    private DockItemViewModel? _hoverItem;
    private ContentPresenter? _hoverContainer;
    private DockItemViewModel? _pressItem;
    private Point _pressPoint;
    private bool _dragging;
    private ContentPresenter? _previewAnchor;
    private int _previewOutsideTicks;

    public DockWindow(DockViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Controller = new EdgeBarController(Bar, Slide, ScreenEdge.Bottom);

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _hoverTimer.Tick += OnHoverTimer;
        _previewTracker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _previewTracker.Tick += OnPreviewTrack;

        _preview.ActivateRequested += (_, hwnd) =>
        {
            HidePreview();
            WindowActions.Activate(hwnd);
        };
        _preview.CloseRequested += (_, hwnd) => WindowActions.Close(hwnd);

        _vm.Items.CollectionChanged += OnItemsChanged;
        _vm.Updated += (_, _) => RefreshPreview();
        Appearance.Current.PropertyChanged += (_, _) => ApplyLayout();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        PreviewMouseMove += OnWindowMouseMove;
        PreviewMouseLeftButtonUp += OnWindowMouseUp;
        Drop += OnDrop;

        ApplyLayout();
    }

    public EdgeBarController Controller { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
    }

    /// <summary>The window spans the screen width; transparent pixels are click-through.</summary>
    public void ApplyLayout()
    {
        Appearance a = Appearance.Current;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = Math.Ceiling(a.DockIconSize * a.DockMagnification + 40);
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight - Height;
        RequestAnimation();
    }

    public NativeMethods.RECT GetBarZone()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double left = Left + (ActualWidth - Bar.ActualWidth) / 2;
        double top = Top + ActualHeight - Bar.ActualHeight;
        return new NativeMethods.RECT(
            (int)(left * dpi.DpiScaleX),
            (int)(top * dpi.DpiScaleY),
            (int)((left + Bar.ActualWidth) * dpi.DpiScaleX),
            (int)((Top + ActualHeight) * dpi.DpiScaleY));
    }

    public new void Hide()
    {
        HidePreview();
        base.Hide();
    }

    // ---- magnification ----

    private void RequestAnimation()
    {
        if (_animating) return;
        _animating = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        bool moving;
        try
        {
            moving = StepMagnification();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            moving = false;
        }

        if (!moving)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
        }
    }

    /// <summary>Eases every icon one frame toward its target size; returns true while still moving.</summary>
    private bool StepMagnification()
    {
        Appearance a = Appearance.Current;
        double slot = a.DockIconSize + ItemGap;
        bool moving = false;

        for (int i = 0; i < Items.Items.Count; i++)
        {
            if (Items.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container) continue;
            if (Items.Items[i] is not DockItemViewModel item) continue;

            if (item.IsSeparator)
            {
                container.Width = SeparatorSlot;
                continue;
            }

            double target = 1;
            if (_mouseX is double mouseX && a.DockMagnification > 1.01)
            {
                double center = container.TranslatePoint(new Point(container.ActualWidth / 2, 0), Items).X;
                double distance = Math.Abs(mouseX - center) / (slot * MagnifyReach);
                if (distance < 1) target = 1 + (a.DockMagnification - 1) * (Math.Cos(distance * Math.PI) + 1) / 2;
            }

            double current = _scales.TryGetValue(container, out double s) ? s : 1;
            double next = current + (target - current) * 0.3;
            if (Math.Abs(next - target) < 0.003) next = target;
            else moving = true;

            _scales[container] = next;
            container.Width = slot * next;
            if (Items.ItemTemplate.FindName("IconHost", container) is FrameworkElement host)
            {
                // Freezables declared inside a DataTemplate are frozen, so each icon gets its own transform.
                if (host.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
                {
                    scale = new ScaleTransform();
                    host.RenderTransform = scale;
                }

                scale.ScaleX = next;
                scale.ScaleY = next;
            }
        }

        return moving;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Move) _scales.Clear();
        Dispatcher.InvokeAsync(RequestAnimation, DispatcherPriority.Loaded);
    }

    // ---- mouse ----

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        Point inItems = e.GetPosition(Items);
        if (Items.IsMouseOver || _dragging)
        {
            _mouseX = inItems.X;
            RequestAnimation();
        }

        if (_pressItem is { IsPinned: true } && !_dragging && e.LeftButton == MouseButtonState.Pressed &&
            Math.Abs(e.GetPosition(this).X - _pressPoint.X) > DragThreshold)
        {
            _dragging = true;
            _vm.SuspendRebuild = true;
            HidePreview();
            Items.CaptureMouse();
        }

        if (_dragging && _pressItem is not null)
        {
            int target = PinIndexAt(inItems.X);
            if (target >= 0) _vm.MovePinned(_pressItem, target);
        }
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        DockItemViewModel? pressed = _pressItem;
        _pressItem = null;

        if (_dragging)
        {
            _dragging = false;
            Items.ReleaseMouseCapture();
            _vm.SuspendRebuild = false;
            _vm.CommitPinnedOrder();
            _vm.Rebuild();
            return;
        }

        if (pressed is not null && pressed == _hoverItem && _hoverContainer is not null) OnItemClicked(pressed, _hoverContainer);
    }

    private void OnItemsMouseLeave(object sender, MouseEventArgs e)
    {
        if (_dragging) return;
        _mouseX = null;
        RequestAnimation();
    }

    private void OnItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not ContentPresenter container || container.Content is not DockItemViewModel item) return;
        _hoverItem = item;
        _hoverContainer = container;
        _hoverTimer.Stop();

        if (_preview.IsVisible)
        {
            if (item.IsRunning && item != _preview.Item) ShowPreview(item, container);
        }
        else if (item.IsRunning && !_dragging)
        {
            _hoverTimer.Start();
        }
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender == _hoverContainer)
        {
            _hoverItem = null;
            _hoverContainer = null;
        }

        _hoverTimer.Stop();
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ContentPresenter { Content: DockItemViewModel item }) return;
        _pressItem = item;
        _pressPoint = e.GetPosition(this);
        _hoverTimer.Stop();
    }

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is ContentPresenter { Content: DockItemViewModel { IsApp: true } item })
        {
            _vm.Launch(item);
            e.Handled = true;
        }
    }

    private void OnItemClicked(DockItemViewModel item, ContentPresenter container)
    {
        if (item.IsApp && item.Windows.Count > 1)
        {
            if (_preview.IsVisible && _preview.Item == item) HidePreview();
            else ShowPreview(item, container);
            return;
        }

        HidePreview();
        _vm.Click(item);
    }

    private int PinIndexAt(double x)
    {
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < Items.Items.Count; i++)
        {
            if (Items.Items[i] is not DockItemViewModel { IsPinned: true } item) continue;
            if (Items.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container) continue;

            double center = container.TranslatePoint(new Point(container.ActualWidth / 2, 0), Items).X;
            double distance = Math.Abs(x - center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = _vm.PinIndexOf(item);
            }
        }

        return best;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) _vm.PinPaths(files);
    }

    // ---- context menus ----

    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ContentPresenter { Content: DockItemViewModel item } container || item.IsSeparator) return;
        e.Handled = true;
        HidePreview();

        var app = (App)Application.Current;
        var entries = new List<(string?, Action?)>();
        switch (item.Kind)
        {
            case DockItemKind.App:
                entries.Add((item.Name, null));
                entries.Add((null, null));
                entries.Add((item.IsRunning ? "New window" : "Open", () => _vm.Launch(item)));
                entries.Add((item.IsPinned ? "Unpin from dock" : "Pin to dock",
                    item.IsPinned ? () => _vm.Unpin(item) : () => _vm.Pin(item)));
                if (item.IsRunning)
                {
                    entries.Add((null, null));
                    entries.Add((item.Windows.Count > 1 ? $"Close all {item.Windows.Count} windows" : "Close window", () => _vm.CloseAll(item)));
                }
                break;
            case DockItemKind.RecycleBin:
                entries.Add(("Open", _vm.OpenRecycleBin));
                entries.Add(("Empty Recycle Bin", _vm.EmptyRecycleBin));
                break;
            case DockItemKind.Start:
                entries.Add(("Open Start", WindowActions.OpenStartMenu));
                break;
        }

        entries.Add((null, null));
        entries.Add(("Sol settings…", app.ShowSettings));
        ShowMenu(MenuFactory.Create(entries.ToArray()), container);
    }

    private void OnBarRightClick(object sender, MouseButtonEventArgs e)
    {
        var app = (App)Application.Current;
        ShowMenu(MenuFactory.Create(
            ("Sol settings…", app.ShowSettings),
            ("Hide dock", () => app.ModifySettings(s => s.DockEnabled = false)),
            (null, null),
            ("Quit Sol Desktop", app.Shutdown)), Bar);
        e.Handled = true;
    }

    private void ShowMenu(ContextMenu menu, UIElement target)
    {
        Controller.KeepOpen = true;
        MenuFactory.Open(menu, target, PlacementMode.Top, () => Controller.KeepOpen = false);
    }

    // ---- window previews ----

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_hoverItem is { IsRunning: true } item && _hoverContainer is not null && !_dragging && _pressItem is null)
        {
            ShowPreview(item, _hoverContainer);
        }
    }

    private void ShowPreview(DockItemViewModel item, ContentPresenter container)
    {
        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;

        Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
        Point center = fromDevice.Transform(container.PointToScreen(new Point(container.ActualWidth / 2, 0)));
        Point barTop = fromDevice.Transform(Bar.PointToScreen(new Point(0, 0)));

        _previewAnchor = container;
        _previewOutsideTicks = 0;
        _preview.ShowFor(item, center.X, barTop.Y);
        Controller.KeepOpen = true;
        _previewTracker.Start();
    }

    private void HidePreview()
    {
        _previewTracker.Stop();
        if (!_preview.IsVisible) return;
        _preview.HideAndClear();
        _previewAnchor = null;
        Controller.KeepOpen = false;
    }

    private void RefreshPreview()
    {
        if (!_preview.IsVisible || _preview.Item is not DockItemViewModel item) return;
        if (!item.IsRunning || !_vm.Items.Contains(item)) HidePreview();
        else _preview.RefreshCards();
    }

    private void OnPreviewTrack(object? sender, EventArgs e)
    {
        if (!_preview.IsVisible)
        {
            _previewTracker.Stop();
            return;
        }

        NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
        if (_preview.ContainsScreenPoint(cursor) || _hoverItem is not null || IsInPreviewGap(cursor))
        {
            _previewOutsideTicks = 0;
            if (_hoverItem is { } hovered && hovered != _preview.Item && _hoverContainer is not null)
            {
                if (hovered.IsRunning) ShowPreview(hovered, _hoverContainer);
                else HidePreview();
            }

            return;
        }

        if (++_previewOutsideTicks >= 3) HidePreview();
    }

    /// <summary>The strip between the preview and its dock icon, so moving up to the preview doesn't close it.</summary>
    private bool IsInPreviewGap(NativeMethods.POINT cursor)
    {
        if (_previewAnchor is null || !_previewAnchor.IsVisible) return false;
        NativeMethods.RECT preview = _preview.ScreenRect;
        Point iconTop = _previewAnchor.PointToScreen(new Point(0, 0));
        Point iconBottom = _previewAnchor.PointToScreen(new Point(_previewAnchor.ActualWidth, _previewAnchor.ActualHeight));

        var gap = new NativeMethods.RECT(
            Math.Min(preview.Left, (int)iconTop.X),
            preview.Top,
            Math.Max(preview.Right, (int)iconBottom.X),
            (int)iconBottom.Y);
        return gap.Contains(cursor);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(ApplyLayout);

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CompositionTarget.Rendering -= OnRendering;
        _preview.Close();
        base.OnClosed(e);
    }
}
