using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolDesktop.Services;

public enum EdgeBarMode
{
    /// <summary>Always fully shown.</summary>
    AlwaysVisible,

    /// <summary>Shown on a clear desktop; tucks away to a sliver when an app window overlaps it.</summary>
    SmartHide,

    /// <summary>Always a sliver until hovered.</summary>
    HoverOnly,
}

public enum ScreenEdge
{
    Top,
    Bottom,
}

public enum TaskbarMode
{
    Leave,
    AutoHide,
    Hide,
}

public enum ThemeKind
{
    /// <summary>Solid near-black bars.</summary>
    Dark,

    /// <summary>Frosted, blurred translucent bars (glassmorphism).</summary>
    Glass,
}

public sealed class AppSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; set; }

    public bool NotchEnabled { get; set; } = true;
    public EdgeBarMode NotchMode { get; set; } = EdgeBarMode.SmartHide;
    public double NotchScale { get; set; } = 1.0;
    public double ControlCentreScale { get; set; } = 1.0;

    public bool DockEnabled { get; set; } = true;
    public EdgeBarMode DockMode { get; set; } = EdgeBarMode.SmartHide;
    public double DockIconSize { get; set; } = 44;
    public double DockMagnification { get; set; } = 1.6;
    public TaskbarMode TaskbarMode { get; set; } = TaskbarMode.Hide;
    public bool CenterStartMenu { get; set; } = true;

    /// <summary>Keep the notch and dock hidden while a fullscreen app is in front, and do not take focus from it.</summary>
    public bool GamingMode { get; set; } = true;

    /// <summary>Shortcut paths, exe paths or shell:AppsFolder\&lt;AppUserModelID&gt; entries. Null until first import.
    /// Folder pins are stored as <c>folder:{id}</c>.</summary>
    public List<string>? PinnedApps { get; set; }

    public List<DockFolder> Folders { get; set; } = new();

    public ThemeKind Theme { get; set; } = ThemeKind.Dark;
    public double GlassOpacity { get; set; } = 0.72;
    public string AccentColor { get; set; } = "#FFB547";
    public string SurfaceColor { get; set; } = "#0D0D10";
    public string ForegroundColor { get; set; } = "#F5F3EE";
    public double SurfaceOpacity { get; set; } = 0.95;
    public string FontFamily { get; set; } = "Segoe UI Variable Text";
    public double CornerRadius { get; set; } = 18;
    public double FlareRadius { get; set; } = 10;

    public bool TaskbarModifiedBySol { get; set; }
    public bool OriginalTaskbarAutoHide { get; set; }
    public bool StartAlignmentModifiedBySol { get; set; }

    /// <summary>Original HKCU TaskbarAl value (0 = left, 1 = centre); null when the value didn't exist.</summary>
    public int? OriginalTaskbarAlignment { get; set; }
}

public sealed class DockFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Folder";
    public List<string> Apps { get; set; } = new();
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SolDesktop");

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error(ex);
            Current = new AppSettings();
        }

        if (Current.Version < 2)
        {
            // v0.1 shipped with the dock switched off because it didn't exist yet.
            Current.DockEnabled = true;
        }

        // Old glass used a 35% grey blur window. The new look is Dark at ~72% opacity.
        if (Math.Abs(Current.GlassOpacity - 0.35) < 0.001) Current.GlassOpacity = 0.72;

        Current.Folders ??= new List<DockFolder>();
        Current.Version = AppSettings.CurrentVersion;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex);
        }
    }
}
