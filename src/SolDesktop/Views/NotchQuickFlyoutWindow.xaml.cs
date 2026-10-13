using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SolDesktop.Interop;
using SolDesktop.ViewModels;

namespace SolDesktop.Views;

public enum NotchQuickKind
{
    WiFi,
    Ethernet,
    Bluetooth,
    Sound,
    Battery,
    Tray,
}

public partial class NotchQuickFlyoutWindow : Window
{
    private readonly ControlCentreViewModel _vm;
    private double _anchorX;
    private double _anchorBottom;
    private bool _suppressDeactivate;

    public NotchQuickFlyoutWindow(ControlCentreViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        SizeChanged += (_, _) => Reposition();
        Deactivated += (_, _) =>
        {
            if (!_suppressDeactivate) HideFlyout();
        };
        _vm.CloseRequested += (_, _) => HideFlyout();
    }

    public event EventHandler? ClosedFlyout;

    public void ShowFor(NotchQuickKind kind, NotchViewModel status, double anchorX, double anchorBottom)
    {
        _anchorX = anchorX;
        _anchorBottom = anchorBottom;
        ApplyKind(kind, status);
        _vm.OnOpened();
        if (!IsVisible) Show();
        Reposition();
        _suppressDeactivate = true;
        Activate();
        Dispatcher.BeginInvoke(() => _suppressDeactivate = false);
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        Hide();
        _vm.OnClosed();
        ClosedFlyout?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.UseRoundedCorners(hwnd);
    }

    private void ApplyKind(NotchQuickKind kind, NotchViewModel status)
    {
        SoundPanel.Visibility = kind == NotchQuickKind.Sound ? Visibility.Visible : Visibility.Collapsed;
        WifiPanel.Visibility = kind == NotchQuickKind.WiFi ? Visibility.Visible : Visibility.Collapsed;
        BluetoothPanel.Visibility = kind == NotchQuickKind.Bluetooth ? Visibility.Visible : Visibility.Collapsed;
        EthernetPanel.Visibility = kind == NotchQuickKind.Ethernet ? Visibility.Visible : Visibility.Collapsed;
        BatteryPanel.Visibility = kind == NotchQuickKind.Battery ? Visibility.Visible : Visibility.Collapsed;

        TitleText.Text = kind switch
        {
            NotchQuickKind.Sound => "Sound",
            NotchQuickKind.WiFi => "Wi-Fi",
            NotchQuickKind.Bluetooth => "Bluetooth",
            NotchQuickKind.Ethernet => "Ethernet",
            NotchQuickKind.Battery => "Battery",
            _ => "",
        };

        if (kind == NotchQuickKind.Sound) _vm.OpenPage(ControlCentrePage.Sound);
        else if (kind == NotchQuickKind.WiFi) _vm.OpenPage(ControlCentrePage.WiFi);
        else if (kind == NotchQuickKind.Bluetooth) _vm.OpenPage(ControlCentrePage.Bluetooth);
        else if (kind == NotchQuickKind.Ethernet) _vm.OpenPage(ControlCentrePage.Ethernet);

        BatteryPercentText.Text = status.BatteryPercent + "%";
        BatteryDetailText.Text = status.BatteryTooltip;
    }

    private void Reposition()
    {
        if (ActualWidth <= 0) return;
        double left = _anchorX - ActualWidth / 2;
        left = Math.Clamp(left, 12, SystemParameters.PrimaryScreenWidth - ActualWidth - 12);
        Left = left;
        Top = Math.Min(_anchorBottom + 8, SystemParameters.PrimaryScreenHeight - ActualHeight - 12);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        HideFlyout();
        e.Handled = true;
    }

    private void OnLevelMouseDown(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        slider.CaptureMouse();
        SetLevelFromPointer(slider, e);
        e.Handled = true;
    }

    private void OnLevelMouseMove(object sender, MouseEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.IsMouseCaptured) SetLevelFromPointer(slider, e);
    }

    private void OnLevelMouseUp(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (!slider.IsMouseCaptured) return;
        slider.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnLevelWheel(object sender, MouseWheelEventArgs e)
    {
        var slider = (Slider)sender;
        slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta) * 5, slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private static void SetLevelFromPointer(Slider slider, MouseEventArgs e)
    {
        if (slider.ActualWidth <= 0) return;
        double fraction = Math.Clamp(e.GetPosition(slider).X / slider.ActualWidth, 0, 1);
        slider.Value = Math.Round(slider.Minimum + fraction * (slider.Maximum - slider.Minimum));
    }

    private void OnJoinClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PasswordBox box, DataContext: WifiNetworkItem item }) Join(item, box);
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not PasswordBox { DataContext: WifiNetworkItem item } box) return;
        Join(item, box);
        e.Handled = true;
    }

    private void Join(WifiNetworkItem item, PasswordBox box)
    {
        string password = box.Password;
        if (password.Length == 0) return;
        box.Clear();
        _ = _vm.ConnectAsync(item, password);
    }
}
