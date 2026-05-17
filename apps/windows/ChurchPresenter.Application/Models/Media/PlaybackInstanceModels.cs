namespace ChurchPresenter.Models.Media;

/// <summary>
/// A single position in the arranged playback sequence. Stable per session; two instances may
/// reference the same base <see cref="SlideId"/> when a group is repeated in a named arrangement.
/// </summary>
public sealed class PlaybackInstance
{
    /// <summary>
    /// Stable key within this playback sequence:
    /// "{sectionGroupId}_{occurrenceIndex}_{slideId}" for named arrangements, or
    /// "{slideId}" for natural/flat playback.
    /// </summary>
    public string InstanceKey { get; init; } = string.Empty;

    /// <summary>The <see cref="SectionGroup.Id"/> this instance belongs to.</summary>
    public string SectionGroupId { get; init; } = string.Empty;

    /// <summary>The base slide ID (shared across repeated occurrences of the same group).</summary>
    public string SlideId { get; init; } = string.Empty;

    /// <summary>Which repetition of this group this is (0 = first occurrence, 1 = second, …).</summary>
    public int OccurrenceIndex { get; init; }

    /// <summary>The actual slide object resolved from the project.</summary>
    public PresentationSlide Slide { get; init; } = null!;
}

/// <summary>
/// Ordered playback sequence for the currently active arrangement, ready for Show and live-session
/// navigation.
/// </summary>
public sealed class PlaybackSequence
{
    public static readonly PlaybackSequence Empty = new(Array.Empty<PlaybackInstance>(), null);

    public PlaybackSequence(IReadOnlyList<PlaybackInstance> instances, string? activeArrangementId)
    {
        Instances = instances;
        ActiveArrangementId = activeArrangementId;
    }

    public IReadOnlyList<PlaybackInstance> Instances { get; }
    public string? ActiveArrangementId { get; }

    public int Count => Instances.Count;

    public PlaybackInstance? FindByInstanceKey(string instanceKey) =>
        Instances.FirstOrDefault(i => string.Equals(i.InstanceKey, instanceKey, StringComparison.Ordinal));

    public PlaybackInstance? FindFirstBySlideId(string slideId) =>
        Instances.FirstOrDefault(i => string.Equals(i.SlideId, slideId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves the current live playback instance using the arrangement-aware instance key when available,
    /// falling back to the first matching base slide ID for legacy callers.
    /// </summary>
    public PlaybackInstance? FindCurrentProgramInstance(string? instanceKey, string? slideId)
    {
        if (!string.IsNullOrWhiteSpace(instanceKey))
        {
            var byInstance = FindByInstanceKey(instanceKey);
            if (byInstance != null)
                return byInstance;
        }

        return string.IsNullOrWhiteSpace(slideId) ? null : FindFirstBySlideId(slideId);
    }

    public int IndexOfInstanceKey(string instanceKey) =>
        Instances.ToList().FindIndex(i => string.Equals(i.InstanceKey, instanceKey, StringComparison.Ordinal));
}