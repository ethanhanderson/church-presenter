namespace ChurchPresenter.Models.Content;

/// <summary>
/// Classifies file-system and content-cache failures in a way application services can surface to operators.
/// </summary>
public enum ContentAccessFailureKind
{
    /// <summary>No failure occurred.</summary>
    None,

    /// <summary>The requested file, directory, or resource no longer exists.</summary>
    Missing,

    /// <summary>The resource is currently locked or busy and may succeed later.</summary>
    Locked,

    /// <summary>The current user or process does not have permission to access the resource.</summary>
    Unauthorized,

    /// <summary>The resource exists but cannot be parsed or validated.</summary>
    Corrupt,

    /// <summary>A cached artifact was derived from an older resource stamp.</summary>
    Outdated,

    /// <summary>An external device, removable disk, network share, or endpoint is not available.</summary>
    Unavailable,

    /// <summary>The current state no longer matches the state captured by a preview or pending operation.</summary>
    Conflict,

    /// <summary>The operation failed for a reason that may be transient.</summary>
    Transient,

    /// <summary>The operation failed for an unclassified reason.</summary>
    Unknown,
}

/// <summary>
/// Describes a failed content access operation without requiring callers to inspect exception types.
/// </summary>
public sealed record ContentAccessFailure
{
    /// <summary>The classified failure kind.</summary>
    public ContentAccessFailureKind Kind { get; init; }

    /// <summary>The path or resource identifier involved in the failure.</summary>
    public string? Path { get; init; }

    /// <summary>Human-readable diagnostic message safe for logs or operator-facing details.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The original exception type name, when an exception caused the failure.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Whether retrying the operation may succeed without changing input.</summary>
    public bool IsRetryable { get; init; }
}

/// <summary>
/// Captures the observable state used to decide whether a content-derived cache entry is still valid.
/// </summary>
public sealed record ContentResourceStamp
{
    /// <summary>Canonical absolute path or stable resource id.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Whether the resource existed when the stamp was captured.</summary>
    public bool Exists { get; init; }

    /// <summary>Last write timestamp, when available.</summary>
    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    /// <summary>Resource length in bytes, when available.</summary>
    public long? Length { get; init; }

    /// <summary>Optional strong hash for destructive or package/sync validation.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Failure captured while attempting to stamp the resource.</summary>
    public ContentAccessFailure? Failure { get; init; }

    /// <summary>Returns <c>true</c> when this stamp identifies the same resource state as <paramref name="other"/>.</summary>
    public bool Matches(ContentResourceStamp? other)
    {
        if (other is null)
            return false;

        return string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)
               && Exists == other.Exists
               && LastWriteTimeUtc == other.LastWriteTimeUtc
               && Length == other.Length
               && string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Result wrapper for content store operations that can either return a value or a classified failure.
/// </summary>
public sealed record ContentAccessResult<T>
{
    private ContentAccessResult(T? value, ContentAccessFailure? failure, ContentResourceStamp? stamp)
    {
        Value = value;
        Failure = failure;
        Stamp = stamp;
    }

    /// <summary>Gets whether the operation completed successfully.</summary>
    public bool Succeeded => Failure is null;

    /// <summary>The returned value when <see cref="Succeeded"/> is <c>true</c>.</summary>
    public T? Value { get; }

    /// <summary>The classified failure when <see cref="Succeeded"/> is <c>false</c>.</summary>
    public ContentAccessFailure? Failure { get; }

    /// <summary>Resource stamp captured by the operation, when available.</summary>
    public ContentResourceStamp? Stamp { get; }

    /// <summary>Creates a successful operation result.</summary>
    public static ContentAccessResult<T> Success(T? value, ContentResourceStamp? stamp = null) =>
        new(value, null, stamp);

    /// <summary>Creates a failed operation result.</summary>
    public static ContentAccessResult<T> Failed(ContentAccessFailure failure, ContentResourceStamp? stamp = null) =>
        new(default, failure, stamp);
}
