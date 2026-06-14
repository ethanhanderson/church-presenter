#nullable enable

using System.Numerics;

using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Controls.Rendering.Engine;
using ChurchPresenter.Hosting;
using ChurchPresenter.Services.Pipeline;

using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;

namespace ChurchPresenter.Adapters.Rendering.Pipeline;

/// <summary>
/// App-lifetime operator preview mirror hosted on the main window shell. The session brush never
/// attaches to the cached Show page, avoiding compositor disconnect races on transient reload.
/// </summary>
public sealed class OperatorPreviewSession
{
    private readonly IProgramRenderService _renderService;
    private readonly CuePipelineRuntime? _pipeline;
    private readonly ScreenMirrorBrush _brush;
    private Canvas? _overlayHost;
    private Border? _clipBorder;
    private Grid? _contentHost;
    private MirrorSurfaceView? _shellMirror;
    private FrameworkElement? _anchor;
    private RectangleClip? _overlayClip;
    private string _targetScreenId = "main";
    private double _frameWidth = 1920;
    private double _frameHeight = 1080;
    private Stretch _stretch = Stretch.Uniform;
    private PixelSize _previewRenderSize = PixelSize.FullHd;

    /// <summary>Initializes the session with a persistent mirror brush.</summary>
    public OperatorPreviewSession(IProgramRenderService renderService, CuePipelineRuntime pipeline)
    {
        _renderService = renderService ?? throw new ArgumentNullException(nameof(renderService));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _brush = new ScreenMirrorBrush(renderService, _targetScreenId) { UsePreviewSurface = true };
        ShowNavigationDiagnostics.Log("PreviewSession.Create", $"brushHash={_brush.GetHashCode()}");
    }

    /// <summary>Persistent brush instance; always connected via the shell mirror.</summary>
    public ScreenMirrorBrush Brush => _brush;

    /// <summary>True when the shell overlay is positioned over the Show preview slot.</summary>
    public bool IsOverlayVisible => _overlayHost?.Visibility == Visibility.Visible;

    /// <summary>Creates the shell-hosted mirror on the main window overlay canvas.</summary>
    public void InitializeOverlay(Canvas overlayHost)
    {
        ArgumentNullException.ThrowIfNull(overlayHost);
        if (_shellMirror is not null)
            return;

        _overlayHost = overlayHost;
        _clipBorder = new Border
        {
            Background = new SolidColorBrush(Colors.Black),
        };
        _contentHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _shellMirror = new MirrorSurfaceView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _shellMirror.AttachBrush(_brush);
        _contentHost.Children.Add(_shellMirror);
        _clipBorder.Child = _contentHost;
        _overlayHost.Children.Add(_clipBorder);
        _overlayHost.Visibility = Visibility.Collapsed;
        ShowNavigationDiagnostics.Log("PreviewSession.OverlayReady", $"brushHash={_brush.GetHashCode()}");
    }

    /// <summary>Re-targets the session brush at another logical screen.</summary>
    public void SetTargetScreen(string screenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);
        _targetScreenId = screenId;
        ShowNavigationDiagnostics.Log("PreviewSession.SetScreen", $"screen={screenId} brushHash={_brush.GetHashCode()}");
        _brush.SetScreen(screenId);
    }

    /// <summary>Ensures the preview-sized render surface exists for the selected screen.</summary>
    public void NudgeCanonicalRender(string screenId)
    {
        if (_previewRenderSize.Width > 0 && _previewRenderSize.Height > 0)
            _renderService.EnsurePreviewSurface(screenId, _previewRenderSize);
    }

    /// <summary>Positions the shell mirror over the Show page preview slot.</summary>
    public void ShowOver(FrameworkElement anchor, double frameWidth, double frameHeight, Stretch stretch)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (_overlayHost is null || _clipBorder is null || _contentHost is null || _shellMirror is null)
            return;

        _anchor = anchor;
        _frameWidth = frameWidth > 0 ? frameWidth : 1920;
        _frameHeight = frameHeight > 0 ? frameHeight : 1080;
        _stretch = stretch;

        anchor.SizeChanged -= Anchor_SizeChanged;
        anchor.SizeChanged += Anchor_SizeChanged;

        UpdateOverlayPosition();
        _overlayHost.Visibility = Visibility.Visible;
        ShowNavigationDiagnostics.Log("PreviewSession.ShowOver", $"anchor={anchor.GetHashCode()} brushHash={_brush.GetHashCode()}");
    }

    /// <summary>Hides the shell overlay when Show leaves the frame; brush stays connected.</summary>
    public void HideOverlay()
    {
        if (_anchor is not null)
            _anchor.SizeChanged -= Anchor_SizeChanged;

        _anchor = null;
        if (_overlayHost is not null)
            _overlayHost.Visibility = Visibility.Collapsed;

        _renderService.ReleasePreviewSurface(_targetScreenId);
        ShowNavigationDiagnostics.Log("PreviewSession.Hide", $"brushHash={_brush.GetHashCode()}");
    }

    private void Anchor_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateOverlayPosition();
    }

    private void UpdateOverlayPosition()
    {
        if (_overlayHost is null || _clipBorder is null || _contentHost is null || _anchor is null)
            return;

        if (_anchor.ActualWidth <= 0 || _anchor.ActualHeight <= 0)
            return;

        GeneralTransform transform = _anchor.TransformToVisual(_overlayHost);
        Point topLeft = transform.TransformPoint(new Point(0, 0));
        double width = _anchor.ActualWidth;
        double height = _anchor.ActualHeight;

        Thickness borderThickness = _anchor is Border anchorBorder
            ? anchorBorder.BorderThickness
            : default;
        CornerRadius cornerRadius = _anchor is Border anchorWithRadius
            ? anchorWithRadius.CornerRadius
            : new CornerRadius(8);

        Canvas.SetLeft(_clipBorder, topLeft.X);
        Canvas.SetTop(_clipBorder, topLeft.Y);
        _clipBorder.Width = width;
        _clipBorder.Height = height;
        _clipBorder.CornerRadius = cornerRadius;
        _clipBorder.Padding = borderThickness;

        double innerWidth = Math.Max(0, width - borderThickness.Left - borderThickness.Right);
        double innerHeight = Math.Max(0, height - borderThickness.Top - borderThickness.Bottom);
        (double contentWidth, double contentHeight) = ComputeContentSize(
            innerWidth,
            innerHeight,
            _frameWidth,
            _frameHeight,
            _stretch);

        _contentHost.Width = contentWidth;
        _contentHost.Height = contentHeight;
        UpdatePreviewRenderSize(contentWidth, contentHeight);
        UpdateOverlayClip(width, height, cornerRadius);
    }

    private void UpdatePreviewRenderSize(double contentWidth, double contentHeight)
    {
        int width = Math.Max(1, (int)Math.Round(contentWidth));
        int height = Math.Max(1, (int)Math.Round(contentHeight));
        var previewSize = new PixelSize(width, height);
        if (_previewRenderSize.Width == previewSize.Width && _previewRenderSize.Height == previewSize.Height)
            return;

        _previewRenderSize = previewSize;
        _renderService.EnsurePreviewSurface(_targetScreenId, previewSize);
        _brush.UsePreviewSurface = true;
        _ = _pipeline?.InvalidateTransitionDefaultsAsync();
    }

    private void UpdateOverlayClip(double width, double height, CornerRadius cornerRadius)
    {
        if (_clipBorder is null)
            return;

        float clipWidth = (float)width;
        float clipHeight = (float)height;
        if (clipWidth <= 0 || clipHeight <= 0)
            return;

        var visual = ElementCompositionPreview.GetElementVisual(_clipBorder);
        RectangleClip clip = _overlayClip ??= visual.Compositor.CreateRectangleClip();

        clip.Left = 0;
        clip.Top = 0;
        clip.Right = clipWidth;
        clip.Bottom = clipHeight;
        clip.TopLeftRadius = new Vector2((float)cornerRadius.TopLeft, (float)cornerRadius.TopLeft);
        clip.TopRightRadius = new Vector2((float)cornerRadius.TopRight, (float)cornerRadius.TopRight);
        clip.BottomLeftRadius = new Vector2((float)cornerRadius.BottomLeft, (float)cornerRadius.BottomLeft);
        clip.BottomRightRadius = new Vector2((float)cornerRadius.BottomRight, (float)cornerRadius.BottomRight);
        visual.Clip = clip;
    }

    private static (double Width, double Height) ComputeContentSize(
        double availableWidth,
        double availableHeight,
        double frameWidth,
        double frameHeight,
        Stretch stretch)
    {
        if (availableWidth <= 0 || availableHeight <= 0 || frameWidth <= 0 || frameHeight <= 0)
            return (0, 0);

        return stretch switch
        {
            Stretch.Fill => (availableWidth, availableHeight),
            Stretch.UniformToFill => ScaleToFill(availableWidth, availableHeight, frameWidth, frameHeight),
            Stretch.None => (Math.Min(availableWidth, frameWidth), Math.Min(availableHeight, frameHeight)),
            _ => LetterboxUniform(availableWidth, availableHeight, frameWidth, frameHeight),
        };
    }

    private static (double Width, double Height) LetterboxUniform(
        double availableWidth,
        double availableHeight,
        double frameWidth,
        double frameHeight)
    {
        double scale = Math.Min(availableWidth / frameWidth, availableHeight / frameHeight);
        return (frameWidth * scale, frameHeight * scale);
    }

    private static (double Width, double Height) ScaleToFill(
        double availableWidth,
        double availableHeight,
        double frameWidth,
        double frameHeight)
    {
        double scale = Math.Max(availableWidth / frameWidth, availableHeight / frameHeight);
        return (frameWidth * scale, frameHeight * scale);
    }
}
