#nullable enable

using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Controls.Rendering.Engine;

using Microsoft.UI.Composition;

namespace ChurchPresenter.Adapters.Media.Pipeline;

/// <summary>
/// Live video binding backed by a <see cref="LiveInputSurfaceRenderer"/> (local camera via
/// MediaCapture/MediaFrameReader). Exposes the composition brush so the output host can mount the
/// live feed into its LiveVideo layer slot exactly like a media-file video layer.
/// </summary>
public sealed class WinUiLiveVideoAdapter : ILiveVideoBinding
{
    private readonly object _gate = new();
    private LiveInputSurfaceRenderer? _renderer;

    /// <inheritdoc />
    public string InputId { get; private set; } = string.Empty;

    /// <inheritdoc />
    public bool IsConnected => _renderer?.IsConnected ?? false;

    /// <summary>The composition brush painting live frames, when connected.</summary>
    public CompositionSurfaceBrush? Brush { get; private set; }

    /// <summary>Connects a live input source to the requested layer slot and returns its brush.</summary>
    public async Task<CompositionSurfaceBrush?> BindAsync(
        Compositor compositor,
        LayerSlotId slotId,
        string inputId,
        string? fit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        cancellationToken.ThrowIfCancellationRequested();
        _ = slotId;

        LiveInputSurfaceRenderer renderer;
        lock (_gate)
        {
            _renderer ??= new LiveInputSurfaceRenderer(compositor);
            renderer = _renderer;
        }

        InputId = inputId;
        Brush = await renderer.ConnectAsync(inputId, fit, cancellationToken).ConfigureAwait(false);
        return Brush;
    }

    /// <summary>Waits until the first live frame is available so transitions start with real pixels.</summary>
    public Task WaitForFirstFrameAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        LiveInputSurfaceRenderer? renderer;
        lock (_gate)
            renderer = _renderer;

        return renderer?.WaitForFirstFrameAsync(timeout, cancellationToken) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        LiveInputSurfaceRenderer? renderer;
        lock (_gate)
        {
            renderer = _renderer;
            _renderer = null;
        }

        if (renderer != null)
        {
            await renderer.DisconnectAsync().ConfigureAwait(false);
            renderer.Dispose();
        }

        Brush = null;
    }
}
