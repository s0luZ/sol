using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using SolDesktop.Interop;
using SolDesktop.ViewModels;

namespace SolDesktop.Views;

/// <summary>
/// The pull-down panel. It follows the pointer while the notch is dragged, then snaps open
/// or closed; it takes focus while open (for the Wi-Fi password box) and closes on deactivation.
/// </summary>
public partial class ControlCentreWindow : Window
{
    private static readonly Duration OpenDuration = TimeSpan.FromMilliseconds(300);
    private static readonly Duration CloseDuration = TimeSpan.FromMilliseconds(200);

    private readonly ControlCentreViewModel _vm;
    private bool _open;

    public ControlCentreWindow(ControlCentreViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Slide.Y = -4000;

        SizeChanged += (_, _) => PositionAtTopCenter();
        Deactivated += (_, _) =>
        {
            if (_open) ClosePanel();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) ClosePanel();
        };
        _vm.CloseRequested += (_, _) => ClosePanel();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public bool IsOpen => _open;

    /// <summary>The panel finished closing (including the slide-away animation).</summary>
    public event EventHandler? PanelClosed;

    private double HiddenOffset => -(Bar.ActualHeight + 30);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
        PositionAtTopCenter();
    }

    /// <summary>The notch started being dragged down.</summary>
    public void BeginPull()
    {
        if (_open) return;
        Prepare();
    }

    public void UpdatePull(double distance)
    {
        if (_open || !IsVisible) return;
        Slide.Y = Math.Min(0, HiddenOffset + distance);
    }

    public void EndPull()
    {
        if (_open || !IsVisible) return;
        if (Slide.Y > HiddenOffset * 0.7) OpenPanel();
        else ClosePanel();
    }

    public void Toggle()
    {
        if (_open)
        {
            ClosePanel();
            return;
        }

        Prepare();
        OpenPanel();
    }

    public void ClosePanel()
    {
        if (!IsVisible) return;
        _open = false;
        Animate(HiddenOffset, CloseDuration, EasingMode.EaseIn, () =>
        {
            if (_open) return;
            Hide();
            _vm.OnClosed();
            PanelClosed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Prepare()
    {
        double current = Slide.Y;
        Slide.BeginAnimation(TranslateTransform.YProperty, null);

        if (IsVisible)
        {
            // Caught mid-close: carry on from where the animation was.
            Slide.Y = current;
            return;
        }

        _vm.OnOpened();
        Slide.Y = -4000;
        Show();
        UpdateLayout();
        PositionAtTopCenter();
        Slide.Y = HiddenOffset;
    }

    private void OpenPanel()
    {
        _open = true;
        Animate(0, OpenDuration, EasingMode.EaseOut, null);
        Activate();
    }

    private void Animate(double to, Duration duration, EasingMode easing, Action? completed)
    {
        double from = Slide.Y;
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = easing },
        };
        if (completed is not null) animation.Completed += (_, _) => completed();
        Slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    // ---- level sliders: drag anywhere on the bar, like iOS ----

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

    // ---- Wi-Fi password ----

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

    private void OnPasswordVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is PasswordBox box)
        {
            Dispatcher.BeginInvoke(() => box.Focus(), DispatcherPriority.Input);
        }
    }

    private void Join(WifiNetworkItem item, PasswordBox box)
    {
        string password = box.Password;
        if (password.Length == 0) return;
        box.Clear();
        _ = _vm.ConnectAsync(item, password);
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
