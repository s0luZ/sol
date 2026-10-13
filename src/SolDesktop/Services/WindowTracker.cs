using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using SolDesktop.Interop;
using static SolDesktop.Interop.NativeMethods;

namespace SolDesktop.Services;

public sealed record TrackedWindow(
    IntPtr Handle,
    string Title,
    int ProcessId,
    string? ExePath,
    string? AppId,
    RECT Bounds,
    bool IsMinimized);

/// <summary>
/// Keeps a live list of taskbar-style app windows, plus whether the foreground app is fullscreen.
/// Driven by WinEvent hooks (throttled) with a slow poll as a safety net.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private static readonly (uint Min, uint Max)[] HookRanges =
    {
        (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND),
        (EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND),
        (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
        (EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE),
        (EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_NAMECHANGE),
        (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
    };

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    private readonly WinEventDelegate _hookProc;
    private readonly List<IntPtr> _hooks = new();
    private readonly DispatcherTimer _throttle;
    private readonly DispatcherTimer _poll;
    private readonly Dictionary<int, string?> _exeByPid = new();
    private readonly Dictionary<IntPtr, string?> _appIdByWindow = new();
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly StringBuilder _text = new(512);

    public IReadOnlyList<TrackedWindow> Windows { get; private set; } = Array.Empty<TrackedWindow>();
    public IntPtr ForegroundWindow { get; private set; }
    public bool IsForegroundFullscreen { get; private set; }

    public event EventHandler? Changed;

    public WindowTracker(Dispatcher dispatcher)
    {
        _throttle = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
        _throttle.Tick += (_, _) =>
        {
            _throttle.Stop();
            Refresh();
        };
        _poll = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);

        _hookProc = OnWinEvent;
        foreach (var (min, max) in HookRanges)
        {
            IntPtr hook = SetWinEventHook(min, max, IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (hook != IntPtr.Zero) _hooks.Add(hook);
        }

        Refresh();
    }

    public bool AnyWindowIntersects(RECT zone) => Windows.Any(w => !w.IsMinimized && w.Bounds.IntersectsWith(zone));

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0) return;

        // Child-window churn is constant and irrelevant; destroyed windows can no longer be queried.
        bool needsTopLevel = eventType is EVENT_OBJECT_CREATE or EVENT_OBJECT_SHOW or EVENT_OBJECT_LOCATIONCHANGE or EVENT_OBJECT_NAMECHANGE;
        if (needsTopLevel && GetAncestor(hwnd, GA_ROOT) != hwnd) return;

        if (!_throttle.IsEnabled) _throttle.Start();
    }

    public void Refresh()
    {
        var windows = new List<TrackedWindow>();
        var livePids = new HashSet<int>();
        var liveHandles = new HashSet<IntPtr>();

        EnumWindows((hwnd, _) =>
        {
            TrackedWindow? window = TryDescribe(hwnd);
            if (window is not null)
            {
                windows.Add(window);
                livePids.Add(window.ProcessId);
                liveHandles.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        foreach (int pid in _exeByPid.Keys.Where(p => !livePids.Contains(p)).ToList()) _exeByPid.Remove(pid);
        foreach (IntPtr h in _appIdByWindow.Keys.Where(h => !liveHandles.Contains(h)).ToList()) _appIdByWindow.Remove(h);

        IntPtr foreground = GetForegroundWindow();
        bool fullscreen = IsFullscreen(foreground);

        if (foreground == ForegroundWindow && fullscreen == IsForegroundFullscreen && windows.SequenceEqual(Windows)) return;

        Windows = windows;
        ForegroundWindow = foreground;
        IsForegroundFullscreen = fullscreen;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private TrackedWindow? TryDescribe(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd)) return null;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownProcessId) return null;

        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        bool isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
        if (!isAppWindow)
        {
            if ((exStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return null;
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return null;
        }

        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return null;

        string title = GetText(hwnd);
        if (title.Length == 0) return null;
        if (IgnoredClasses.Contains(GetClass(hwnd))) return null;

        string? exePath = GetExePath((int)pid);
        if (!_appIdByWindow.TryGetValue(hwnd, out string? appId))
        {
            appId = ShellProperties.GetWindowAppId(hwnd);
            _appIdByWindow[hwnd] = appId;
        }

        // Store apps run inside ApplicationFrameHost; the real process owns the hosted CoreWindow.
        if (exePath is not null && exePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            IntPtr core = FindWindowEx(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
            if (core != IntPtr.Zero)
            {
                GetWindowThreadProcessId(core, out uint corePid);
                exePath = GetExePath((int)corePid) ?? exePath;
            }
        }

        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) != 0)
        {
            GetWindowRect(hwnd, out bounds);
        }

        return new TrackedWindow(hwnd, title, (int)pid, exePath, appId, bounds, IsIconic(hwnd));
    }

    private bool IsFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || IsZoomed(hwnd)) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownProcessId || IgnoredClasses.Contains(GetClass(hwnd))) return false;

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0) return false;
        if (!GetWindowRect(hwnd, out RECT r)) return false;

        RECT m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private string GetText(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length == 0) return "";
        _text.Clear();
        _text.EnsureCapacity(length + 1);
        GetWindowText(hwnd, _text, _text.Capacity);
        return _text.ToString();
    }

    private string GetClass(IntPtr hwnd)
    {
        _text.Clear();
        GetClassName(hwnd, _text, 256);
        return _text.ToString();
    }

    private string? GetExePath(int pid)
    {
        if (_exeByPid.TryGetValue(pid, out string? cached)) return cached;

        string? path = null;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (QueryFullProcessImageName(process, 0, buffer, ref size)) path = buffer.ToString();
            }
            finally
            {
                CloseHandle(process);
            }
        }

        _exeByPid[pid] = path;
        return path;
    }

    public void Dispose()
    {
        _throttle.Stop();
        _poll.Stop();
        foreach (IntPtr hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
    }
}
