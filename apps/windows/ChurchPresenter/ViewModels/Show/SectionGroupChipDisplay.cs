
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace ChurchPresenter.ViewModels;

/// <summary>Section group chip in the arrangements bar (colored background from <see cref="SlideGroupThumbnailColors"/>).</summary>
public sealed class SectionGroupChipDisplay
{
    public SectionGroup Section { get; init; } = null!;

    public SolidColorBrush BackgroundBrush { get; init; } = null!;

    public SolidColorBrush ForegroundBrush { get; init; } = null!;

    public static SectionGroupChipDisplay Create(SectionGroup group, PresentationProject? project, bool fixedLayout = false)
    {
        var hex = SlideGroupThumbnailColors.GetHexColorForSectionGroup(group, project);
        var (bgBrush, fgBrush) = fixedLayout ? CreateBrushesForFixedOrder(hex) : CreateBrushesFromHex(hex);
        return new SectionGroupChipDisplay
        {
            Section = group,
            BackgroundBrush = bgBrush,
            ForegroundBrush = fgBrush,
        };
    }

    /// <summary>Dimmed brushes for Master / fixed-order UI while preserving the same color pairing.</summary>
    public static (SolidColorBrush Background, SolidColorBrush Foreground) CreateBrushesForFixedOrder(string hex)
    {
        var bg = ParseHexToColor(hex);
        var fg = PickForegroundForBackground(bg);
        return (
            new SolidColorBrush(WithAlpha(bg, 0.54f)),
            new SolidColorBrush(WithAlpha(fg, 0.78f)));
    }

    /// <summary>Background and label brushes for a group hex color (shared with arrangement manager rows).</summary>
    public static (SolidColorBrush Background, SolidColorBrush Foreground) CreateBrushesFromHex(string hex)
    {
        var bg = ParseHexToColor(hex);
        return (new SolidColorBrush(bg), new SolidColorBrush(PickForegroundForBackground(bg)));
    }

    private static Color ParseHexToColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Color.FromArgb(255, 100, 116, 139);
        hex = hex.TrimStart('#');
        if (hex.Length != 6)
            return Color.FromArgb(255, 100, 116, 139);
        var r = Convert.ToByte(hex[..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return Color.FromArgb(255, r, g, b);
    }

    private static Color PickForegroundForBackground(Color bg)
    {
        var r = bg.R / 255.0;
        var g = bg.G / 255.0;
        var b = bg.B / 255.0;
        var lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return lum > 0.55
            ? Color.FromArgb(255, 15, 23, 42)
            : Color.FromArgb(255, 255, 255, 255);
    }

    private static Color WithAlpha(Color color, float opacity)
    {
        var alpha = (byte)Math.Clamp((int)Math.Round(255 * opacity), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}