using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using SolDesktop.Interop;
using SolDesktop.Services;

namespace SolDesktop.ViewModels;

/// <summary>
/// Builds the dock: Start, pinned apps (with their windows), running unpinned apps, Recycle Bin.
/// </summary>
public sealed class DockViewModel : IDisposable
{
    public const string FolderPrefix = "folder:";
    private const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    private readonly WindowTracker _tracker;
    private readonly SettingsService _settings;
    private readonly IconService _icons;
    private readonly Dictionary<string, DockItemViewModel> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PinnedApp> _pinInfo = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _exeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _runningOrder = new();
    private readonly DockItemViewModel _start;
    private readonly DockItemViewModel _recycleBin;
    private readonly DockItemViewModel _runningSeparator;
    private readonly DockItemViewModel _endSeparator;
    private readonly DispatcherTimer _recycleBinTimer;
    private bool? _recycleBinFull;

    private sealed record PinnedApp(string Path, string Name, string? ExePath, string? AppId);

    public DockViewModel(WindowTracker tracker, SettingsService settings, IconService icons)
    {
        _tracker = tracker;
        _settings = settings;
        _icons = icons;

        if (_settings.Current.PinnedApps is null)
        {
            _settings.Current.PinnedApps = ImportTaskbarPins();
            _settings.Save();
        }

        _start = new DockItemViewModel("start", DockItemKind.Start) { Name = "Start", ToolTipText = "Start" };
        _recycleBin = new DockItemViewModel("recycle", DockItemKind.RecycleBin) { Name = "Recycle Bin", ToolTipText = "Recycle Bin" };
        _runningSeparator = new DockItemViewModel("sep:running", DockItemKind.Separator);
        _endSeparator = new DockItemViewModel("sep:end", DockItemKind.Separator);

        _recycleBinTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => RefreshRecycleBin(), Dispatcher.CurrentDispatcher);
        RefreshRecycleBin();

        _tracker.Changed += OnTrackerChanged;
        Rebuild();
    }

    public ObservableCollection<DockItemViewModel> Items { get; } = new();

    /// <summary>Raised after every rebuild, even if the item list itself did not change.</summary>
    public event EventHandler? Updated;

    /// <summary>Set while the user drags an icon so live window changes don't reshuffle the dock.</summary>
    public bool SuspendRebuild { get; set; }

    public event EventHandler<DockItemViewModel>? FolderRequested;

    private List<string> Pins => _settings.Current.PinnedApps ??= new List<string>();
    private List<DockFolder> Folders => _settings.Current.Folders ??= new List<DockFolder>();

    private void OnTrackerChanged(object? sender, EventArgs e) => Rebuild();

    public void Rebuild()
    {
        if (SuspendRebuild) return;

        IntPtr foreground = _tracker.ForegroundWindow;
        var remaining = _tracker.Windows.ToList();
        var result = new List<DockItemViewModel> { _start };

        foreach (string path in Pins)
        {
            if (path.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                DockFolder? folder = FindFolderByPin(path);
                if (folder is null) continue;

                DockItemViewModel folderItem = GetOrCreate("pin:" + path, () => new DockItemViewModel("pin:" + path, DockItemKind.Folder)
                {
                    Name = folder.Name,
                    LaunchTarget = path,
                    FolderId = folder.Id,
                    ToolTipText = folder.Name,
                });
                folderItem.IsPinned = true;
                folderItem.Name = folder.Name;
                folderItem.ToolTipText = folder.Name;
                var folderWindows = remaining.Where(w => FolderContains(folder, w)).ToList();
                remaining.RemoveAll(folderWindows.Contains);
                folderItem.SetWindows(folderWindows, foreground);
                LoadFolderIcons(folderItem, folder);
                result.Add(folderItem);
                continue;
            }

            PinnedApp pin = ResolvePin(path);
            var windows = remaining.Where(w => Matches(pin, w)).ToList();
            remaining.RemoveAll(windows.Contains);

            DockItemViewModel item = GetOrCreate("pin:" + path, () => new DockItemViewModel("pin:" + path, DockItemKind.App)
            {
                Name = pin.Name,
                LaunchTarget = path,
                AppId = pin.AppId,
                ExePath = pin.ExePath,
            }, path);
            item.IsPinned = true;
            item.SetWindows(windows, foreground);
            result.Add(item);
        }

        var groups = remaining.GroupBy(GroupKey).ToDictionary(g => g.Key, g => g.ToList());
        _runningOrder.RemoveAll(k => !groups.ContainsKey(k));
        foreach (string key in groups.Keys)
        {
            if (!_runningOrder.Contains(key)) _runningOrder.Add(key);
        }

        if (_runningOrder.Count > 0 && Pins.Count > 0) result.Add(_runningSeparator);

        foreach (string key in _runningOrder)
        {
            List<TrackedWindow> windows = groups[key];
            TrackedWindow first = windows[0];
            string itemKey = "run:" + key;
            DockItemViewModel item = GetOrCreate(itemKey, () => new DockItemViewModel(itemKey, DockItemKind.App)
            {
                Name = DisplayNameFor(first),
                LaunchTarget = LaunchTargetFor(first),
                AppId = first.AppId,
                ExePath = first.ExePath,
            }, IconNamesFor(first));
            item.IsPinned = false;
            item.SetWindows(windows, foreground);
            result.Add(item);
        }

        result.Add(_endSeparator);
        result.Add(_recycleBin);

        foreach (string stale in _items.Keys.Where(k => result.All(i => i.Key != k)).ToList()) _items.Remove(stale);

        if (!result.SequenceEqual(Items))
        {
            Items.Clear();
            foreach (DockItemViewModel item in result) Items.Add(item);
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    // ---- actions ----

    public void Click(DockItemViewModel item)
    {
        switch (item.Kind)
        {
            case DockItemKind.Start:
                WindowActions.OpenStartMenu();
                break;
            case DockItemKind.RecycleBin:
                OpenRecycleBin();
                break;
            case DockItemKind.Folder:
                FolderRequested?.Invoke(this, item);
                break;
            case DockItemKind.App when item.Windows.Count == 0:
                Launch(item);
                break;
            case DockItemKind.App:
                TrackedWindow window = item.Windows[0];
                if (window.Handle == _tracker.ForegroundWindow && !window.IsMinimized) WindowActions.Minimize(window.Handle);
                else WindowActions.Activate(window.Handle);
                break;
        }
    }

    public void Launch(DockItemViewModel item)
    {
        string? target = item.LaunchTarget ?? item.ExePath;
        if (target is not null) WindowActions.Launch(target);
    }

    public void CloseAll(DockItemViewModel item)
    {
        foreach (TrackedWindow window in item.Windows) WindowActions.Close(window.Handle);
    }

    public void Pin(DockItemViewModel item)
    {
        if (item.LaunchTarget is null || Pins.Contains(item.LaunchTarget, StringComparer.OrdinalIgnoreCase)) return;
        Pins.Add(item.LaunchTarget);
        _settings.Save();
        Rebuild();
    }

    public void Unpin(DockItemViewModel item)
    {
        if (item.IsFolder)
        {
            DeleteFolder(item);
            return;
        }

        if (item.LaunchTarget is null) return;
        Pins.RemoveAll(p => p.Equals(item.LaunchTarget, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        Rebuild();
    }

    public IReadOnlyList<DockFolder> FolderList => Folders;

    public DockFolder CreateFolder(string name)
    {
        var folder = new DockFolder { Name = string.IsNullOrWhiteSpace(name) ? "Folder" : name.Trim() };
        Folders.Add(folder);
        Pins.Add(FolderPrefix + folder.Id);
        _settings.Save();
        Rebuild();
        return folder;
    }

    public void RenameFolder(DockItemViewModel item, string name)
    {
        DockFolder? folder = FindFolder(item);
        if (folder is null || string.IsNullOrWhiteSpace(name)) return;
        folder.Name = name.Trim();
        _settings.Save();
        Rebuild();
    }

    public void DeleteFolder(DockItemViewModel item)
    {
        DockFolder? folder = FindFolder(item);
        if (folder is null) return;
        Folders.RemoveAll(f => f.Id == folder.Id);
        Pins.RemoveAll(p => p.Equals(FolderPrefix + folder.Id, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        Rebuild();
    }

    public void AddToFolder(DockItemViewModel folderItem, string path)
    {
        DockFolder? folder = FindFolder(folderItem);
        if (folder is null || string.IsNullOrWhiteSpace(path) || path.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)) return;
        if (!folder.Apps.Contains(path, StringComparer.OrdinalIgnoreCase)) folder.Apps.Add(path);
        Pins.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        Rebuild();
    }

    public void AddPathsToFolder(DockItemViewModel folderItem, IEnumerable<string> paths)
    {
        DockFolder? folder = FindFolder(folderItem);
        if (folder is null) return;
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!folder.Apps.Contains(path, StringComparer.OrdinalIgnoreCase)) folder.Apps.Add(path);
            Pins.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        _settings.Save();
        Rebuild();
    }

    public void RemoveFromFolder(DockItemViewModel folderItem, string path)
    {
        DockFolder? folder = FindFolder(folderItem);
        if (folder is null) return;
        folder.Apps.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        Rebuild();
    }

    public void MoveAppToFolder(DockItemViewModel app, DockItemViewModel folder)
    {
        string? path = app.LaunchTarget ?? app.ExePath;
        if (path is null) return;
        AddToFolder(folder, path);
    }

    public IEnumerable<(string Path, string Name)> ListFolderApps(DockItemViewModel folderItem)
    {
        DockFolder? folder = FindFolder(folderItem);
        if (folder is null) yield break;
        foreach (string path in folder.Apps)
        {
            yield return (path, ResolvePin(path).Name);
        }
    }

    public void PinPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (!Pins.Contains(path, StringComparer.OrdinalIgnoreCase)) Pins.Add(path);
        }

        _settings.Save();
        Rebuild();
    }

    /// <summary>Moves a pinned item while dragging; pinned items sit directly after Start.</summary>
    public void MovePinned(DockItemViewModel item, int newPinIndex)
    {
        if (item.LaunchTarget is null) return;
        int oldPinIndex = Pins.FindIndex(p => p.Equals(item.LaunchTarget, StringComparison.OrdinalIgnoreCase));
        newPinIndex = Math.Clamp(newPinIndex, 0, Pins.Count - 1);
        if (oldPinIndex < 0 || oldPinIndex == newPinIndex) return;

        string path = Pins[oldPinIndex];
        Pins.RemoveAt(oldPinIndex);
        Pins.Insert(newPinIndex, path);
        Items.Move(1 + oldPinIndex, 1 + newPinIndex);
    }

    public int PinIndexOf(DockItemViewModel item) =>
        item.LaunchTarget is null ? -1 : Pins.FindIndex(p => p.Equals(item.LaunchTarget, StringComparison.OrdinalIgnoreCase));

    public void CommitPinnedOrder() => _settings.Save();

    public void OpenRecycleBin() => WindowActions.Launch("shell:RecycleBinFolder");

    public void EmptyRecycleBin()
    {
        NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, 0);
        RefreshRecycleBin();
    }

    // ---- helpers ----

    private DockItemViewModel GetOrCreate(string key, Func<DockItemViewModel> create, params string?[] iconNames)
    {
        if (_items.TryGetValue(key, out DockItemViewModel? existing)) return existing;
        DockItemViewModel item = create();
        _items[key] = item;
        LoadIcon(item, iconNames);
        return item;
    }

    private async void LoadIcon(DockItemViewModel item, params string?[] names)
    {
        item.Icon = await _icons.GetAsync(names);
    }

    private void RefreshRecycleBin()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        bool full = NativeMethods.SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems > 0;
        if (full == _recycleBinFull) return;

        _recycleBinFull = full;
        _icons.Invalidate(RecycleBinParsingName);
        LoadIcon(_recycleBin, RecycleBinParsingName);
    }

    private DockFolder? FindFolder(DockItemViewModel item) =>
        item.FolderId is null ? FindFolderByPin(item.LaunchTarget) : Folders.FirstOrDefault(f => f.Id == item.FolderId);

    private DockFolder? FindFolderByPin(string? path)
    {
        if (path is null || !path.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        string id = path[FolderPrefix.Length..];
        return Folders.FirstOrDefault(f => f.Id == id);
    }

    private bool FolderContains(DockFolder folder, TrackedWindow window) =>
        folder.Apps.Any(path => Matches(ResolvePin(path), window));

    private async void LoadFolderIcons(DockItemViewModel item, DockFolder folder)
    {
        var icons = new ImageSource?[Math.Min(4, folder.Apps.Count)];
        for (int i = 0; i < icons.Length; i++)
        {
            PinnedApp pin = ResolvePin(folder.Apps[i]);
            icons[i] = await _icons.GetAsync(folder.Apps[i], pin.ExePath);
        }

        item.SetFolderIcons(icons);
    }

    private PinnedApp ResolvePin(string path)
    {
        if (_pinInfo.TryGetValue(path, out PinnedApp? cached)) return cached;

        string? exePath = null;
        string? appId = null;
        string name;

        if (path.StartsWith(WindowActions.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            appId = path[WindowActions.AppsFolderPrefix.Length..];
            name = ShellProperties.GetDisplayName(path) ?? appId;
        }
        else if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            (exePath, appId) = ShellProperties.GetShortcutInfo(path);
            name = Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            exePath = path;
            name = ExeDisplayName(path);
        }

        var pin = new PinnedApp(path, name, exePath, appId);
        _pinInfo[path] = pin;
        return pin;
    }

    private static bool Matches(PinnedApp pin, TrackedWindow window)
    {
        if (pin.AppId is not null && window.AppId is not null)
        {
            return pin.AppId.Equals(window.AppId, StringComparison.OrdinalIgnoreCase);
        }

        return pin.ExePath is not null && window.ExePath is not null &&
               pin.ExePath.Equals(window.ExePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GroupKey(TrackedWindow w) => w.AppId ?? w.ExePath ?? w.Handle.ToString();

    private static bool IsPackagedApp(TrackedWindow w) =>
        w.AppId is not null && (w.ExePath is null || w.ExePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase));

    private static string? LaunchTargetFor(TrackedWindow w) =>
        IsPackagedApp(w) ? WindowActions.AppsFolderPrefix + w.AppId : w.ExePath;

    private static string?[] IconNamesFor(TrackedWindow w) =>
        w.AppId is not null
            ? new[] { WindowActions.AppsFolderPrefix + w.AppId, w.ExePath }
            : new[] { w.ExePath };

    private string DisplayNameFor(TrackedWindow w)
    {
        if (IsPackagedApp(w)) return ShellProperties.GetDisplayName(WindowActions.AppsFolderPrefix + w.AppId) ?? w.Title;
        return w.ExePath is not null ? ExeDisplayName(w.ExePath) : w.Title;
    }

    private string ExeDisplayName(string exePath)
    {
        if (_exeNames.TryGetValue(exePath, out string? cached)) return cached;

        string name = Path.GetFileNameWithoutExtension(exePath);
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
        }

        _exeNames[exePath] = name;
        return name;
    }

    private static List<string> ImportTaskbarPins()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

        try
        {
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.lnk").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex);
            return new List<string>();
        }
    }

    public void Dispose()
    {
        _recycleBinTimer.Stop();
        _tracker.Changed -= OnTrackerChanged;
    }
}
