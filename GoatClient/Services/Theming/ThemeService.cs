using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using GoatClient.Core.Constants;
using GoatClient.Services.Logging;

namespace GoatClient.Services.Theming;

/// <summary>Base surface colors of a theme.</summary>
public sealed record ThemeBase(string Id, string Name, Color Background, Color Sidebar, Color Card, Color Secondary, Color Input, Color BorderSubtle, Color BorderStrong);

/// <summary>Accent pair (primary accent and secondary accent for gradients).</summary>
public sealed record ThemeAccent(string Id, string Name, Color Accent, Color Accent2);

/// <summary>
/// Themes and accent colors. The chosen theme is applied at startup – before any window or
/// style is created – by replacing the palette resources, so every control picks it up.
/// Changing the theme therefore takes effect after a restart.
/// </summary>
public static class ThemeService
{
    public const string DefaultThemeId = "goat-dark";
    public const string DefaultAccentId = "goat";

    public static IReadOnlyList<ThemeBase> Themes { get; } =
    [
        new("goat-dark", "GOAT Dark", C("#080A10"), C("#0B0E17"), C("#101522"), C("#182137"), C("#0C111C"), C("#1A2338"), C("#26324D")),
        new("midnight", "Midnight Blue", C("#070B18"), C("#0A1024"), C("#0E1630"), C("#17224A"), C("#0B1328"), C("#1C2A55"), C("#27396E")),
        new("oled", "OLED Black", C("#000000"), C("#030304"), C("#0B0B0F"), C("#15151C"), C("#07070A"), C("#1C1C24"), C("#2A2A35")),
        new("graphite", "Graphite", C("#101114"), C("#141519"), C("#1A1C21"), C("#24272E"), C("#15161A"), C("#2A2D35"), C("#373B45")),
    ];

    public static IReadOnlyList<ThemeAccent> Accents { get; } =
    [
        new("goat", "GOAT Cyan / Violet", C("#00D9FF"), C("#7A5CFF")),
        new("emerald", "Emerald", C("#2EE59D"), C("#00B3FF")),
        new("sunset", "Sunset", C("#FF8A3D"), C("#FF3D7F")),
        new("royal", "Royal", C("#8B5CFF"), C("#FF4FD8")),
        new("gold", "Gold", C("#FFC23D"), C("#FF7A3D")),
        new("crimson", "Crimson", C("#FF4D5E"), C("#B03DFF")),
    ];

    public static ThemeBase FindTheme(string? id) => Themes.FirstOrDefault(t => t.Id == id) ?? Themes[0];

    public static ThemeAccent FindAccent(string? id) => Accents.FirstOrDefault(a => a.Id == id) ?? Accents[0];

    /// <summary>Reads theme/accent directly from settings.json (runs before the settings service exists).</summary>
    public static (string Theme, string Accent) ReadFromSettingsFile()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
                var root = doc.RootElement;
                var theme = root.TryGetProperty("Theme", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var accent = root.TryGetProperty("Accent", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                return (theme ?? DefaultThemeId, accent ?? DefaultAccentId);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt settings are handled by the settings service later; fall back to defaults here.
        }

        return (DefaultThemeId, DefaultAccentId);
    }

    /// <summary>Replaces the palette resources in every dictionary that defines them.</summary>
    public static void Apply(ResourceDictionary root, string? themeId, string? accentId, ILogger logger)
    {
        var theme = FindTheme(themeId);
        var accent = FindAccent(accentId);
        if (theme.Id == DefaultThemeId && accent.Id == DefaultAccentId)
        {
            return; // XAML already contains the default GOAT palette.
        }

        var values = new Dictionary<string, object>
        {
            ["BackgroundColor"] = theme.Background,
            ["CardColor"] = theme.Card,
            ["SecondaryColor"] = theme.Secondary,
            ["AccentColor"] = accent.Accent,
            ["Accent2Color"] = accent.Accent2,
            ["BackgroundBrush"] = Brush(theme.Background),
            ["SidebarBrush"] = Brush(theme.Sidebar),
            ["CardBrush"] = Brush(theme.Card),
            ["SecondaryBrush"] = Brush(theme.Secondary),
            ["InputBrush"] = Brush(theme.Input),
            ["BorderSubtleBrush"] = Brush(theme.BorderSubtle),
            ["BorderStrongBrush"] = Brush(theme.BorderStrong),
            ["AccentBrush"] = Brush(accent.Accent),
            ["Accent2Brush"] = Brush(accent.Accent2),
            ["AccentSoftBrush"] = Brush(WithAlpha(accent.Accent, 0x1A)),
            ["AccentGradientBrush"] = Gradient(new Point(0, 0), new Point(1, 1), accent.Accent, accent.Accent2),
            ["AccentGradientVerticalBrush"] = Gradient(new Point(0, 0), new Point(0, 1), accent.Accent, accent.Accent2),
            ["ActiveNavBrush"] = Gradient(new Point(0, 0.5), new Point(1, 0.5), WithAlpha(accent.Accent, 0x26), WithAlpha(accent.Accent2, 0x0A)),
            ["HeroGlowBrush"] = HeroGlow(accent, theme),
        };

        var replaced = 0;
        foreach (var dictionary in Flatten(root))
        {
            foreach (var (key, value) in values)
            {
                if (dictionary.Contains(key))
                {
                    dictionary[key] = value;
                    replaced++;
                }
            }
        }

        logger.Info($"Theme applied: {theme.Name} · {accent.Name} ({replaced} resources).");
    }

    private static IEnumerable<ResourceDictionary> Flatten(ResourceDictionary dictionary)
    {
        yield return dictionary;
        foreach (var merged in dictionary.MergedDictionaries)
        {
            foreach (var inner in Flatten(merged))
            {
                yield return inner;
            }
        }
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(Point start, Point end, Color from, Color to)
    {
        var brush = new LinearGradientBrush(from, to, start, end);
        brush.Freeze();
        return brush;
    }

    private static RadialGradientBrush HeroGlow(ThemeAccent accent, ThemeBase theme)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.85, 0.3),
            GradientOrigin = new Point(0.85, 0.3),
            RadiusX = 0.6,
            RadiusY = 0.9,
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(accent.Accent, 0x22), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(accent.Accent2, 0x10), 0.5));
        brush.GradientStops.Add(new GradientStop(WithAlpha(theme.Card, 0x00), 1));
        brush.Freeze();
        return brush;
    }
}
