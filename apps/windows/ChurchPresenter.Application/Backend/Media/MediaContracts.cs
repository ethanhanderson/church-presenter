
namespace ChurchPresenter.Backend.Media;

/// <summary>
/// Stable backend asset categories used by media, audio, and live-input contracts.
/// </summary>
public enum MediaAssetKind
{
    Image,
    Video,
    Audio,
    LiveVideoInput,
}

/// <summary>
/// Determines how ChurchPresenter owns and resolves the underlying media payload.
/// </summary>
public enum MediaStoragePolicy
{
    Managed,
    Referenced,
    ImportedPackage,
    Package,
}

/// <summary>
/// Current availability for a media asset independent from its stable identity.
/// </summary>
public enum MediaAvailabilityStatus
{
    Available,
    Missing,
    Relinked,
}

/// <summary>
/// Foreground/background role used by cue resolution and retrigger semantics.
/// </summary>
public enum MediaCueRole
{
    Foreground,
    Background,
}

/// <summary>
/// Scaling modes sourced from the ProPresenter media inspector/import model.
/// </summary>
public enum MediaScalingMode
{
    ScaleToFit,
    ScaleToFill,
    StretchToFill,
    ScaleAndBlur,
}

/// <summary>
/// Playback policy for image, video, and audio cues.
/// </summary>
public enum MediaPlaybackMode
{
    Stop,
    Loop,
    PlayNext,
}

/// <summary>
/// Crop region normalized to the source media bounds.
/// </summary>
public sealed record MediaCropRegion
{
    public double Left { get; init; }

    public double Top { get; init; }

    public double Right { get; init; }

    public double Bottom { get; init; }

    public bool IsEmpty => Left <= 0 && Top <= 0 && Right <= 0 && Bottom <= 0;
}

/// <summary>
/// Non-destructive visual adjustments applied at cue resolution time.
/// </summary>
public sealed record MediaEffectSettings
{
    public double Opacity { get; init; } = 1d;

    public double Brightness { get; init; }

    public double Contrast { get; init; } = 1d;

    public double Saturation { get; init; } = 1d;
}

/// <summary>
/// Health snapshot for a configured live video input.
/// </summary>
public enum LiveVideoInputHealth
{
    Unknown,
    Ready,
    Missing,
    Faulted,
}

/// <summary>
/// Source technology for a live video input.
/// </summary>
public enum LiveVideoTransportKind
{
    UsbCapture,
    Sdi,
    Ndi,
    Syphon,
    Other,
}

/// <summary>
/// Immutable availability snapshot for one media asset resolution pass.
/// </summary>
public sealed record MediaAvailability
{
    public MediaAvailabilityStatus Status { get; init; } = MediaAvailabilityStatus.Available;

    public string? LastKnownPath { get; init; }

    public string? DiagnosticMessage { get; init; }

    public ContentAccessFailureKind? FailureKind { get; init; }

    public DateTimeOffset? CheckedAtUtc { get; init; }

    public string? RelinkedFromPath { get; init; }

    public string? SearchRootId { get; init; }

    public bool IsPlayable => Status != MediaAvailabilityStatus.Missing;

    public static MediaAvailability Available(string? path = null, DateTimeOffset? checkedAtUtc = null)
    {
        return new MediaAvailability
        {
            Status = MediaAvailabilityStatus.Available,
            LastKnownPath = path,
            CheckedAtUtc = checkedAtUtc,
        };
    }

    public static MediaAvailability Missing(
        string? lastKnownPath,
        string? diagnosticMessage,
        DateTimeOffset? checkedAtUtc = null,
        ContentAccessFailureKind failureKind = ContentAccessFailureKind.Missing)
    {
        return new MediaAvailability
        {
            Status = MediaAvailabilityStatus.Missing,
            LastKnownPath = lastKnownPath,
            DiagnosticMessage = diagnosticMessage,
            CheckedAtUtc = checkedAtUtc,
            FailureKind = failureKind,
        };
    }

    public static MediaAvailability Relinked(
        string resolvedPath,
        string? relinkedFromPath,
        string? searchRootId,
        DateTimeOffset? checkedAtUtc = null)
    {
        return new MediaAvailability
        {
            Status = MediaAvailabilityStatus.Relinked,
            LastKnownPath = resolvedPath,
            RelinkedFromPath = relinkedFromPath,
            SearchRootId = searchRootId,
            CheckedAtUtc = checkedAtUtc,
        };
    }
}

/// <summary>
/// Placeholder metadata for future audio-channel and output-device routing.
/// </summary>
public sealed record AudioRoutingMetadata
{
    public int InternalChannelCount { get; init; } = 2;

    public IReadOnlyList<int> TargetInternalChannels { get; init; } = Array.Empty<int>();

    public string? PreferredOutputDeviceId { get; init; }

    public string? InspectorOutputDeviceId { get; init; }

    public string? NdiOutputId { get; init; }

    public string? SdiOutputId { get; init; }

    public TimeSpan? OutputDelay { get; init; }

    public bool MuteMainOutputWhenOnlyNetworkOutputs { get; init; }
}

/// <summary>
/// Stable asset identity decoupled from its current path or live input location.
/// </summary>
public sealed record MediaAsset
{
    public string AssetId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public MediaAssetKind Kind { get; init; }

    public MediaStoragePolicy StoragePolicy { get; init; } = MediaStoragePolicy.Managed;

    public string? OriginalSourcePath { get; init; }

    public string? ResolvedPath { get; init; }

    public string? PackagePath { get; init; }

    public string? SourceFingerprint { get; init; }

    public MediaAvailability Availability { get; init; } = MediaAvailability.Available();

    public MediaCueProfile DefaultCue { get; init; } = new();

    public bool OwnsManagedPayload => StoragePolicy is MediaStoragePolicy.Managed or MediaStoragePolicy.ImportedPackage or MediaStoragePolicy.Package;

    public MediaAsset MarkMissing(string diagnosticMessage, DateTimeOffset? checkedAtUtc = null)
    {
        return this with
        {
            ResolvedPath = null,
            Availability = MediaAvailability.Missing(
                ResolvedPath ?? OriginalSourcePath ?? PackagePath,
                diagnosticMessage,
                checkedAtUtc),
        };
    }

    public MediaAsset ApplyRelink(MediaRelinkResult relinkResult)
    {
        ArgumentNullException.ThrowIfNull(relinkResult);

        return relinkResult.Success
            ? this with
            {
                ResolvedPath = relinkResult.ResolvedPath,
                Availability = relinkResult.Availability,
            }
            : this with
            {
                ResolvedPath = null,
                Availability = relinkResult.Availability,
            };
    }
}

/// <summary>
/// Stable reference to a media asset from a slide, playlist, macro, package, or other content graph node.
/// </summary>
public sealed record AssetReference
{
    public string ReferenceId { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public MediaAssetKind? ExpectedKind { get; init; }

    public string? OwnerReferenceId { get; init; }

    public MediaReferenceSurface? Surface { get; init; }

    public string? PathHint { get; init; }

    public MediaAsset? Resolve(IEnumerable<MediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        MediaAsset? asset = assets.FirstOrDefault(candidate =>
            string.Equals(candidate.AssetId, AssetId, StringComparison.OrdinalIgnoreCase));

        if (asset is null)
            return null;

        if (ExpectedKind.HasValue && asset.Kind != ExpectedKind.Value)
            return null;

        return asset;
    }
}

/// <summary>
/// Operator-configured search root used for missing-media recovery.
/// </summary>
public sealed record MediaSearchRoot
{
    public string RootId { get; init; } = string.Empty;

    public string RootPath { get; init; } = string.Empty;

    public int Priority { get; init; }

    public bool IsEnabled { get; init; } = true;
}

/// <summary>
/// Outcome of one relink attempt against configured search roots.
/// </summary>
public sealed record MediaRelinkResult
{
    public bool Success { get; init; }

    public string? ResolvedPath { get; init; }

    public MediaAvailability Availability { get; init; } = MediaAvailability.Available();
}

/// <summary>
/// Testable relink behavior over a discovered file catalog rather than direct file I/O.
/// </summary>
public static class MediaRelinker
{
    public static MediaRelinkResult TryRelink(
        MediaAsset asset,
        IEnumerable<MediaSearchRoot> searchRoots,
        IEnumerable<string> discoveredPaths,
        DateTimeOffset? checkedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(searchRoots);
        ArgumentNullException.ThrowIfNull(discoveredPaths);

        string? missingPath = asset.ResolvedPath ?? asset.OriginalSourcePath ?? asset.PackagePath;
        string? fileName = missingPath is null ? null : Path.GetFileName(missingPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new MediaRelinkResult
            {
                Success = false,
                Availability = MediaAvailability.Missing(
                    missingPath,
                    "Asset does not have enough path information to relink.",
                    checkedAtUtc),
            };
        }

        string originalPath = missingPath ?? fileName;
        List<RelinkCandidate> candidates = new();
        foreach (MediaSearchRoot root in searchRoots
                     .Where(static root => root.IsEnabled && !string.IsNullOrWhiteSpace(root.RootPath))
                     .OrderBy(root => root.Priority))
        {
            string normalizedRoot = NormalizeDirectory(root.RootPath);
            foreach (string discoveredPath in discoveredPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string normalizedCandidate = NormalizeFile(discoveredPath);
                if (!string.Equals(Path.GetFileName(normalizedCandidate), fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!IsUnderRoot(normalizedCandidate, normalizedRoot))
                    continue;

                string relativePath = normalizedCandidate[normalizedRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                candidates.Add(new RelinkCandidate
                {
                    SearchRootId = root.RootId,
                    SearchRootPriority = root.Priority,
                    FullPath = normalizedCandidate,
                    RelativePath = relativePath,
                    TailMatchDepth = CalculateTailMatchDepth(originalPath, relativePath),
                });
            }
        }

        RelinkCandidate? bestCandidate = candidates
            .OrderByDescending(candidate => candidate.TailMatchDepth)
            .ThenBy(candidate => candidate.SearchRootPriority)
            .ThenBy(candidate => candidate.RelativePath.Length)
            .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (bestCandidate is null)
        {
            return new MediaRelinkResult
            {
                Success = false,
                Availability = MediaAvailability.Missing(
                    missingPath,
                    $"No relink candidate was found for '{fileName}'.",
                    checkedAtUtc),
            };
        }

        return new MediaRelinkResult
        {
            Success = true,
            ResolvedPath = bestCandidate.FullPath,
            Availability = MediaAvailability.Relinked(
                bestCandidate.FullPath,
                missingPath,
                bestCandidate.SearchRootId,
                checkedAtUtc),
        };
    }

    private static bool IsUnderRoot(string candidatePath, string normalizedRoot)
    {
        return candidatePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static int CalculateTailMatchDepth(string originalPath, string candidateRelativePath)
    {
        string[] originalSegments = SplitPathSegments(originalPath);
        string[] candidateSegments = SplitPathSegments(candidateRelativePath);
        int matchCount = 0;

        for (int originalIndex = originalSegments.Length - 1, candidateIndex = candidateSegments.Length - 1;
             originalIndex >= 0 && candidateIndex >= 0;
             originalIndex--, candidateIndex--)
        {
            if (!string.Equals(originalSegments[originalIndex], candidateSegments[candidateIndex], StringComparison.OrdinalIgnoreCase))
                break;

            matchCount++;
        }

        return matchCount;
    }

    private static string[] SplitPathSegments(string path)
    {
        return path
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeDirectory(string path)
    {
        string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized + Path.DirectorySeparatorChar;
    }

    private static string NormalizeFile(string path)
    {
        return Path.GetFullPath(path);
    }

    private sealed record RelinkCandidate
    {
        public string SearchRootId { get; init; } = string.Empty;

        public int SearchRootPriority { get; init; }

        public string FullPath { get; init; } = string.Empty;

        public string RelativePath { get; init; } = string.Empty;

        public int TailMatchDepth { get; init; }
    }
}

/// <summary>
/// Live video input modeled as a media-like contract with device health and optional audio linkage.
/// </summary>
public sealed record LiveVideoInput
{
    public string InputId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public LiveVideoTransportKind TransportKind { get; init; } = LiveVideoTransportKind.Other;

    public string? SourceName { get; init; }

    public string? SourceIdentifier { get; init; }

    public string? PreferredVideoMode { get; init; }

    public string? PreviewThumbnailPath { get; init; }

    public string? LinkedAudioSourceId { get; init; }

    public LiveVideoInputHealth Health { get; init; } = LiveVideoInputHealth.Unknown;

    public AudioRoutingMetadata AudioRouting { get; init; } = new();

    public MediaCueRole DefaultRole { get; init; } = MediaCueRole.Background;

    public MediaAsset ToAsset()
    {
        MediaAvailability availability = Health switch
        {
            LiveVideoInputHealth.Ready => MediaAvailability.Available(SourceIdentifier),
            LiveVideoInputHealth.Missing => MediaAvailability.Missing(SourceIdentifier, "Live video input is unavailable."),
            LiveVideoInputHealth.Faulted => MediaAvailability.Missing(SourceIdentifier, "Live video input reported a fault."),
            _ => MediaAvailability.Available(SourceIdentifier),
        };

        return new MediaAsset
        {
            AssetId = InputId,
            DisplayName = DisplayName,
            Kind = MediaAssetKind.LiveVideoInput,
            StoragePolicy = MediaStoragePolicy.Referenced,
            OriginalSourcePath = SourceIdentifier,
            ResolvedPath = availability.IsPlayable ? SourceIdentifier : null,
            Availability = availability,
            DefaultCue = new MediaCueProfile
            {
                Role = DefaultRole,
                AudioRouting = AudioRouting,
            },
        };
    }
}