namespace ChurchPresenter.Backend.Content;

/// <summary>
/// Durable package/document boundaries inferred from ProPresenter export, sync, and migration behavior.
/// </summary>
public enum ContentPackageBoundaryKind
{
    ShowFile,
    PresentationDocument,
    PresentationBundle,
    PlaylistPackage,
    PlaylistPackageWithMedia,
    SupportFilePackage,
    SharedConfiguration,
    SyncBackup,
}

/// <summary>
/// Capability description for a package or document boundary.
/// </summary>
public sealed record ContentPackageBoundaryDescriptor
{
    /// <summary>Boundary kind.</summary>
    public ContentPackageBoundaryKind Kind { get; init; }

    /// <summary>Whether the boundary can carry presentation documents.</summary>
    public bool IncludesPresentationDocuments { get; init; }

    /// <summary>Whether the boundary can carry playlist structure.</summary>
    public bool IncludesPlaylistStructure { get; init; }

    /// <summary>Whether the boundary can embed media payloads.</summary>
    public bool IncludesMediaPayloads { get; init; }

    /// <summary>Whether the boundary carries shared configuration such as Looks or stage layouts.</summary>
    public bool IncludesSharedConfiguration { get; init; }

    /// <summary>Whether the boundary can preserve external plan links.</summary>
    public bool SupportsExternalPlanLinks { get; init; }

    /// <summary>Whether the boundary is allowed to express destructive replace semantics.</summary>
    public bool SupportsDestructiveReplace { get; init; }

    /// <summary>Whether content provenance should survive round-tripping through this boundary.</summary>
    public bool PreservesProvenance { get; init; }

    /// <summary>
    /// Returns whether a playlist item kind is valid within this boundary.
    /// </summary>
    public bool SupportsPlaylistItem(PlaylistItemKind itemKind)
    {
        if (!IncludesPlaylistStructure)
        {
            return false;
        }

        return itemKind switch
        {
            PlaylistItemKind.PresentationReference => true,
            PlaylistItemKind.MediaReference => true,
            PlaylistItemKind.Header => true,
            PlaylistItemKind.Placeholder => true,
            PlaylistItemKind.ExternalPlanReference => SupportsExternalPlanLinks,
            _ => false,
        };
    }
}

/// <summary>
/// Static lookup for content package/document boundary semantics.
/// </summary>
public static class ContentPackageBoundaries
{
    private static readonly IReadOnlyDictionary<ContentPackageBoundaryKind, ContentPackageBoundaryDescriptor> Descriptors
        = new Dictionary<ContentPackageBoundaryKind, ContentPackageBoundaryDescriptor>
        {
            [ContentPackageBoundaryKind.ShowFile] = new()
            {
                Kind = ContentPackageBoundaryKind.ShowFile,
                IncludesPresentationDocuments = false,
                IncludesPlaylistStructure = true,
                IncludesMediaPayloads = false,
                IncludesSharedConfiguration = false,
                SupportsExternalPlanLinks = true,
                SupportsDestructiveReplace = false,
                PreservesProvenance = true,
            },
            [ContentPackageBoundaryKind.PresentationDocument] = new()
            {
                Kind = ContentPackageBoundaryKind.PresentationDocument,
                IncludesPresentationDocuments = true,
                IncludesPlaylistStructure = false,
                IncludesMediaPayloads = false,
                IncludesSharedConfiguration = false,
                SupportsExternalPlanLinks = false,
                SupportsDestructiveReplace = false,
                PreservesProvenance = true,
            },
            [ContentPackageBoundaryKind.PresentationBundle] = new()
            {
                Kind = ContentPackageBoundaryKind.PresentationBundle,
                IncludesPresentationDocuments = true,
                IncludesPlaylistStructure = false,
                IncludesMediaPayloads = true,
                IncludesSharedConfiguration = false,
                SupportsExternalPlanLinks = false,
                SupportsDestructiveReplace = false,
                PreservesProvenance = true,
            },
            [ContentPackageBoundaryKind.PlaylistPackage] = new()
            {
                Kind = ContentPackageBoundaryKind.PlaylistPackage,
                IncludesPresentationDocuments = false,
                IncludesPlaylistStructure = true,
                IncludesMediaPayloads = false,
                IncludesSharedConfiguration = false,
                SupportsExternalPlanLinks = true,
                SupportsDestructiveReplace = false,
                PreservesProvenance = true,
            },
            [ContentPackageBoundaryKind.PlaylistPackageWithMedia] = new()
            {
                Kind = ContentPackageBoundaryKind.PlaylistPackageWithMedia,
                IncludesPresentationDocuments = false,
                IncludesPlaylistStructure = true,
                IncludesMediaPayloads = true,
                IncludesSharedConfiguration = false,
                SupportsExternalPlanLinks = true,
                SupportsDestructiveReplace = false,
                PreservesProvenance = true,
            },
            [ContentPackageBoundaryKind.SupportFilePackage] = new()
            {
                Kind = ContentPackageBoundaryKind.SupportFilePackage,
                IncludesPresentationDocuments = false,
                IncludesPlaylistStructure = false,
                IncludesMediaPayloads = false,
                IncludesSharedConfiguration = true,
                SupportsExternalPlanLinks = false,
                SupportsDestructiveReplace = false,
                PreservesProvenance = false,
            },
            [ContentPackageBoundaryKind.SharedConfiguration] = new()
            {
                Kind = ContentPackageBoundaryKind.SharedConfiguration,
                IncludesPresentationDocuments = false,
                IncludesPlaylistStructure = false,
                IncludesMediaPayloads = false,
                IncludesSharedConfiguration = true,
                SupportsExternalPlanLinks = false,
                SupportsDestructiveReplace = false,
                PreservesProvenance = false,
            },
            [ContentPackageBoundaryKind.SyncBackup] = new()
            {
                Kind = ContentPackageBoundaryKind.SyncBackup,
                IncludesPresentationDocuments = true,
                IncludesPlaylistStructure = true,
                IncludesMediaPayloads = true,
                IncludesSharedConfiguration = true,
                SupportsExternalPlanLinks = true,
                SupportsDestructiveReplace = true,
                PreservesProvenance = true,
            },
        };

    /// <summary>
    /// Describe a durable package/document boundary.
    /// </summary>
    public static ContentPackageBoundaryDescriptor Describe(ContentPackageBoundaryKind kind)
    {
        return Descriptors[kind];
    }

    /// <summary>
    /// Returns whether the boundary can retain provenance-bearing content.
    /// </summary>
    public static bool CanRetainProvenance(ContentPackageBoundaryKind kind, ContentProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        ContentPackageBoundaryDescriptor descriptor = Describe(kind);
        return descriptor.PreservesProvenance
               && (descriptor.IncludesPresentationDocuments || descriptor.IncludesPlaylistStructure);
    }
}