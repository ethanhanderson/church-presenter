using ChurchPresenter.Core.Cpres;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.TestSupport;

internal static class TestPresentationBundles
{
    public static string WriteBundle(string directory, string fileName, string title, string? presentationId = null)
    {
        return WriteBundle(directory, fileName, title, presentationId, includeMedia: false);
    }

    public static string WriteBundle(string directory, string fileName, string title, string? presentationId, bool includeMedia)
    {
        Directory.CreateDirectory(directory);
        var id = presentationId ?? Guid.NewGuid().ToString();
        var path = Path.Combine(directory, fileName);
        var now = DateTime.UtcNow.ToString("O");
        var mediaSourcePath = Path.Combine(directory, "background-source.mp4");
        if (includeMedia)
            File.WriteAllText(mediaSourcePath, "background");
        var mediaJson = includeMedia
            ? """[{"id":"media-1","filename":"background.mp4","path":"media/background.mp4","mime":"video/mp4","byteSize":10,"type":"video"}]"""
            : "[]";

        var state = new BundleSaveState
        {
            ManifestJson =
                $$"""
                {"formatVersion":"1.0.0","presentationId":"{{id}}","title":"{{title}}","createdAt":"{{now}}","updatedAt":"{{now}}","media":{{mediaJson}},"fonts":[]}
                """,
            SlidesJson = """[{"id":"slide-1","type":"blank","layers":[{"id":"layer-1","type":"text","content":"Hello"}]}]""",
            ArrangementJson = """{"order":["slide-1"],"sections":[]}""",
            Themes = Array.Empty<ThemeFileEntry>(),
            Media = includeMedia
                ? [new MediaFileRef { Id = "media-1", SourcePath = mediaSourcePath, BundlePath = "media/background.mp4" }]
                : Array.Empty<MediaFileRef>(),
            Fonts = Array.Empty<FontFileRef>(),
        };

        var cpres = new CpresDocumentService(NullLogger<CpresDocumentService>.Instance);
        cpres.Save(path, state);
        return path;
    }
}