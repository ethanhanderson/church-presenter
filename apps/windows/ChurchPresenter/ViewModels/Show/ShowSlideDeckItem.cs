using System.Globalization;
using System.Text;


using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

using Colors = Microsoft.UI.Colors;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// One slide in the Show deck grid: one rounded group-color card behind inset thumb + footer.
/// </summary>
public sealed partial class ShowSlideDeckItem : ObservableObject
{
    private const double ThumbnailCardInnerWidthValue = 228;
    private const double ThumbnailCardOuterInsetValue = 6;
    private const double ThumbnailPreviewInsetValue = 4;
    private const double ThumbnailFooterHeightValue = 30;

    public ShowSlideDeckItem(PresentationSlide slide, int ordinal)
        : this(slide, ordinal, null, null, null)
    {
    }

    /// <param name="slide">Slide model.</param>
    /// <param name="ordinal">1-based index in the deck.</param>
    /// <param name="presentationPath">When set (browse stack), selection matches this path plus slide id.</param>
    /// <param name="thumbnailProject">Project used by <see cref="ChurchPresenter.Controls.SlideStageView"/>; required for thumbnails.</param>
    /// <param name="instanceKey">Stable playback instance key; null falls back to slide ID.</param>
    public ShowSlideDeckItem(PresentationSlide slide, int ordinal, string? presentationPath, PresentationProject? thumbnailProject, string? instanceKey = null)
    {
        Slide = slide;
        _thumbnailPreviewSlide = slide;
        Ordinal = ordinal;
        PresentationPath = presentationPath;
        ThumbnailProject = thumbnailProject;
        InstanceKey = instanceKey ?? slide.Id;
        FooterLabel = BuildFooterLabel(slide);
        _groupBaseColor = ParseHex(SlideGroupThumbnailColors.GetHexColorForSlide(slide));
    }

    public PresentationSlide Slide { get; }

    /// <summary>
    /// Stable playback instance key used to identify this deck item within the arranged sequence,
    /// disambiguating repeated group occurrences.
    /// </summary>
    public string InstanceKey { get; }

    /// <summary>Parent .cpres path when shown in the multi-presentation browse stack; otherwise null.</summary>
    public string? PresentationPath { get; }

    /// <summary>Resolved project for rendering this slide (browse stack or single-deck).</summary>
    public PresentationProject? ThumbnailProject { get; }

    /// <summary>Logical slide width for thumbnail layout (manifest aspect / slide size).</summary>
    public int ThumbnailStageWidth => GetThumbnailBaseSize().Width;

    /// <summary>Logical slide height for thumbnail layout (manifest aspect / slide size).</summary>
    public int ThumbnailStageHeight => GetThumbnailBaseSize().Height;

    /// <summary>Logical outer width for the uniformly scaled slide card.</summary>
    public double ThumbnailCardOuterWidth => ThumbnailCardInnerWidth + (ThumbnailCardOuterInset * 2);

    /// <summary>Logical outer height for the uniformly scaled slide card.</summary>
    public double ThumbnailCardOuterHeight => ThumbnailCardInnerHeight + (ThumbnailCardOuterInset * 2);

    /// <summary>Logical width of the colored card behind the thumbnail and footer.</summary>
    public double ThumbnailCardInnerWidth => ThumbnailCardInnerWidthValue;

    /// <summary>Logical height of the colored card behind the thumbnail and footer.</summary>
    public double ThumbnailCardInnerHeight => ThumbnailPreviewInset + ThumbnailPreviewHeight + ThumbnailFooterHeight;

    /// <summary>Logical width of the rendered preview surface inside the card.</summary>
    public double ThumbnailPreviewWidth => ThumbnailCardInnerWidth - (ThumbnailPreviewInset * 2);

    /// <summary>Logical height of the rendered preview surface, preserving presentation aspect ratio.</summary>
    public double ThumbnailPreviewHeight => ThumbnailPreviewWidth * ThumbnailStageHeight / ThumbnailStageWidth;

    /// <summary>Logical footer height under the preview surface.</summary>
    public double ThumbnailFooterHeight => ThumbnailFooterHeightValue;

    /// <summary>Logical inset between the selection outline and the colored card.</summary>
    public double ThumbnailCardOuterInset => ThumbnailCardOuterInsetValue;

    /// <summary>Logical inset around the preview surface inside the colored card.</summary>
    public double ThumbnailPreviewInset => ThumbnailPreviewInsetValue;

    /// <summary>1-based slide index in the deck.</summary>
    public int Ordinal { get; }

    /// <summary>
    /// When false (not the first slide in a contiguous group run), the footer hides the section label so only the ordinal shows; group color still indicates the section (ProPresenter-style).
    /// </summary>
    [ObservableProperty]
    private bool _showFooterSectionLabel = true;

    public string FooterLabel { get; }

    public MediaLayersState ThumbnailMediaLayers => SlideMediaLayerBuilder.Build(Slide);

    [ObservableProperty]
    private PresentationSlide _thumbnailPreviewSlide;

    /// <summary>True when this row is the current operator selection.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True when this row matches the current program payload on the slide output layer.</summary>
    [ObservableProperty]
    private bool _isLive;

    /// <summary>
    /// Solid colour painted behind a transparent-background thumbnail instead of the checkerboard.
    /// Set to <see cref="Colors.Transparent"/> to fall back to the checkerboard pattern.
    /// </summary>
    [ObservableProperty]
    private Color _thumbnailBgColor = Colors.Transparent;

    /// <summary>Height of each list-view row (thumbnail height). Driven by DeckScaleStep on the ViewModel.</summary>
    [ObservableProperty]
    private double _deckListItemHeight = 70;

    private readonly Color _groupBaseColor;

    /// <summary>Card fill behind inset thumbnail and footer (group-tinted in-app acrylic).</summary>
    public Brush ThumbnailContainerBrush => InAppAcrylicBrushFactory.CreateGroupTint(_groupBaseColor);

    public Brush FooterForegroundBrush => new SolidColorBrush(ContrastForeground(_groupBaseColor));

    /// <summary>Operator selection ring — shown whenever this row is selected, including when it is also live.</summary>
    public bool ShowSelectedOutline => IsSelected;

    /// <summary>Green program ring shown when this row is live on the slide output layer.</summary>
    public bool ShowLiveSlideOutputOutline => IsLive;

    public bool IsDisabled => Slide.Disabled;

    public double CardOpacity => IsDisabled ? 0.45 : 1;

    public bool HasSlideTransitionOverride =>
        !string.IsNullOrWhiteSpace(Slide.Animations?.Transition?.Type);

    public string SlideTransitionOverrideBadgeText
    {
        get
        {
            var label = MediaCueTransitionFormatter.FormatLabel(Slide.Animations?.Transition);
            return string.IsNullOrWhiteSpace(label) ? "Transition" : label;
        }
    }

    public string SlideTransitionOverrideToolTip =>
        $"Slide transition override: {SlideTransitionOverrideBadgeText}";

    /// <summary>
    /// Concatenated visible text-layer content from the slide, used in the text-only and list deck views.
    /// </summary>
    public string RawText => BuildRawText(Slide);

    /// <summary>Width of the thumbnail shown in list mode, preserving 16:9 aspect ratio.</summary>
    public double DeckListThumbWidth => DeckListItemHeight * 16.0 / 9.0;

    /// <summary>Call when app theme (light/dark) changes so theme brush lookups refresh.</summary>
    public void NotifyThemeChromeChanged()
    {
        OnPropertyChanged(nameof(ThumbnailContainerBrush));
        OnPropertyChanged(nameof(FooterForegroundBrush));
        OnPropertyChanged(nameof(ThumbnailMediaLayers));
        OnPropertyChanged(nameof(ThumbnailPreviewSlide));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(RawText));
    }

    public void SetTransientThumbnailPreview(IEnumerable<TextLayer> editableTextLayers, IDictionary<string, string> textUpdates)
    {
        ArgumentNullException.ThrowIfNull(editableTextLayers);
        ArgumentNullException.ThrowIfNull(textUpdates);

        var preview = PresentationModelUtilities.CloneSlide(Slide);
        foreach (var editableLayer in editableTextLayers)
        {
            if (preview.Layers.OfType<TextLayer>().Any(layer => string.Equals(layer.Id, editableLayer.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            preview.Layers.Add(PresentationModelUtilities.DeepClone(editableLayer) ?? editableLayer);
        }

        foreach (var textLayer in preview.Layers.OfType<TextLayer>())
        {
            if (textUpdates.TryGetValue(textLayer.Id, out var content))
                textLayer.Content = content;
        }

        ThumbnailPreviewSlide = preview;
    }

    public void ResetTransientThumbnailPreview()
    {
        ThumbnailPreviewSlide = Slide;
    }

    /// <summary>Refreshes bindings that depend on the slide's media cues.</summary>
    public void NotifyMediaCueChanged()
    {
        OnPropertyChanged(nameof(ThumbnailMediaLayers));
        OnPropertyChanged(nameof(ThumbnailPreviewSlide));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSelectedOutline));
    }

    partial void OnIsLiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLiveSlideOutputOutline));
    }

    partial void OnDeckListItemHeightChanged(double value)
    {
        OnPropertyChanged(nameof(DeckListThumbWidth));
    }

    private SlideSizeDto GetThumbnailBaseSize() =>
        PresentationModelUtilities.GetBaseSlideSize(
            ThumbnailProject?.Manifest.AspectRatio,
            ThumbnailProject?.Manifest.SlideSize);

    private static string BuildFooterLabel(PresentationSlide slide)
    {
        if (!string.IsNullOrWhiteSpace(slide.SectionLabel))
            return slide.SectionLabel.Trim();
        if (!string.IsNullOrWhiteSpace(slide.Section))
            return PresentationModelUtilities.FormatSectionLabel(slide.Section, slide.SectionIndex);
        return "Slide";
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6)
            return Color.FromArgb(255, 100, 116, 139);

        return Color.FromArgb(
            255,
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static Color ContrastForeground(Color background)
    {
        var lum = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return lum > 0.55 ? Color.FromArgb(255, 15, 23, 42) : Color.FromArgb(255, 255, 255, 255);
    }

    private static string BuildRawText(PresentationSlide slide)
    {
        var sb = new StringBuilder();
        foreach (var layer in slide.Layers)
        {
            if (!layer.Visible || layer is not TextLayer textLayer)
                continue;
            if (string.IsNullOrWhiteSpace(textLayer.Content))
                continue;
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(textLayer.Content.Trim());
        }

        return sb.ToString();
    }
}