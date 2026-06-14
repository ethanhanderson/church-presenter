#nullable enable

using System.Threading;

using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Documents;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Render-only preview surface built on the rendering core. Compiles a <see cref="PresentationSlide"/>
/// to a <see cref="SlideScene"/> and rasterizes it with the shared <see cref="ThumbnailEngine"/>
/// (same compiler + <see cref="SceneSurfaceRenderer"/> as live output), letterboxed to the slide
/// aspect ratio with an optional safe-area overlay. Reactive: any change to <see cref="Project"/>,
/// <see cref="Slide"/>, <see cref="Intent"/>, or <see cref="VisibleLayerIds"/> recompiles and
/// re-rasterizes. Replaces <c>SlideStageView</c> for non-interactive previews (Themes/Reflow/Editor
/// canvas, slide-list thumbnails). Video/live-video/web layers are not composited here; the editor's
/// interactive adorner layer and media/web preview are layered on separately.
/// </summary>
public sealed class SceneCanvasView : UserControl
{
    /// <summary>Identifies the <see cref="Project"/> property.</summary>
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(PresentationProject), typeof(SceneCanvasView), new PropertyMetadata(null, OnRenderInputChanged));

    /// <summary>Identifies the <see cref="Slide"/> property.</summary>
    public static readonly DependencyProperty SlideProperty = DependencyProperty.Register(
        nameof(Slide), typeof(PresentationSlide), typeof(SceneCanvasView), new PropertyMetadata(null, OnRenderInputChanged));

    /// <summary>Identifies the <see cref="Intent"/> property.</summary>
    public static readonly DependencyProperty IntentProperty = DependencyProperty.Register(
        nameof(Intent), typeof(RenderIntent), typeof(SceneCanvasView), new PropertyMetadata(RenderIntent.Preview, OnRenderInputChanged));

    /// <summary>Identifies the <see cref="ShowSafeArea"/> property.</summary>
    public static readonly DependencyProperty ShowSafeAreaProperty = DependencyProperty.Register(
        nameof(ShowSafeArea), typeof(bool), typeof(SceneCanvasView), new PropertyMetadata(false, OnSafeAreaChanged));

    /// <summary>Identifies the <see cref="VisibleLayerIds"/> property.</summary>
    public static readonly DependencyProperty VisibleLayerIdsProperty = DependencyProperty.Register(
        nameof(VisibleLayerIds), typeof(IEnumerable<string>), typeof(SceneCanvasView), new PropertyMetadata(null, OnRenderInputChanged));

    private readonly Viewbox _viewbox = new() { Stretch = Stretch.Uniform };
    private readonly Grid _canvas = new();
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Border _safeArea = new()
    {
        BorderBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private int _renderToken;
    private CancellationTokenSource? _renderCts;

    /// <summary>Initializes the render-only scene canvas.</summary>
    public SceneCanvasView()
    {
        _canvas.Children.Add(_image);
        _canvas.Children.Add(_safeArea);
        _viewbox.Child = _canvas;

        var root = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        root.Children.Add(_viewbox);

        Content = root;
        Loaded += (_, _) => QueueRender();
        Unloaded += (_, _) => CancelRender();
    }

    /// <summary>Presentation project providing theme resolution.</summary>
    public PresentationProject? Project
    {
        get => (PresentationProject?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    /// <summary>Slide to render.</summary>
    public PresentationSlide? Slide
    {
        get => (PresentationSlide?)GetValue(SlideProperty);
        set => SetValue(SlideProperty, value);
    }

    /// <summary>Render intent (defaults to Preview).</summary>
    public RenderIntent Intent
    {
        get => (RenderIntent)GetValue(IntentProperty);
        set => SetValue(IntentProperty, value);
    }

    /// <summary>Whether to draw the title/action safe-area guide.</summary>
    public bool ShowSafeArea
    {
        get => (bool)GetValue(ShowSafeAreaProperty);
        set => SetValue(ShowSafeAreaProperty, value);
    }

    /// <summary>Optional set of layer ids to render (for Looks/visibility filtering).</summary>
    public IEnumerable<string>? VisibleLayerIds
    {
        get => (IEnumerable<string>?)GetValue(VisibleLayerIdsProperty);
        set => SetValue(VisibleLayerIdsProperty, value);
    }

    private static void OnRenderInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SceneCanvasView)d).QueueRender();

    private static void OnSafeAreaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (SceneCanvasView)d;
        view._safeArea.Visibility = view.ShowSafeArea ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Cancels in-flight raster work and releases the rendered image before another page (for example the
    /// Show page) attaches its own GPU surfaces on the shared Win2D device. Called from the owning page's
    /// <c>OnNavigatedFrom</c> so editor/theme/reflow GPU work stops before Show output composition starts.
    /// </summary>
    public void TeardownForNavigation()
    {
        CancelRender();
        _image.Source = null;
    }

    private void CancelRender()
    {
        _renderToken++;
        _renderCts?.Cancel();
        // Not disposed: the in-flight render still holds this token and may register continuations on it.
        // A CancellationTokenSource without a timer does not require disposal, so dropping the reference is
        // safe and avoids an ObjectDisposedException race with the awaiting render task.
        _renderCts = null;
    }

    private void QueueRender()
    {
        int token = ++_renderToken;
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        _ = RenderAsync(token, cts.Token);
    }

    private async Task RenderAsync(int token, CancellationToken cancellationToken)
    {
        PresentationSlide? slide = Slide;
        if (slide == null)
        {
            _image.Source = null;
            return;
        }

        if (App.Services?.GetService<ThumbnailEngine>() is not { } engine)
            return;

        try
        {
            (Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource image, PixelSize size) = await engine
                .RenderSlideSizedAsync(
                    Project,
                    slide,
                    Intent,
                    VisibleLayerIds?.ToArray(),
                    isDarkTheme: ActualTheme == ElementTheme.Dark,
                    cancellationToken: cancellationToken);

            if (token != _renderToken || cancellationToken.IsCancellationRequested)
                return;

            _canvas.Width = Math.Max(1, size.Width);
            _canvas.Height = Math.Max(1, size.Height);
            double insetX = _canvas.Width * 0.05;
            double insetY = _canvas.Height * 0.05;
            _safeArea.Margin = new Thickness(insetX, insetY, insetX, insetY);
            _image.Source = image;
        }
        catch (OperationCanceledException)
        {
            // Expected when navigation cancels an in-flight render; leave any prior image untouched.
        }
        catch (Exception ex)
        {
            _ = ex;
            if (token == _renderToken)
                _image.Source = null;
        }
    }
}
