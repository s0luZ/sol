using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SolDesktop.Interop;
using SolDesktop.ViewModels;
using static SolDesktop.Interop.NativeMethods;

namespace SolDesktop.Views;

public sealed record PreviewCard(IntPtr Handle, string Title, ImageSource? Icon);

/// <summary>Hover popup above a dock icon with a live DWM thumbnail for each of the app's windows.</summary>
public partial class WindowPreviewWindow : Window
{
    private readonly List<(IntPtr Thumb, FrameworkElement Host)> _thumbnails = new();
    private IntPtr _hwnd;
    private double _anchorCenterX;
    private double _anchorTop;

    public WindowPreviewWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            Reposition();
            UpdateThumbnailRects();
        };
    }

    public DockItemViewModel? Item { get; private set; }

    public event EventHandler<IntPtr>? ActivateRequested;
    public event EventHandler<IntPtr>? CloseRequested;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        AddExtendedStyle(_hwnd, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        UseRoundedCorners(_hwnd);
    }

    /// <summary>Keep the picker above the taskbar and Start while Win is held.</summary>
    public void RaiseAbove()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Move the cards above the Windows taskbar, then re-assert z-order.</summary>
    public void KeepClickable()
    {
        if (!IsVisible || ActualHeight <= 0) return;
        Reposition();
        RaiseAbove();
    }

    /// <param name="anchorCenterX">Horizontal centre of the dock icon, in DIPs.</param>
    /// <param name="anchorTop">Top of the dock bar, in DIPs.</param>
    public void ShowFor(DockItemViewModel item, double anchorCenterX, double anchorTop)
    {
        Item = item;
        _anchorCenterX = anchorCenterX;
        _anchorTop = anchorTop;
        SetCards(item);
        if (!IsVisible) Show();
        RaiseAbove();
        Dispatcher.InvokeAsync(() =>
        {
            Reposition();
            RegisterThumbnails();
            RaiseAbove();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Called when the window list changes while the preview is open.</summary>
    public void RefreshCards()
    {
        if (Item is null || Cards.ItemsSource is not List<PreviewCard> current) return;

        bool unchanged = current.Count == Item.Windows.Count &&
                         current.Zip(Item.Windows).All(p => p.First.Handle == p.Second.Handle && p.First.Title == p.Second.Title);
        if (unchanged) return;

        SetCards(Item);
        Dispatcher.InvokeAsync(RegisterThumbnails, DispatcherPriority.Loaded);
    }

    public void HideAndClear()
    {
        UnregisterThumbnails();
        Cards.ItemsSource = null;
        Item = null;
        Hide();
    }

    public bool ContainsScreenPoint(POINT p) => IsVisible && GetWindowRect(_hwnd, out RECT r) && r.Contains(p);

    public RECT ScreenRect => GetWindowRect(_hwnd, out RECT r) ? r : default;

    private void SetCards(DockItemViewModel item)
    {
        UnregisterThumbnails();
        Cards.ItemsSource = item.Windows.Select(w => new PreviewCard(w.Handle, w.Title, item.Icon)).ToList();
    }

    private void Reposition()
    {
        double width = ActualWidth;
        double maxLeft = SystemParameters.PrimaryScreenWidth - width - 8;
        Left = Math.Max(8, Math.Min(_anchorCenterX - width / 2, maxLeft));
        Top = _anchorTop - ActualHeight - 10;
        LiftAboveTaskbar();
    }

    /// <summary>
    /// The Windows taskbar (and the short bar Start brings with it) paints over the picker.
    /// Sit the cards fully above that strip so each window can be clicked.
    /// </summary>
    private void LiftAboveTaskbar()
    {
        if (ActualHeight <= 0) return;
        double ceiling = double.PositiveInfinity;

        foreach (IntPtr tray in BottomBars())
        {
            if (!IsWindowVisible(tray) || !GetWindowRect(tray, out RECT bar)) continue;
            int barHeight = bar.Bottom - bar.Top;
            if (barHeight is < 8 or > 160) continue;

            uint dpi = GetDpiForWindow(tray);
            if (dpi == 0) dpi = 96;
            double scale = 96.0 / dpi;
            double barLeft = bar.Left * scale;
            double barRight = bar.Right * scale;
            double barTop = bar.Top * scale;
            if (barRight < Left || barLeft > Left + ActualWidth) continue;
            if (barTop < SystemParameters.PrimaryScreenHeight * 0.45) continue;
            if (barTop < ceiling) ceiling = barTop;
        }

        if (double.IsPositiveInfinity(ceiling)) return;
        if (Top + ActualHeight > ceiling - 8) Top = Math.Max(8, ceiling - ActualHeight - 8);
    }

    private static List<IntPtr> BottomBars()
    {
        var bars = new List<IntPtr>();
        IntPtr primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) bars.Add(primary);

        IntPtr secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            bars.Add(secondary);

        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && IsStartHost(hwnd)) bars.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return bars;
    }

    private static readonly string[] StartHostNames =
    {
        "StartMenuExperienceHost.exe",
        "SearchHost.exe",
    };

    private static bool IsStartHost(IntPtr hwnd)
    {
        var className = new StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        string name = className.ToString();
        if (name is not ("XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow" or "ApplicationFrameWindow"))
            return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            var buffer = new StringBuilder(260);
            int size = buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size)) return false;
            string file = Path.GetFileName(buffer.ToString());
            foreach (string host in StartHostNames)
            {
                if (file.Equals(host, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private void RegisterThumbnails()
    {
        UnregisterThumbnails();
        if (_hwnd == IntPtr.Zero || Cards.ItemsSource is not List<PreviewCard> cards) return;

        for (int i = 0; i < cards.Count; i++)
        {
            if (Cards.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container) continue;
            if (Cards.ItemTemplate.FindName("ThumbHost", container) is not FrameworkElement host) continue;
            if (DwmRegisterThumbnail(_hwnd, cards[i].Handle, out IntPtr thumb) == 0) _thumbnails.Add((thumb, host));
        }

        UpdateThumbnailRects();
    }

    private void UpdateThumbnailRects()
    {
        if (_thumbnails.Count == 0) return;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);

        foreach (var (thumb, host) in _thumbnails)
        {
            if (!host.IsLoaded) continue;
            Rect slot = host.TransformToAncestor(this).TransformBounds(new Rect(host.RenderSize));
            slot = new Rect(slot.X * dpi.DpiScaleX, slot.Y * dpi.DpiScaleY, slot.Width * dpi.DpiScaleX, slot.Height * dpi.DpiScaleY);
            slot.Inflate(-4, -4);

            // Letterbox the window into the slot, keeping its aspect ratio.
            Rect dest = slot;
            if (DwmQueryThumbnailSourceSize(thumb, out SIZE source) == 0 && source.cx > 0 && source.cy > 0)
            {
                double scale = Math.Min(slot.Width / source.cx, slot.Height / source.cy);
                double w = source.cx * scale, h = source.cy * scale;
                dest = new Rect(slot.X + (slot.Width - w) / 2, slot.Y + (slot.Height - h) / 2, w, h);
            }

            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new RECT((int)dest.Left, (int)dest.Top, (int)dest.Right, (int)dest.Bottom),
                opacity = 255,
                fVisible = true,
                fSourceClientAreaOnly = false,
            };
            DwmUpdateThumbnailProperties(thumb, ref props);
        }
    }

    private void UnregisterThumbnails()
    {
        foreach (var (thumb, _) in _thumbnails) DwmUnregisterThumbnail(thumb);
        _thumbnails.Clear();
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PreviewCard card) ActivateRequested?.Invoke(this, card.Handle);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PreviewCard card) CloseRequested?.Invoke(this, card.Handle);
    }
}
