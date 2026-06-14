using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Services.Media;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Delegates media transport commands to the active pipeline media slot binding.
/// </summary>
public sealed class MediaTransportService(CuePipelineRuntime runtime) : IMediaTransportService
{
    private const int DefaultSeekSeconds = 5;
    private readonly CuePipelineRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private double? _scrubPositionSeconds;

    /// <inheritdoc />
    public string? SelectedTransportTarget => _runtime.ActiveMediaBinding?.PlayerId;

    /// <inheritdoc />
    public TransportDisplaySnapshot GetDisplay(MediaPlaybackTarget target)
    {
        OutputLayerKind layerKind = ResolveLayerKind(target);
        LayerState? layerState = TryGetLayerState(layerKind);
        if (layerState is not { Payload: not null }
            || !layerState.IsVisible
            || layerState.IsSuppressed
            || layerState.IsCleared)
        {
            return new TransportDisplaySnapshot
            {
                HasActiveCue = false,
                IsEnabled = false,
                Name = string.Empty,
            };
        }

        IMediaSlotBinding? binding = GetPrimaryBinding(target);
        return BuildDisplay(binding, ResolvePayloadDisplayName(layerState.Payload, target), hasActiveCue: true);
    }

    /// <inheritdoc />
    public Task PlayAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IMediaSlotBinding> bindings = GetBindings(target);
        return bindings.Count == 0
            ? Task.CompletedTask
            : Task.WhenAll(bindings.Select(binding => binding.PlayAsync(cancellationToken)));
    }

    /// <inheritdoc />
    public Task PauseAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IMediaSlotBinding> bindings = GetBindings(target);
        return bindings.Count == 0
            ? Task.CompletedTask
            : Task.WhenAll(bindings.Select(binding => binding.PauseAsync(cancellationToken)));
    }

    /// <inheritdoc />
    public async Task SeekForwardAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IMediaSlotBinding> bindings = GetBindings(target);
        if (bindings.Count == 0)
            return;

        await Task.WhenAll(bindings.Select(binding =>
        {
            TimeSpan next = binding.Position + TimeSpan.FromSeconds(DefaultSeekSeconds);
            if (binding.Duration.HasValue)
                next = next > binding.Duration.Value ? binding.Duration.Value : next;

            return binding.SeekAsync(next, cancellationToken);
        })).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SeekBackwardAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IMediaSlotBinding> bindings = GetBindings(target);
        if (bindings.Count == 0)
            return;

        await Task.WhenAll(bindings.Select(binding =>
        {
            TimeSpan next = binding.Position - TimeSpan.FromSeconds(DefaultSeekSeconds);
            if (next < TimeSpan.Zero)
                next = TimeSpan.Zero;

            return binding.SeekAsync(next, cancellationToken);
        })).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task RestartAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IMediaSlotBinding> bindings = GetBindings(target);
        return bindings.Count == 0
            ? Task.CompletedTask
            : Task.WhenAll(bindings.Select(binding => binding.RestartAsync(cancellationToken)));
    }

    /// <inheritdoc />
    public void BeginScrub(MediaPlaybackTarget target)
    {
        IMediaSlotBinding? binding = GetPrimaryBinding(target);
        _scrubPositionSeconds = binding?.Position.TotalSeconds ?? 0;
    }

    /// <inheritdoc />
    public void UpdateScrub(double positionSeconds)
    {
        _scrubPositionSeconds = Math.Max(0, positionSeconds);
    }

    /// <inheritdoc />
    public Task CommitScrubAsync(MediaPlaybackTarget target, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IMediaSlotBinding> bindings = GetBindings(target);
        if (bindings.Count == 0 || !_scrubPositionSeconds.HasValue)
            return Task.CompletedTask;

        TimeSpan position = TimeSpan.FromSeconds(_scrubPositionSeconds.Value);
        _scrubPositionSeconds = null;
        return Task.WhenAll(bindings.Select(binding => binding.SeekAsync(position, cancellationToken)));
    }

    private IMediaSlotBinding? GetPrimaryBinding(MediaPlaybackTarget target) =>
        _runtime.GetActiveMediaBinding(ResolveLayerKind(target));

    private IReadOnlyList<IMediaSlotBinding> GetBindings(MediaPlaybackTarget target) =>
        _runtime.GetActiveMediaBindings(ResolveLayerKind(target));

    private LayerState? TryGetLayerState(OutputLayerKind layerKind)
    {
        LiveRenderSessionState state = _runtime.Snapshot.SessionState;
        return state.Layers.TryGetValue(layerKind, out LayerState? layerState) ? layerState : null;
    }

    private static OutputLayerKind ResolveLayerKind(MediaPlaybackTarget target) =>
        target switch
        {
            MediaPlaybackTarget.AudioFiles => OutputLayerKind.Audio,
            MediaPlaybackTarget.Announcements => OutputLayerKind.Announcements,
            _ => OutputLayerKind.Media,
        };

    private static TransportDisplaySnapshot BuildDisplay(IMediaSlotBinding? binding, string displayName, bool hasActiveCue)
    {
        if (binding == null)
        {
            return new TransportDisplaySnapshot
            {
                HasActiveCue = hasActiveCue,
                IsEnabled = false,
                Name = displayName,
            };
        }

        double positionSeconds = binding.Position.TotalSeconds;
        double? durationSeconds = binding.Duration?.TotalSeconds;
        TransportDisplaySnapshot display = new()
        {
            HasActiveCue = hasActiveCue,
            IsEnabled = true,
            Name = displayName,
            PositionSeconds = positionSeconds,
            DurationSeconds = durationSeconds,
            PositionLabel = FormatTime(positionSeconds),
            DurationLabel = durationSeconds.HasValue ? FormatTime(durationSeconds.Value) : "0:00",
            IsPlaying = binding.IsPlaying,
        };

        return display;
    }

    private static string ResolvePayloadDisplayName(RenderPayloadDescriptor payload, MediaPlaybackTarget target)
    {
        return payload.Detail switch
        {
            MediaRenderPayload media => FirstNonWhiteSpace(
                media.Media.DisplayName,
                FileNameWithoutExtension(media.Media.ResolvedSourcePath),
                FileNameWithoutExtension(media.Media.MediaId),
                FileNameWithoutExtension(payload.DisplayName),
                payload.DisplayName,
                payload.Id),
            PresentationRenderPayload presentation => FirstNonWhiteSpace(
                FileNameWithoutExtension(presentation.PresentationPath),
                payload.DisplayName,
                presentation.SlideId,
                target == MediaPlaybackTarget.Announcements ? "Announcement" : "Presentation"),
            _ => FirstNonWhiteSpace(
                FileNameWithoutExtension(payload.SourceReference),
                FileNameWithoutExtension(payload.DisplayName),
                payload.DisplayName,
                payload.Id),
        };
    }

    private static string? FileNameWithoutExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string fileName = Path.GetFileNameWithoutExtension(value);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static string FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string FormatTime(double totalSeconds)
    {
        int seconds = Math.Max(0, (int)Math.Floor(totalSeconds));
        int minutes = seconds / 60;
        seconds %= 60;
        return $"{minutes}:{seconds:00}";
    }
}
