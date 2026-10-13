using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using SolDesktop.Services;

namespace SolDesktop.ViewModels;

public enum DockItemKind
{
    Start,
    App,
    Separator,
    RecycleBin,
    Folder,
    TrayOverflow,
}

public sealed class DockItemViewModel : ObservableObject
{
    private string _name = "";
    private ImageSource? _icon;
    private bool _isPinned;
    private bool _isRunning;
    private bool _isActive;
    private string? _toolTipText;
    private ImageSource? _folderIcon0;
    private ImageSource? _folderIcon1;
    private ImageSource? _folderIcon2;
    private ImageSource? _folderIcon3;
    private bool _folderHasIcons;

    public DockItemViewModel(string key, DockItemKind kind)
    {
        Key = key;
        Kind = kind;
    }

    public string Key { get; }
    public DockItemKind Kind { get; }

    public bool IsStart => Kind == DockItemKind.Start;
    public bool IsApp => Kind == DockItemKind.App;
    public bool IsSeparator => Kind == DockItemKind.Separator;
    public bool IsRecycleBin => Kind == DockItemKind.RecycleBin;
    public bool IsFolder => Kind == DockItemKind.Folder;
    public bool IsTrayOverflow => Kind == DockItemKind.TrayOverflow;
    public bool ShowsImage => Kind is DockItemKind.App or DockItemKind.RecycleBin;

    public string? FolderId { get; set; }

    /// <summary>What to open when there are no windows: shortcut, exe or shell:AppsFolder\&lt;id&gt;.</summary>
    public string? LaunchTarget { get; set; }
    public string? AppId { get; set; }
    public string? ExePath { get; set; }

    public IReadOnlyList<TrackedWindow> Windows { get; private set; } = Array.Empty<TrackedWindow>();

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public ImageSource? Icon { get => _icon; set => SetProperty(ref _icon, value); }
    public bool IsPinned { get => _isPinned; set => SetProperty(ref _isPinned, value); }
    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    public bool IsActive { get => _isActive; private set => SetProperty(ref _isActive, value); }

    /// <summary>Null for running apps: they show a live window preview instead.</summary>
    public string? ToolTipText { get => _toolTipText; set => SetProperty(ref _toolTipText, value); }

    public ImageSource? FolderIcon0 { get => _folderIcon0; private set => SetProperty(ref _folderIcon0, value); }
    public ImageSource? FolderIcon1 { get => _folderIcon1; private set => SetProperty(ref _folderIcon1, value); }
    public ImageSource? FolderIcon2 { get => _folderIcon2; private set => SetProperty(ref _folderIcon2, value); }
    public ImageSource? FolderIcon3 { get => _folderIcon3; private set => SetProperty(ref _folderIcon3, value); }
    public bool FolderHasIcons { get => _folderHasIcons; private set => SetProperty(ref _folderHasIcons, value); }

    public void SetFolderIcons(ImageSource?[] icons)
    {
        FolderIcon0 = icons.Length > 0 ? icons[0] : null;
        FolderIcon1 = icons.Length > 1 ? icons[1] : null;
        FolderIcon2 = icons.Length > 2 ? icons[2] : null;
        FolderIcon3 = icons.Length > 3 ? icons[3] : null;
        FolderHasIcons = FolderIcon0 is not null;
    }

    public void SetWindows(IReadOnlyList<TrackedWindow> windows, IntPtr foreground)
    {
        Windows = windows;
        IsRunning = windows.Count > 0;
        IsActive = windows.Any(w => w.Handle == foreground);
        if (IsApp) ToolTipText = IsRunning ? null : Name;
    }
}
