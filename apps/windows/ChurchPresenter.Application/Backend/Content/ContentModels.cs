using System.Diagnostics.CodeAnalysis;

namespace ChurchPresenter.Backend.Content;

/// <summary>
/// Supported provenance categories for imported or generated content.
/// </summary>
public enum ContentProvenanceKind
{
    Manual,
    BibleGeneration,
    SongSelectImport,
    QuickSearchImport,
    PlanningCenterAttachment,
    TextImport,
    PackageImport,
}

/// <summary>
/// Theme application scopes modeled from ProPresenter behavior.
/// </summary>
public enum ThemeScopeKind
{
    Slide,
    Presentation,
    Library,
    BibleGeneration,
    LookVariant,
}

/// <summary>
/// Supported playlist item categories.
/// </summary>
public enum PlaylistItemKind
{
    PresentationReference,
    MediaReference,
    Header,
    Placeholder,
    ExternalPlanReference,
}

/// <summary>
/// Provenance attached to content so refresh, reporting, and packaging can respect the source workflow.
/// </summary>
public sealed record ContentProvenance
{
    /// <summary>Source category.</summary>
    public ContentProvenanceKind Kind { get; init; } = ContentProvenanceKind.Manual;

    /// <summary>Source system or workflow label.</summary>
    public string SourceSystem { get; init; } = "manual";

    /// <summary>Stable source identity when available.</summary>
    public string? SourceId { get; init; }

    /// <summary>Additional source context such as a plan id or package id.</summary>
    public string? SourceContext { get; init; }

    /// <summary>Whether the source can be refreshed or re-imported.</summary>
    public bool CanRefreshFromSource { get; init; }

    /// <summary>Whether refresh operations should preserve local edits by default.</summary>
    public bool PreserveLocalEdits { get; init; } = true;

    /// <summary>Create provenance for operator-authored content.</summary>
    public static ContentProvenance Manual(string? sourceId = null)
    {
        return new ContentProvenance
        {
            Kind = ContentProvenanceKind.Manual,
            SourceSystem = "manual",
            SourceId = sourceId,
            CanRefreshFromSource = false,
            PreserveLocalEdits = true,
        };
    }

    /// <summary>Create provenance for imported package content.</summary>
    public static ContentProvenance PackageImport(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return new ContentProvenance
        {
            Kind = ContentProvenanceKind.PackageImport,
            SourceSystem = "package",
            SourceId = packageId,
            SourceContext = packageId,
            CanRefreshFromSource = false,
            PreserveLocalEdits = true,
        };
    }

    /// <summary>Create provenance for Planning Center attachments or linked imports.</summary>
    public static ContentProvenance PlanningCenterAttachment(string itemId, string? planId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return new ContentProvenance
        {
            Kind = ContentProvenanceKind.PlanningCenterAttachment,
            SourceSystem = "planning-center",
            SourceId = itemId,
            SourceContext = planId,
            CanRefreshFromSource = true,
            PreserveLocalEdits = true,
        };
    }
}

/// <summary>
/// Theme identity and scope independent from any editor or output implementation.
/// </summary>
public sealed record ContentTheme
{
    /// <summary>Stable theme id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing theme name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Primary scope where this theme is intended to apply.</summary>
    public ThemeScopeKind Scope { get; init; } = ThemeScopeKind.Presentation;

    /// <summary>Optional variant id used by Looks or generated content.</summary>
    public string? VariantId { get; init; }

    /// <summary>Where this theme came from.</summary>
    public ContentProvenance Provenance { get; init; } = ContentProvenance.Manual();
}

/// <summary>
/// Library membership entry for a reusable presentation document.
/// </summary>
public sealed record LibraryPresentationEntry
{
    /// <summary>Referenced presentation id.</summary>
    public string PresentationId { get; init; } = string.Empty;

    /// <summary>Display title captured at the library edge.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional default theme for this library entry.</summary>
    public string? DefaultThemeId { get; init; }
}

/// <summary>
/// A reusable content library independent from show or playlist state.
/// </summary>
public sealed record ContentLibrary
{
    /// <summary>Stable library id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing library name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Optional default theme for new content in this library.</summary>
    public string? DefaultThemeId { get; init; }

    /// <summary>Reusable presentation entries contained by the library.</summary>
    public IReadOnlyList<LibraryPresentationEntry> Presentations { get; init; } = Array.Empty<LibraryPresentationEntry>();
}

/// <summary>
/// A show document that points at one operational playlist plus the libraries it draws from.
/// </summary>
public sealed record ServiceShow
{
    /// <summary>Stable show id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing show name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Libraries visible to this show.</summary>
    public IReadOnlyList<string> LibraryIds { get; init; } = Array.Empty<string>();

    /// <summary>Playlist used as the ordered service flow.</summary>
    public string PlaylistId { get; init; } = string.Empty;

    /// <summary>Optional source template id for recurring shows.</summary>
    public string? SourceTemplateId { get; init; }

    /// <summary>Package boundary used for the durable show document.</summary>
    public ContentPackageBoundaryKind BoundaryKind { get; init; } = ContentPackageBoundaryKind.ShowFile;
}

/// <summary>
/// A presentation document containing reusable slides, groups, arrangements, theme defaults, and provenance.
/// </summary>
public sealed record ContentPresentation
{
    /// <summary>Stable presentation id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Presentation title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Slides in the presentation.</summary>
    public IReadOnlyList<ContentSlide> Slides { get; init; } = Array.Empty<ContentSlide>();

    /// <summary>Named logical groups used by arrangements and reflow workflows.</summary>
    public IReadOnlyList<ContentGroup> Groups { get; init; } = Array.Empty<ContentGroup>();

    /// <summary>Available arrangements for this presentation.</summary>
    public IReadOnlyList<ContentArrangement> Arrangements { get; init; } = Array.Empty<ContentArrangement>();

    /// <summary>Optional default arrangement id for new placements.</summary>
    public string? DefaultArrangementId { get; init; }

    /// <summary>Optional default theme id.</summary>
    public string? DefaultThemeId { get; init; }

    /// <summary>Content provenance.</summary>
    public ContentProvenance Provenance { get; init; } = ContentProvenance.Manual();

    /// <summary>
    /// Resolve an arrangement selection for a playlist occurrence.
    /// </summary>
    public ArrangementResolution ResolveArrangement(string? arrangementId = null)
    {
        if (!string.IsNullOrWhiteSpace(arrangementId))
        {
            ContentArrangement? selected = Arrangements.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, arrangementId, StringComparison.OrdinalIgnoreCase));

            return selected == null
                ? ArrangementResolution.Missing(arrangementId)
                : ArrangementResolution.Resolved(selected, arrangementId, usedFallback: false);
        }

        ContentArrangement? fallback = Arrangements.FirstOrDefault(candidate =>
                                         string.Equals(candidate.Id, DefaultArrangementId, StringComparison.OrdinalIgnoreCase))
                                     ?? Arrangements.FirstOrDefault(candidate => candidate.IsNatural)
                                     ?? Arrangements.FirstOrDefault();

        return fallback == null
            ? ArrangementResolution.Missing(null)
            : ArrangementResolution.Resolved(
                fallback,
                requestedArrangementId: null,
                usedFallback: !string.IsNullOrWhiteSpace(DefaultArrangementId) || !fallback.IsNatural);
    }

    /// <summary>
    /// Attempt to build a new arrangement from an exact ordered group sequence.
    /// </summary>
    public ArrangementCreationResult TryCreateArrangementFromSequence(string arrangementId, string name, IReadOnlyList<string> groupNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arrangementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(groupNames);

        List<string> groupIds = new(groupNames.Count);
        foreach (string groupName in groupNames)
        {
            ContentGroup? group = Groups.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, groupName, StringComparison.Ordinal));

            if (group == null)
            {
                return ArrangementCreationResult.MissingGroup(groupName);
            }

            groupIds.Add(group.Id);
        }

        return ArrangementCreationResult.Created(new ContentArrangement
        {
            Id = arrangementId,
            Name = name,
            GroupIds = groupIds,
        });
    }
}

/// <summary>
/// One slide in a reusable presentation.
/// </summary>
public sealed record ContentSlide
{
    /// <summary>Stable slide id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Optional operator-facing title or preview label.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional group membership.</summary>
    public string? GroupId { get; init; }

    /// <summary>Optional notes.</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Named section grouping used by lyrics and service arrangements.
/// </summary>
public sealed record ContentGroup
{
    /// <summary>Stable group id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Exact group name used for sequence matching.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Slides contained by the group.</summary>
    public IReadOnlyList<string> SlideIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Ordered arrangement of presentation groups.
/// </summary>
public sealed record ContentArrangement
{
    /// <summary>Stable arrangement id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Arrangement name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>True when the arrangement mirrors the natural document order.</summary>
    public bool IsNatural { get; init; }

    /// <summary>Ordered group ids for the arrangement.</summary>
    public IReadOnlyList<string> GroupIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Result of selecting an arrangement for a presentation occurrence.
/// </summary>
public sealed record ArrangementResolution
{
    /// <summary>Selected arrangement, if any.</summary>
    public ContentArrangement? Arrangement { get; init; }

    /// <summary>Requested arrangement id from the occurrence, if any.</summary>
    public string? RequestedArrangementId { get; init; }

    /// <summary>Whether a valid arrangement was selected.</summary>
    [MemberNotNullWhen(true, nameof(Arrangement))]
    public bool IsResolved { get; init; }

    /// <summary>Whether a fallback arrangement was used instead of an explicit request.</summary>
    public bool UsedFallback { get; init; }

    /// <summary>Optional reason when resolution failed.</summary>
    public string? Diagnostic { get; init; }

    internal static ArrangementResolution Resolved(ContentArrangement arrangement, string? requestedArrangementId, bool usedFallback)
    {
        return new ArrangementResolution
        {
            Arrangement = arrangement,
            RequestedArrangementId = requestedArrangementId,
            IsResolved = true,
            UsedFallback = usedFallback,
        };
    }

    internal static ArrangementResolution Missing(string? requestedArrangementId)
    {
        return new ArrangementResolution
        {
            RequestedArrangementId = requestedArrangementId,
            IsResolved = false,
            Diagnostic = string.IsNullOrWhiteSpace(requestedArrangementId)
                ? "Presentation does not define an arrangement."
                : $"Arrangement '{requestedArrangementId}' is not defined.",
        };
    }
}

/// <summary>
/// Result of creating an arrangement from an external sequence.
/// </summary>
public sealed record ArrangementCreationResult
{
    /// <summary>Created arrangement when matching succeeded.</summary>
    public ContentArrangement? Arrangement { get; init; }

    /// <summary>Whether the arrangement could be created.</summary>
    [MemberNotNullWhen(true, nameof(Arrangement))]
    public bool Succeeded { get; init; }

    /// <summary>Missing group name when matching fails.</summary>
    public string? MissingGroupName { get; init; }

    internal static ArrangementCreationResult Created(ContentArrangement arrangement)
    {
        return new ArrangementCreationResult
        {
            Arrangement = arrangement,
            Succeeded = true,
        };
    }

    internal static ArrangementCreationResult MissingGroup(string groupName)
    {
        return new ArrangementCreationResult
        {
            MissingGroupName = groupName,
            Succeeded = false,
        };
    }
}

/// <summary>
/// Reusable reference to a presentation document from a library or playlist occurrence.
/// </summary>
public sealed record PresentationReference
{
    /// <summary>Stable referenced presentation id.</summary>
    public string PresentationId { get; init; } = string.Empty;

    /// <summary>Owning library id when the reference comes from a library.</summary>
    public string? LibraryId { get; init; }

    /// <summary>Optional arrangement selection for this occurrence.</summary>
    public string? ArrangementId { get; init; }

    /// <summary>Optional placement title captured when inserted into a playlist.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Resolve the requested arrangement against a presentation document.</summary>
    public ArrangementResolution ResolveAgainst(ContentPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (!string.Equals(PresentationId, presentation.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Presentation reference '{PresentationId}' cannot resolve presentation '{presentation.Id}'.");
        }

        return presentation.ResolveArrangement(ArrangementId);
    }
}

/// <summary>
/// Reference to a standalone media item placed into a playlist.
/// </summary>
public sealed record MediaReference
{
    /// <summary>Stable media id.</summary>
    public string MediaId { get; init; } = string.Empty;

    /// <summary>Operator-facing media title.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional managed or external asset path.</summary>
    public string? Path { get; init; }

    /// <summary>Media provenance.</summary>
    public ContentProvenance Provenance { get; init; } = ContentProvenance.Manual();
}

/// <summary>
/// Link to an external plan item that can later be matched to local media or presentations.
/// </summary>
public sealed record ExternalPlanReference
{
    /// <summary>External system name.</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>External service or event id.</summary>
    public string ServiceId { get; init; } = string.Empty;

    /// <summary>External plan id.</summary>
    public string PlanId { get; init; } = string.Empty;

    /// <summary>External item id.</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>Optional display title from the external plan.</summary>
    public string? Title { get; init; }

    /// <summary>Optional linked presentation reference.</summary>
    public PresentationReference? LinkedPresentation { get; init; }

    /// <summary>Optional linked media reference.</summary>
    public MediaReference? LinkedMedia { get; init; }

    /// <summary>Optional arrangement chosen for the linked presentation.</summary>
    public string? ArrangementId { get; init; }

    /// <summary>Whether the external item is hidden locally without unlinking it.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Whether local changes may be uploaded to the source system later.</summary>
    public bool AllowUpload { get; init; }

    /// <summary>Whether the linked item currently has a local executable target.</summary>
    public bool HasLocalTarget => LinkedPresentation != null || LinkedMedia != null;
}

/// <summary>
/// Base playlist item contract for executable and structural playlist rows.
/// </summary>
public abstract record PlaylistItem
{
    /// <summary>Stable placement id.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Operator-facing label.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Item kind.</summary>
    public abstract PlaylistItemKind Kind { get; }

    /// <summary>Whether the item contributes structure rather than executable content.</summary>
    public virtual bool IsStructural => Kind is PlaylistItemKind.Header or PlaylistItemKind.Placeholder;

    /// <summary>Whether the item can execute without additional resolution.</summary>
    public virtual bool IsExecutable => Kind is PlaylistItemKind.PresentationReference or PlaylistItemKind.MediaReference;

    /// <summary>Create a new playlist-scoped instance derived from this item.</summary>
    public abstract PlaylistItem CreatePlaylistInstance();

    /// <summary>Create a new stable playlist placement id.</summary>
    protected static string CreatePlacementId() => Guid.NewGuid().ToString("N");
}

/// <summary>
/// Playlist row that reuses a presentation document.
/// </summary>
public sealed record PresentationPlaylistItem : PlaylistItem
{
    /// <summary>Referenced presentation.</summary>
    public PresentationReference Presentation { get; init; } = new();

    /// <inheritdoc />
    public override PlaylistItemKind Kind => PlaylistItemKind.PresentationReference;

    /// <inheritdoc />
    public override PlaylistItem CreatePlaylistInstance() => this with { Id = CreatePlacementId() };
}

/// <summary>
/// Playlist row that triggers standalone media.
/// </summary>
public sealed record MediaPlaylistItem : PlaylistItem
{
    /// <summary>Referenced media.</summary>
    public MediaReference Media { get; init; } = new();

    /// <inheritdoc />
    public override PlaylistItemKind Kind => PlaylistItemKind.MediaReference;

    /// <inheritdoc />
    public override PlaylistItem CreatePlaylistInstance() => this with { Id = CreatePlacementId() };
}

/// <summary>
/// Structural playlist header used to label a service section.
/// </summary>
public sealed record HeaderPlaylistItem : PlaylistItem
{
    /// <summary>Optional notes or automation hints for the section.</summary>
    public string? Notes { get; init; }

    /// <inheritdoc />
    public override PlaylistItemKind Kind => PlaylistItemKind.Header;

    /// <inheritdoc />
    public override bool IsExecutable => false;

    /// <inheritdoc />
    public override PlaylistItem CreatePlaylistInstance() => this with { Id = CreatePlacementId() };
}

/// <summary>
/// Placeholder row for content that will be added later.
/// </summary>
public sealed record PlaceholderPlaylistItem : PlaylistItem
{
    /// <summary>Optional expected kind for the future content.</summary>
    public PlaylistItemKind? ExpectedKind { get; init; }

    /// <summary>Optional fulfillment hint shown to the operator.</summary>
    public string? Hint { get; init; }

    /// <inheritdoc />
    public override PlaylistItemKind Kind => PlaylistItemKind.Placeholder;

    /// <inheritdoc />
    public override bool IsExecutable => false;

    /// <inheritdoc />
    public override PlaylistItem CreatePlaylistInstance() => this with { Id = CreatePlacementId() };
}

/// <summary>
/// External plan row that may or may not yet resolve to local executable content.
/// </summary>
public sealed record ExternalPlanPlaylistItem : PlaylistItem
{
    /// <summary>External plan link.</summary>
    public ExternalPlanReference ExternalPlan { get; init; } = new();

    /// <inheritdoc />
    public override PlaylistItemKind Kind => PlaylistItemKind.ExternalPlanReference;

    /// <inheritdoc />
    public override bool IsExecutable => ExternalPlan.HasLocalTarget && !ExternalPlan.IsHidden;

    /// <inheritdoc />
    public override PlaylistItem CreatePlaylistInstance() => this with { Id = CreatePlacementId() };
}

/// <summary>
/// Ordered operational playlist for a service.
/// </summary>
public sealed record ContentPlaylist
{
    /// <summary>Stable playlist id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Playlist name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional source template id.</summary>
    public string? SourceTemplateId { get; init; }

    /// <summary>Ordered playlist items.</summary>
    public IReadOnlyList<PlaylistItem> Items { get; init; } = Array.Empty<PlaylistItem>();
}

/// <summary>
/// Reusable template that captures recurring playlist structure and references.
/// </summary>
public sealed record PlaylistTemplate
{
    /// <summary>Stable template id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Template name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Template items in service order.</summary>
    public IReadOnlyList<PlaylistItem> Items { get; init; } = Array.Empty<PlaylistItem>();

    /// <summary>Create a concrete playlist instance from this template.</summary>
    public ContentPlaylist CreatePlaylist(string playlistId, string playlistName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistName);

        return new ContentPlaylist
        {
            Id = playlistId,
            Name = playlistName,
            SourceTemplateId = Id,
            Items = Items.Select(item => item.CreatePlaylistInstance()).ToArray(),
        };
    }
}