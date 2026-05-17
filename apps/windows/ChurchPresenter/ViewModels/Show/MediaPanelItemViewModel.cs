using System.Globalization;

using ChurchPresenter;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// UI-facing media-browser item with async thumbnail loading for the Show page media panel.
/// </summary>
public sealed partial class MediaPanelItemViewModel : ObservableObject
{
    private readonly IMediaLibraryService _mediaLibrary;
    private readonly string _resolvedFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaPanelItemViewModel"/> class.
    /// </summary>
    /// <param name="mediaItem">The backing media-library item.</param>
    /// <param name="mediaLibrary">Library service used to persist file-derived metadata.</param>
    public MediaPanelItemViewModel(MediaLibraryItem mediaItem, IMediaLibraryService mediaLibrary)
    {
        MediaItem = mediaItem ?? throw new ArgumentNullException(nameof(mediaItem));
        _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
        _resolvedFilePath = _mediaLibrary.ResolveStoredMediaPath(mediaItem.Path);
        _ = LoadThumbnailAsync();
        _ = EnrichFileMetadataAsync();
    }

    /// <summary>
    /// Type used for thumbnails and icons, reconciling <see cref="MediaLibraryItem.Type"/> with the file extension.
    /// </summary>
    private string EffectiveMediaType => MediaInference.ResolveEffectiveMediaType(MediaItem.Type, MediaItem.Path);

    /// <summary>The backing media-library item.</summary>
    public MediaLibraryItem MediaItem { get; }

    /// <summary>Stable media item id.</summary>
    public string Id => MediaItem.Id;

    /// <summary>1-based row index in the current filtered media list (table # column).</summary>
    public int DisplayIndex { get; internal set; }

    /// <summary>When true, the list table shows an inline editor for <see cref="Name"/>.</summary>
    [ObservableProperty]
    private bool _isNameEditMode;

    /// <summary>Draft text for inline rename (list table).</summary>
    [ObservableProperty]
    private string _nameEditDraft = "";

    /// <summary>Display name shown in the media panel.</summary>
    public string Name => MediaItem.Name;

    /// <summary>Absolute file path for the imported media file.</summary>
    public string Path => MediaItem.Path;

    /// <summary>Logical media type: image, video, or audio.</summary>
    public string Type => MediaItem.Type;

    /// <summary>Mutable per-asset cue defaults.</summary>
    public MediaCueDefaults CueDefaults => MediaItem.CueDefaults;

    /// <summary>True when a per-asset transition is set (not only show defaults).</summary>
    public bool IsMediaTransitionExplicit => CueDefaults.Transition != null;

    /// <summary>Catalog transition label for the media table, or an em dash when unset.</summary>
    public string MediaTableTransitionDisplay
    {
        get
        {
            var label = MediaCueTransitionFormatter.FormatLabel(CueDefaults.Transition);
            return string.IsNullOrEmpty(label) ? "—" : label;
        }
    }

    /// <summary>Raises property changed for fields derived from <see cref="CueDefaults"/>.</summary>
    public void NotifyCueDefaultsUiChanged()
    {
        OnPropertyChanged(nameof(IsMediaTransitionExplicit));
        OnPropertyChanged(nameof(MediaTableTransitionDisplay));
    }

    /// <summary>Fallback glyph used when no thumbnail image can be shown.</summary>
    public string TypeGlyph => EffectiveMediaType.ToLowerInvariant() switch
    {
        "video" => "\uE714",
        "audio" => "\uE189",
        _ => "\uE91B",
    };

    /// <summary>Width ÷ height for the thumbnail frame (from metadata or type defaults).</summary>
    public double ThumbnailAspectRatio
    {
        get
        {
            if (MediaItem.Width is > 0 and var w && MediaItem.Height is > 0 and var h)
            {
                return (double)w / h;
            }

            return EffectiveMediaType.ToLowerInvariant() switch
            {
                "audio" => 1.0,
                "video" => 16.0 / 9.0,
                _ => 4.0 / 3.0,
            };
        }
    }

    /// <summary>Formatted duration for video/audio when known.</summary>
    public string DurationLabel
    {
        get
        {
            if (MediaItem.Duration is not { } d || d <= 0)
            {
                return "";
            }

            var ts = TimeSpan.FromSeconds(d);
            if (ts.TotalHours >= 1)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            }

            return string.Format(CultureInfo.CurrentCulture, "{0}:{1:D2}", (int)ts.TotalMinutes, ts.Seconds);
        }
    }

    /// <summary>Duration as <c>HH:MM:SS</c> for the media table; empty when unknown.</summary>
    public string DurationTableLabel
    {
        get
        {
            if (MediaItem.Duration is not { } d || d <= 0)
            {
                return "";
            }

            var ts = TimeSpan.FromSeconds(d);
            var hours = (int)ts.TotalHours;
            return string.Format(CultureInfo.CurrentCulture, "{0:D2}:{1:D2}:{2:D2}", hours, ts.Minutes, ts.Seconds);
        }
    }

    /// <summary>File kind for the table format column (e.g. <c>MP4 File</c>).</summary>
    public string FormatLabel
    {
        get
        {
            var filePath = MediaItem.Path ?? "";
            var ext = global::System.IO.Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            if (ext.Length > 0)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0} File", ext);
            }

            return string.Format(CultureInfo.CurrentCulture, "{0} File", TypeDisplayName);
        }
    }

    /// <summary>True when <see cref="DurationLabel"/> is non-empty.</summary>
    public bool HasDurationLabel => DurationLabel.Length > 0;

    /// <summary>Pixel dimensions when known.</summary>
    public string DimensionsLabel
    {
        get
        {
            if (MediaItem.Width is > 0 and var w && MediaItem.Height is > 0 and var h)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0}×{1}", w, h);
            }

            return "";
        }
    }

    /// <summary>True when <see cref="DimensionsLabel"/> is non-empty.</summary>
    public bool HasDimensionsLabel => DimensionsLabel.Length > 0;

    /// <summary>Duration or dimensions text for the footer; never the media kind (use <see cref="TypeGlyph"/> + <see cref="TypeDisplayName"/>).</summary>
    public string FooterRightMetaText
    {
        get
        {
            if (HasDurationLabel)
            {
                return DurationLabel;
            }

            if (HasDimensionsLabel)
            {
                return DimensionsLabel;
            }

            return "";
        }
    }

    /// <summary>True when <see cref="FooterRightMetaText"/> is non-empty.</summary>
    public bool HasFooterRightMetaText => HasDurationLabel || HasDimensionsLabel;

    /// <summary>Accessible name and tooltip text for the media kind (Video, Audio, Image).</summary>
    public string TypeDisplayName => EffectiveMediaType.ToLowerInvariant() switch
    {
        "video" => "Video",
        "audio" => "Audio",
        "image" => "Image",
        _ => "Media",
    };

    /// <summary>Whether a thumbnail image was successfully loaded.</summary>
    [ObservableProperty]
    private bool _hasThumbnail;

    /// <summary>Thumbnail image source for the media card.</summary>
    [ObservableProperty]
    private ImageSource? _thumbnailSource;

    /// <summary>Loads the current media file thumbnail.</summary>
    /// <returns>A task that completes when the current thumbnail load finishes.</returns>
    public async Task LoadThumbnailAsync()
    {
        var dq = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

        ImageSource? source = null;
        try
        {
            source = await MediaThumbnailLoader.TryLoadAsync(_resolvedFilePath, EffectiveMediaType, 768)
                .ConfigureAwait(false);
        }
        catch
        {
            source = null;
        }

        void Apply()
        {
            ThumbnailSource = source;
            HasThumbnail = source != null;
        }

        if (dq == null)
        {
            Apply();
            return;
        }

        if (dq.HasThreadAccess)
        {
            Apply();
            return;
        }

        dq.TryEnqueue(Apply);
    }

    /// <summary>Probes the file for duration/dimensions when missing and persists to the media library.</summary>
    private async Task EnrichFileMetadataAsync()
    {
        var t = EffectiveMediaType.ToLowerInvariant();
        if (t is not ("video" or "audio"))
        {
            return;
        }

        if (MediaItem.Duration is > 0)
        {
            return;
        }

        MediaFileMetadata? meta;
        try
        {
            meta = await MediaFileMetadataReader.TryReadAsync(_resolvedFilePath, EffectiveMediaType).ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (meta is null)
        {
            return;
        }

        var m = meta.Value;

        var changed = false;
        if (m.DurationSeconds is > 0 && (MediaItem.Duration is not { } ed || ed <= 0))
        {
            MediaItem.Duration = m.DurationSeconds;
            changed = true;
        }

        if (m.Width is > 0 && (MediaItem.Width is not { } w || w <= 0))
        {
            MediaItem.Width = m.Width;
            changed = true;
        }

        if (m.Height is > 0 && (MediaItem.Height is not { } h || h <= 0))
        {
            MediaItem.Height = m.Height;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        try
        {
            await _mediaLibrary.UpdateMediaItemFileMetadataAsync(
                Id,
                m.DurationSeconds,
                m.Width,
                m.Height).ConfigureAwait(true);
        }
        catch
        {
            // UI still shows in-memory values
        }

        NotifyFooterLabelsChanged();
    }

    private void NotifyFooterLabelsChanged()
    {
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(DurationTableLabel));
        OnPropertyChanged(nameof(HasDurationLabel));
        OnPropertyChanged(nameof(DimensionsLabel));
        OnPropertyChanged(nameof(HasDimensionsLabel));
        OnPropertyChanged(nameof(FooterRightMetaText));
        OnPropertyChanged(nameof(HasFooterRightMetaText));
        OnPropertyChanged(nameof(ThumbnailAspectRatio));
    }
}