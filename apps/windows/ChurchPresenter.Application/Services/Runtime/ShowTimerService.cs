using ChurchPresenter.Backend.Overlays;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// Runtime and persistence service for Show timers referenced by slide actions.
/// </summary>
public interface IShowTimerService
{
    /// <summary>
    /// Gets the currently active timer id, if any.
    /// </summary>
    string? CurrentTimerId { get; }

    /// <summary>
    /// Gets the currently active timer definition, if any.
    /// </summary>
    ShowTimerDefinition? CurrentTimer { get; }

    /// <summary>
    /// Gets the runtime start time of the current timer.
    /// </summary>
    DateTimeOffset? CurrentTimerStartedAt { get; }

    /// <summary>
    /// Loads the available timers from persisted settings.
    /// </summary>
    Task<IReadOnlyList<ShowTimerDefinition>> GetTimersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a named timer definition.
    /// </summary>
    Task<ShowTimerDefinition> SaveTimerAsync(ShowTimerDefinition timer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a timer definition and clears runtime state if it is active.
    /// </summary>
    Task DeleteTimerAsync(string timerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the requested timer definition.
    /// </summary>
    void ActivateTimer(string? timerId);

    /// <summary>Starts or resumes a timer definition.</summary>
    void StartTimer(string timerId);

    /// <summary>Stops a timer while preserving the generated snapshot.</summary>
    void StopTimer(string timerId);

    /// <summary>Resets a timer to its configured start value.</summary>
    void ResetTimer(string timerId);
}

/// <inheritdoc />
public sealed class ShowTimerService(
    ISettingsService settings,
    ILogger<ShowTimerService> logger,
    ILiveProductionFacade? liveProduction = null) : IShowTimerService
{
    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<ShowTimerService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILiveProductionFacade? _liveProduction = liveProduction;

    /// <inheritdoc />
    public string? CurrentTimerId { get; private set; }

    /// <inheritdoc />
    public ShowTimerDefinition? CurrentTimer { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? CurrentTimerStartedAt { get; private set; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ShowTimerDefinition>> GetTimersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _settings.LoadAsync().ConfigureAwait(false);
        return _settings.Settings.Show.Timers
            .OrderBy(timer => timer.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CloneTimer)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ShowTimerDefinition> SaveTimerAsync(ShowTimerDefinition timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        cancellationToken.ThrowIfCancellationRequested();

        await _settings.LoadAsync().ConfigureAwait(false);
        var normalized = CloneTimer(timer);
        if (string.IsNullOrWhiteSpace(normalized.Id))
            normalized.Id = Guid.NewGuid().ToString("N");
        normalized.Name = string.IsNullOrWhiteSpace(normalized.Name) ? "Timer" : normalized.Name.Trim();
        normalized.Kind = NormalizeTimerKind(normalized.Kind);
        normalized.DurationSeconds = Math.Max(0, normalized.DurationSeconds);
        normalized.StartSeconds = Math.Max(0, normalized.StartSeconds);
        normalized.EndSeconds = normalized.EndSeconds is int endSeconds ? Math.Max(0, endSeconds) : null;

        var timers = _settings.Settings.Show.Timers;
        var existingIndex = timers.FindIndex(item => string.Equals(item.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            timers[existingIndex] = normalized;
        else
            timers.Add(normalized);

        await _settings.SaveAsync().ConfigureAwait(false);
        return CloneTimer(normalized);
    }

    /// <inheritdoc />
    public async Task DeleteTimerAsync(string timerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        cancellationToken.ThrowIfCancellationRequested();

        await _settings.LoadAsync().ConfigureAwait(false);
        _settings.Settings.Show.Timers.RemoveAll(timer => string.Equals(timer.Id, timerId, StringComparison.OrdinalIgnoreCase));
        await _settings.SaveAsync().ConfigureAwait(false);

        if (string.Equals(CurrentTimerId, timerId, StringComparison.OrdinalIgnoreCase))
            ActivateTimer(null);
    }

    /// <inheritdoc />
    public void ActivateTimer(string? timerId)
    {
        ShowTimerDefinition? previousTimer = CurrentTimer;
        CurrentTimerId = string.IsNullOrWhiteSpace(timerId) ? null : timerId;
        CurrentTimer = _settings.Settings.Show.Timers.FirstOrDefault(timer =>
            string.Equals(timer.Id, CurrentTimerId, StringComparison.OrdinalIgnoreCase));
        CurrentTimerStartedAt = CurrentTimer == null ? null : DateTimeOffset.UtcNow;
        PublishTimerSnapshot(previousTimer);
        _logger.LogInformation("Activated show timer {TimerId}.", CurrentTimerId ?? "<none>");
    }

    /// <inheritdoc />
    public void StartTimer(string timerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        ActivateTimer(timerId);
    }

    /// <inheritdoc />
    public void StopTimer(string timerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        ShowTimerDefinition? timer = _settings.Settings.Show.Timers.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, timerId, StringComparison.OrdinalIgnoreCase));
        if (timer == null)
            return;

        if (string.Equals(CurrentTimerId, timerId, StringComparison.OrdinalIgnoreCase))
        {
            CurrentTimerId = null;
            CurrentTimer = null;
            CurrentTimerStartedAt = null;
        }

        _liveProduction?.SetTimer(CreateTimerSnapshot(timer, GeneratedTimerStatus.Paused, ResolveConfiguredDuration(timer)));
    }

    /// <inheritdoc />
    public void ResetTimer(string timerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        ShowTimerDefinition? timer = _settings.Settings.Show.Timers.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, timerId, StringComparison.OrdinalIgnoreCase));
        if (timer == null)
            return;

        _liveProduction?.SetTimer(CreateTimerSnapshot(timer, GeneratedTimerStatus.Stopped, ResolveConfiguredDuration(timer)));
        if (string.Equals(CurrentTimerId, timerId, StringComparison.OrdinalIgnoreCase))
        {
            CurrentTimerId = null;
            CurrentTimer = null;
            CurrentTimerStartedAt = null;
        }
    }

    private void PublishTimerSnapshot(ShowTimerDefinition? previousTimer)
    {
        if (_liveProduction == null)
            return;

        if (CurrentTimer == null)
        {
            if (previousTimer != null)
                _liveProduction.SetTimer(CreateTimerSnapshot(previousTimer, GeneratedTimerStatus.Stopped, TimeSpan.Zero));
            return;
        }

        if (CurrentTimerStartedAt == null)
            return;

        TimeSpan duration = ResolveConfiguredDuration(CurrentTimer);
        _liveProduction.SetTimer(CreateTimerSnapshot(CurrentTimer, GeneratedTimerStatus.Running, duration));
    }

    private static TimerSnapshot CreateTimerSnapshot(
        ShowTimerDefinition timer,
        GeneratedTimerStatus status,
        TimeSpan remaining)
    {
        return new TimerSnapshot
        {
            Id = timer.Id,
            Name = timer.Name,
            Kind = ShowControlsModelHelpers.ParseTimerKind(timer.Kind),
            Status = status,
            Remaining = remaining,
            DisplayValue = FormatDuration(remaining),
            IsOverrun = status == GeneratedTimerStatus.Overrun,
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        TimeSpan normalized = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return normalized.TotalHours >= 1
            ? normalized.ToString(@"h\:mm\:ss")
            : normalized.ToString(@"mm\:ss");
    }

    private static ShowTimerDefinition CloneTimer(ShowTimerDefinition timer)
    {
        return new ShowTimerDefinition
        {
            Id = timer.Id,
            Name = timer.Name,
            Kind = NormalizeTimerKind(timer.Kind),
            DurationSeconds = timer.DurationSeconds,
            TargetTime = timer.TargetTime,
            StartSeconds = timer.StartSeconds,
            EndSeconds = timer.EndSeconds,
            AllowsOverrun = timer.AllowsOverrun,
        };
    }

    private static TimeSpan ResolveConfiguredDuration(ShowTimerDefinition timer)
    {
        return ShowControlsModelHelpers.ParseTimerKind(timer.Kind) == GeneratedTimerKind.ElapsedTime
            ? TimeSpan.FromSeconds(Math.Max(0, timer.EndSeconds ?? timer.DurationSeconds))
            : TimeSpan.FromSeconds(Math.Max(0, timer.DurationSeconds));
    }

    private static string NormalizeTimerKind(string? kind) =>
        ShowControlsModelHelpers.ToTimerKindKey(ShowControlsModelHelpers.ParseTimerKind(kind));
}