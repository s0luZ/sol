using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using SolDesktop.Interop;
using static SolDesktop.Interop.NativeMethods;

namespace SolDesktop.Services;

public sealed class NotifyIconEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ExePath { get; init; }
    public IntPtr CallbackWindow { get; init; }
    public uint CallbackMessage { get; init; }
    public uint Uid { get; init; }
}

/// <summary>
/// Lists Windows notification-area icons (Discord, NVIDIA, Cursor, …) so Sol can show them
/// in its own overflow flyout. Uses the Win11 NotifyIconSettings registry plus the classic
/// explorer toolbar when it is still present.
/// </summary>
public static class NotifyIconService
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    private static readonly HashSet<string> HiddenExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe",
        "SolDesktop.exe",
        "SecurityHealthSystray.exe",
        "SearchHost.exe",
        "StartMenuExperienceHost.exe",
    };

    public static IReadOnlyList<NotifyIconEntry> List()
    {
        var byExe = new Dictionary<string, NotifyIconEntry>(StringComparer.OrdinalIgnoreCase);

        // Explorer's live toolbars are the actual tray / overflow. Prefer those so we
        // don't dump every app that ever registered a notify icon.
        foreach (NotifyIconEntry entry in FromToolbars()) TryAdd(byExe, entry);
        if (byExe.Count > 0)
        {
            return byExe.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        HashSet<string> running = RunningExePaths();
        foreach (NotifyIconEntry entry in FromRegistry())
        {
            if (entry.ExePath is not null && running.Contains(entry.ExePath)) TryAdd(byExe, entry);
        }

        return byExe.Values
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void LeftClick(NotifyIconEntry entry)
    {
        if (PostClick(entry, WM_LBUTTONDOWN, WM_LBUTTONUP, NIN_SELECT)) return;
        ActivateOrLaunch(entry.ExePath);
    }

    public static void RightClick(NotifyIconEntry entry)
    {
        if (PostClick(entry, WM_RBUTTONDOWN, WM_RBUTTONUP, 0)) return;
        ActivateOrLaunch(entry.ExePath);
    }

    /// <summary>Ask the app to close, then end its process if it stays in the tray.</summary>
    public static void Quit(NotifyIconEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ExePath)) return;
        string file = Path.GetFileName(entry.ExePath);
        if (HiddenExes.Contains(file)) return;

        if (entry.CallbackWindow != IntPtr.Zero && IsWindow(entry.CallbackWindow))
        {
            PostMessage(entry.CallbackWindow, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;
            string? path = ExePathForPid(pid);
            if (path is not null && path.Equals(entry.ExePath, StringComparison.OrdinalIgnoreCase))
            {
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }

            return true;
        }, IntPtr.Zero);

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string? path = ExePathForPid((uint)process.Id);
                if (path is null || !path.Equals(entry.ExePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (!process.CloseMainWindow() || !process.WaitForExit(600))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool PostClick(NotifyIconEntry entry, uint down, uint up, uint extra)
    {
        if (entry.CallbackWindow == IntPtr.Zero || entry.CallbackMessage == 0 || !IsWindow(entry.CallbackWindow))
        {
            return false;
        }

        try
        {
            SetForegroundWindow(entry.CallbackWindow);
            IntPtr id = new(entry.Uid);
            PostMessage(entry.CallbackWindow, entry.CallbackMessage, id, new IntPtr(down));
            PostMessage(entry.CallbackWindow, entry.CallbackMessage, id, new IntPtr(up));
            if (extra != 0) PostMessage(entry.CallbackWindow, entry.CallbackMessage, id, new IntPtr(extra));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return false;
        }
    }

    private static void ActivateOrLaunch(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;

        bool activated = false;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;
            string? path = ExePathForPid(pid);
            if (path is null || !path.Equals(exePath, StringComparison.OrdinalIgnoreCase)) return true;
            WindowActions.Activate(hwnd);
            activated = true;
            return false;
        }, IntPtr.Zero);

        if (!activated) WindowActions.Launch(exePath);
    }

    private static void TryAdd(Dictionary<string, NotifyIconEntry> byExe, NotifyIconEntry entry)
    {
        if (entry.ExePath is null) return;
        string file = Path.GetFileName(entry.ExePath);
        if (HiddenExes.Contains(file)) return;

        if (byExe.TryGetValue(entry.ExePath, out NotifyIconEntry? existing))
        {
            if (existing.CallbackWindow == IntPtr.Zero && entry.CallbackWindow != IntPtr.Zero)
            {
                byExe[entry.ExePath] = entry;
            }

            return;
        }

        byExe[entry.ExePath] = entry;
    }

    private static List<NotifyIconEntry> FromRegistry()
    {
        var entries = new List<NotifyIconEntry>();
        RegistryKey? root;
        try
        {
            root = Registry.CurrentUser.OpenSubKey(SettingsKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return entries;
        }

        if (root is null) return entries;

        using (root)
        {
            foreach (string id in root.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? key = root.OpenSubKey(id);
                    string? exe = key?.GetValue("ExecutablePath") as string;
                    if (string.IsNullOrWhiteSpace(exe)) continue;

                    uint uid = 0;
                    if (key?.GetValue("UID") is int uidValue) uid = unchecked((uint)uidValue);

                    entries.Add(new NotifyIconEntry
                    {
                        Id = id,
                        Name = DisplayName(exe),
                        ExePath = exe,
                        Uid = uid,
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error(ex);
                }
            }
        }

        return entries;
    }

    private static IEnumerable<NotifyIconEntry> FromToolbars()
    {
        var entries = new List<NotifyIconEntry>();
        foreach (IntPtr toolbar in TrayToolbars()) entries.AddRange(ReadToolbar(toolbar));
        return entries;
    }

    private static IEnumerable<IntPtr> TrayToolbars()
    {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero)
        {
            IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            IntPtr pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
            IntPtr toolbar = FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null);
            if (toolbar != IntPtr.Zero) yield return toolbar;
        }

        IntPtr overflow = FindWindow("NotifyIconOverflowWindow", null);
        if (overflow != IntPtr.Zero)
        {
            IntPtr toolbar = FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null);
            if (toolbar != IntPtr.Zero) yield return toolbar;
        }
    }

    private static List<NotifyIconEntry> ReadToolbar(IntPtr toolbar)
    {
        var result = new List<NotifyIconEntry>();
        GetWindowThreadProcessId(toolbar, out uint pid);
        if (pid == 0) return result;

        IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return result;

        int buttonSize = Marshal.SizeOf<ToolbarButton>();
        IntPtr remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)buttonSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (remote == IntPtr.Zero)
        {
            CloseHandle(process);
            return result;
        }

        try
        {
            int count = SendMessage(toolbar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (count is < 0 or > 256) return result;

            var buffer = new byte[buttonSize];
            var extra = new byte[40];

            for (int i = 0; i < count; i++)
            {
                if (SendMessage(toolbar, TB_GETBUTTON, new IntPtr(i), remote) == IntPtr.Zero) continue;
                if (!ReadProcessMemory(process, remote, buffer, buttonSize, out int read) || read != buttonSize) continue;

                var button = MemoryMarshal.Read<ToolbarButton>(buffer);
                if (button.dwData == IntPtr.Zero) continue;
                if (!ReadProcessMemory(process, button.dwData, extra, extra.Length, out read) || read < 16) continue;

                IntPtr hwnd = IntPtr.Size == 8
                    ? (IntPtr)BitConverter.ToInt64(extra, 0)
                    : (IntPtr)BitConverter.ToInt32(extra, 0);
                int offset = IntPtr.Size;
                uint uid = BitConverter.ToUInt32(extra, offset);
                uint callback = BitConverter.ToUInt32(extra, offset + 4);
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) continue;

                GetWindowThreadProcessId(hwnd, out uint iconPid);
                string? exe = ExePathForPid(iconPid);
                if (exe is null) continue;

                result.Add(new NotifyIconEntry
                {
                    Id = hwnd.ToInt64().ToString("X") + ":" + uid,
                    Name = DisplayName(exe),
                    ExePath = exe,
                    CallbackWindow = hwnd,
                    CallbackMessage = callback,
                    Uid = uid,
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        finally
        {
            VirtualFreeEx(process, remote, UIntPtr.Zero, MEM_RELEASE);
            CloseHandle(process);
        }

        return result;
    }

    private static HashSet<string> RunningExePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string? path = ExePathForPid((uint)process.Id);
                if (path is not null) paths.Add(path);
            }
            catch (Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return paths;
    }

    private static string? ExePathForPid(uint pid)
    {
        if (pid == 0) return null;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(260);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string DisplayName(string exePath)
    {
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
        }

        return Path.GetFileNameWithoutExtension(exePath);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ToolbarButton
    {
        public int iBitmap;
        public int idCommand;
        public byte fsState;
        public byte fsStyle;
        public byte bReserved0;
        public byte bReserved1;
        public byte bReserved2;
        public byte bReserved3;
        public byte bReserved4;
        public byte bReserved5;
        public IntPtr dwData;
        public IntPtr iString;
    }
}
