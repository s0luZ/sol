using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SolDesktop.Interop;
using SolDesktop.Services;
using SolDesktop.ViewModels;

namespace SolDesktop.Views;

public sealed class FolderAppItem
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public ImageSource? Icon { get; set; }
}

public partial class FolderFlyoutWindow : Window
{
    private readonly DockViewModel _dock;
    private readonly IconService _icons;
    private DockItemViewModel _folder;

    public FolderFlyoutWindow(DockViewModel dock, IconService icons)
    {
        InitializeComponent();
        _dock = dock;
        _icons = icons;
        _folder = new DockItemViewModel("folder:pending", DockItemKind.Folder);
        Apps.ItemsSource = Items;
        Deactivated += (_, _) => HideFlyout();
    }

    public ObservableCollection<FolderAppItem> Items { get; } = new();

    public bool IsShowing => IsVisible;

    public event EventHandler? ClosedFlyout;

    public async void ShowFor(DockItemViewModel folder)
    {
        _folder = folder;
        TitleText.Text = folder.Name;
        await Reload();

        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Left = 0;
        Top = 0;
        if (!IsVisible) Show();
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
        NativeMethods.AddExtendedStyle(new WindowInteropHelper(this).Handle, NativeMethods.WS_EX_TOOLWINDOW);
    }

    private async System.Threading.Tasks.Task Reload()
    {
        Items.Clear();
        foreach (var (path, name) in _dock.ListFolderApps(_folder))
        {
            Items.Add(new FolderAppItem
            {
                Path = path,
                Name = name,
                Icon = await _icons.GetAsync(path),
            });
        }

        EmptyHint.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Apps.Visibility = Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || Panel.IsMouseOver) return;
        HideFlyout();
    }

    private void OnPanelClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

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
        if (sender is not FrameworkElement { DataContext: FolderAppItem item }) return;
        WindowActions.Launch(item.Path);
        e.Handled = true;
    }

    private void OnAppRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderAppItem item } element) return;
        e.Handled = true;
        var menu = MenuFactory.Create(
            (item.Name, null),
            (null, null),
            ("Open", () => WindowActions.Launch(item.Path)),
            ("Remove from folder", () =>
            {
                _dock.RemoveFromFolder(_folder, item.Path);
                _ = Reload();
            }));
        MenuFactory.Open(menu, element, PlacementMode.MousePoint);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _dock.AddPathsToFolder(_folder, files);
            _ = Reload();
        }

        e.Handled = true;
    }
}
