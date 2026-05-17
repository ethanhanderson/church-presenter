using System.IO.Compression;
using System.Text;

using ChurchPresenter.Core.Resources;

namespace ChurchPresenter.Core.Cpres;

/// <summary>
/// Writes <c>.cpres</c> bundles using an atomic replace pattern (temp file then move).
/// </summary>
public static class CpresBundleWriter
{
    /// <summary>
    /// Atomically saves a bundle: writes a temporary zip in the target directory, then replaces the destination file.
    /// </summary>
    /// <param name="path">Destination path for the <c>.cpres</c> file.</param>
    /// <param name="state">JSON fragments and binary references to embed.</param>
    /// <exception cref="CpresException">Thrown when the path is invalid.</exception>
    public static void Save(string path, BundleSaveState state)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new CpresException(ErrorMessageResources.PathRequired);

        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent))
            throw new CpresException(ErrorMessageResources.InvalidPath);

        Directory.CreateDirectory(parent);
        var temp = Path.Combine(parent, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        var existingBundlePath = File.Exists(full) ? full : null;

        try
        {
            WriteZipToPath(temp, existingBundlePath, state);
            if (File.Exists(full))
                File.Delete(full);
            File.Move(temp, full);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }

    private static void WriteZipToPath(string tempPath, string? existingBundlePath, BundleSaveState state)
    {
        using var fs = File.Create(tempPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        WriteString(zip, "manifest.json", state.ManifestJson);
        WriteString(zip, "slides.json", state.SlidesJson);
        WriteString(zip, "arrangement.json", state.ArrangementJson);

        foreach (var theme in state.Themes)
            WriteString(zip, theme.FileName.Replace('\\', '/'), theme.Content);

        foreach (var m in state.Media)
        {
            var data = ResolveSourceBytes(existingBundlePath, m.SourcePath);
            WriteBytes(zip, m.BundlePath.Replace('\\', '/'), data);
        }

        foreach (var f in state.Fonts)
        {
            var data = ResolveSourceBytes(existingBundlePath, f.SourcePath);
            WriteBytes(zip, f.BundlePath.Replace('\\', '/'), data);
        }
    }

    private static byte[] ResolveSourceBytes(string? existingBundlePath, string sourcePath)
    {
        const string bundlePrefix = "bundle:";
        if (sourcePath.StartsWith(bundlePrefix, StringComparison.Ordinal))
        {
            var inner = sourcePath[bundlePrefix.Length..];
            if (string.IsNullOrEmpty(existingBundlePath) || !File.Exists(existingBundlePath))
                return Array.Empty<byte>();
            return CpresBundleReader.ReadMediaFromBundle(existingBundlePath, inner);
        }

        return File.ReadAllBytes(sourcePath);
    }

    private static void WriteString(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes);
    }

    private static void WriteBytes(ZipArchive zip, string entryName, byte[] content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(content);
    }
}