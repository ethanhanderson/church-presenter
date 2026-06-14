using System.Text.RegularExpressions;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Sanitizes display names into portable file and folder names. Stable ids live in catalog/registry JSON, not on disk.
/// </summary>
public static class ContentNaming
{
    private static readonly Regex WhitespaceRuns = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes a human-readable name for use as a file or folder name segment.
    /// </summary>
    public static string SanitizeFileName(string? value, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Untitled";

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
                continue;

            buffer[length++] = character;
        }

        var sanitized = WhitespaceRuns.Replace(buffer[..length].ToString().Trim(), " ").Trim();
        if (sanitized.Length == 0)
            sanitized = "Untitled";

        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength].TrimEnd();

        return sanitized.Length == 0 ? "Untitled" : sanitized;
    }

    /// <summary>
    /// Returns <paramref name="desiredPath"/> or the same base name with <c>_2</c>, <c>_3</c>, … before the extension when taken.
    /// </summary>
    public static string EnsureUniqueFilePath(string desiredPath, string? excludePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredPath);

        var fullDesiredPath = Path.GetFullPath(desiredPath);
        var fullExcludePath = string.IsNullOrWhiteSpace(excludePath) ? null : Path.GetFullPath(excludePath);
        if (!File.Exists(fullDesiredPath)
            || (fullExcludePath != null && string.Equals(fullDesiredPath, fullExcludePath, StringComparison.OrdinalIgnoreCase)))
        {
            return fullDesiredPath;
        }

        var directory = Path.GetDirectoryName(fullDesiredPath)!;
        var baseName = Path.GetFileNameWithoutExtension(fullDesiredPath);
        var extension = Path.GetExtension(fullDesiredPath);

        for (var counter = 2; ; counter++)
        {
            var candidate = Path.Combine(directory, $"{baseName}_{counter}{extension}");
            if (!File.Exists(candidate)
                || (fullExcludePath != null && string.Equals(candidate, fullExcludePath, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Returns <paramref name="desiredDirectory"/> or the same name with <c>_2</c>, <c>_3</c>, … when taken.
    /// </summary>
    public static string EnsureUniqueDirectoryPath(string desiredDirectory, string? excludeDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredDirectory);

        var fullDesiredDirectory = Path.GetFullPath(desiredDirectory);
        var fullExcludeDirectory = string.IsNullOrWhiteSpace(excludeDirectory) ? null : Path.GetFullPath(excludeDirectory);
        if (!Directory.Exists(fullDesiredDirectory)
            || (fullExcludeDirectory != null && string.Equals(fullDesiredDirectory, fullExcludeDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            return fullDesiredDirectory;
        }

        var parent = Path.GetDirectoryName(fullDesiredDirectory)!;
        var baseName = Path.GetFileName(fullDesiredDirectory);

        for (var counter = 2; ; counter++)
        {
            var candidate = Path.Combine(parent, $"{baseName}_{counter}");
            if (!Directory.Exists(candidate)
                || (fullExcludeDirectory != null && string.Equals(candidate, fullExcludeDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Picks a unique folder name under <paramref name="parentDirectory"/> from a display name.
    /// </summary>
    public static string AllocateDirectoryName(string parentDirectory, string displayName, string? currentDirectoryName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);

        var sanitized = SanitizeFileName(displayName);
        var desired = Path.Combine(parentDirectory, sanitized);
        if (!string.IsNullOrWhiteSpace(currentDirectoryName))
        {
            var currentFullPath = Path.Combine(parentDirectory, currentDirectoryName);
            if (string.Equals(sanitized, currentDirectoryName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(desired, currentFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return EnsureUniqueDirectoryPath(desired, currentFullPath);
            }
        }

        return EnsureUniqueDirectoryPath(desired, excludeDirectory: null);
    }
}
