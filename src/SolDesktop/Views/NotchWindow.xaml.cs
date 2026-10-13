using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using SolDesktop.Interop;
using SolDesktop.Services;

namespace SolDesktop.Views;

public partial class NotchWindow : Window
{
    public NotchWindow()
    {
        InitializeComponent();
        Controller = new EdgeBarController(Bar, Slide, ScreenEdge.Top);
        SizeChanged += (_, _) => PositionAtTopCenter();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Bar.MouseEnter += (_, _) => AnimateGrabber(true);
        Bar.MouseLeave += (_, _) => AnimateGrabber(false);
    }

    private const double PullThreshold = 6;

    private Point? _pressPoint;
    private bool _pulling;

    public EdgeBarController Controller { get; }

    /// <summary>The user started dragging the notch downwards.</summary>
    public event EventHandler? PullStarted;

    /// <summary>Distance (DIPs) dragged below the press point.</summary>
    public event EventHandler<double>? PullMoved;

    /// <summary>Drag released with its final distance.</summary>
    public event EventHandler<double>? PullEnded;

    /// <summary>The notch was clicked without dragging.</summary>
    public event EventHandler? Clicked;

    /// <summary>A status icon or the tray chevron was clicked. Anchor is the bottom-centre of the icon, in DIPs.</summary>
    public event EventHandler<(NotchQuickKind Kind, Point Anchor)>? QuickRequested;

    /// <summary>Current on-screen size of the flared bar, used to morph into Control Centre.</summary>
    public Size GetBarSize() => new(Bar.ActualWidth, Bar.ActualHeight);

    /// <summary>Hide the homescreen notch so Control Centre can take its place.</summary>
    public void Cover()
    {
        Opacity = 0;
        IsHitTestVisible = false;
    }

    /// <summary>Bring the notch back after Control Centre has finished closing.</summary>
    public void Uncover()
    {
        Opacity = 1;
        IsHitTestVisible = true;
        AnimateGrabber(Bar.IsMouseOver);
    }

    private void OnBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(this);
        _pulling = false;
        Bar.CaptureMouse();
        e.Handled = true;
    }

    private void OnBarMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressPoint is not Point press) return;

        double distance = e.GetPosition(this).Y - press.Y;
        if (!_pulling && distance > PullThreshold)
        {
            _pulling = true;
            PullStarted?.Invoke(this, EventArgs.Empty);
        }

        if (_pulling) PullMoved?.Invoke(this, Math.Max(0, distance));
    }

    private void OnBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressPoint is not Point press) return;

        double distance = e.GetPosition(this).Y - press.Y;
        bool pulling = _pulling;
        _pressPoint = null;
        _pulling = false;
        Bar.ReleaseMouseCapture();

        if (pulling) PullEnded?.Invoke(this, Math.Max(0, distance));
        else Clicked?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnBarLostCapture(object sender, MouseEventArgs e)
    {
        if (_pressPoint is null) return;

        bool pulling = _pulling;
        _pressPoint = null;
        _pulling = false;
        if (pulling) PullEnded?.Invoke(this, 0);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Keep the notch out of Alt+Tab and stop it from stealing focus when clicked.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

        PositionAtTopCenter();
    }

    /// <summary>Screen area (physical pixels) the notch occupies when fully shown.</summary>
    public NativeMethods.RECT GetBarZone()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double left = Left + (ActualWidth - Bar.ActualWidth) / 2;
        return new NativeMethods.RECT(
            (int)(left * dpi.DpiScaleX),
            (int)(Top * dpi.DpiScaleY),
            (int)((left + Bar.ActualWidth) * dpi.DpiScaleX),
            (int)((Top + Bar.ActualHeight) * dpi.DpiScaleY));
    }

    private void OnWifiClick(object sender, RoutedEventArgs e) => RaiseQuick(NotchQuickKind.WiFi, sender);
    private void OnEthernetClick(object sender, RoutedEventArgs e) => RaiseQuick(NotchQuickKind.Ethernet, sender);
    private void OnBluetoothClick(object sender, RoutedEventArgs e) => RaiseQuick(NotchQuickKind.Bluetooth, sender);
    private void OnSoundClick(object sender, RoutedEventArgs e) => RaiseQuick(NotchQuickKind.Sound, sender);
    private void OnBatteryClick(object sender, RoutedEventArgs e) => RaiseQuick(NotchQuickKind.Battery, sender);
    private void OnTrayClick(object sender, RoutedEventArgs e) => RaiseQuick(NotchQuickKind.Tray, sender);

    private void RaiseQuick(NotchQuickKind kind, object sender)
    {
        if (sender is not FrameworkElement element) return;
        PresentationSource? source = PresentationSource.FromVisual(element);
        if (source?.CompositionTarget is null) return;

        Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
        Point anchor = fromDevice.Transform(element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight)));
        QuickRequested?.Invoke(this, (kind, anchor));
    }

    private void OnBarRightClick(object sender, MouseButtonEventArgs e)
    {
        var app = (App)Application.Current;
        var menu = MenuFactory.Create(
            ("Sol settings…", app.ShowSettings),
            ("Hide notch", () => app.ModifySettings(s => s.NotchEnabled = false)),
            (null, null),
            ("Quit Sol Desktop", app.Shutdown));
        Controller.KeepOpen = true;
        MenuFactory.Open(menu, Bar, PlacementMode.MousePoint, () => Controller.KeepOpen = false);
        e.Handled = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(PositionAtTopCenter);

    private void AnimateGrabber(bool shown)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Grabber.BeginAnimation(OpacityProperty, new DoubleAnimation(shown ? 1 : 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        Grabber.BeginAnimation(WidthProperty, new DoubleAnimation(shown ? 42 : 10, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private void PositionAtTopCenter()
    {
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top = 0;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }
}
