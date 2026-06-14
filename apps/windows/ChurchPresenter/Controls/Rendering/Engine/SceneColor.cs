#nullable enable

using Windows.UI;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Shared color parsing for scene rendering. Accepts #RGB, #RRGGBB, #AARRGGBB,
/// "transparent", and the common named colors used by the content model.
/// </summary>
public static class SceneColor
{
    /// <summary>Fully transparent color.</summary>
    public static Color Transparent { get; } = Color.FromArgb(0, 0, 0, 0);

    /// <summary>Opaque white fallback color.</summary>
    public static Color White { get; } = Color.FromArgb(255, 255, 255, 255);

    /// <summary>Opaque black color.</summary>
    public static Color Black { get; } = Color.FromArgb(255, 0, 0, 0);

    /// <summary>Parses a color string, returning <paramref name="fallback"/> when empty or invalid.</summary>
    public static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string trimmed = value.Trim();
        if (trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return Transparent;

        if (!trimmed.StartsWith('#'))
        {
            return trimmed.ToLowerInvariant() switch
            {
                "black" => Black,
                "white" => White,
                _ => fallback,
            };
        }

        string hex = trimmed[1..];
        if (hex.Length == 3)
            hex = $"FF{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        else if (hex.Length == 6)
            hex = $"FF{hex}";

        if (hex.Length != 8)
            return fallback;

        try
        {
            return Color.FromArgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (OverflowException)
        {
            return fallback;
        }
    }

    /// <summary>Applies an opacity multiplier to a color's alpha channel.</summary>
    public static Color WithOpacity(Color color, double opacity)
    {
        double clamped = Math.Clamp(opacity, 0d, 1d);
        return Color.FromArgb((byte)Math.Round(color.A * clamped), color.R, color.G, color.B);
    }
}
