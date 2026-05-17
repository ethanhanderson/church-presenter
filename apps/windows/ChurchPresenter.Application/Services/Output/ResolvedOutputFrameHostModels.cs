using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// Identifies the kind of resolved backend frame a display host should apply.
/// </summary>
public enum ResolvedOutputFrameHostKind
{
    /// <summary>Audience/program output frame.</summary>
    Audience,

    /// <summary>Stage/confidence output frame.</summary>
    Stage,
}

/// <summary>
/// UI-neutral resolved-frame snapshot consumed by output hosts.
/// </summary>
public sealed record ResolvedOutputFrameHostSnapshot
{
    /// <summary>Screen kind for this host snapshot.</summary>
    public ResolvedOutputFrameHostKind Kind { get; init; }

    /// <summary>Logical screen id represented by the snapshot.</summary>
    public string ScreenId { get; init; } = string.Empty;

    /// <summary>Monotonic backend frame sequence.</summary>
    public long Sequence { get; init; }

    /// <summary>Nominal render size in pixels.</summary>
    public PixelSize RenderSize { get; init; } = PixelSize.FullHd;

    /// <summary>Short title suitable for fullscreen host chrome.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional subtitle with route or layout details.</summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>Optional frame-level status message.</summary>
    public string? DiagnosticsMessage { get; init; }

    /// <summary>Visible resolved payload items to paint.</summary>
    public IReadOnlyList<ResolvedOutputFrameHostItem> VisibleItems { get; init; } = Array.Empty<ResolvedOutputFrameHostItem>();

    /// <summary>Resolved items that are present in the backend frame but currently hidden or suppressed.</summary>
    public IReadOnlyList<ResolvedOutputFrameHostItem> InactiveItems { get; init; } = Array.Empty<ResolvedOutputFrameHostItem>();
}

/// <summary>
/// One resolved payload entry for an output host.
/// </summary>
public sealed record ResolvedOutputFrameHostItem
{
    /// <summary>Stable layer, payload, or stage element id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display label for the host section.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Resolved payload descriptor supplied by the backend frame.</summary>
    public RenderPayloadDescriptor Payload { get; init; } = new();

    /// <summary>Whether the item should be visually prominent.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Whether the item is visible after backend routing.</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>Whether the item was suppressed by resolved backend state.</summary>
    public bool IsSuppressed { get; init; }

    /// <summary>Optional item-level diagnostics.</summary>
    public string? Diagnostics { get; init; }
}

/// <summary>
/// Maps backend render frames into display-host snapshots without recomputing production state.
/// </summary>
public static class ResolvedOutputFrameHostMapper
{
    /// <summary>Maps an audience frame into a host snapshot.</summary>
    public static ResolvedOutputFrameHostSnapshot Map(AudienceRenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        ResolvedOutputFrameHostItem[] items = frame.Layers
            .Select(static layer => new ResolvedOutputFrameHostItem
            {
                Id = string.IsNullOrWhiteSpace(layer.Payload.Id) ? layer.Kind.ToString() : layer.Payload.Id,
                Label = layer.Kind.ToString(),
                Payload = layer.Payload,
                IsPrimary = layer.Kind == OutputLayerKind.Slide,
                IsVisible = layer.IsVisible && !layer.IsSuppressed,
                IsSuppressed = layer.IsSuppressed,
                Diagnostics = layer.Diagnostics,
            })
            .ToArray();

        return new ResolvedOutputFrameHostSnapshot
        {
            Kind = ResolvedOutputFrameHostKind.Audience,
            ScreenId = frame.ScreenId,
            Sequence = frame.Sequence,
            RenderSize = frame.RenderSize,
            Title = "Audience output",
            Subtitle = string.IsNullOrWhiteSpace(frame.LookPresetId)
                ? "No Look preset"
                : $"Look: {frame.LookPresetId}",
            DiagnosticsMessage = frame.Diagnostics.Message,
            VisibleItems = items.Where(static item => item.IsVisible).ToArray(),
            InactiveItems = items.Where(static item => !item.IsVisible).ToArray(),
        };
    }

    /// <summary>Maps a stage frame into a host snapshot.</summary>
    public static ResolvedOutputFrameHostSnapshot Map(StageRenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        ResolvedOutputFrameHostItem[] items = frame.Payloads
            .Select((payload, index) => new ResolvedOutputFrameHostItem
            {
                Id = string.IsNullOrWhiteSpace(payload.Id) ? $"stage-payload-{index}" : payload.Id,
                Label = ResolveStagePayloadLabel(payload, index),
                Payload = payload,
                IsPrimary = index == 0,
            })
            .ToArray();

        return new ResolvedOutputFrameHostSnapshot
        {
            Kind = ResolvedOutputFrameHostKind.Stage,
            ScreenId = frame.ScreenId,
            Sequence = frame.Sequence,
            RenderSize = frame.RenderSize,
            Title = "Stage output",
            Subtitle = string.IsNullOrWhiteSpace(frame.StageLayoutId)
                ? $"Mode: {FormatCommandMode(frame.CommandMode)}"
                : $"Layout: {frame.StageLayoutId} - Mode: {FormatCommandMode(frame.CommandMode)}",
            DiagnosticsMessage = frame.Diagnostics.Message,
            VisibleItems = items,
        };
    }

    private static string ResolveStagePayloadLabel(RenderPayloadDescriptor payload, int index)
    {
        if (!string.IsNullOrWhiteSpace(payload.SourceReference))
            return payload.SourceReference;

        return payload.Kind == RenderPayloadKind.None
            ? $"Stage item {index + 1}"
            : payload.Kind.ToString();
    }

    private static string FormatCommandMode(StageAudienceCommandMode mode) =>
        mode switch
        {
            StageAudienceCommandMode.StageOnly => "Stage only",
            _ => "Stage and audience",
        };
}