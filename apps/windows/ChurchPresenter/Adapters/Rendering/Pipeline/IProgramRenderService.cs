#nullable enable

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;

using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Adapters.Rendering.Pipeline;

/// <summary>
/// App-lifetime owner of the single canonical render per logical output screen. Each screen is rendered
/// exactly once by a persistent <see cref="ChurchPresenter.Controls.Rendering.Engine.LayerStackHost"/>
/// parented to an always-composing shell anchor; audience windows, stage windows, and the operator
/// preview are thin <see cref="CompositionVisualSurface"/> mirrors of these renders rather than separate
/// composition trees. Because the canonical render is decoupled from the pages, switching pages never tears
/// down and rebuilds the GPU output composition tree on the shared compositor, and it lets the audience and
/// stage outputs keep rendering identically no matter which page the operator is on.
/// </summary>
public interface IProgramRenderService
{
    /// <summary>
    /// Binds the service to the persistent shell anchor that hosts every canonical render. Must be a
    /// present-and-composing element (full size, not collapsed/0x0/off-screen); it is kept visually
    /// hidden (opacity 0) so the operator never sees the raw canonical renders, while the compositor
    /// still ticks them so the mirror surfaces update continuously. Call once from the main window.
    /// </summary>
    void Initialize(Panel anchor);

    /// <summary>
    /// Returns the mirror surface for a logical screen, or <c>null</c> until its canonical render has
    /// been created (the first composed frame for that screen). Mirrors paint this with a
    /// <see cref="CompositionSurfaceBrush"/>; re-pointing a mirror at a different screen is an instant
    /// brush swap because every screen's surface is always rendering.
    /// </summary>
    CompositionVisualSurface? GetScreenSurface(string screenId);

    /// <summary>
    /// Ensures a preview-sized render exists for the operator card mirror. The preview host rasterizes
    /// the same live frame at <paramref name="previewSize"/> so the shell preview does not downscale a
    /// full-resolution canonical surface.
    /// </summary>
    void EnsurePreviewSurface(string screenId, PixelSize previewSize);

    /// <summary>
    /// Returns the preview-sized mirror surface when <see cref="EnsurePreviewSurface"/> has created one.
    /// </summary>
    CompositionVisualSurface? GetPreviewSurface(string screenId);

    /// <summary>Releases the preview-sized render for a screen when the operator preview hides.</summary>
    void ReleasePreviewSurface(string screenId);

    /// <summary>Raised on the UI thread whenever the set of canonical screen surfaces changes.</summary>
    event EventHandler? ScreensChanged;
}
