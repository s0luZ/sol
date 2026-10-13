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
/// The pull-down panel. It morphs out of the notch — same flared bar, growing from notch
/// size into the full Control Centre — then snaps open or closed.
/// </summary>
public partial class ControlCentreWindow : Window
{
    public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(
        nameof(Reveal), typeof(double), typeof(ControlCentreWindow),
        new PropertyMetadata(0.0, (d, _) => ((ControlCentreWindow)d).ApplyReveal()));

    private static readonly Duration OpenDuration = TimeSpan.FromMilliseconds(340);
    private static readonly Duration CloseDuration = TimeSpan.FromMilliseconds(240);
    private const double PullDistance = 220;
    private const double SheetWidth = 356;

    private readonly ControlCentreViewModel _vm;
    private bool _open;
    private Size _from = new(270, 36);
    private double _fullSheetHeight = 400;

    public ControlCentreWindow(ControlCentreViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Slide.Y = 0;

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

    public double Reveal
    {
        get => (double)GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    public bool IsOpen => _open;

    /// <summary>The panel finished closing (including the morph-away animation).</summary>
    public event EventHandler? PanelClosed;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
        PositionAtTopCenter();
    }

    /// <summary>The notch started being dragged down.</summary>
    public void BeginPull(Size from)
    {
        if (_open) return;
        Prepare(from);
    }

    public void UpdatePull(double distance)
    {
        if (_open || !IsVisible) return;
        Reveal = Math.Clamp(distance / PullDistance, 0, 1);
    }

    public void EndPull()
    {
        if (_open || !IsVisible) return;
        if (Reveal > 0.28) OpenPanel();
        else ClosePanel();
    }

    public void Toggle(Size from)
    {
        if (_open)
        {
            ClosePanel();
            return;
        }

        Prepare(from);
        OpenPanel();
    }

    public void ClosePanel()
    {
        if (!IsVisible) return;
        _open = false;
        _fullSheetHeight = Math.Max(Sheet.ActualHeight, _fullSheetHeight);
        AnimateReveal(0, CloseDuration, EasingMode.EaseIn, () =>
        {
            if (_open) return;
            Hide();
            _vm.OnClosed();
            PanelClosed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Prepare(Size from)
    {
        _from = from.Width > 1 && from.Height > 1 ? from : new Size(270, 36);
        BeginAnimation(RevealProperty, null);

        if (IsVisible)
        {
            return;
        }

        _vm.OnOpened();
        Sheet.MaxHeight = double.PositiveInfinity;
        Sheet.Width = SheetWidth;
        Show();
        UpdateLayout();
        _fullSheetHeight = Math.Max(Sheet.DesiredSize.Height, Sheet.ActualHeight);
        PositionAtTopCenter();
        Reveal = 0;
        ApplyReveal();
        UpdateLayout();
        PositionAtTopCenter();
    }

    private void OpenPanel()
    {
        _open = true;
        AnimateReveal(1, OpenDuration, EasingMode.EaseOut, null);
        Activate();
    }

    private void AnimateReveal(double to, Duration duration, EasingMode easing, Action? completed)
    {
        var animation = new DoubleAnimation(Reveal, to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = easing },
        };
        if (completed is not null) animation.Completed += (_, _) => completed();
        BeginAnimation(RevealProperty, animation);
    }

    private void ApplyReveal()
    {
        if (Sheet is null) return;

        double t = Math.Clamp(Reveal, 0, 1);
        double fromWidth = Math.Max(180, _from.Width - 28);
        double fromHeight = Math.Max(28, _from.Height - 4);

        if (t >= 0.999)
        {
            Sheet.Width = SheetWidth;
            Sheet.MaxHeight = double.PositiveInfinity;
            return;
        }

        Sheet.Width = fromWidth + (SheetWidth - fromWidth) * t;
        Sheet.MaxHeight = fromHeight + (Math.Max(_fullSheetHeight, fromHeight) - fromHeight) * t;
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
