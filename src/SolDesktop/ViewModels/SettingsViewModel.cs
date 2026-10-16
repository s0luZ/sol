using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SolDesktop.Services;

namespace SolDesktop.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly Action _changed;
    private bool _startWithWindows;

    public SettingsViewModel(SettingsService settings, Action changed)
    {
        _settings = settings;
        _changed = changed;
        _startWithWindows = StartupService.IsEnabled;

        Fonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ResetAppearanceCommand = new RelayCommand(ResetAppearance);
        OpenDataFolderCommand = new RelayCommand(() => WindowActions.Launch(SettingsService.DataFolder));
        QuitCommand = new RelayCommand(() => Application.Current.Shutdown());
    }

    private AppSettings S => _settings.Current;

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

    public IReadOnlyList<string> Fonts { get; }

    public IReadOnlyList<string> AccentSwatches { get; } = new[]
    {
        "#FFB547", "#FF7A45", "#FF5C7A", "#C86BFA", "#7C8CFF", "#3FA9F5", "#2DD4BF", "#5BD48A", "#E8E4DA",
    };

    public IReadOnlyList<string> SurfaceSwatches { get; } = new[]
    {
        "#0D0D10", "#16161A", "#1C1B22", "#14181F", "#101A16", "#1F1A14", "#2A2A2E", "#F4F2EE", "#FFFFFF",
    };

    public IReadOnlyList<string> ForegroundSwatches { get; } = new[]
    {
        "#F5F3EE", "#FFFFFF", "#D9D6CF", "#1A1A1D", "#000000",
    };

    public ICommand ResetAppearanceCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand QuitCommand { get; }

    // ---- General ----

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value)) return;
            try
            {
                StartupService.SetEnabled(value);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }

    // ---- Notch ----

    public bool NotchEnabled { get => S.NotchEnabled; set => Set(S.NotchEnabled, value, v => S.NotchEnabled = v); }
    public EdgeBarMode NotchMode { get => S.NotchMode; set => Set(S.NotchMode, value, v => S.NotchMode = v); }
    public double NotchScale { get => S.NotchScale; set => Set(S.NotchScale, Math.Round(value, 2), v => S.NotchScale = v); }
    public double ControlCentreScale { get => S.ControlCentreScale; set => Set(S.ControlCentreScale, Math.Round(value, 2), v => S.ControlCentreScale = v); }

    // ---- Dock ----

    public bool DockEnabled { get => S.DockEnabled; set => Set(S.DockEnabled, value, v => S.DockEnabled = v); }
    public EdgeBarMode DockMode { get => S.DockMode; set => Set(S.DockMode, value, v => S.DockMode = v); }
    public double DockIconSize { get => S.DockIconSize; set => Set(S.DockIconSize, Math.Round(value), v => S.DockIconSize = v); }
    public double DockMagnification { get => S.DockMagnification; set => Set(S.DockMagnification, Math.Round(value, 2), v => S.DockMagnification = v); }
    public TaskbarMode TaskbarMode { get => S.TaskbarMode; set => Set(S.TaskbarMode, value, v => S.TaskbarMode = v); }
    public bool GamingMode { get => S.GamingMode; set => Set(S.GamingMode, value, v => S.GamingMode = v); }
    public bool CenterStartMenu { get => S.CenterStartMenu; set => Set(S.CenterStartMenu, value, v => S.CenterStartMenu = v); }

    // ---- Appearance ----

    public ThemeKind Theme
    {
        get => S.Theme;
        set
        {
            Set(S.Theme, value, v => S.Theme = v);
            OnPropertyChanged(nameof(IsGlassTheme));
        }
    }

    public bool IsGlassTheme => S.Theme == ThemeKind.Glass;
    public double GlassOpacity { get => S.GlassOpacity; set => Set(S.GlassOpacity, Math.Round(value, 2), v => S.GlassOpacity = v); }

    public string AccentColor { get => S.AccentColor; set => SetColor(S.AccentColor, value, v => S.AccentColor = v); }
    public string SurfaceColor { get => S.SurfaceColor; set => SetColor(S.SurfaceColor, value, v => S.SurfaceColor = v); }
    public string ForegroundColor { get => S.ForegroundColor; set => SetColor(S.ForegroundColor, value, v => S.ForegroundColor = v); }
    public double SurfaceOpacity { get => S.SurfaceOpacity; set => Set(S.SurfaceOpacity, Math.Round(value, 2), v => S.SurfaceOpacity = v); }
    public string FontFamily { get => S.FontFamily; set => Set(S.FontFamily, value, v => S.FontFamily = v); }
    public double CornerRadius { get => S.CornerRadius; set => Set(S.CornerRadius, Math.Round(value), v => S.CornerRadius = v); }
    public double FlareRadius { get => S.FlareRadius; set => Set(S.FlareRadius, Math.Round(value), v => S.FlareRadius = v); }

    /// <summary>Re-read everything after settings were changed elsewhere (tray, bar menus).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private void Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        assign(value);
        OnPropertyChanged(name);
        _changed();
    }

    private void SetColor(string current, string? value, Action<string> assign, [CallerMemberName] string? name = null)
    {
        if (ThemeService.TryParseColor(value, out Color color))
        {
            Set(current, ThemeService.ToHex(color), assign, name);
        }
        else
        {
            // Rejected input: push the stored value back into the text box.
            OnPropertyChanged(name);
        }
    }

    private void ResetAppearance()
    {
        var d = new AppSettings();
        S.AccentColor = d.AccentColor;
        S.SurfaceColor = d.SurfaceColor;
        S.ForegroundColor = d.ForegroundColor;
        S.SurfaceOpacity = d.SurfaceOpacity;
        S.GlassOpacity = d.GlassOpacity;
        S.FontFamily = d.FontFamily;
        S.CornerRadius = d.CornerRadius;
        S.FlareRadius = d.FlareRadius;
        S.NotchScale = d.NotchScale;
        S.ControlCentreScale = d.ControlCentreScale;
        S.DockIconSize = d.DockIconSize;
        S.DockMagnification = d.DockMagnification;
        Refresh();
        _changed();
    }
}
