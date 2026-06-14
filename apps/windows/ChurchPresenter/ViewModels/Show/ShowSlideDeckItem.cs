using System.Globalization;
using System.Text;


using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Controls;
using ChurchPresenter.Controls.Rendering.Engine;
using ChurchPresenter.Hosting;

using static ChurchPresenter.Hosting.DeckThumbnailVisibilityDiagnostics;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

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
    /// <param name="thumbnailProject">Project used by <c>ThumbnailEngine</c> to rasterize deck thumbnails; required for thumbnails.</param>
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
        UpdateThumbnailBackdropChrome();
    }

    public PresentationSlide Slide { get; private set; }

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

    private ImageSource? _thumbnail;
    private bool _thumbnailIsNavigationSnapshot;
    private bool _thumbnailRequested;
    private int _thumbnailRequestGeneration = -1;
    private bool _suppressThumbnailRefresh;
    private bool _thumbnailContentRefreshPending;
    private int _thumbnailContentRevision;
    private bool _thumbnailPreviewInstantSwap;
    private bool _previewRefreshEnqueuePending;
    private bool _skipPreviewSlideThumbnailRefresh;
    private Brush? _thumbnailCheckerboardBrush;
    private Brush? _deckListCheckerboardBrush;
    private Brush? _thumbnailBackdropBrush;

    /// <summary>
    /// Offscreen-rasterized slide thumbnail produced by <c>ThumbnailEngine</c>. Raster work is
    /// scheduled only from the deck-thumbnail warm queue after Show compositor settle —
    /// never from the property getter (ItemsRepeater bind must not start GPU work).
    /// </summary>
    public ImageSource? Thumbnail => _thumbnail;

    /// <summary>Applies a cached thumbnail when available; never starts GPU raster.</summary>
    public bool ApplyCachedThumbnail()
    {
        if (!TryResolveCachedThumbnail())
            return false;

        PublishThumbnailBinding();
        return true;
    }

    /// <summary>True when <see cref="Thumbnail"/> is a temporary navigation copy, not a live compositor surface.</summary>
    internal bool IsNavigationSnapshot => _thumbnailIsNavigationSnapshot;

    /// <summary>True while a content refresh is queued but the current image should stay visible.</summary>
    internal bool IsThumbnailContentRefreshPending => _thumbnailContentRefreshPending;

    /// <summary>Quick Edit typing preview swaps thumbnail images instantly instead of crossfading.</summary>
    public bool ThumbnailPreviewInstantSwap => _thumbnailPreviewInstantSwap;

    /// <summary>Checkerboard behind card-layout thumbnails when deck background opacity is below 100%.</summary>
    public Brush? ThumbnailCheckerboardBrush => _thumbnailCheckerboardBrush;

    /// <summary>Checkerboard behind list-layout thumbnails when deck background opacity is below 100%.</summary>
    public Brush? DeckListCheckerboardBrush => _deckListCheckerboardBrush;

    /// <summary>Semi-transparent or opaque deck tint painted over the checkerboard.</summary>
    public Brush? ThumbnailBackdropBrush => _thumbnailBackdropBrush;

    /// <summary>Checkerboard is hidden only when the deck background is fully opaque.</summary>
    public bool ThumbnailShowsCheckerboard => ThumbnailBgColor.A < 255;

    /// <summary>Deck tint is shown whenever background opacity is above zero.</summary>
    public bool ThumbnailShowsBackdrop => ThumbnailBgColor.A > 0;

    internal string GetThumbnailCacheKey() => BuildThumbnailCacheKey();

    /// <summary>Prefix shared by every raster variant for this deck row (background, checkerboard tone, etc.).</summary>
    internal string GetThumbnailCacheKeyPrefix() => BuildThumbnailCacheKeyPrefix();

    /// <summary>Binds a navigation-safe display copy (pixel-buffer clone, not a live compositor surface).</summary>
    internal void BindNavigationDisplay(ImageSource snapshot, string moment)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _thumbnailRequested = false;
        _thumbnailRequestGeneration = -1;
        _thumbnail = snapshot;
        _thumbnailIsNavigationSnapshot = true;
        PublishThumbnailBinding();
        LogItem(moment, this);
    }

    /// <summary>Replaces a navigation snapshot with a fresh compositor thumbnail.</summary>
    internal void CommitCompositorThumbnail(ImageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _thumbnail = source;
        _thumbnailIsNavigationSnapshot = false;
        _thumbnailRequested = false;
        _thumbnailRequestGeneration = -1;
        _thumbnailContentRefreshPending = false;
        PublishThumbnailBinding();
        LogItem("compositor.commit", this);
    }

    /// <summary>Clears the bound thumbnail when Show is hidden off the visual tree.</summary>
    public void ReleasePublishedThumbnail(string moment)
    {
        bool hadPublished = _thumbnail is not null;
        _thumbnailRequested = false;
        _thumbnailRequestGeneration = -1;
        _thumbnail = null;
        _thumbnailIsNavigationSnapshot = false;
        if (hadPublished)
        {
            PublishThumbnailBinding();
            LogItem(moment, this);
        }
    }

    /// <summary>Rasterizes into the app cache and binds when still current. Called only from ShowViewModel warm queue.</summary>
    internal async Task WarmThumbnailCacheAsync(CancellationToken cancellationToken)
    {
        if (_thumbnailRequested && !_thumbnailContentRefreshPending)
            return;

        if (_thumbnail is not null && !_thumbnailIsNavigationSnapshot && !_thumbnailContentRefreshPending)
            return;

        if (ShowRenderBisect.SuppressSlideDeckThumbnails)
            return;

        if (!_thumbnailContentRefreshPending
            && !_thumbnailIsNavigationSnapshot
            && ApplyCachedThumbnail())
            return;

        cancellationToken.ThrowIfCancellationRequested();

        int requestGeneration = App.Services?.GetService<IShowRenderSession>()?.Generation ?? 0;
        _thumbnailRequested = true;
        _thumbnailRequestGeneration = requestGeneration;
        await LoadThumbnailAsync(requestGeneration, cancellationToken).ConfigureAwait(true);
    }

    private bool TryResolveCachedThumbnail()
    {
        if (_thumbnail is not null)
            return true;

        if (App.Services?.GetService<ISlideDeckThumbnailCache>() is not { } cache)
            return false;

        string cacheKey = BuildThumbnailCacheKey();
        if (!cache.TryGet(cacheKey, out ImageSource? cached) || cached is null)
            return false;

        _thumbnail = cached;
        return true;
    }

    private async Task LoadThumbnailAsync(int requestGeneration, CancellationToken cancellationToken)
    {
        if (App.Services?.GetService<ISlideDeckThumbnailCache>() is not { } cache
            || App.Services?.GetService<ThumbnailEngine>() is not { } engine)
        {
            return;
        }

        string cacheKey = BuildThumbnailCacheKey();
        IShowRenderSession? session = App.Services?.GetService<IShowRenderSession>();
        CancellationToken navigationToken = session?.NavigationWorkToken ?? CancellationToken.None;
        int contentRevision = _thumbnailContentRevision;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            navigationToken);

        try
        {
            if (_thumbnailContentRefreshPending)
            {
                DeckThumbnailRasterBundle bundle = await engine.RenderSlideBundleAsync(
                    ThumbnailProject,
                    ThumbnailPreviewSlide,
                    RenderIntent.Thumbnail,
                    cancellationToken: linked.Token).ConfigureAwait(true);

                if (!IsThumbnailRequestCurrent(requestGeneration, contentRevision))
                    return;

                cache.UpdateCompositorSurface(cacheKey, bundle.Source, bundle);

                if (!ShouldPublishThumbnailBinding())
                {
                    _thumbnailRequested = false;
                    _thumbnailRequestGeneration = -1;
                    _thumbnailContentRefreshPending = false;
                    return;
                }

                CommitCompositorThumbnail(bundle.Source);
                return;
            }

            ImageSource? rendered = await cache.GetOrRenderAsync(
                cacheKey,
                async renderToken =>
                {
                    return await engine.RenderSlideBundleAsync(
                        ThumbnailProject,
                        ThumbnailPreviewSlide,
                        RenderIntent.Thumbnail,
                        cancellationToken: renderToken).ConfigureAwait(true);
                },
                linked.Token).ConfigureAwait(true);

            if (!IsThumbnailRequestCurrent(requestGeneration, contentRevision))
                return;

            if (rendered is null)
            {
                _thumbnailRequested = false;
                _thumbnailRequestGeneration = -1;
                return;
            }

            if (ShouldPublishThumbnailBinding())
            {
                CommitCompositorThumbnail(rendered);
                return;
            }

            _thumbnailRequested = false;
            _thumbnailRequestGeneration = -1;
        }
        catch (OperationCanceledException)
        {
            _thumbnailRequested = false;
            _thumbnailRequestGeneration = -1;
        }
        catch (Exception)
        {
            _thumbnailRequested = false;
            _thumbnailRequestGeneration = -1;
        }
    }

    private void PublishThumbnailBinding() => OnPropertyChanged(nameof(Thumbnail));

    private static bool ShouldPublishThumbnailBinding()
    {
        if (App.Services?.GetService<IShowRenderSession>() is not { } session)
            return true;

        return session.ThumbnailRasterPhase == DeckThumbnailRasterPhase.OnFrameReady;
    }

    private bool IsThumbnailRequestCurrent(int requestGeneration, int contentRevision)
    {
        if (_thumbnailContentRevision != contentRevision)
            return false;

        if (_thumbnailRequestGeneration != requestGeneration)
            return false;

        if (App.Services?.GetService<IShowRenderSession>() is not { } session)
            return true;

        return session.Generation == requestGeneration
            && session.ThumbnailRasterPhase != DeckThumbnailRasterPhase.Blocked;
    }

    private string BuildThumbnailCacheKeyPrefix()
    {
        string projectKey = ThumbnailProject?.SourcePath
            ?? ThumbnailProject?.Manifest.PresentationId
            ?? "none";
        return string.Join('|', ThumbnailPreviewSlide.Id, InstanceKey, projectKey) + "|";
    }

    private string BuildThumbnailCacheKey() =>
        string.Join(
            '|',
            BuildThumbnailCacheKeyPrefix().TrimEnd('|'),
            ThumbnailStageWidth,
            ThumbnailStageHeight);

    /// <summary>Applies a deck-pref background colour without per-row refresh scheduling.</summary>
    internal void SetThumbnailBgColorForDeckPref(Color color)
    {
        _suppressThumbnailRefresh = true;
        try
        {
            if (ThumbnailBgColor == color)
            {
                UpdateThumbnailBackdropChrome();
                return;
            }

            ThumbnailBgColor = color;
        }
        finally
        {
            _suppressThumbnailRefresh = false;
        }
    }

    internal void InvalidateForThumbnailRerender() => RequestThumbnailContentRefresh("invalidate");

    private void RequestThumbnailRefresh(string moment)
    {
        if (_suppressThumbnailRefresh)
            return;

        if (App.Services?.GetService<IShowDeckThumbnailService>() is not { } thumbnails
            || App.Services?.GetService<ShowViewModel>() is not { } show)
            return;

        if (IsSmoothContentRefreshMoment(moment))
            thumbnails.RefreshDeckThumbnailContent(show, this, moment);
        else
            thumbnails.RefreshDeckThumbnail(show, this, moment);
    }

    private static bool IsSmoothContentRefreshMoment(string moment) =>
        moment is "preview.changed.commit" or "slide.updated";

    private void InvalidateThumbnail()
    {
        _thumbnail = null;
        _thumbnailIsNavigationSnapshot = false;
        _thumbnailRequested = false;
        _thumbnailRequestGeneration = -1;
        OnPropertyChanged(nameof(Thumbnail));
    }

    [ObservableProperty]
    private PresentationSlide _thumbnailPreviewSlide;

    partial void OnThumbnailPreviewSlideChanged(PresentationSlide value)
    {
        _ = value;
        UpdateThumbnailBackdropChrome();
        if (_skipPreviewSlideThumbnailRefresh)
            return;

        RequestThumbnailContentRefresh("preview.changed");
    }

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

    partial void OnThumbnailBgColorChanged(Color value)
    {
        _ = value;
        UpdateThumbnailBackdropChrome();
    }

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

    /// <summary>
    /// Concatenated visible text-layer content from the slide, used in the text-only and list deck views.
    /// </summary>
    public string RawText => BuildRawText(Slide);

    /// <summary>Width of the thumbnail shown in list mode, preserving 16:9 aspect ratio.</summary>
    public double DeckListThumbWidth => DeckListItemHeight * 16.0 / 9.0;

    /// <summary>
    /// Refreshes deck chrome bindings after app light/dark theme change.
    /// Thumbnail raster is content-derived and is not invalidated here.
    /// </summary>
    public void NotifyThemeChromeChanged()
    {
        OnPropertyChanged(nameof(ThumbnailContainerBrush));
        OnPropertyChanged(nameof(FooterForegroundBrush));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(RawText));
    }

    /// <summary>Updates the row slide model after an in-place text mutation without recreating the deck item.</summary>
    public void ApplySlideModel(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        Slide = slide;
        _skipPreviewSlideThumbnailRefresh = true;
        try
        {
            ThumbnailPreviewSlide = slide;
        }
        finally
        {
            _skipPreviewSlideThumbnailRefresh = false;
        }

        OnPropertyChanged(nameof(Slide));
        OnPropertyChanged(nameof(RawText));
        OnPropertyChanged(nameof(ThumbnailMediaLayers));
        RequestThumbnailContentRefresh("slide.updated");
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
        UpdateThumbnailBackdropChrome();
        RequestThumbnailContentRefresh("media.cue");
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
        UpdateThumbnailBackdropChrome();
    }

    internal void CancelPendingThumbnailPreviewRefresh() => _previewRefreshEnqueuePending = false;

    private void RequestThumbnailContentRefresh(string moment)
    {
        _thumbnailContentRevision++;
        _thumbnailContentRefreshPending = true;
        _thumbnailRequested = false;
        _thumbnailRequestGeneration = -1;
        SetThumbnailPreviewInstantSwap(moment is "preview.changed" or "preview.changed.commit");

        if (moment == "preview.changed")
        {
            ScheduleCoalescedPreviewRefresh();
            return;
        }

        RequestThumbnailRefresh(moment);
    }

    private void SetThumbnailPreviewInstantSwap(bool enabled)
    {
        if (_thumbnailPreviewInstantSwap == enabled)
            return;

        _thumbnailPreviewInstantSwap = enabled;
        OnPropertyChanged(nameof(ThumbnailPreviewInstantSwap));
    }

    private void ScheduleCoalescedPreviewRefresh()
    {
        if (_previewRefreshEnqueuePending)
            return;

        DispatcherQueue? queue = App.MainWindow?.DispatcherQueue;
        if (queue is null)
        {
            RequestThumbnailRefresh("preview.changed.commit");
            return;
        }

        _previewRefreshEnqueuePending = true;
        queue.TryEnqueue(DispatcherQueuePriority.Normal, CoalescedPreviewRefreshTick);
    }

    private void CoalescedPreviewRefreshTick()
    {
        _previewRefreshEnqueuePending = false;
        if (!_thumbnailContentRefreshPending)
            return;

        SetThumbnailPreviewInstantSwap(true);
        RequestThumbnailRefresh("preview.changed.commit");
    }

    private void UpdateThumbnailBackdropChrome()
    {
        bool showsCheckerboard = ThumbnailBgColor.A < 255;
        bool showsBackdrop = ThumbnailBgColor.A > 0;
        bool checkerboardIsDark = SlideCheckerboardTone.UseDarkCheckerboard(ThumbnailPreviewSlide);

        int cardWidth = Math.Max(1, (int)Math.Round(ThumbnailPreviewWidth));
        int cardHeight = Math.Max(1, (int)Math.Round(ThumbnailPreviewHeight));
        int listWidth = Math.Max(1, (int)Math.Round(DeckListThumbWidth));
        int listHeight = Math.Max(1, (int)Math.Round(DeckListItemHeight));
        int cardCellSize = Math.Max(3, cardWidth / CheckerboardPatternBrush.ThumbnailTileCount);
        int listCellSize = Math.Max(3, listWidth / CheckerboardPatternBrush.ThumbnailTileCount);

        _thumbnailCheckerboardBrush = showsCheckerboard
            ? CheckerboardPatternBrush.CreateBrush(cardWidth, cardHeight, cardCellSize, checkerboardIsDark)
            : null;
        _deckListCheckerboardBrush = showsCheckerboard
            ? CheckerboardPatternBrush.CreateBrush(listWidth, listHeight, listCellSize, checkerboardIsDark)
            : null;
        _thumbnailBackdropBrush = showsBackdrop
            ? new SolidColorBrush(ThumbnailBgColor)
            : null;

        OnPropertyChanged(nameof(ThumbnailCheckerboardBrush));
        OnPropertyChanged(nameof(DeckListCheckerboardBrush));
        OnPropertyChanged(nameof(ThumbnailBackdropBrush));
        OnPropertyChanged(nameof(ThumbnailShowsCheckerboard));
        OnPropertyChanged(nameof(ThumbnailShowsBackdrop));
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
