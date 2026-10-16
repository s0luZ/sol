using System;
using System.Diagnostics;
using System.Windows;
using static SolDesktop.Interop.NativeMethods;

namespace SolDesktop.Services;

internal static class WindowActions
{
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    public static void Activate(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        if (SetForegroundWindow(hwnd)) return;

        // Windows only lets the process that received the last input steal focus;
        // a synthetic Alt tap satisfies that rule.
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
    }

    public static void Minimize(IntPtr hwnd) => ShowWindow(hwnd, SW_MINIMIZE);

    public static void ToggleMaximize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, IsZoomed(hwnd) ? SW_RESTORE : SW_MAXIMIZE);
    }

    public static void Close(IntPtr hwnd) => PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Native show-desktop toggle. Does not synthesize the Windows key.</summary>
    public static void ShowDesktop()
    {
        try
        {
            Type? shell = Type.GetTypeFromProgID("Shell.Application");
            if (shell is null) return;
            dynamic app = Activator.CreateInstance(shell)!;
            app.ToggleDesktop();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    public static void Lock() => LockWorkStation();

    public static void Sleep() => SetSuspendState(false, false, false);

    public static void Restart() => RunShutdown("/r /t 0");

    public static void ShutDown() => RunShutdown("/s /t 0");

    public static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    private static void RunShutdown(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    public static void OpenStartMenu()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void Launch(string target)
    {
        try
        {
            if (target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }
}
