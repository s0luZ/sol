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
}

public sealed class DockItemViewModel : ObservableObject
{
    private string _name = "";
    private ImageSource? _icon;
    private bool _isPinned;
    private bool _isRunning;
    private bool _isActive;
    private string? _toolTipText;

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
    public bool ShowsImage => Kind is DockItemKind.App or DockItemKind.RecycleBin;

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

    public void SetWindows(IReadOnlyList<TrackedWindow> windows, IntPtr foreground)
    {
        Windows = windows;
        IsRunning = windows.Count > 0;
        IsActive = windows.Any(w => w.Handle == foreground);
        if (IsApp) ToolTipText = IsRunning ? null : Name;
    }
}
