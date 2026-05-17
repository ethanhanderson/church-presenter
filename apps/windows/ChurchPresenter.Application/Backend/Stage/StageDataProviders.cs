using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Stage;

/// <summary>
/// Provides the default stage data providers used by backend stage-frame resolution.
/// </summary>
public static class StageDataProviderCatalog
{
    /// <summary>Default providers for presentation, preview, generated, and custom stage layout elements.</summary>
    public static IReadOnlyList<IStageDataProvider> DefaultProviders { get; } =
    [
        new StagePresentationDataProvider(),
        new StagePreviewDataProvider(),
        new StageGeneratedDataProvider(),
        new StageCustomDataProvider(),
    ];
}

/// <summary>
/// Resolves current slide, next slide, notes, and group values for stage layouts.
/// </summary>
public sealed class StagePresentationDataProvider : IStageDataProvider
{
    /// <inheritdoc />
    public bool CanResolve(StageLayoutElement element)
    {
        return element.Kind is StageLayoutElementKind.CurrentSlideText
            or StageLayoutElementKind.CurrentSlidePreview
            or StageLayoutElementKind.NextSlideText
            or StageLayoutElementKind.NextSlidePreview
            or StageLayoutElementKind.Notes
            or StageLayoutElementKind.GroupName
            or StageLayoutElementKind.GroupColor;
    }

    /// <inheritdoc />
    public StageDataResult Resolve(StageDataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        RenderPayloadDescriptor? payload = request.Element.Kind switch
        {
            StageLayoutElementKind.CurrentSlideText => CreateTextPayload(
                request,
                request.State.StagePresentation.CurrentSlideText,
                "stage-current-slide-text"),
            StageLayoutElementKind.CurrentSlidePreview => request.State.StagePresentation.CurrentSlidePreview
                ?? ResolveLayerPayload(request.State, OutputLayerKind.Slide, "current-slide-preview"),
            StageLayoutElementKind.NextSlideText => CreateTextPayload(
                request,
                request.State.StagePresentation.NextSlideText,
                "stage-next-slide-text"),
            StageLayoutElementKind.NextSlidePreview => request.State.StagePresentation.NextSlidePreview,
            StageLayoutElementKind.Notes => CreateTextPayload(
                request,
                request.State.StagePresentation.Notes,
                "stage-notes"),
            StageLayoutElementKind.GroupName => CreateTextPayload(
                request,
                request.State.StagePresentation.CurrentGroupName,
                "stage-group-name"),
            StageLayoutElementKind.GroupColor => CreateTextPayload(
                request,
                request.State.StagePresentation.CurrentGroupColor,
                "stage-group-color"),
            _ => null,
        };

        return new StageDataResult { Payload = payload };
    }

    private static RenderPayloadDescriptor? ResolveLayerPayload(
        LiveRenderSessionState state,
        OutputLayerKind layerKind,
        string stageRole)
    {
        if (!state.Layers.TryGetValue(layerKind, out LayerState? layerState) || layerState.Payload == null)
        {
            return null;
        }

        return layerState.Payload with
        {
            SourceReference = string.IsNullOrWhiteSpace(layerState.Payload.SourceReference)
                ? stageRole
                : layerState.Payload.SourceReference,
        };
    }

    private static RenderPayloadDescriptor? CreateTextPayload(
        StageDataRequest request,
        string? value,
        string sourceReference)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return request.Element.VisibleWhenActive
                ? null
                : CreateGeneratedPayload(request, string.Empty, sourceReference);
        }

        return CreateGeneratedPayload(request, value, sourceReference);
    }

    private static RenderPayloadDescriptor CreateGeneratedPayload(
        StageDataRequest request,
        string value,
        string sourceReference)
    {
        return new RenderPayloadDescriptor
        {
            Id = $"stage:{request.ScreenId}:{request.Element.Id}",
            Kind = RenderPayloadKind.Overlay,
            DisplayName = value,
            SourceReference = sourceReference,
        };
    }
}

/// <summary>
/// Resolves audience and stage screen preview placeholders for stage layouts.
/// </summary>
public sealed class StagePreviewDataProvider : IStageDataProvider
{
    /// <inheritdoc />
    public bool CanResolve(StageLayoutElement element)
    {
        return element.Kind is StageLayoutElementKind.AudienceScreenPreview
            or StageLayoutElementKind.StageScreenPreview;
    }

    /// <inheritdoc />
    public StageDataResult Resolve(StageDataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string previewScreenId = request.Element.SourceId ?? request.ScreenId;
        string sourceReference = request.Element.Kind == StageLayoutElementKind.AudienceScreenPreview
            ? "audience-screen-preview"
            : "stage-screen-preview";

        return new StageDataResult
        {
            Payload = new RenderPayloadDescriptor
            {
                Id = $"stage:{request.ScreenId}:{request.Element.Id}",
                Kind = RenderPayloadKind.Presentation,
                DisplayName = request.Element.Label ?? $"Preview {previewScreenId}",
                SourceReference = $"{sourceReference}:{previewScreenId}",
            },
        };
    }
}

/// <summary>
/// Resolves generated stage content such as messages, timers, video countdowns, clocks, and capture status.
/// </summary>
public sealed class StageGeneratedDataProvider : IStageDataProvider
{
    /// <inheritdoc />
    public bool CanResolve(StageLayoutElement element)
    {
        return element.Kind is StageLayoutElementKind.StageMessage
            or StageLayoutElementKind.Timer
            or StageLayoutElementKind.SystemClock
            or StageLayoutElementKind.VideoCountdown
            or StageLayoutElementKind.CaptureStatus;
    }

    /// <inheritdoc />
    public StageDataResult Resolve(StageDataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        RenderPayloadDescriptor? payload = request.Element.Kind switch
        {
            StageLayoutElementKind.StageMessage => CreateTextPayload(
                request,
                request.State.GeneratedState.StageMessageText,
                "stage-message"),
            StageLayoutElementKind.Timer => CreateTimerPayload(request),
            StageLayoutElementKind.SystemClock => CreateTimerPayload(request, GeneratedTimerKind.SystemClock),
            StageLayoutElementKind.VideoCountdown => CreateTimerPayload(request, GeneratedTimerKind.VideoCountdown),
            StageLayoutElementKind.CaptureStatus => CreateCapturePayload(request),
            _ => null,
        };

        return new StageDataResult { Payload = payload };
    }

    private static RenderPayloadDescriptor? CreateTextPayload(
        StageDataRequest request,
        string? value,
        string sourceReference)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return request.Element.VisibleWhenActive
                ? null
                : CreateGeneratedPayload(request, string.Empty, sourceReference);
        }

        return CreateGeneratedPayload(request, value, sourceReference);
    }

    private static RenderPayloadDescriptor? CreateTimerPayload(
        StageDataRequest request,
        GeneratedTimerKind? requiredKind = null)
    {
        TimerSnapshot? timer = null;
        if (!string.IsNullOrWhiteSpace(request.Element.SourceId))
        {
            request.State.GeneratedState.Timers.TryGetValue(request.Element.SourceId, out timer);
        }
        else if (requiredKind != null)
        {
            timer = request.State.GeneratedState.Timers.Values.FirstOrDefault(candidate => candidate.Kind == requiredKind.Value);
        }

        if (timer == null)
        {
            return request.Element.VisibleWhenActive
                ? null
                : CreateGeneratedPayload(request, string.Empty, "stage-timer");
        }

        return new RenderPayloadDescriptor
        {
            Id = $"timer:{timer.Id}",
            Kind = RenderPayloadKind.Overlay,
            DisplayName = timer.DisplayValue,
            SourceReference = $"timer:{timer.Id}",
            ThemeVariantId = timer.ActiveColor,
        };
    }

    private static RenderPayloadDescriptor? CreateCapturePayload(StageDataRequest request)
    {
        CaptureSessionState? capture = null;
        if (!string.IsNullOrWhiteSpace(request.Element.SourceId))
        {
            request.State.GeneratedState.CaptureSessions.TryGetValue(request.Element.SourceId, out capture);
        }
        else
        {
            capture = request.State.GeneratedState.CaptureSessions.Values.FirstOrDefault(candidate => candidate.IsActive);
        }

        if (capture == null)
        {
            return request.Element.VisibleWhenActive
                ? null
                : CreateGeneratedPayload(request, "Capture idle", "capture");
        }

        return new RenderPayloadDescriptor
        {
            Id = $"capture:{capture.Metadata.Id}",
            Kind = RenderPayloadKind.Overlay,
            DisplayName = $"{capture.Metadata.Name}: {capture.Health}",
            SourceReference = $"capture:{capture.Metadata.SourceScreenId}",
            ThemeVariantId = capture.Health.ToString(),
        };
    }

    private static RenderPayloadDescriptor CreateGeneratedPayload(
        StageDataRequest request,
        string value,
        string sourceReference)
    {
        return new RenderPayloadDescriptor
        {
            Id = $"stage:{request.ScreenId}:{request.Element.Id}",
            Kind = RenderPayloadKind.Overlay,
            DisplayName = value,
            SourceReference = sourceReference,
        };
    }
}

/// <summary>
/// Resolves custom text and shape placeholders for stage layouts.
/// </summary>
public sealed class StageCustomDataProvider : IStageDataProvider
{
    /// <inheritdoc />
    public bool CanResolve(StageLayoutElement element)
    {
        return element.Kind is StageLayoutElementKind.CustomText
            or StageLayoutElementKind.CustomShape;
    }

    /// <inheritdoc />
    public StageDataResult Resolve(StageDataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        RenderPayloadDescriptor payload = request.Element.Kind == StageLayoutElementKind.CustomShape
            ? new RenderPayloadDescriptor
            {
                Id = $"stage:{request.ScreenId}:{request.Element.Id}",
                Kind = RenderPayloadKind.Overlay,
                DisplayName = request.Element.Label ?? request.Element.StaticText ?? "Shape",
                SourceReference = "stage-shape",
            }
            : new RenderPayloadDescriptor
            {
                Id = $"stage:{request.ScreenId}:{request.Element.Id}",
                Kind = RenderPayloadKind.Overlay,
                DisplayName = request.Element.StaticText ?? string.Empty,
                SourceReference = "stage-custom-text",
            };

        return new StageDataResult { Payload = payload };
    }
}