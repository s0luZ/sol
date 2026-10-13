using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static SolDesktop.Interop.NativeMethods;

namespace SolDesktop.Services;

/// <summary>
/// Hides or auto-hides the Windows taskbar while the dock is in charge, and always puts it back.
/// The original state is persisted so a crash can be undone on the next launch.
/// </summary>
public sealed class TaskbarService
{
    private readonly SettingsService _settings;
    private TaskbarMode? _applied;

    public TaskbarService(SettingsService settings)
    {
        _settings = settings;
    }

    public void Apply(TaskbarMode mode)
    {
        if (_applied == mode) return;

        if (mode == TaskbarMode.Leave)
        {
            Restore();
            return;
        }

        AppSettings s = _settings.Current;
        if (!s.TaskbarModifiedBySol)
        {
            s.OriginalTaskbarAutoHide = IsAutoHideEnabled();
            s.TaskbarModifiedBySol = true;
            _settings.Save();
        }

        // Auto-hide frees the reserved work area so maximized windows reach the bottom edge.
        SetAutoHide(true);
        SetTaskbarsVisible(mode != TaskbarMode.Hide);
        _applied = mode;
    }

    /// <summary>Explorer re-shows the taskbar on some events (Start menu, explorer restart).</summary>
    public void Enforce()
    {
        if (_applied == TaskbarMode.Hide) SetTaskbarsVisible(false);
    }

    public void Restore()
    {
        _applied = TaskbarMode.Leave;
        AppSettings s = _settings.Current;
        if (!s.TaskbarModifiedBySol) return;

        SetTaskbarsVisible(true);
        SetAutoHide(s.OriginalTaskbarAutoHide);
        s.TaskbarModifiedBySol = false;
        _settings.Save();
    }

    private static IEnumerable<IntPtr> Taskbars()
    {
        IntPtr primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) yield return primary;

        IntPtr secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            yield return secondary;
        }
    }

    private static void SetTaskbarsVisible(bool visible)
    {
        foreach (IntPtr taskbar in Taskbars())
        {
            if (IsWindowVisible(taskbar) != visible) ShowWindow(taskbar, visible ? SW_SHOWNA : SW_HIDE);
        }
    }

    private static bool IsAutoHideEnabled()
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        return ((long)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
    }

    private static void SetAutoHide(bool enabled)
    {
        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = FindWindow("Shell_TrayWnd", null),
            lParam = new IntPtr(enabled ? ABS_AUTOHIDE : ABS_ALWAYSONTOP),
        };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }
}
