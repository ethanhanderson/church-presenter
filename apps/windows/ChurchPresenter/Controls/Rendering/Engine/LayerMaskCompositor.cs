#nullable enable

using Microsoft.UI.Composition;

using System.Numerics;

namespace ChurchPresenter.Controls.Rendering.Engine;

/// <summary>
/// Real layer masking for the output stack. Flattens the masked content subtree into a
/// <see cref="CompositionVisualSurface"/>, then paints a presentation visual with a
/// <see cref="CompositionMaskBrush"/> whose source is the flattened stack and whose mask
/// is a supplied alpha surface (rendered from a vector path or alpha image). When no mask
/// is set the content is shown unmodified. Replaces the previous no-op mask compositor.
/// </summary>
public sealed class LayerMaskCompositor : IDisposable
{
    private readonly Compositor _compositor;
    private readonly ContainerVisual _content;
    private readonly SpriteVisual _maskedPresentation;
    private CompositionVisualSurface? _visualSurface;
    private CompositionSurfaceBrush? _sourceBrush;
    private CompositionMaskBrush? _maskBrush;
    private bool _maskActive;

    /// <summary>Initializes a mask compositor over a content container and its parent root.</summary>
    public LayerMaskCompositor(Compositor compositor, ContainerVisual content, Vector2 size)
    {
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _maskedPresentation = compositor.CreateSpriteVisual();
        _maskedPresentation.Size = size;
        _maskedPresentation.IsVisible = false;
    }

    /// <summary>The presentation visual used while a mask is active; mount it alongside the content.</summary>
    public SpriteVisual MaskedPresentation => _maskedPresentation;

    /// <summary>Whether a mask is currently applied.</summary>
    public bool IsMaskActive => _maskActive;

    /// <summary>Resizes the mask surfaces.</summary>
    public void Resize(Vector2 size)
    {
        _maskedPresentation.Size = size;
        if (_visualSurface != null)
            _visualSurface.SourceSize = size;
    }

    /// <summary>
    /// Applies a mask alpha brush, or clears it when null. While active, the content
    /// subtree is hidden and rendered through the masked presentation visual instead.
    /// </summary>
    public void ApplyMask(CompositionBrush? maskAlpha)
    {
        if (maskAlpha == null)
        {
            if (!_maskActive)
                return;

            _maskActive = false;
            _maskedPresentation.IsVisible = false;
            _maskedPresentation.Brush = null;
            _content.IsVisible = true;
            return;
        }

        _visualSurface ??= CreateVisualSurface();
        _sourceBrush ??= _compositor.CreateSurfaceBrush(_visualSurface);
        _maskBrush ??= _compositor.CreateMaskBrush();
        _maskBrush.Source = _sourceBrush;
        _maskBrush.Mask = maskAlpha;
        _maskedPresentation.Brush = _maskBrush;
        _maskedPresentation.IsVisible = true;
        _content.IsVisible = false;
        _maskActive = true;
    }

    private CompositionVisualSurface CreateVisualSurface()
    {
        CompositionVisualSurface surface = _compositor.CreateVisualSurface();
        surface.SourceVisual = _content;
        surface.SourceSize = _maskedPresentation.Size;
        surface.SourceOffset = Vector2.Zero;
        return surface;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Do NOT call Dispose() on composition objects (brushes, surfaces, visuals).
        // The compositor render thread may still reference them for the in-flight frame;
        // calling Dispose() while the render thread reads the object causes a native
        // fail-fast (RoFailFast). The compositor manages the native lifetime; GC handles
        // the managed wrappers once no longer referenced.
        _maskBrush = null;
        _sourceBrush = null;
        _visualSurface = null;
    }
}
