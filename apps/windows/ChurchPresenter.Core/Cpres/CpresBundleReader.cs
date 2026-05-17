using System.IO.Compression;
using System.Text;
using System.Text.Json;

using ChurchPresenter.Core.Resources;

namespace ChurchPresenter.Core.Cpres;

/// <summary>
/// Reads <c>.cpres</c> bundles (ZIP archives with JSON and theme files).
/// </summary>
public static class CpresBundleReader
{
    /// <summary>
    /// Opens a bundle from disk and parses required JSON entries and theme files.
    /// </summary>
    /// <param name="path">Absolute or relative path to the <c>.cpres</c> file.</param>
    /// <returns>Parsed manifest, slides, arrangement, and theme file contents.</returns>
    /// <exception cref="CpresException">Thrown when the path is invalid or the archive is missing required entries.</exception>
    public static ParsedBundle Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new CpresException(ErrorMessageResources.PathRequired);

        using var fs = File.OpenRead(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

        var manifest = ReadEntry(zip, "manifest.json");
        var slides = ReadEntry(zip, "slides.json");
        var arrangement = ReadEntry(zip, "arrangement.json");

        ValidateManifest(manifest);

        var themes = new List<ThemeFileEntry>();
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith("themes/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var s = entry.Open();
                using var reader = new StreamReader(s, Encoding.UTF8);
                var content = reader.ReadToEnd();
                themes.Add(new ThemeFileEntry { FileName = name, Content = content });
            }
        }

        return new ParsedBundle
        {
            ManifestJson = manifest,
            SlidesJson = slides,
            ArrangementJson = arrangement,
            Themes = themes,
        };
    }

    /// <summary>
    /// Reads a binary entry from an existing bundle without loading the full parse model.
    /// </summary>
    /// <param name="bundlePath">Path to the <c>.cpres</c> file.</param>
    /// <param name="mediaPath">Path inside the archive (forward slashes).</param>
    /// <returns>Raw bytes of the entry.</returns>
    /// <exception cref="CpresException">Thrown when the entry does not exist.</exception>
    public static byte[] ReadMediaFromBundle(string bundlePath, string mediaPath)
    {
        using var fs = File.OpenRead(bundlePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        var entry = zip.GetEntry(NormalizeZipPath(mediaPath))
            ?? zip.GetEntry(mediaPath.Replace('/', '\\'));
        if (entry == null)
            throw new CpresException(ErrorMessageResources.MissingBundleFile(mediaPath));

        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string NormalizeZipPath(string path) => path.Replace('\\', '/');

    private static string ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? zip.GetEntry(name.Replace('/', '\\'));
        if (entry == null)
            throw new CpresException(ErrorMessageResources.MissingBundleFile(name));

        using var s = entry.Open();
        using var reader = new StreamReader(s, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void ValidateManifest(string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("formatVersion", out _) || !root.TryGetProperty("presentationId", out _))
            throw new CpresException(ErrorMessageResources.InvalidManifest);
    }
}