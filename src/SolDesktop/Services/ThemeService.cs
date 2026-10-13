using System;
using System.Windows;
using System.Windows.Media;
using SolDesktop.ViewModels;

namespace SolDesktop.Services;

/// <summary>Pushes the user's colours and font into application resources (consumed via DynamicResource).</summary>
public static class ThemeService
{
    public static void Apply(AppSettings s)
    {
        var defaults = new AppSettings();
        Color accent = Parse(s.AccentColor, defaults.AccentColor);
        Color surface = Parse(s.SurfaceColor, defaults.SurfaceColor);
        Color foreground = Parse(s.ForegroundColor, defaults.ForegroundColor);

        bool glass = s.Theme == ThemeKind.Glass;

        ResourceDictionary r = Application.Current.Resources;
        r["Sol.AccentColor"] = accent;
        r["Sol.AccentBrush"] = Brush(accent);
        r["Sol.OnAccentBrush"] = Brush(IsLight(accent) ? Color.FromRgb(0x14, 0x14, 0x16) : Colors.White);
        r["Sol.SurfaceSolidBrush"] = Brush(surface);

        // Glass is the same Dark palette with the bars see-through, so wallpaper shows
        // through the flared shape. No extra blur window — that drew a rectangle and
        // slid in from a monitor above the primary display.
        double barAlpha = glass ? s.GlassOpacity : s.SurfaceOpacity;
        r["Sol.SurfaceBrush"] = Brush(WithAlpha(surface, barAlpha));
        r["Sol.SurfaceRaisedBrush"] = Brush(WithAlpha(Mix(surface, foreground, 0.06), Math.Min(1, barAlpha + 0.06)));
        r["Sol.SurfaceHoverBrush"] = Brush(WithAlpha(Mix(surface, foreground, 0.12), Math.Min(1, barAlpha + 0.10)));
        r["Sol.SurfaceBorderBrush"] = Brush(WithAlpha(foreground, glass ? 0.18 : 0.13));

        r["Sol.ForegroundBrush"] = Brush(foreground);
        r["Sol.ForegroundDimBrush"] = Brush(WithAlpha(foreground, 0.6));
        r["Sol.ForegroundFaintBrush"] = Brush(WithAlpha(foreground, 0.25));
        r["Sol.TextFont"] = new FontFamily($"{s.FontFamily}, Segoe UI");

        Appearance.Current.Update(s);
    }

    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            string value = text.Trim();
            if (!value.StartsWith('#')) value = "#" + value;
            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color Parse(string text, string fallback) =>
        TryParseColor(text, out Color c) ? c : (Color)ColorConverter.ConvertFromString(fallback);

    private static SolidColorBrush Brush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    private static bool IsLight(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150;
}
