using System.Text.Json.Serialization;

namespace ChurchPresenter.Models.Support;

/// <summary>
/// Supported import changes for portable support packages and sync snapshots.
/// </summary>
public enum SupportPackageChangeKind
{
    Add,
    Replace,
    Delete,
    Unchanged,
    Conflict,
    Warning,
}

/// <summary>
/// Media or presentation payload copy action required by a package import.
/// </summary>
public enum PackageCopyRequirementKind
{
    None,
    CopyPresentationBundle,
    CopyEmbeddedMediaPayload,
    CopyReferencedMediaPayload,
}

/// <summary>
/// One previewed support-package change.
/// </summary>
public sealed class SupportPackagePreviewChange
{
    [JsonPropertyName("kind")]
    public SupportPackageChangeKind Kind { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("isDestructive")]
    public bool IsDestructive { get; init; }

    [JsonPropertyName("requiresConfirmation")]
    public bool RequiresConfirmation { get; init; }

    [JsonPropertyName("destinationStamp")]
    public ContentResourceStamp? DestinationStamp { get; init; }
}

/// <summary>
/// Import preview for a support package before any local files are changed.
/// </summary>
public sealed class SupportPackagePreview
{
    [JsonPropertyName("packageType")]
    public string PackageType { get; init; } = string.Empty;

    [JsonPropertyName("replaceMissingFiles")]
    public bool ReplaceMissingFiles { get; init; }

    [JsonPropertyName("changes")]
    public IReadOnlyList<SupportPackagePreviewChange> Changes { get; init; } = [];

    [JsonPropertyName("packageStamp")]
    public ContentResourceStamp? PackageStamp { get; init; }

    [JsonIgnore]
    public bool HasDestructiveChanges => Changes.Any(static change => change.IsDestructive);

    [JsonIgnore]
    public bool RequiresConfirmation => Changes.Any(static change => change.RequiresConfirmation || change.IsDestructive);
}

/// <summary>
/// Options controlling support-package export.
/// </summary>
public sealed class SupportPackageExportOptions
{
    public bool ReplaceMissingFilesOnImport { get; init; }
}

/// <summary>
/// Options controlling support-package import.
/// </summary>
public sealed class SupportPackageImportOptions
{
    public bool AllowDestructiveReplace { get; init; }
}

/// <summary>
/// Options controlling collection package import.
/// </summary>
public sealed class CollectionPackageImportOptions
{
    public bool AllowDestructiveReplace { get; init; }

    public bool ReplaceConflictingPresentationBundles { get; init; }
}

/// <summary>
/// One media or bundle copy action surfaced before package import writes files.
/// </summary>
public sealed class PackageCopyRequirement
{
    [JsonPropertyName("kind")]
    public PackageCopyRequirementKind Kind { get; init; }

    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; init; } = string.Empty;

    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; init; } = string.Empty;

    [JsonPropertyName("byteSize")]
    public long? ByteSize { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// One previewed change from a collection package import.
/// </summary>
public sealed class CollectionPackagePreviewChange
{
    [JsonPropertyName("kind")]
    public SupportPackageChangeKind Kind { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("isDestructive")]
    public bool IsDestructive { get; init; }

    [JsonPropertyName("requiresConfirmation")]
    public bool RequiresConfirmation { get; init; }

    [JsonPropertyName("destinationStamp")]
    public ContentResourceStamp? DestinationStamp { get; init; }

    [JsonPropertyName("copyRequirements")]
    public IReadOnlyList<PackageCopyRequirement> CopyRequirements { get; init; } = [];
}

/// <summary>
/// Preview for a library or playlist package before local collections are modified.
/// </summary>
public sealed class CollectionPackagePreview
{
    [JsonPropertyName("packageType")]
    public string PackageType { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("changes")]
    public IReadOnlyList<CollectionPackagePreviewChange> Changes { get; init; } = [];

    [JsonPropertyName("copyRequirements")]
    public IReadOnlyList<PackageCopyRequirement> CopyRequirements { get; init; } = [];

    [JsonPropertyName("packageStamp")]
    public ContentResourceStamp? PackageStamp { get; init; }

    [JsonIgnore]
    public bool HasDestructiveChanges => Changes.Any(static change => change.IsDestructive);

    [JsonIgnore]
    public bool RequiresConfirmation => Changes.Any(static change => change.RequiresConfirmation || change.IsDestructive);
}