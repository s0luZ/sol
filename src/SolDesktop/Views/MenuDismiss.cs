using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using static SolDesktop.Interop.NativeMethods;

namespace SolDesktop.Views;

/// <summary>
/// The notch and dock never take focus, so a WPF context menu never sees "click elsewhere"
/// and stays open. A low-level mouse hook closes it on any press outside the menu.
/// </summary>
internal static class MenuDismiss
{
    private static readonly LowLevelMouseProc HookProc = OnMouse;
    private static ContextMenu? _menu;
    private static IntPtr _hook;

    public static void Attach(ContextMenu menu)
    {
        Detach();
        _menu = menu;
        menu.Closed += OnClosed;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, HookProc, GetModuleHandle(null), 0);
    }

    public static void Detach()
    {
        if (_menu is not null) _menu.Closed -= OnClosed;
        _menu = null;
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private static void OnClosed(object? sender, RoutedEventArgs e) => Detach();

    private static IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _menu is { IsOpen: true } menu)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (!IsOverMenu(menu, info.pt))
                {
                    menu.Dispatcher.BeginInvoke(() => menu.IsOpen = false, DispatcherPriority.Send);
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsOverMenu(ContextMenu menu, POINT screen)
    {
        if (PresentationSource.FromVisual(menu) is HwndSource source)
        {
            IntPtr hit = WindowFromPoint(screen);
            IntPtr root = GetAncestor(hit, GA_ROOT);
            if (root == source.Handle || hit == source.Handle) return true;
        }

        try
        {
            Point local = menu.PointFromScreen(new Point(screen.X, screen.Y));
            return local.X >= 0 && local.Y >= 0 && local.X <= menu.RenderSize.Width && local.Y <= menu.RenderSize.Height;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
