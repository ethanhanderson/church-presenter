
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Media;

public sealed class MediaCueDisplayNameResolverTests
{
    [Fact]
    public void Resolve_prefers_explicit_display_name_when_present()
    {
        var project = new PresentationProject
        {
            Manifest = new PresentationManifest
            {
                Media =
                [
                    new MediaEntry
                    {
                        Id = "media-1",
                        FileName = "Walk In.mp4",
                        Path = "Media/Files/walkin.mp4",
                    },
                ],
            },
        };

        var cue = new SlideMediaCue
        {
            Id = "cue-1",
            MediaId = "media-1",
            DisplayName = " Prelude ",
        };

        MediaCueDisplayNameResolver.Resolve(cue, project).Should().Be("Prelude");
    }

    [Fact]
    public void Resolve_uses_project_manifest_media_name_when_display_name_is_missing()
    {
        var project = new PresentationProject
        {
            Manifest = new PresentationManifest
            {
                Media =
                [
                    new MediaEntry
                    {
                        Id = "media-1",
                        FileName = "Walk In.mp4",
                        Path = "Media/Files/walkin.mp4",
                    },
                ],
            },
        };

        var cue = new SlideMediaCue
        {
            Id = "cue-1",
            MediaId = "media-1",
        };

        MediaCueDisplayNameResolver.Resolve(cue, project).Should().Be("Walk In");
    }

    [Fact]
    public void Resolve_falls_back_to_media_path_stem_when_no_metadata_exists()
    {
        var media = new OutputLayerMedia
        {
            MediaId = @"C:\media\prelude-loop.mp4",
            MediaType = "video",
        };

        MediaCueDisplayNameResolver.Resolve(media).Should().Be("prelude-loop");
    }
}