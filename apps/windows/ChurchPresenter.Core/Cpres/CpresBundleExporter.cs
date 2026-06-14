using System.Text;
using System.Text.Json;

namespace ChurchPresenter.Core.Cpres;

/// <summary>
/// Rewrites an on-disk <c>.cpres</c> bundle for portable export (document vs bundle mode).
/// </summary>
public static class CpresBundleExporter
{
    /// <summary>
    /// Writes a new bundle to <paramref name="destinationPath"/> using the requested payload flags.
    /// </summary>
    public static void Export(string sourcePath, string destinationPath, CpresBundleExportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(options);

        var parsed = CpresBundleReader.Open(sourcePath);
        var bundleMode = options.ResolveBundleMode();
        var manifestJson = WithBundleMode(parsed.ManifestJson, bundleMode);

        var mediaRefs = new List<MediaFileRef>();
        if (options.IncludeMedia)
        {
            foreach (var entry in ParseInventoryEntries(parsed.ManifestJson, "media"))
            {
                mediaRefs.Add(new MediaFileRef
                {
                    Id = entry.Id,
                    SourcePath = $"bundle:{entry.Path}",
                    BundlePath = entry.Path,
                });
            }
        }

        var fontRefs = new List<FontFileRef>();
        if (options.IncludeFonts)
        {
            foreach (var entry in ParseInventoryEntries(parsed.ManifestJson, "fonts"))
            {
                fontRefs.Add(new FontFileRef
                {
                    Id = entry.Id,
                    SourcePath = $"bundle:{entry.Path}",
                    BundlePath = entry.Path,
                });
            }
        }

        var themes = options.IncludeEmbeddedThemes
            ? parsed.Themes
            : (IReadOnlyList<ThemeFileEntry>)Array.Empty<ThemeFileEntry>();

        var saveState = new BundleSaveState
        {
            ManifestJson = manifestJson,
            SlidesJson = parsed.SlidesJson,
            ArrangementJson = parsed.ArrangementJson,
            Themes = themes,
            Media = mediaRefs,
            Fonts = fontRefs,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        CpresBundleWriter.Save(destinationPath, saveState);
    }

    private static string WithBundleMode(string manifestJson, string bundleMode)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var wroteBundleMode = false;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.NameEquals("bundleMode"))
                {
                    writer.WriteString("bundleMode", bundleMode);
                    wroteBundleMode = true;
                    continue;
                }

                property.WriteTo(writer);
            }

            if (!wroteBundleMode)
                writer.WriteString("bundleMode", bundleMode);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IEnumerable<InventoryEntry> ParseInventoryEntries(string manifestJson, string propertyName)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        if (!doc.RootElement.TryGetProperty(propertyName, out var entries) || entries.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                continue;

            if (!entry.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
                continue;

            var path = pathElement.GetString();
            if (string.IsNullOrWhiteSpace(path))
                continue;

            yield return new InventoryEntry(idElement.GetString()!, path.Replace('\\', '/'));
        }
    }

    private readonly record struct InventoryEntry(string Id, string Path);
}
