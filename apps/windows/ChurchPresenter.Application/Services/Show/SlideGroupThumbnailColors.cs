using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;


namespace ChurchPresenter.Services.Show;

/// <summary>
/// Maps slide section / group labels to thumbnail rim colors (ProPresenter-style group palette).
/// </summary>
public static class SlideGroupThumbnailColors
{
    // Verses — blues
    private const string VerseStandardBlue = "#2563EB";
    private const string VerseNavy = "#1E3A8A";
    private const string VerseDark = "#0F172A";

    // Choruses — magentas / burgundy
    private const string ChorusBright = "#EC4899";
    private const string ChorusDeep = "#BE185D";
    private const string ChorusBurgundy = "#881337";

    // Bridges — purples
    private const string BridgeBright = "#A855F7";
    private const string BridgeMid = "#7E22CE";
    private const string BridgeDark = "#4C1D95";

    // Other parts
    private const string PreChorusPink = "#F472B6";
    private const string TagRed = "#EF4444";
    private const string IntroOlive = "#9CAF3A";
    private const string EndingOlive = "#6B7C3F";
    private const string OutroOlive = "#4A5D2E";
    private const string KellyGreen = "#22C55E";

    private const string BlankBlack = "#000000";
    private const string NeutralFallback = "#64748B";

    /// <summary>Returns a CSS-style #RRGGBB color for a section group, matching slide thumbnail group colors.</summary>
    public static string GetHexColorForSectionGroup(SectionGroup group, PresentationProject? project)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (project?.Slides != null && group.SlideIds.Count > 0)
        {
            foreach (var id in group.SlideIds)
            {
                var slide = project.Slides.FirstOrDefault(s =>
                    string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
                if (slide != null)
                    return GetHexColorForSlide(slide);
            }
        }

        var synthetic = new PresentationSlide
        {
            Type = "content",
            Section = group.Section,
            SectionLabel = string.IsNullOrWhiteSpace(group.Label) ? null : group.Label,
            SectionIndex = null,
        };
        return GetHexColorForSlide(synthetic);
    }

    /// <summary>Returns a CSS-style #RRGGBB color for the slide's group.</summary>
    public static string GetHexColorForSlide(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);

        if (string.Equals(slide.Type, "blank", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(slide.Section) &&
            string.IsNullOrWhiteSpace(slide.SectionLabel))
            return BlankBlack;

        if (!string.IsNullOrWhiteSpace(slide.SectionLabel))
        {
            var fromExplicitLabel = ResolveFromLabel(slide.SectionLabel.Trim());
            if (fromExplicitLabel != null)
                return fromExplicitLabel;
        }

        var fromSection = TryResolveFromSection(slide);
        if (fromSection != null)
            return fromSection;

        var label = BuildFooterLabel(slide);
        return ResolveFromLabel(label) ?? NeutralFallback;
    }

    private static string? TryResolveFromSection(PresentationSlide slide)
    {
        var section = slide.Section?.Trim();
        if (string.IsNullOrEmpty(section))
            return null;

        var s = section.ToLowerInvariant();
        var idx = slide.SectionIndex;

        switch (s)
        {
            case "verse":
                return VerseColorForIndex(idx);
            case "chorus":
                return ChorusColorForIndex(idx);
            case "bridge":
                return BridgeColorForIndex(idx);
            case "pre-chorus":
            case "prechorus":
                return PreChorusPink;
            case "tag":
                return TagRed;
            case "intro":
                return IntroOlive;
            case "ending":
                return EndingOlive;
            case "outro":
                return OutroOlive;
            case "interlude":
            case "vamp":
            case "turnaround":
                return KellyGreen;
            case "title":
                return NeutralFallback;
            case "refrain":
                return ChorusBright;
        }

        return null;
    }

    private static string VerseColorForIndex(int? sectionIndex)
    {
        if (sectionIndex is null or < 0)
            return VerseStandardBlue;

        var n = sectionIndex.Value + 1;
        return n switch
        {
            1 or 2 or 4 => VerseStandardBlue,
            3 or 5 => VerseNavy,
            6 => VerseDark,
            _ => VerseStandardBlue,
        };
    }

    private static string ChorusColorForIndex(int? sectionIndex)
    {
        if (sectionIndex is null or < 0)
            return ChorusBright;

        var n = sectionIndex.Value + 1;
        return n switch
        {
            1 or 4 => ChorusBright,
            2 => ChorusDeep,
            3 => ChorusBurgundy,
            _ => ChorusBright,
        };
    }

    private static string BridgeColorForIndex(int? sectionIndex)
    {
        if (sectionIndex is null or < 0)
            return BridgeBright;

        var n = sectionIndex.Value + 1;
        return n switch
        {
            1 => BridgeBright,
            2 => BridgeMid,
            3 => BridgeDark,
            _ => BridgeBright,
        };
    }

    private static string BuildFooterLabel(PresentationSlide slide)
    {
        if (!string.IsNullOrWhiteSpace(slide.SectionLabel))
            return slide.SectionLabel.Trim();
        if (!string.IsNullOrWhiteSpace(slide.Section))
            return PresentationModelUtilities.FormatSectionLabel(slide.Section, slide.SectionIndex);
        return "Slide";
    }

    private static string? ResolveFromLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var key = label.Trim();
        var lower = key.ToLowerInvariant();

        if (lower is "blank" or "black")
            return BlankBlack;

        // Normalized hyphen variants
        var hyphen = Regex.Replace(lower, @"\s+", " ");

        // Verse N
        var verseMatch = Regex.Match(hyphen, @"^verse\s*(\d+)?$", RegexOptions.IgnoreCase);
        if (verseMatch.Success)
        {
            if (!verseMatch.Groups[1].Success)
                return VerseStandardBlue;
            var num = int.Parse(verseMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            return num switch
            {
                1 or 2 or 4 => VerseStandardBlue,
                3 or 5 => VerseNavy,
                6 => VerseDark,
                _ => VerseStandardBlue,
            };
        }

        var chorusMatch = Regex.Match(hyphen, @"^chorus\s*(\d+)?$", RegexOptions.IgnoreCase);
        if (chorusMatch.Success)
        {
            if (!chorusMatch.Groups[1].Success)
                return ChorusBright;
            var num = int.Parse(chorusMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            return num switch
            {
                1 or 4 => ChorusBright,
                2 => ChorusDeep,
                3 => ChorusBurgundy,
                _ => ChorusBright,
            };
        }

        var bridgeMatch = Regex.Match(hyphen, @"^bridge\s*(\d+)?$", RegexOptions.IgnoreCase);
        if (bridgeMatch.Success)
        {
            if (!bridgeMatch.Groups[1].Success)
                return BridgeBright;
            var num = int.Parse(bridgeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            return num switch
            {
                1 => BridgeBright,
                2 => BridgeMid,
                3 => BridgeDark,
                _ => BridgeBright,
            };
        }

        return lower switch
        {
            "verse" => VerseStandardBlue,
            "chorus" => ChorusBright,
            "bridge" => BridgeBright,
            "pre-chorus" or "prechorus" => PreChorusPink,
            "tag" => TagRed,
            "intro" => IntroOlive,
            "ending" => EndingOlive,
            "outro" => OutroOlive,
            "interlude" or "vamp" or "turnaround" => KellyGreen,
            "title" => NeutralFallback,
            "refrain" => ChorusBright,
            _ => null,
        };
    }
}