using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
