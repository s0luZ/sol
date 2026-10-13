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

public sealed class AppSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; set; }

    public bool NotchEnabled { get; set; } = true;
    public EdgeBarMode NotchMode { get; set; } = EdgeBarMode.SmartHide;
    public double NotchScale { get; set; } = 1.0;

    public bool DockEnabled { get; set; } = true;
    public EdgeBarMode DockMode { get; set; } = EdgeBarMode.SmartHide;
    public double DockIconSize { get; set; } = 44;
    public double DockMagnification { get; set; } = 1.6;
    public TaskbarMode TaskbarMode { get; set; } = TaskbarMode.Hide;

    /// <summary>Shortcut paths, exe paths or shell:AppsFolder\&lt;AppUserModelID&gt; entries. Null until first import.</summary>
    public List<string>? PinnedApps { get; set; }

    public string AccentColor { get; set; } = "#FFB547";
    public string SurfaceColor { get; set; } = "#0D0D10";
    public string ForegroundColor { get; set; } = "#F5F3EE";
    public double SurfaceOpacity { get; set; } = 0.95;
    public string FontFamily { get; set; } = "Segoe UI Variable Text";
    public double CornerRadius { get; set; } = 18;
    public double FlareRadius { get; set; } = 10;

    public bool TaskbarModifiedBySol { get; set; }
    public bool OriginalTaskbarAutoHide { get; set; }
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
