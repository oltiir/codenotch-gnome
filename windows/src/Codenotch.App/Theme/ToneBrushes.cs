using System;
using System.Windows;
using System.Windows.Media;
using Codenotch.Core.Presentation;
using Microsoft.Win32;

namespace Codenotch.App.Theme;

/// <summary>
/// Owns the brushes that WPF's ThemeMode does not: the usage tones, the track, the
/// notch pill. ThemeMode="System" restyles the Fluent controls; these colours are
/// ours, so they are swapped by hand on a theme change — by mutating the existing
/// SolidColorBrush objects, so every binding and every drawn ring picks up the new
/// colour without anything being rebuilt.
/// </summary>
internal static class ToneBrushes
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark { get; private set; } = true;

    /// <summary>Reads AppsUseLightTheme (0 = dark). Defaults to dark when the value is missing.</summary>
    public static bool ReadSystemIsDark()
    {
        try
        {
            object? value = Registry.GetValue($@"HKEY_CURRENT_USER\{PersonalizeKey}", "AppsUseLightTheme", null);
            if (value is int light)
                return light == 0;
        }
        catch (Exception)
        {
            // A locked or absent key just means we keep the current guess.
        }
        return IsDark;
    }

    public static SolidColorBrush For(ToneKind tone) => tone switch
    {
        ToneKind.Ok => Brush("ToneOkBrush"),
        ToneKind.Warn => Brush("ToneWarnBrush"),
        ToneKind.Critical => Brush("ToneCriticalBrush"),
        _ => Brush("ToneStaleBrush"),
    };

    public static SolidColorBrush Track => Brush("TrackBrush");

    public static SolidColorBrush Text => Brush("TextBrush");

    public static SolidColorBrush Muted => Brush("MutedTextBrush");

    public static SolidColorBrush Glyph => Brush("GlyphBrush");

    public static SolidColorBrush Notch => Brush("NotchBrush");

    public static SolidColorBrush NotchOpen => Brush("NotchOpenBrush");

    public static SolidColorBrush NotchBorder => Brush("NotchBorderBrush");

    public static SolidColorBrush Surface => Brush("SurfaceBrush");

    /// <summary>A plain (non-resource) colour for the tone, for the tray renderer.</summary>
    public static Color ColorFor(ToneKind tone, bool dark) => Parse(ToneColors.Hex(tone, dark));

    public static Color TrackColor(bool dark) => Parse(ToneColors.TrackHex(dark));

    /// <summary>Repaints the palette for the given theme. Safe to call repeatedly.</summary>
    public static void Apply(bool dark)
    {
        IsDark = dark;
        Set("ToneOkBrush", ToneColors.Hex(ToneKind.Ok, dark));
        Set("ToneWarnBrush", ToneColors.Hex(ToneKind.Warn, dark));
        Set("ToneCriticalBrush", ToneColors.Hex(ToneKind.Critical, dark));
        Set("ToneStaleBrush", ToneColors.Hex(ToneKind.Stale, dark));
        Set("TrackBrush", ToneColors.TrackHex(dark));

        Set("TextBrush", dark ? "#FFFFFFFF" : "#FF1A1A1A");
        Set("MutedTextBrush", dark ? "#FF9A9996" : "#FF6E7781");
        Set("GlyphBrush", dark ? "#EBFFFFFF" : "#DE000000");

        // The pill: rgba(12,12,14,.90/.96) in dark, a near-white equivalent in light.
        Set("NotchBrush", dark ? "#E60C0C0E" : "#EBFAFAFA");
        Set("NotchOpenBrush", dark ? "#F50C0C0E" : "#F7FAFAFA");
        Set("NotchBorderBrush", dark ? "#12FFFFFF" : "#1A000000");
        Set("SurfaceBrush", dark ? "#F21C1C1E" : "#F2F3F3F3");
    }

    private static void Set(string key, string hex)
    {
        SolidColorBrush brush = Brush(key);
        Color color = Parse(hex);
        if (brush.IsFrozen)
            return;                      // a frozen brush cannot be recoloured; leave it
        brush.Color = color;
    }

    private static SolidColorBrush Brush(string key)
    {
        Application? app = Application.Current;
        if (app is not null && app.TryFindResource(key) is SolidColorBrush found)
            return found;
        // Design time, or before App.xaml's resources exist.
        return new SolidColorBrush(Colors.Transparent);
    }

    /// <summary>#RRGGBB or #AARRGGBB. Falls back to magenta so a typo is visible, not invisible.</summary>
    internal static Color Parse(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color color)
                return color;
        }
        catch (Exception)
        {
            // fall through
        }
        return Colors.Magenta;
    }
}
