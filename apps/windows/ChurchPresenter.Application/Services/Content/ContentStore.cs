using System.Text.Json;


using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Implements resilient file-system access for canonical content-root persistence.
/// </summary>
public sealed class ContentStore(ILogger<ContentStore> logger) : IContentStore
{
    private readonly ILogger<ContentStore> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.Exists(path);
    }

    /// <inheritdoc />
    public void EnsureDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
    }

    /// <inheritdoc />
    public async Task<T?> ReadJsonAsync<T>(string path, JsonSerializerOptions options, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read JSON content from {Path}.", path);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteJsonAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ContentAccessResult<ContentResourceStamp> GetStamp(string path, bool includeHash = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                var failure = CreateFailure(ContentAccessFailureKind.Missing, fullPath, "Resource does not exist.", null, isRetryable: false);
                return ContentAccessResult<ContentResourceStamp>.Failed(
                    failure,
                    new ContentResourceStamp { Path = fullPath, Exists = false, Failure = failure });
            }

            var info = new FileInfo(fullPath);
            var stamp = new ContentResourceStamp
            {
                Path = fullPath,
                Exists = true,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                Length = info.Length,
                Sha256 = includeHash ? ComputeSha256(fullPath) : null,
            };

            return ContentAccessResult<ContentResourceStamp>.Success(stamp, stamp);
        }
        catch (Exception ex) when (IsContentAccessException(ex))
        {
            var failure = ClassifyFailure(path, ex);
            return ContentAccessResult<ContentResourceStamp>.Failed(
                failure,
                new ContentResourceStamp { Path = path, Exists = false, Failure = failure });
        }
    }

    /// <inheritdoc />
    public async Task<ContentAccessResult<T>> TryReadJsonAsync<T>(string path, JsonSerializerOptions options, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        var stampResult = GetStamp(path);
        if (!stampResult.Succeeded)
            return ContentAccessResult<T>.Failed(stampResult.Failure!, stampResult.Stamp);

        try
        {
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken).ConfigureAwait(false);
            return value is null
                ? ContentAccessResult<T>.Failed(
                    CreateFailure(ContentAccessFailureKind.Corrupt, path, "JSON content was empty or invalid.", null, isRetryable: false),
                    stampResult.Stamp)
                : ContentAccessResult<T>.Success(value, stampResult.Stamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            var failure = ex is JsonException
                ? CreateFailure(ContentAccessFailureKind.Corrupt, path, ex.Message, ex, isRetryable: false)
                : ClassifyFailure(path, ex);
            _logger.LogWarning(ex, "Could not read JSON content from {Path}.", path);
            return ContentAccessResult<T>.Failed(failure, stampResult.Stamp);
        }
    }

    /// <inheritdoc />
    public async Task<ContentAccessResult<ContentResourceStamp>> TryWriteJsonAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        var tempPath = string.Empty;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            tempPath = Path.Combine(directory ?? Path.GetTempPath(), $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken).ConfigureAwait(false);
            }

            MoveTempFileIntoPlace(tempPath, fullPath);
            tempPath = string.Empty;
            return GetStamp(fullPath);
        }
        catch (Exception ex) when (IsContentAccessException(ex) || ex is JsonException)
        {
            var failure = ex is JsonException
                ? CreateFailure(ContentAccessFailureKind.Corrupt, path, ex.Message, ex, isRetryable: false)
                : ClassifyFailure(path, ex);
            _logger.LogWarning(ex, "Could not write JSON content to {Path}.", path);
            return ContentAccessResult<ContentResourceStamp>.Failed(failure);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Could not delete temporary content file {Path}.", tempPath);
                }
            }
        }
    }

    /// <inheritdoc />
    public ContentAccessResult<ContentResourceStamp> TryCopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        try
        {
            var fullDestination = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(fullDestination);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.Copy(sourcePath, fullDestination, overwrite);
            return GetStamp(fullDestination);
        }
        catch (Exception ex) when (IsContentAccessException(ex))
        {
            var failure = ClassifyFailure(sourcePath, ex);
            _logger.LogWarning(ex, "Could not copy file {SourcePath} to {DestinationPath}.", sourcePath, destinationPath);
            return ContentAccessResult<ContentResourceStamp>.Failed(failure);
        }
    }

    /// <inheritdoc />
    public ContentAccessResult<ContentResourceStamp> TryMoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        try
        {
            var fullDestination = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(fullDestination);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.Move(sourcePath, fullDestination, overwrite);
            return GetStamp(fullDestination);
        }
        catch (Exception ex) when (IsContentAccessException(ex))
        {
            var failure = ClassifyFailure(sourcePath, ex);
            _logger.LogWarning(ex, "Could not move file {SourcePath} to {DestinationPath}.", sourcePath, destinationPath);
            return ContentAccessResult<ContentResourceStamp>.Failed(failure);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        if (!Directory.Exists(path))
            return [];

        try
        {
            return Directory.EnumerateFiles(path, searchPattern, searchOption).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not enumerate files under {Path}.", path);
            return [];
        }
    }

    /// <inheritdoc />
    public ContentAccessResult<IReadOnlyList<string>> TryEnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        if (!Directory.Exists(path))
            return ContentAccessResult<IReadOnlyList<string>>.Failed(
                CreateFailure(ContentAccessFailureKind.Missing, path, "Directory does not exist.", null, isRetryable: false));

        try
        {
            return ContentAccessResult<IReadOnlyList<string>>.Success(
                Directory.EnumerateFiles(path, searchPattern, searchOption).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var failure = ClassifyFailure(path, ex);
            _logger.LogWarning(ex, "Could not enumerate files under {Path}.", path);
            return ContentAccessResult<IReadOnlyList<string>>.Failed(failure);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateDirectories(string path, SearchOption searchOption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Directory.Exists(path))
            return [];

        try
        {
            return Directory.EnumerateDirectories(path, "*", searchOption).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not enumerate directories under {Path}.", path);
            return [];
        }
    }

    /// <inheritdoc />
    public ContentAccessResult<IReadOnlyList<string>> TryEnumerateDirectories(string path, SearchOption searchOption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Directory.Exists(path))
            return ContentAccessResult<IReadOnlyList<string>>.Failed(
                CreateFailure(ContentAccessFailureKind.Missing, path, "Directory does not exist.", null, isRetryable: false));

        try
        {
            return ContentAccessResult<IReadOnlyList<string>>.Success(
                Directory.EnumerateDirectories(path, "*", searchOption).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var failure = ClassifyFailure(path, ex);
            _logger.LogWarning(ex, "Could not enumerate directories under {Path}.", path);
            return ContentAccessResult<IReadOnlyList<string>>.Failed(failure);
        }
    }

    /// <inheritdoc />
    public bool TryDeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            return false;

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete file {Path}.", path);
            return false;
        }
    }

    /// <inheritdoc />
    public ContentAccessResult<bool> TryDeleteFileDetailed(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            return ContentAccessResult<bool>.Failed(
                CreateFailure(ContentAccessFailureKind.Missing, path, "File does not exist.", null, isRetryable: false));

        try
        {
            File.Delete(path);
            return ContentAccessResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var failure = ClassifyFailure(path, ex);
            _logger.LogDebug(ex, "Could not delete file {Path}.", path);
            return ContentAccessResult<bool>.Failed(failure);
        }
    }

    /// <inheritdoc />
    public bool TryDeleteDirectory(string path, bool recursive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Directory.Exists(path))
            return false;

        try
        {
            Directory.Delete(path, recursive);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete directory {Path}.", path);
            return false;
        }
    }

    /// <inheritdoc />
    public ContentAccessResult<bool> TryDeleteDirectoryDetailed(string path, bool recursive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Directory.Exists(path))
            return ContentAccessResult<bool>.Failed(
                CreateFailure(ContentAccessFailureKind.Missing, path, "Directory does not exist.", null, isRetryable: false));

        try
        {
            Directory.Delete(path, recursive);
            return ContentAccessResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var failure = ClassifyFailure(path, ex);
            _logger.LogDebug(ex, "Could not delete directory {Path}.", path);
            return ContentAccessResult<bool>.Failed(failure);
        }
    }

    private static void MoveTempFileIntoPlace(string tempPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(tempPath, destinationPath);
    }

    private static bool IsContentAccessException(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or FileNotFoundException
            or PathTooLongException
            or NotSupportedException;

    private static ContentAccessFailure ClassifyFailure(string path, Exception ex)
    {
        if (ex is FileNotFoundException or DirectoryNotFoundException)
            return CreateFailure(ContentAccessFailureKind.Missing, path, ex.Message, ex, isRetryable: false);
        if (ex is UnauthorizedAccessException)
            return CreateFailure(ContentAccessFailureKind.Unauthorized, path, ex.Message, ex, isRetryable: false);
        if (ex is IOException)
            return CreateFailure(ContentAccessFailureKind.Locked, path, ex.Message, ex, isRetryable: true);
        if (ex is NotSupportedException or PathTooLongException)
            return CreateFailure(ContentAccessFailureKind.Unavailable, path, ex.Message, ex, isRetryable: false);

        return CreateFailure(ContentAccessFailureKind.Unknown, path, ex.Message, ex, isRetryable: false);
    }

    private static ContentAccessFailure CreateFailure(
        ContentAccessFailureKind kind,
        string? path,
        string message,
        Exception? exception,
        bool isRetryable) =>
        new()
        {
            Kind = kind,
            Path = path,
            Message = message,
            ExceptionType = exception?.GetType().Name,
            IsRetryable = isRetryable,
        };

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal static class ContentStoreDefaults
{
    internal static IContentStore Instance { get; } = new ContentStore(NullLogger<ContentStore>.Instance);
}