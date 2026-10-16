using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;
using static SolDesktop.Interop.NativeMethods;

namespace SolDesktop.Services;

/// <summary>
/// Hides or auto-hides the Windows taskbar while the dock is in charge, and always puts it back.
/// The original state is persisted so a crash can be undone on the next launch.
/// </summary>
public sealed class TaskbarService
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string AlignmentValue = "TaskbarAl";

    private static readonly string[] StartHosts =
    {
        "StartMenuExperienceHost.exe",
        "SearchHost.exe",
        "PhoneExperienceHost.exe",
        "CrossDeviceResume.exe",
    };

    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _startPoll;
    private readonly WinEventDelegate _showHookProc;
    private readonly WinEventDelegate _foregroundHookProc;
    private readonly LowLevelMouseProc _keyboardProc;
    private IntPtr _showHook;
    private IntPtr _foregroundHook;
    private IntPtr _keyboardHook;
    private (TaskbarMode Mode, bool CenterStart)? _applied;
    private int _centerGeneration;
    private volatile bool _startVisible;
    private volatile bool _winHeld;
    private bool _sawStartWindow;
    private int _goneTicks;
    private int _winUpTicks;

    public TaskbarService(SettingsService settings)
    {
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _showHookProc = OnWindowShown;
        _foregroundHookProc = OnForegroundChanged;
        _keyboardProc = OnKeyboard;
        _startPoll = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
        _startPoll.Tick += (_, _) => PollStart();
    }

    /// <summary>True while the Start menu (or Search/Phone flyout) is on screen.</summary>
    public event EventHandler<bool>? StartVisibilityChanged;

    /// <summary>Fires while the Win key or Start menu is holding the dock open, so it can stay above a game.</summary>
    public event EventHandler? HoldPulse;

    public bool IsStartMenuVisible() => StartIsVisible();

    public void Apply(TaskbarMode mode, bool centerStart)
    {
        if (_applied == (mode, centerStart)) return;

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
        // Hide the taskbar as soon as Explorer flashes it (Start, flyouts). Watch the Win key
        // and Start itself so the dock can rise into the gap under the menu.
        SetShowHook(true);
        SetKeyboardHook(true);
        _startPoll.Start();

        // TaskbarAl helps when the bar is only auto-hidden. A fully hidden bar ignores it, so
        // CenterStartWindows() also moves the Start window after it appears.
        if (centerStart) SetTaskbarAlignmentCentered();
        else RestoreStartAlignment();

        _applied = (mode, centerStart);
    }

    /// <summary>Explorer re-shows the taskbar on some events (Start menu, explorer restart).</summary>
    public void Enforce()
    {
        if (_applied?.Mode == TaskbarMode.Hide) SetTaskbarsVisible(false);
    }

    public void Restore()
    {
        _applied = (TaskbarMode.Leave, false);
        Interlocked.Increment(ref _centerGeneration);
        _startPoll.Stop();
        SetKeyboardHook(false);
        SetShowHook(false);
        PublishStart(false);
        RestoreStartAlignment();

        AppSettings s = _settings.Current;
        if (!s.TaskbarModifiedBySol) return;

        SetTaskbarsVisible(true);
        SetAutoHide(s.OriginalTaskbarAutoHide);
        s.TaskbarModifiedBySol = false;
        _settings.Save();
    }

    /// <summary>
    /// Explorer re-shows the taskbar whenever Start or a flyout opens; hide it the moment it appears
    /// instead of waiting for the next window change.
    /// </summary>
    private void SetShowHook(bool enabled)
    {
        if (enabled && _showHook == IntPtr.Zero)
        {
            _showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _showHookProc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundHookProc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }
        else if (!enabled && _showHook != IntPtr.Zero)
        {
            UnhookWinEvent(_showHook);
            _showHook = IntPtr.Zero;
            if (_foregroundHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
        }
    }

    private void SetKeyboardHook(bool enabled)
    {
        if (enabled && _keyboardHook == IntPtr.Zero)
        {
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
        }
        else if (!enabled && _keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            _winHeld = false;
        }
    }

    private IntPtr OnKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode is VK_LWIN or VK_RWIN)
            {
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    _winHeld = true;
                    _goneTicks = 0;
                    PublishStart(true);
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    _winHeld = false;
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void PollStart()
    {
        // Games (Valorant included) often swallow the Win key before the low-level hook.
        // The async key state still updates, so the dock can come up over fullscreen.
        if (WinIsDown())
        {
            _winHeld = true;
            _winUpTicks = 0;
        }
        else if (_winHeld && ++_winUpTicks >= 3)
        {
            _winHeld = false;
        }

        bool startOpen = StartIsVisible();
        if (startOpen) _sawStartWindow = true;

        if (startOpen || _winHeld)
        {
            _goneTicks = 0;
            PublishStart(true);
            HoldPulse?.Invoke(this, EventArgs.Empty);
            return;
        }

        _goneTicks++;
        int limit = _sawStartWindow ? 4 : 18;
        if (_goneTicks >= limit)
        {
            _sawStartWindow = false;
            PublishStart(false);
        }
        else if (_startVisible)
        {
            HoldPulse?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool WinIsDown() =>
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

    private void PublishStart(bool visible)
    {
        if (_startVisible == visible) return;
        _startVisible = visible;
        StartVisibilityChanged?.Invoke(this, visible);
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0) return;
        if (IsStartHost(hwnd)) ScheduleCenterStart();
    }

    private void OnWindowShown(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_applied is null) return;

        var className = new StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        string name = className.ToString();

        if (_applied.Value.Mode == TaskbarMode.Hide && name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            ShowWindow(hwnd, SW_HIDE);
        }

        if (idObject != 0 || idChild != 0) return;
        if (IsStartHost(hwnd)) ScheduleCenterStart();
    }

    private bool StartIsVisible()
    {
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            if (IsStartHost(hwnd) && TryGetVisibleBounds(hwnd, out RECT bounds) &&
                bounds.Right - bounds.Left >= 80 && bounds.Bottom - bounds.Top >= 80)
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// Explorer places Start at the left of a hidden taskbar. Move every Start/Search flyout
    /// together so a two-pane menu stays intact, retrying while the open animation finishes.
    /// </summary>
    private void ScheduleCenterStart()
    {
        int generation = Interlocked.Increment(ref _centerGeneration);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            // Explorer re-shows the taskbar and repositions Start during the open animation.
            // Keep correcting both until Start has been gone for a short stretch.
            int gone = 0;
            for (int i = 0; i < 200 && generation == Volatile.Read(ref _centerGeneration); i++)
            {
                if (_applied?.Mode == TaskbarMode.Hide) SetTaskbarsVisible(false);
                bool startOpen = StartIsVisible();
                if (startOpen && _applied?.CenterStart == true) CenterStartWindows();
                gone = startOpen ? 0 : gone + 1;
                if (gone > 8 && i > 8) break;
                Thread.Sleep(40);
            }
        });
    }

    private void CenterStartWindows()
    {
        var windows = new List<(IntPtr Hwnd, RECT Bounds)>();
        EnumWindows((hwnd, _) =>
        {
            if (IsStartHost(hwnd) && TryGetVisibleBounds(hwnd, out RECT bounds) &&
                bounds.Right - bounds.Left >= 80 && bounds.Bottom - bounds.Top >= 80)
            {
                windows.Add((hwnd, bounds));
            }

            return true;
        }, IntPtr.Zero);

        if (windows.Count == 0) return;

        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var (_, bounds) in windows)
        {
            left = Math.Min(left, bounds.Left);
            top = Math.Min(top, bounds.Top);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }

        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);
        int dx = (screenW - (right - left)) / 2 - left;
        int dy = (screenH - (bottom - top)) / 2 - top;
        if (dx == 0 && dy == 0) return;

        foreach (var (hwnd, bounds) in windows)
        {
            SetWindowPos(hwnd, IntPtr.Zero, bounds.Left + dx, bounds.Top + dy, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    private static bool IsStartHost(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return false;

        try
        {
            var buffer = new StringBuilder(260);
            int size = buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size)) return false;
            string file = Path.GetFileName(buffer.ToString());
            foreach (string host in StartHosts)
            {
                if (file.Equals(host, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool TryGetVisibleBounds(IntPtr hwnd, out RECT bounds)
    {
        bounds = default;
        if (!IsWindowVisible(hwnd)) return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf<RECT>()) != 0)
        {
            if (!GetWindowRect(hwnd, out bounds)) return false;
        }

        return true;
    }

    private void SetTaskbarAlignmentCentered()
    {
        AppSettings s = _settings.Current;
        if (!s.StartAlignmentModifiedBySol)
        {
            s.OriginalTaskbarAlignment = ReadAlignment();
            s.StartAlignmentModifiedBySol = true;
            _settings.Save();
        }

        if (ReadAlignment() != 1) WriteAlignment(1);
    }

    private void RestoreStartAlignment()
    {
        AppSettings s = _settings.Current;
        if (!s.StartAlignmentModifiedBySol) return;

        if (ReadAlignment() != s.OriginalTaskbarAlignment) WriteAlignment(s.OriginalTaskbarAlignment);
        s.StartAlignmentModifiedBySol = false;
        _settings.Save();
    }

    private static int? ReadAlignment()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
            return key?.GetValue(AlignmentValue) is int value ? value : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    private static void WriteAlignment(int? value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AdvancedKey);
            if (value is int v) key.SetValue(AlignmentValue, v, RegistryValueKind.DWord);
            else key.DeleteValue(AlignmentValue, false);

            // Explorer re-reads taskbar settings when this broadcast arrives.
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "TraySettings",
                SMTO_ABORTIFHUNG, 1000, out _);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
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
