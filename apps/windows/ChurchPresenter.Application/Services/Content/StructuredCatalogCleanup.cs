using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

internal static class StructuredCatalogCleanup
{
    /// <summary>
    /// Clears ReadOnly/Hidden/System attributes on a directory tree so it can be deleted.
    /// Called by registry services before they delete managed domain folders.
    /// </summary>
    public static void PrepareDirectoryForDeletion(string path, ILogger logger) =>
        PrepareDirectoryTreeForDeletion(path, logger);


    public static IReadOnlyList<string> EnumerateFilesSafe(
        string root,
        string searchPattern,
        SearchOption option,
        ILogger logger)
    {
        try
        {
            if (!Directory.Exists(root))
                return [];

            return Directory.EnumerateFiles(root, searchPattern, option).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            logger.LogWarning(ex, "Could not enumerate files under {Path}.", root);
            return [];
        }
    }

    public static IReadOnlyList<string> EnumerateDirectoriesSafe(
        string root,
        SearchOption option,
        ILogger logger)
    {
        try
        {
            if (!Directory.Exists(root))
                return [];

            return Directory.EnumerateDirectories(root, "*", option).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            logger.LogWarning(ex, "Could not enumerate directories under {Path}.", root);
            return [];
        }
    }

    public static void DeleteLegacyStructuredCatalogArtifacts(
        string librariesRoot,
        string playlistsRoot,
        string aggregatePlaylistsPath,
        ILogger logger,
        string libraryDirectoryLogMessage,
        string playlistFileLogMessage)
    {
        if (Directory.Exists(librariesRoot))
        {
            foreach (var existingDir in EnumerateDirectoriesSafe(librariesRoot, SearchOption.TopDirectoryOnly, logger)
                         .Where(IsStructuredLibraryDirectory))
            {
                TryDeleteDirectoryTree(existingDir, logger, libraryDirectoryLogMessage);
            }
        }

        if (!Directory.Exists(playlistsRoot))
            return;

        var aggregatePlaylistFileName = Path.GetFileName(aggregatePlaylistsPath);
        foreach (var existingFile in EnumerateFilesSafe(playlistsRoot, "*.json", SearchOption.TopDirectoryOnly, logger)
                     .Where(path =>
                     {
                         var name = Path.GetFileName(path);
                         if (string.Equals(name, aggregatePlaylistFileName, StringComparison.OrdinalIgnoreCase))
                             return false;
                         // Registry index lives at Playlists/Index.json alongside legacy per-playlist JSON files.
                         if (string.Equals(name, "Index.json", StringComparison.OrdinalIgnoreCase))
                             return false;
                         return true;
                     }))
        {
            TryDeleteFile(existingFile, logger, playlistFileLogMessage);
        }
    }

    /// <summary>
    /// Legacy structured layout used lowercase <c>library.json</c> per folder. The registry uses
    /// <c>Library.json</c>; on case-sensitive file systems those differ. On Windows, <see cref="File.Exists"/>
    /// with "library.json" matches "Library.json", which incorrectly treated migrated registry folders as legacy.
    /// </summary>
    private static bool IsStructuredLibraryDirectory(string path)
    {
        if (!Directory.Exists(path))
            return false;

        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            if (string.Equals(Path.GetFileName(file), "library.json", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void TryDeleteFile(string path, ILogger logger, string logMessage)
    {
        try
        {
            if (!File.Exists(path))
                return;

            ClearDeletionBlockingAttributes(path, logger);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, logMessage, path);
        }
    }

    private static void TryDeleteDirectoryTree(string path, ILogger logger, string logMessage)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            PrepareDirectoryTreeForDeletion(path, logger);
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, logMessage, path);
        }
    }

    private static void PrepareDirectoryTreeForDeletion(string path, ILogger logger)
    {
        foreach (var file in EnumerateFilesSafe(path, "*", SearchOption.AllDirectories, logger))
            ClearDeletionBlockingAttributes(file, logger);

        foreach (var directory in EnumerateDirectoriesSafe(path, SearchOption.AllDirectories, logger)
                     .OrderByDescending(static candidate => candidate.Length))
        {
            ClearDeletionBlockingAttributes(directory, logger);
        }

        ClearDeletionBlockingAttributes(path, logger);
    }

    private static void ClearDeletionBlockingAttributes(string path, ILogger logger)
    {
        try
        {
            var existingAttributes = File.GetAttributes(path);
            var normalizedAttributes = existingAttributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
            if (normalizedAttributes != existingAttributes)
                File.SetAttributes(path, normalizedAttributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not normalize attributes before deleting {Path}.", path);
        }
    }
}