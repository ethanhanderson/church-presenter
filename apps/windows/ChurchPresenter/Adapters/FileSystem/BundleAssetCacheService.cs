using System.Security.Cryptography;
using System.Text;

using ChurchPresenter.Core.Cpres;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Adapters.FileSystem;

/// <summary>
/// Resolves bundled media and font assets into reusable local cache files for WinUI rendering.
/// </summary>
public interface IBundleAssetCacheService
{
    /// <summary>
    /// Resolves a media path from the project manifest and extracts bundled assets when needed.
    /// </summary>
    string? ResolveMediaPath(PresentationProject? project, string? mediaId);

    /// <summary>
    /// Resolves a font family for rendering text, extracting bundled fonts when needed.
    /// </summary>
    FontFamily ResolveFontFamily(PresentationProject? project, string? familyName);
}

/// <inheritdoc />
public sealed class BundleAssetCacheService(
    IContentDirectoryService contentDirectories,
    ILogger<BundleAssetCacheService> logger) : IBundleAssetCacheService
{
    private readonly IContentDirectoryService _contentDirectories = contentDirectories ?? throw new ArgumentNullException(nameof(contentDirectories));
    private readonly ILogger<BundleAssetCacheService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string? ResolveMediaPath(PresentationProject? project, string? mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            return null;

        // Fast path: if the mediaId is an absolute path to an existing file it references a
        // global-library item directly — no bundle lookup needed.
        if (Path.IsPathRooted(mediaId) && File.Exists(mediaId))
            return mediaId;

        // Content-root-relative paths (e.g. Media/Files/{id}.mp4 from the global media library).
        if (!Path.IsPathRooted(mediaId))
        {
            var candidate = Path.GetFullPath(Path.Combine(_contentDirectories.GetDocumentsDataDirectory(), mediaId.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(candidate))
                return candidate;
        }

        if (project == null)
            return null;

        var entry = project.Manifest.Media.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, mediaId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return null;

        return ResolveAssetPath(project.SourcePath, entry.SourcePath, entry.Path, entry.FileName);
    }

    /// <inheritdoc />
    public FontFamily ResolveFontFamily(PresentationProject? project, string? familyName)
    {
        if (project == null || string.IsNullOrWhiteSpace(familyName))
            return new FontFamily(string.IsNullOrWhiteSpace(familyName) ? "Segoe UI" : familyName);

        var bundledFont = project.Manifest.Fonts.FirstOrDefault(candidate =>
            string.Equals(candidate.Family, familyName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.FullName, familyName, StringComparison.OrdinalIgnoreCase));
        if (bundledFont == null)
            return new FontFamily(familyName);

        try
        {
            var localPath = ResolveAssetPath(project.SourcePath, bundledFont.SourcePath, bundledFont.Path, bundledFont.FullName);
            if (string.IsNullOrWhiteSpace(localPath))
                return new FontFamily(familyName);

            var fileUri = new Uri(localPath).AbsoluteUri;
            return new FontFamily($"{fileUri}#{bundledFont.Family}");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falling back to system font family {FamilyName}.", familyName);
            return new FontFamily(familyName);
        }
    }

    private string? ResolveAssetPath(string? bundlePath, string? sourcePath, string bundleRelativePath, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) &&
            !sourcePath.StartsWith("bundle:", StringComparison.OrdinalIgnoreCase))
        {
            return Path.IsPathRooted(sourcePath)
                ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(bundlePath ?? string.Empty) ?? string.Empty, sourcePath));
        }

        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath) || string.IsNullOrWhiteSpace(bundleRelativePath))
            return null;

        var normalizedEntryPath = bundleRelativePath.Replace('\\', '/');
        var extension = Path.GetExtension(displayName ?? normalizedEntryPath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = Path.GetExtension(normalizedEntryPath);

        var cachePath = GetCachePath(bundlePath, normalizedEntryPath, extension);
        if (File.Exists(cachePath))
            return cachePath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var bytes = CpresBundleReader.ReadMediaFromBundle(bundlePath, normalizedEntryPath);
            File.WriteAllBytes(cachePath, bytes);
            return cachePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extract bundle asset {AssetPath} from {BundlePath}.", normalizedEntryPath, bundlePath);
            return null;
        }
    }

    private string GetCachePath(string bundlePath, string entryPath, string? extension)
    {
        var cacheRoot = Path.Combine(_contentDirectories.GetAppDataDirectory(), "cache", "bundle-assets");
        var bundleStamp = GetBundleStamp(bundlePath);
        var hash = ComputeHash($"{bundlePath}|{bundleStamp}|{entryPath}");
        var cleanExtension = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension;
        return Path.Combine(cacheRoot, $"{hash}{cleanExtension}");
    }

    private static string GetBundleStamp(string bundlePath)
    {
        try
        {
            var info = new FileInfo(bundlePath);
            return info.Exists
                ? $"{info.LastWriteTimeUtc.Ticks}:{info.Length}"
                : "missing";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.GetType().Name;
        }
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}