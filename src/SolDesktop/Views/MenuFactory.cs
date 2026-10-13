using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SolDesktop.Views;

internal static class MenuFactory
{
    /// <summary>Builds a context menu; a null header inserts a separator.</summary>
    public static ContextMenu Create(params (string? Header, Action? Action)[] items)
    {
        var menu = new ContextMenu();
        foreach (var (header, action) in items)
        {
            if (header is null)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem { Header = header };
            if (action is not null) item.Click += (_, _) => action();
            else item.IsEnabled = false;
            menu.Items.Add(item);
        }

        return menu;
    }

    public static void Open(ContextMenu menu, UIElement target, PlacementMode placement, Action? closed = null)
    {
        menu.StaysOpen = false;
        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.Closed += OnClosed;
        menu.Opened += OnOpened;
        menu.IsOpen = true;

        void OnOpened(object sender, RoutedEventArgs e) => MenuDismiss.Attach(menu);

        void OnClosed(object sender, RoutedEventArgs e)
        {
            menu.Opened -= OnOpened;
            menu.Closed -= OnClosed;
            MenuDismiss.Detach();
            closed?.Invoke();
        }
    }
}
