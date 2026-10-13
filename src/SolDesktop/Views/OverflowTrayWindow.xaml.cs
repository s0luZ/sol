using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SolDesktop.Interop;
using SolDesktop.Services;

namespace SolDesktop.Views;

public sealed class OverflowAppItem
{
    public required NotifyIconEntry Entry { get; init; }
    public string Name => Entry.Name;
    public ImageSource? Icon { get; set; }
}

public partial class OverflowTrayWindow : Window
{
    private readonly IconService _icons;
    private double _anchorCenterX;
    private double _anchorY;
    private bool _below;
    private bool _menuOpen;

    public OverflowTrayWindow(IconService icons)
    {
        InitializeComponent();
        _icons = icons;
        Apps.ItemsSource = Items;
        SizeChanged += (_, _) => Reposition();
        Deactivated += (_, _) =>
        {
            if (!_menuOpen) HideFlyout();
        };
    }

    public ObservableCollection<OverflowAppItem> Items { get; } = new();

    public event EventHandler? ClosedFlyout;

    public void ShowAbove(double anchorCenterX, double anchorTop) =>
        ShowAt(anchorCenterX, anchorTop, below: false);

    public void ShowBelow(double anchorCenterX, double anchorBottom) =>
        ShowAt(anchorCenterX, anchorBottom, below: true);

    public async void ShowAt(double anchorCenterX, double anchorY, bool below)
    {
        _anchorCenterX = anchorCenterX;
        _anchorY = anchorY;
        _below = below;
        await Reload();
        if (!IsVisible) Show();
        Reposition();
        Activate();
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        Hide();
        ClosedFlyout?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.UseRoundedCorners(hwnd);
    }

    private async System.Threading.Tasks.Task Reload()
    {
        Items.Clear();
        foreach (NotifyIconEntry entry in NotifyIconService.List())
        {
            Items.Add(new OverflowAppItem
            {
                Entry = entry,
                Icon = entry.ExePath is null ? null : await _icons.GetAsync(entry.ExePath),
            });
        }

        EmptyHint.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reposition()
    {
        if (ActualWidth <= 0) return;
        double left = _anchorCenterX - ActualWidth / 2;
        left = Math.Clamp(left, 12, SystemParameters.PrimaryScreenWidth - ActualWidth - 12);
        Left = left;
        Top = _below
            ? Math.Min(_anchorY + 8, SystemParameters.PrimaryScreenHeight - ActualHeight - 12)
            : Math.Max(12, _anchorY - ActualHeight - 10);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => HideFlyout();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideFlyout();
            e.Handled = true;
        }
    }

    private void OnAppClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverflowAppItem item }) return;
        NotifyIconService.LeftClick(item.Entry);
        HideFlyout();
        e.Handled = true;
    }

    private void OnAppRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverflowAppItem item } element) return;
        e.Handled = true;
        _menuOpen = true;
        MenuFactory.Open(MenuFactory.Create(
            (item.Name, null),
            (null, null),
            ("Open", () =>
            {
                NotifyIconService.LeftClick(item.Entry);
                HideFlyout();
            }),
            ($"Quit {item.Name}", () =>
            {
                NotifyIconService.Quit(item.Entry);
                _ = Reload();
            })),
            element,
            PlacementMode.Bottom,
            () => _menuOpen = false);
    }
}
