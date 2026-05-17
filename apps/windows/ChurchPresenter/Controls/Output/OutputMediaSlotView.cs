
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ChurchPresenter.Controls;

/// <summary>
/// Persistent live host for a single output media slot.
/// Kept outside presentation transitions so engine media can survive slide changes unchanged.
/// </summary>
public sealed class OutputMediaSlotView : UserControl
{
    private static readonly TimeSpan MediaReadyTimeout = TimeSpan.FromSeconds(3);

    public static readonly DependencyProperty MediaProperty =
        DependencyProperty.Register(nameof(Media), typeof(OutputLayerMedia), typeof(OutputMediaSlotView),
            new PropertyMetadata(null, OnMediaPropertyChanged));

    public static readonly DependencyProperty PlaceholderBrushProperty =
        DependencyProperty.Register(nameof(PlaceholderBrush), typeof(Brush), typeof(OutputMediaSlotView),
            new PropertyMetadata(null, OnPlaceholderBrushChanged));

    private readonly Grid _root;
    private readonly Border _placeholder;
    private readonly Image _image;
    private readonly MediaPlayerElement _mediaElement;
    private readonly ILogger<OutputMediaSlotView>? _logger;
    private readonly MediaPlayer _player;
    private string? _signature;
    private Task _readyTask = Task.CompletedTask;
    private long _loadVersion;
    private bool _showsImage;
    private bool _usesPlayer;
    private bool _pendingAutoplay;
    private bool _isVideoSlot;
    private bool _requestedMuted;
    private RoutedEventHandler? _imageOpenedHandler;
    private ExceptionRoutedEventHandler? _imageFailedHandler;
    private TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? _mediaFailedHandler;
    private TypedEventHandler<MediaPlaybackSession, object>? _playbackStateChangedHandler;
    // #region agent log
    private static readonly object _dbgSync = new();
    internal static void DbgLog(string hyp, string loc, string msg, string dataJson)
    {
        try
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var line = $"{{\"sessionId\":\"684e34\",\"runId\":\"load-trace\",\"id\":\"log_{ts}_{Environment.CurrentManagedThreadId}\",\"timestamp\":{ts},\"location\":\"{loc}\",\"message\":\"{msg}\",\"data\":{dataJson},\"hypothesisId\":\"{hyp}\"}}\n";
            lock (_dbgSync) System.IO.File.AppendAllText(@"c:\Users\ethan\Documents\Development\NCBF\church-presenter\debug-684e34.log", line);
        }
        catch { }
    }
    // #endregion
    /// <summary>Initializes a new instance of the <see cref="OutputMediaSlotView"/> control.</summary>
    public OutputMediaSlotView()
        : this(App.Services)
    {
    }

    private OutputMediaSlotView(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _logger = services.GetService<ILogger<OutputMediaSlotView>>();
        _root = new Grid();
        _placeholder = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _image = new Image
        {
            Visibility = Visibility.Collapsed,
        };
        _player = CreatePlayer();
        _mediaElement = new MediaPlayerElement
        {
            AreTransportControlsEnabled = false,
            AutoPlay = false,
            Visibility = Visibility.Collapsed,
        };
        _mediaElement.SetMediaPlayer(_player);
        _root.Children.Add(_placeholder);
        _root.Children.Add(_image);
        _root.Children.Add(_mediaElement);
        ApplyPlaceholderBrush();
        Content = _root;
        Unloaded += (_, _) => DisposePlayer();
    }

    /// <summary>The current media payload for the slot.</summary>
    public OutputLayerMedia? Media
    {
        get => (OutputLayerMedia?)GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    public Brush? PlaceholderBrush
    {
        get => (Brush?)GetValue(PlaceholderBrushProperty);
        set => SetValue(PlaceholderBrushProperty, value);
    }

    /// <summary>Currently active media player, if the slot is using media playback.</summary>
    public MediaPlayer? ActivePlayer => _usesPlayer ? _player : null;

    private static void OnMediaPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is OutputMediaSlotView view)
            view.Refresh();
    }

    private static void OnPlaceholderBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is OutputMediaSlotView view)
            view.ApplyPlaceholderBrush();
    }

    internal async Task WaitForReadyAsync()
    {
        Task readyTask = _readyTask;
        if (readyTask.IsCompleted)
        {
            await readyTask.ConfigureAwait(true);
            return;
        }

        try
        {
            await readyTask.WaitAsync(MediaReadyTimeout).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
        }
    }

    internal void BeginRevealPlayback()
    {
        if (_showsImage || _isVideoSlot)
            _placeholder.Visibility = Visibility.Collapsed;

        if (!_usesPlayer)
            return;

        _player.IsMuted = _requestedMuted;

        if (!_pendingAutoplay)
            return;

        try
        {
            // #region agent log
            DbgLog("H-3", "OutputMediaSlotView.cs:BeginRevealPlayback",
                "Play() called",
                $"{{\"playerState\":\"{_player.PlaybackSession.PlaybackState}\",\"pos\":{_player.PlaybackSession.Position.TotalSeconds:F3},\"mediaId\":\"{Media?.MediaId?.Replace("\\", "\\\\")??"null"}\"}}");
            // #endregion
            _player.Play();
            _pendingAutoplay = false;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to begin output media playback.");
        }
    }

    private void Refresh()
    {
        var signature = BuildSignature(Media);
        // #region agent log
        var isHit = string.Equals(signature, _signature, StringComparison.Ordinal);
        DbgLog("H-1", "OutputMediaSlotView.cs:Refresh",
            isHit ? "sig-HIT" : "sig-MISS",
            $"{{\"hit\":{(isHit ? "true" : "false")},\"mediaId\":\"{Media?.MediaId?.Replace("\\", "\\\\")??"null"}\"}}");
        // #endregion
        if (isHit)
            return;

        _signature = signature;
        _loadVersion++;
        DetachMediaEvents();

        ResetVisualState();

        if (Media == null)
            return;

        var source = ResolveSource(Media);
        if (string.IsNullOrWhiteSpace(source))
            return;

        var effectiveType = MediaInference.ResolveEffectiveMediaType(Media.MediaType, source);
        if (string.Equals(effectiveType, "audio", StringComparison.OrdinalIgnoreCase))
        {
            PreparePlayerSource(source, Media, showVideo: false);
            return;
        }

        if (string.Equals(effectiveType, "video", StringComparison.OrdinalIgnoreCase))
        {
            PreparePlayerSource(source, Media, showVideo: true);
            return;
        }

        PrepareImageSource(source, Media);
    }

    private static string? ResolveSource(OutputLayerMedia media)
    {
        if (!string.IsNullOrWhiteSpace(media.ResolvedSourcePath) && File.Exists(media.ResolvedSourcePath))
            return media.ResolvedSourcePath;

        if (!string.IsNullOrWhiteSpace(media.MediaId) && File.Exists(media.MediaId))
            return media.MediaId;

        return media.ResolvedSourcePath ?? media.MediaId;
    }

    private static MediaPlayer CreatePlayer()
    {
        return new MediaPlayer
        {
            AutoPlay = false,
            RealTimePlayback = true,
        };
    }

    private void PrepareImageSource(string source, OutputLayerMedia media)
    {
        _showsImage = true;
        _image.Visibility = Visibility.Visible;
        _image.Stretch = ParseStretch(media.Fit);
        _mediaElement.Visibility = Visibility.Collapsed;

        var loadVersion = _loadVersion;
        var readySource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _readyTask = readySource.Task;

        try
        {
            var bitmap = new BitmapImage(new Uri(source));
            _imageOpenedHandler = (_, _) =>
            {
                if (loadVersion != _loadVersion)
                    return;

                readySource.TrySetResult(true);
                DetachMediaEvents();
            };
            _imageFailedHandler = (_, args) =>
            {
                if (loadVersion != _loadVersion)
                    return;

                _logger?.LogDebug("Failed to open output image media {Path}: {Error}.", source, args.ErrorMessage);
                readySource.TrySetResult(true);
                DetachMediaEvents();
            };

            bitmap.ImageOpened += _imageOpenedHandler;
            bitmap.ImageFailed += _imageFailedHandler;
            _image.Source = bitmap;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to load output image media from {Path}.", source);
            readySource.TrySetResult(true);
        }
    }

    private void PreparePlayerSource(string source, OutputLayerMedia media, bool showVideo)
    {
        _usesPlayer = true;
        _isVideoSlot = showVideo;
        _pendingAutoplay = media.Autoplay;
        _requestedMuted = media.Muted;
        _showsImage = false;
        _image.Visibility = Visibility.Collapsed;
        _mediaElement.Visibility = showVideo ? Visibility.Visible : Visibility.Collapsed;
        _mediaElement.Stretch = ParseStretch(media.Fit);
        _player.IsMuted = media.Muted;
        _player.AutoPlay = false;
        // IsLoopingEnabled is intentionally NOT used for looping: it causes the player to seek
        // back to zero via the rebuffer path, producing a visible freeze at the loop boundary.
        // Instead, a MediaPlaybackList with AutoRepeatEnabled is used, which keeps the decode
        // pipeline alive across the loop boundary for gapless repeat (see CreatePlaybackSource).
        _player.IsLoopingEnabled = false;
        _player.RealTimePlayback = true;

        var loadVersion = _loadVersion;

        // Complete the ready task when the player has the first frame buffered (Paused when AutoPlay=false,
        // or Playing when the player begins faster than the notification arrives). This ensures
        // WaitForReadyAsync holds until the first frame is decoded, so PlayNextTransition never calls
        // BeginRevealPlayback with the player still Opening — eliminating the black-flash on cut transitions.
        var readySource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _readyTask = readySource.Task;

        _mediaFailedHandler = (_, args) =>
        {
            if (loadVersion != _loadVersion)
                return;

            _logger?.LogDebug("Failed to open output media {Path}: {Error}.", source, args.ErrorMessage);
            readySource.TrySetResult(false);
            DetachMediaEvents();
        };

        _player.MediaFailed += _mediaFailedHandler;

        // #region agent log
        var t0Src = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // #endregion
        _playbackStateChangedHandler = (session, _) =>
        {
            if (loadVersion != _loadVersion)
                return;
            var st = session.PlaybackState;
            // #region agent log
            DbgLog("H-3", "OutputMediaSlotView.cs:PlaybackStateChanged",
                $"state={st}",
                $"{{\"state\":\"{st}\",\"elapsedMsSinceSrc\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t0Src},\"mediaId\":\"{media.MediaId?.Replace("\\", "\\\\")??"null"}\"}}");
            // #endregion
            if (st == MediaPlaybackState.Paused
                || st == MediaPlaybackState.Playing)
            {
                readySource.TrySetResult(true);
            }
        };
        _player.PlaybackSession.PlaybackStateChanged += _playbackStateChangedHandler;

        try
        {
            _player.Source = CreatePlaybackSource(new Uri(source), media.Loop);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to prepare output media from {Path}.", source);
            readySource.TrySetResult(false);
            DetachMediaEvents();
            ResetVisualState();
        }
    }

    /// <summary>
    /// Builds the appropriate <see cref="IMediaPlaybackSource"/> for the given URI and loop flag.
    /// When <paramref name="loop"/> is <c>true</c> a single-item <see cref="MediaPlaybackList"/> with
    /// <see cref="MediaPlaybackList.AutoRepeatEnabled"/> is returned instead of a bare
    /// <see cref="MediaSource"/>.  The list-based path keeps the decode pipeline alive across the
    /// loop boundary, producing gapless repeat without the rebuffering stall that
    /// <see cref="MediaPlayer.IsLoopingEnabled"/> causes.
    /// </summary>
    private static IMediaPlaybackSource CreatePlaybackSource(Uri uri, bool loop)
    {
        if (!loop)
            return MediaSource.CreateFromUri(uri);

        var item = new MediaPlaybackItem(MediaSource.CreateFromUri(uri));
        // MaxPlayedItemsToKeepOpen = 1: keep the single item decoded across the loop boundary so the
        // next repeat starts from the already-open pipeline instead of re-initialising from scratch.
        // Without this, the default of 0 closes the item after it plays, causing a rebuffer stall.
        var list = new MediaPlaybackList { AutoRepeatEnabled = true, MaxPlayedItemsToKeepOpen = 1 };
        list.Items.Add(item);
        return list;
    }

    private static Stretch ParseStretch(string? fit)
    {
        return fit?.ToLowerInvariant() switch
        {
            "contain" => Stretch.Uniform,
            "fill" => Stretch.Fill,
            "none" => Stretch.None,
            _ => Stretch.UniformToFill,
        };
    }

    private static string BuildSignature(OutputLayerMedia? media)
    {
        if (media == null)
            return string.Empty;

        return string.Join("|",
            media.MediaId?.Trim() ?? string.Empty,
            media.MediaType?.Trim() ?? string.Empty,
            media.Fit?.Trim() ?? string.Empty,
            media.Loop,
            media.Muted,
            media.Autoplay,
            media.ResolvedSourcePath?.Trim() ?? string.Empty);
    }

    private void DisposePlayer()
    {
        DetachMediaEvents();

        try
        {
            _player.Source = null;
            _player.Pause();
            _mediaElement.SetMediaPlayer(null);
            _player.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to dispose output media player.");
        }
    }

    private void ResetVisualState()
    {
        _readyTask = Task.CompletedTask;
        _showsImage = false;
        _usesPlayer = false;
        _pendingAutoplay = false;
        _isVideoSlot = false;
        _requestedMuted = false;
        _placeholder.Visibility = Visibility.Visible;
        _image.Visibility = Visibility.Collapsed;
        _image.Source = null;
        _mediaElement.Visibility = Visibility.Collapsed;

        try
        {
            _player.Pause();
            _player.Source = null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to reset output media player state.");
        }
    }

    private void DetachMediaEvents()
    {
        if (_image.Source is BitmapImage bitmap)
        {
            if (_imageOpenedHandler != null)
            {
                bitmap.ImageOpened -= _imageOpenedHandler;
                _imageOpenedHandler = null;
            }

            if (_imageFailedHandler != null)
            {
                bitmap.ImageFailed -= _imageFailedHandler;
                _imageFailedHandler = null;
            }
        }

        if (_mediaFailedHandler != null)
        {
            _player.MediaFailed -= _mediaFailedHandler;
            _mediaFailedHandler = null;
        }

        if (_playbackStateChangedHandler != null)
        {
            _player.PlaybackSession.PlaybackStateChanged -= _playbackStateChangedHandler;
            _playbackStateChangedHandler = null;
        }
    }

    private void ApplyPlaceholderBrush()
    {
        _placeholder.Background = PlaceholderBrush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
