using ChurchPresenter.Models.Documents;

namespace ChurchPresenter.Backend.Rendering;

/// <summary>
/// Picks the checkerboard palette for transparent slide thumbnails from slide content luminance.
/// Light content uses the dark checkerboard variant; dark content uses the light variant.
/// This overrides host UI light/dark theme.
/// </summary>
public static class SlideCheckerboardTone
{
    private const double LuminanceThreshold = 0.5;
    private const double StageAreaWeight = 1.0;
    private const double MinimumLayerWeight = 0.01;

    /// <summary>
    /// True when thumbnails should use the dark checkerboard palette (behind predominantly light content).
    /// </summary>
    public static bool UseDarkCheckerboard(PresentationSlide? slide)
    {
        if (slide is null)
            return false;

        double totalWeight = 0;
        double weightedLuminance = 0;

        AddBackgroundSamples(slide.Background, ref totalWeight, ref weightedLuminance);

        foreach (SlideLayer layer in slide.Layers)
        {
            if (!layer.Visible)
                continue;

            AddLayerSamples(layer, ref totalWeight, ref weightedLuminance);
        }

        if (totalWeight <= 0)
            return false;

        return (weightedLuminance / totalWeight) > LuminanceThreshold;
    }

    /// <summary>Cache-key segment for the content-derived checkerboard palette.</summary>
    public static string GetCacheKeySegment(PresentationSlide slide) =>
        UseDarkCheckerboard(slide) ? "cb-dark" : "cb-light";

    private static void AddBackgroundSamples(
        SlideBackground? background,
        ref double totalWeight,
        ref double weightedLuminance)
    {
        switch (background)
        {
            case SolidSlideBackground solid:
                AddColorSample(solid.Color, StageAreaWeight, ref totalWeight, ref weightedLuminance);
                break;

            case GradientSlideBackground gradient when gradient.Stops.Count > 0:
            {
                double stopWeight = StageAreaWeight / gradient.Stops.Count;
                foreach (GradientStop stop in gradient.Stops)
                    AddColorSample(stop.Color, stopWeight, ref totalWeight, ref weightedLuminance);
                break;
            }

            case TransparentSlideBackground:
            case ImageSlideBackground:
            case VideoSlideBackground:
            case null:
                break;
        }
    }

    private static void AddLayerSamples(
        SlideLayer layer,
        ref double totalWeight,
        ref double weightedLuminance)
    {
        double weight = Math.Max(
            MinimumLayerWeight,
            Math.Max(0, layer.Transform.Width) * Math.Max(0, layer.Transform.Height));

        switch (layer)
        {
            case TextLayer text:
                string textColor = text.Style?.Color ?? "#FFFFFF";
                if (!string.IsNullOrWhiteSpace(text.Content) || text.Style is not null)
                    AddColorSample(textColor, weight, ref totalWeight, ref weightedLuminance);
                break;

            case ShapeLayer shape:
                if (shape.Fills is { Count: > 0 })
                {
                    foreach (LayerFillModel fill in shape.Fills)
                    {
                        if (fill.Enabled == false)
                            continue;

                        AddColorSample(fill.Color, weight, fill.Opacity, ref totalWeight, ref weightedLuminance);
                    }
                }
                else
                {
                    AddColorSample(shape.Style.Fill, weight, shape.Style.FillOpacity, ref totalWeight, ref weightedLuminance);
                }

                break;

            case VectorLayer vector when vector.Fills is { Count: > 0 }:
                foreach (LayerFillModel fill in vector.Fills)
                {
                    if (fill.Enabled == false)
                        continue;

                    AddColorSample(fill.Color, weight, fill.Opacity, ref totalWeight, ref weightedLuminance);
                }

                break;
        }
    }

    private static void AddColorSample(
        string? color,
        double weight,
        ref double totalWeight,
        ref double weightedLuminance) =>
        AddColorSample(color, weight, opacity: 1, ref totalWeight, ref weightedLuminance);

    private static void AddColorSample(
        string? color,
        double weight,
        double opacity,
        ref double totalWeight,
        ref double weightedLuminance)
    {
        if (weight <= 0 || !TryParseOpaqueRgb(color, out byte r, out byte g, out byte b))
            return;

        double effectiveWeight = weight * Math.Clamp(opacity, 0, 1);
        if (effectiveWeight <= 0)
            return;

        totalWeight += effectiveWeight;
        weightedLuminance += GetRelativeLuminance(r, g, b) * effectiveWeight;
    }

    private static bool TryParseOpaqueRgb(string? value, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        if (trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!trimmed.StartsWith('#'))
        {
            return trimmed.ToLowerInvariant() switch
            {
                "white" => SetRgb(255, 255, 255, out r, out g, out b),
                "black" => SetRgb(0, 0, 0, out r, out g, out b),
                _ => false,
            };
        }

        string hex = trimmed[1..];
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));

        if (hex.Length == 6)
        {
            if (!TryParseBytePair(hex, 0, out r)
                || !TryParseBytePair(hex, 2, out g)
                || !TryParseBytePair(hex, 4, out b))
                return false;

            return true;
        }

        if (hex.Length == 8
            && TryParseBytePair(hex, 0, out byte a)
            && TryParseBytePair(hex, 2, out r)
            && TryParseBytePair(hex, 4, out g)
            && TryParseBytePair(hex, 6, out b))
        {
            return a > 0;
        }

        return false;
    }

    private static bool SetRgb(byte red, byte green, byte blue, out byte r, out byte g, out byte b)
    {
        r = red;
        g = green;
        b = blue;
        return true;
    }

    private static bool TryParseBytePair(string hex, int start, out byte value)
    {
        value = 0;
        try
        {
            value = Convert.ToByte(hex.Substring(start, 2), 16);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static double GetRelativeLuminance(byte r, byte g, byte b)
    {
        static double Linearize(byte channel)
        {
            double normalized = channel / 255d;
            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        double rs = Linearize(r);
        double gs = Linearize(g);
        double bs = Linearize(b);
        return (0.2126 * rs) + (0.7152 * gs) + (0.0722 * bs);
    }
}
