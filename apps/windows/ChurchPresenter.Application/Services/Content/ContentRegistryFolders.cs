namespace ChurchPresenter.Services.Content;

/// <summary>
/// Resolves domain storage folder names from registry indexes (name-based folders; ids in JSON only).
/// </summary>
internal static class ContentRegistryFolders
{
    public static string ResolveFolderName(DomainIndex index, string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        var entry = index.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, entityId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(entry?.FolderName))
            return entry.FolderName;

        return entityId;
    }

    public static string AllocateFolderName(
        string parentDirectory,
        DomainIndex index,
        string entityId,
        string displayName)
    {
        var entry = index.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, entityId, StringComparison.OrdinalIgnoreCase));
        var currentFolder = entry?.FolderName;
        if (string.IsNullOrWhiteSpace(currentFolder))
            currentFolder = ResolveFolderName(index, entityId);

        var allocated = ContentNaming.AllocateDirectoryName(parentDirectory, displayName, currentFolder);
        return Path.GetFileName(allocated);
    }

    public static void ApplyFolderName(DomainIndexEntry entry, string folderName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        entry.FolderName = folderName;
    }
}
