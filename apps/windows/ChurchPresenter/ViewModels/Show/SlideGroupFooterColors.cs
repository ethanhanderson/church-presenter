using Windows.UI;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// Footer bar colors by song section for Show slide thumbnails and browse decks.
/// </summary>
public static class SlideGroupFooterColors
{
    private static readonly Dictionary<string, Color> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = ColorFromHex("#0f172a"),
        ["intro"] = ColorFromHex("#64748b"),
        ["verse"] = ColorFromHex("#3b82f6"),
        ["pre-chorus"] = ColorFromHex("#06b6d4"),
        ["chorus"] = ColorFromHex("#10b981"),
        ["bridge"] = ColorFromHex("#8b5cf6"),
        ["refrain"] = ColorFromHex("#eab308"),
        ["tag"] = ColorFromHex("#f97316"),
        ["vamp"] = ColorFromHex("#a16207"),
        ["interlude"] = ColorFromHex("#22d3ee"),
        ["outro"] = ColorFromHex("#ef4444"),
        ["ending"] = ColorFromHex("#dc2626"),
        ["custom"] = ColorFromHex("#ec4899"),
    };

    /// <summary>Neutral footer when section has no mapped color.</summary>
    public static Color DefaultFooter => ColorFromHex("#334155");

    public static Color GetFooterColor(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return DefaultFooter;
        return Map.TryGetValue(section.Trim(), out var c) ? c : DefaultFooter;
    }

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return Color.FromArgb(255, r, g, b);
        }

        return DefaultFooter;
    }
}