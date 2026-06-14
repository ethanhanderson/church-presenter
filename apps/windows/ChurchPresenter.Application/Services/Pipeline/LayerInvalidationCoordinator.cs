using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Content;
using ChurchPresenter.Services.Content;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Debounced content invalidation coordinator for live pipeline layer replacement.
/// </summary>
public sealed class LayerInvalidationCoordinator : IContentCacheInvalidator, IDisposable
{
    private readonly ICuePreparationCoordinator _preparationCoordinator;
    private readonly CuePipelineRuntime _runtime;
    private readonly object _gate = new();
    private readonly Dictionary<string, ContentRef> _pending = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _debounceCts;
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Creates the coordinator and subscribes to content changes when a bus is supplied.</summary>
    public LayerInvalidationCoordinator(
        ICuePreparationCoordinator preparationCoordinator,
        CuePipelineRuntime runtime,
        IContentChangeBus? contentChanges = null)
    {
        _preparationCoordinator = preparationCoordinator ?? throw new ArgumentNullException(nameof(preparationCoordinator));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        if (contentChanges != null)
            contentChanges.Changed += HandleContentChanged;
    }

    private void HandleContentChanged(object? sender, ContentChangeEvent change)
    {
        _ = sender;
        HandleContentChanged(change);
    }

    /// <inheritdoc />
    public void HandleContentChanged(ContentChangeEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);

        ContentRef? contentRef = MapContentRef(change);
        if (contentRef == null)
            return;

        lock (_gate)
        {
            _pending[BuildKey(contentRef)] = contentRef;
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            CancellationToken token = _debounceCts.Token;
            _ = DebounceFlushAsync(token);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }

    private async Task DebounceFlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounceDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ContentRef[] refs;
        lock (_gate)
        {
            refs = _pending.Values.ToArray();
            _pending.Clear();
        }

        foreach (ContentRef contentRef in refs)
            await InvalidateLiveSlotAsync(contentRef, cancellationToken).ConfigureAwait(false);
    }

    private async Task InvalidateLiveSlotAsync(ContentRef contentRef, CancellationToken cancellationToken)
    {
        _preparationCoordinator.HandleInvalidation(contentRef);

        LiveSessionSnapshot snapshot = _runtime.Snapshot;
        foreach ((OutputLayerKind layer, LayerSlot slot) in snapshot.LayerSlots)
        {
            if (slot.PreparedCue == null || !Matches(slot.PreparedCue, contentRef))
                continue;

            CueRequest request = new()
            {
                CueKind = layer == OutputLayerKind.Audio ? CueKind.Audio : layer == OutputLayerKind.Media ? CueKind.Media : CueKind.Slide,
                Layer = layer,
                ContentRef = contentRef,
            };

            PreparedCueRecord? refreshed = await _preparationCoordinator.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
            if (refreshed != null)
                await _runtime.ReplaceLayerPayloadAsync(layer, refreshed, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool Matches(PreparedCueRecord cached, ContentRef target)
    {
        ContentRef contentRef = cached.ContentRef;
        if (contentRef.Kind == target.Kind
            && (string.Equals(contentRef.Id, target.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(contentRef.Version, target.Version, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (target.Kind == ContentRefKind.Presentation
            && (PresentationPathsMatch(contentRef.Version, target.Id)
                || PresentationPathsMatch(contentRef.Version, target.Version)
                || PresentationPathsMatch(cached.ResourceStamp.ContentVersion, target.Id)
                || PresentationPathsMatch(cached.ResourceStamp.ContentVersion, target.Version)))
        {
            return true;
        }

        if (target.Kind == ContentRefKind.Theme
            && (string.Equals(cached.ResourceStamp.ThemeVersion, target.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(cached.ResourceStamp.ThemeVersion, target.Version, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string BuildKey(ContentRef contentRef) => $"{contentRef.Kind}:{contentRef.Id}:{contentRef.Version}";

    private static bool PresentationPathsMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        string normalizedLeft = NormalizePresentationPath(left);
        string normalizedRight = NormalizePresentationPath(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedLeft.EndsWith('/' + normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedRight.EndsWith('/' + normalizedLeft, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePresentationPath(string path) =>
        path.Trim().Replace('\\', '/');

    private static ContentRef? MapContentRef(ContentChangeEvent change)
    {
        if (string.IsNullOrWhiteSpace(change.SubjectId))
            return null;

        ContentRefKind kind = change.Kind switch
        {
            ContentChangeKind.PresentationUpdated
                or ContentChangeKind.PresentationChanged
                or ContentChangeKind.PresentationRenamed
                or ContentChangeKind.PresentationReplaced => ContentRefKind.Presentation,
            ContentChangeKind.PresentationDeleted => ContentRefKind.Presentation,
            ContentChangeKind.ThemeChanged => ContentRefKind.Theme,
            ContentChangeKind.MediaAssetUpdated or ContentChangeKind.MediaAssetRelinked => ContentRefKind.MediaAsset,
            ContentChangeKind.MediaAssetDeleted => ContentRefKind.MediaAsset,
            ContentChangeKind.MediaCueChanged => ContentRefKind.MediaCue,
            ContentChangeKind.SupportRecordChanged => ContentRefKind.Message,
            ContentChangeKind.LinkedSourceChanged => ContentRefKind.Presentation,
            ContentChangeKind.LookOrScreenConfigChanged => ContentRefKind.Theme,
            _ => ContentRefKind.Presentation,
        };

        return new ContentRef
        {
            Kind = kind,
            Id = change.SubjectId,
            Version = change.Stamp?.Sha256 ?? change.Stamp?.Path,
        };
    }
}
