using System.Text.Json;


namespace ChurchPresenter.Services.Content;

/// <summary>
/// Provides the shared file-system abstraction for canonical portable content storage.
/// </summary>
public interface IContentStore
{
    /// <summary>
    /// Returns <c>true</c> when the target file exists.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// Returns <c>true</c> when the target directory exists.
    /// </summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Ensures the supplied directory exists.
    /// </summary>
    void EnsureDirectory(string path);

    /// <summary>
    /// Reads a JSON file or returns <c>null</c> when the file is missing or invalid.
    /// </summary>
    Task<T?> ReadJsonAsync<T>(string path, JsonSerializerOptions options, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Writes a JSON file, creating parent directories when needed.
    /// </summary>
    Task WriteJsonAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a resource stamp for cache validation and preview revalidation.
    /// </summary>
    ContentAccessResult<ContentResourceStamp> GetStamp(string path, bool includeHash = false);

    /// <summary>
    /// Reads a JSON file and returns either the value plus resource stamp or a classified failure.
    /// </summary>
    Task<ContentAccessResult<T>> TryReadJsonAsync<T>(string path, JsonSerializerOptions options, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Writes a JSON file atomically when possible and returns the new resource stamp.
    /// </summary>
    Task<ContentAccessResult<ContentResourceStamp>> TryWriteJsonAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a file and returns the destination resource stamp.
    /// </summary>
    ContentAccessResult<ContentResourceStamp> TryCopyFile(string sourcePath, string destinationPath, bool overwrite = false);

    /// <summary>
    /// Moves a file and returns the destination resource stamp.
    /// </summary>
    ContentAccessResult<ContentResourceStamp> TryMoveFile(string sourcePath, string destinationPath, bool overwrite = false);

    /// <summary>
    /// Returns a defensive list of files under a directory.
    /// </summary>
    IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);

    /// <summary>
    /// Enumerates files with a classified failure when traversal cannot complete.
    /// </summary>
    ContentAccessResult<IReadOnlyList<string>> TryEnumerateFiles(string path, string searchPattern, SearchOption searchOption);

    /// <summary>
    /// Returns a defensive list of directories under a directory.
    /// </summary>
    IReadOnlyList<string> EnumerateDirectories(string path, SearchOption searchOption);

    /// <summary>
    /// Enumerates directories with a classified failure when traversal cannot complete.
    /// </summary>
    ContentAccessResult<IReadOnlyList<string>> TryEnumerateDirectories(string path, SearchOption searchOption);

    /// <summary>
    /// Deletes a file and returns <c>true</c> when the delete completed.
    /// </summary>
    bool TryDeleteFile(string path);

    /// <summary>
    /// Deletes a file and returns a classified operation result.
    /// </summary>
    ContentAccessResult<bool> TryDeleteFileDetailed(string path);

    /// <summary>
    /// Deletes a directory and returns <c>true</c> when the delete completed.
    /// </summary>
    bool TryDeleteDirectory(string path, bool recursive);

    /// <summary>
    /// Deletes a directory and returns a classified operation result.
    /// </summary>
    ContentAccessResult<bool> TryDeleteDirectoryDetailed(string path, bool recursive);
}