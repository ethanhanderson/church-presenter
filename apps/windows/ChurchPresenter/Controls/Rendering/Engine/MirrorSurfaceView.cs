#nullable enable

using ChurchPresenter.Adapters.Rendering.Pipeline;

using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using System.Numerics;

using Windows.Foundation;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// A passive, letterboxed mirror of one logical screen's canonical render.
/// </summary>
public sealed class MirrorSurfaceView : Grid
{
    private readonly Rectangle _surface;
    private IProgramRenderService? _service;
    private ScreenMirrorBrush? _brush;
    private bool _usesExternalBrush;
    private RectangleClip? _surfaceBoundsClip;

    /// <summary>Initializes the mirror surface view.</summary>
    public MirrorSurfaceView()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        _surface = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Children.Add(_surface);
        Loaded += (_, _) => UpdateBoundsClip();
        SizeChanged += (_, _) => UpdateBoundsClip();
        Unloaded += OnUnloaded;
    }

    /// <summary>Keeps the composition mirror brush inside this element's layout bounds.</summary>
    private void UpdateBoundsClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            Clip = null;
            ElementCompositionPreview.GetElementVisual(_surface).Clip = null;
            return;
        }

        Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, ActualWidth, ActualHeight),
        };

        var visual = ElementCompositionPreview.GetElementVisual(_surface);
        RectangleClip clip = _surfaceBoundsClip ??= visual.Compositor.CreateRectangleClip();
        clip.Left = 0;
        clip.Top = 0;
        clip.Right = (float)ActualWidth;
        clip.Bottom = (float)ActualHeight;
        clip.TopLeftRadius = Vector2.Zero;
        clip.TopRightRadius = Vector2.Zero;
        clip.BottomLeftRadius = Vector2.Zero;
        clip.BottomRightRadius = Vector2.Zero;
        visual.Clip = clip;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_usesExternalBrush)
        {
            // TeardownForNavigation already cleared the page fill; session brush parks on shell keeper.
            return;
        }

        Unbind();
    }

    /// <summary>True when the mirror rectangle currently has a fill brush assigned.</summary>
    public bool HasBrushAttached => _surface.Fill is not null;

    /// <summary>Disconnects the mirror from the rectangle without destroying an app-owned brush.</summary>
    public void DetachFromSurface()
    {
        _surface.Fill = null;
        if (!_usesExternalBrush)
            _brush = null;
        _service = null;
    }

    /// <summary>Legacy unbind — also drops external brush ownership.</summary>
    public void Unbind()
    {
        _usesExternalBrush = false;
        DetachFromSurface();
        _brush = null;
    }

    /// <summary>Attaches a persistent brush owned by <see cref="ViewModels.OutputViewModel"/>.</summary>
    public void AttachBrush(ScreenMirrorBrush brush)
    {
        ArgumentNullException.ThrowIfNull(brush);
        _usesExternalBrush = true;
        _brush = brush;
        _surface.Fill = brush;
    }

    /// <summary>Points this mirror at a logical screen's canonical render (instant brush swap).</summary>
    public void Bind(IProgramRenderService service, string screenId)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenId);

        _service = service;
        _usesExternalBrush = false;
        if (_brush is null)
        {
            _brush = new ScreenMirrorBrush(service, screenId);
            _surface.Fill = _brush;
        }
        else
        {
            _brush.SetScreen(screenId);
        }
    }
}
