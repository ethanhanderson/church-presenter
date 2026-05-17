using System.IO;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;

using Windows.Storage.Streams;

namespace ChurchPresenter.Controls;

/// <summary>
/// Hosts a slide web layer either as live WebView2 content or as a captured bitmap snapshot.
/// Live mode is used by output surfaces; snapshot mode is used by thumbnails, editor previews, and export.
/// </summary>
public sealed class WebLayerView : UserControl
{
    public static readonly DependencyProperty UrlProperty =
        DependencyProperty.Register(nameof(Url), typeof(string), typeof(WebLayerView),
            new PropertyMetadata(string.Empty, OnWebPropertyChanged));

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(WebLayerView),
            new PropertyMetadata(1d, OnWebPropertyChanged));

    public static readonly DependencyProperty InteractiveProperty =
        DependencyProperty.Register(nameof(Interactive), typeof(bool), typeof(WebLayerView),
            new PropertyMetadata(false, OnWebPropertyChanged));

    public static readonly DependencyProperty UseLiveContentProperty =
        DependencyProperty.Register(nameof(UseLiveContent), typeof(bool), typeof(WebLayerView),
            new PropertyMetadata(false, OnWebPropertyChanged));

    public static readonly DependencyProperty RefreshIntervalProperty =
        DependencyProperty.Register(nameof(RefreshInterval), typeof(int?), typeof(WebLayerView),
            new PropertyMetadata(null, OnWebPropertyChanged));

    private readonly Grid _root;
    private readonly Image _snapshotImage;
    private readonly Border _placeholder;
    private readonly ILogger<WebLayerView>? _logger;
    private WebView2? _webView;
    private DispatcherQueueTimer? _refreshTimer;
    private bool _isRefreshing;
    private bool _refreshRequested;
    private TaskCompletionSource<bool>? _pendingSnapshot;

    /// <summary>Initializes a new instance of the <see cref="WebLayerView"/> control.</summary>
    public WebLayerView()
        : this(App.Services)
    {
    }

    private WebLayerView(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _logger = services.GetService(typeof(ILogger<WebLayerView>)) as ILogger<WebLayerView>;
        _snapshotImage = new Image
        {
            Stretch = Stretch.Fill,
        };
        _placeholder = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.08 },
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.55 },
                Text = "Web layer",
            },
        };
        _root = new Grid();
        _root.Children.Add(_placeholder);
        _root.Children.Add(_snapshotImage);
        Content = _root;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) =>
        {
            if (!UseLiveContent)
                RequestRefresh();
        };
    }

    /// <summary>The target URL to navigate to.</summary>
    public string Url
    {
        get => (string)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    /// <summary>Logical layer zoom factor. Values less than or equal to zero are normalized to 1.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Whether the live web surface should accept interaction.</summary>
    public bool Interactive
    {
        get => (bool)GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }

    /// <summary>Whether to host live WebView2 content instead of a captured snapshot.</summary>
    public bool UseLiveContent
    {
        get => (bool)GetValue(UseLiveContentProperty);
        set => SetValue(UseLiveContentProperty, value);
    }

    /// <summary>Optional refresh interval in seconds for live or snapshot web content.</summary>
    public int? RefreshInterval
    {
        get => (int?)GetValue(RefreshIntervalProperty);
        set => SetValue(RefreshIntervalProperty, value);
    }

    /// <summary>Waits for the current snapshot capture, if any, to finish.</summary>
    public Task WaitUntilReadyAsync() => _pendingSnapshot?.Task ?? Task.CompletedTask;

    private static void OnWebPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is WebLayerView view)
            view.RequestRefresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RequestRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        StopRefreshTimer();
        DisposeWebView();
    }

    private void RequestRefresh()
    {
        if (!IsLoaded)
            return;

        if (_isRefreshing)
        {
            _refreshRequested = true;
            return;
        }

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            if (UseLiveContent)
                await ConfigureLiveViewAsync().ConfigureAwait(true);
            else
                await CaptureSnapshotAsync().ConfigureAwait(true);
        }
        finally
        {
            _isRefreshing = false;
        }

        if (_refreshRequested)
        {
            _refreshRequested = false;
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task ConfigureLiveViewAsync()
    {
        _snapshotImage.Source = null;
        _snapshotImage.Visibility = Visibility.Collapsed;
        EnsureWebView();
        _webView!.Opacity = 1;
        _webView.Visibility = Visibility.Visible;
        _webView.IsHitTestVisible = Interactive;
        _placeholder.Visibility = Visibility.Visible;

        await InitializeWebViewAsync(_webView).ConfigureAwait(true);
        ApplyWebViewSizing(_webView);
        ConfigureRefreshTimer();

        if (TryCreateUri(Url, out var uri))
        {
            if (_webView.Source == null || _webView.Source != uri)
                _webView.Source = uri;
        }
        else
        {
            _webView.Source = null;
        }
    }

    private async Task CaptureSnapshotAsync()
    {
        _pendingSnapshot = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _snapshotImage.Visibility = Visibility.Visible;
        _placeholder.Visibility = Visibility.Visible;

        EnsureWebView();
        _webView!.Opacity = 0.01;
        _webView.Visibility = Visibility.Visible;
        _webView.IsHitTestVisible = false;

        await InitializeWebViewAsync(_webView).ConfigureAwait(true);
        ApplyWebViewSizing(_webView);
        ConfigureRefreshTimer();

        if (!TryCreateUri(Url, out var uri))
        {
            _snapshotImage.Source = null;
            _pendingSnapshot.TrySetResult(true);
            return;
        }

        TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs> navigationTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            sender.NavigationCompleted -= OnNavigationCompleted;
            navigationTcs.TrySetResult(args);
        }

        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.Source = uri;

        try
        {
            var navigation = await navigationTcs.Task.ConfigureAwait(true);
            if (navigation.IsSuccess)
            {
                await ApplyZoomScriptAsync(_webView).ConfigureAwait(true);
                await Task.Delay(150).ConfigureAwait(true);
                _snapshotImage.Source = await CapturePreviewImageAsync(_webView).ConfigureAwait(true);
            }
            else
            {
                _snapshotImage.Source = null;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to capture web-layer snapshot for {Url}.", Url);
            _snapshotImage.Source = null;
        }
        finally
        {
            _pendingSnapshot.TrySetResult(true);
        }
    }

    private void EnsureWebView()
    {
        if (_webView != null)
            return;

        _webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        _root.Children.Add(_webView);
    }

    private async Task InitializeWebViewAsync(WebView2 webView)
    {
        await webView.EnsureCoreWebView2Async();
        if (webView.CoreWebView2 == null)
            return;

        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = Interactive;
        webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.IsZoomControlEnabled = Interactive;
    }

    private void ApplyWebViewSizing(WebView2 webView)
    {
        var zoom = NormalizeZoom(Zoom);
        var width = Math.Max(1, ActualWidth / zoom);
        var height = Math.Max(1, ActualHeight / zoom);

        webView.Width = width;
        webView.Height = height;
        webView.RenderTransform = new ScaleTransform
        {
            ScaleX = zoom,
            ScaleY = zoom,
        };
        webView.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        webView.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)),
        };
    }

    private async Task ApplyZoomScriptAsync(WebView2 webView)
    {
        if (webView.CoreWebView2 == null)
            return;

        var zoom = NormalizeZoom(Zoom).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var script =
            "(() => {" +
            $"const z = {zoom};" +
            "document.documentElement.style.margin = '0';" +
            "document.documentElement.style.overflow = 'hidden';" +
            "if (document.body) {" +
            "document.body.style.margin = '0';" +
            "document.body.style.overflow = 'hidden';" +
            "document.body.style.zoom = String(z);" +
            "}" +
            "document.documentElement.style.zoom = String(z);" +
            "window.scrollTo(0, 0);" +
            "})();";

        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to apply web-layer zoom script for {Url}.", Url);
        }
    }

    private static async Task<ImageSource?> CapturePreviewImageAsync(WebView2 webView)
    {
        if (webView.CoreWebView2 == null)
            return null;

        using var ras = new InMemoryRandomAccessStream();
        await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ras);
        ras.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(ras);
        return bitmap;
    }

    private void ConfigureRefreshTimer()
    {
        StopRefreshTimer();

        if (RefreshInterval is not > 0)
            return;

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue == null)
            return;

        _refreshTimer = queue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(RefreshInterval.Value);
        _refreshTimer.IsRepeating = true;
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
    }

    private void RefreshTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _ = sender;
        _ = args;

        if (_webView?.CoreWebView2 == null)
            return;

        if (UseLiveContent)
            _webView.Reload();
        else
            RequestRefresh();
    }

    private void StopRefreshTimer()
    {
        if (_refreshTimer == null)
            return;

        _refreshTimer.Tick -= RefreshTimer_Tick;
        _refreshTimer.Stop();
        _refreshTimer = null;
    }

    private void DisposeWebView()
    {
        if (_webView == null)
            return;

        _root.Children.Remove(_webView);
        _webView = null;
    }

    private static bool TryCreateUri(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri);
    }

    private static double NormalizeZoom(double zoom) =>
        zoom > 0 ? zoom : 1;
}
