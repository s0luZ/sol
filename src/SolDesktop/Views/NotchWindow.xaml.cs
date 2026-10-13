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

    public EdgeBarController Controller { get; }

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
        menu.PlacementTarget = Bar;
        menu.Placement = PlacementMode.MousePoint;
        Controller.KeepOpen = true;
        menu.Closed += (_, _) => Controller.KeepOpen = false;
        menu.IsOpen = true;
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
