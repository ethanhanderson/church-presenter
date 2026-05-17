
using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Media;

public sealed class MediaInferenceTests
{
    [Fact]
    public void ResolveEffectiveMediaType_prefers_extension_when_catalogued_as_image_but_file_is_video()
    {
        MediaInference.ResolveEffectiveMediaType("image", @"C:\Media\clip.mp4").Should().Be("video");
        MediaInference.ResolveEffectiveMediaType("image", "Media/Files/abc123.mp4").Should().Be("video");
    }

    [Fact]
    public void ResolveEffectiveMediaType_keeps_catalogued_video_when_extension_is_ambiguous()
    {
        MediaInference.ResolveEffectiveMediaType("video", "Media/Files/unknown.bin").Should().Be("video");
    }

    [Fact]
    public void InferMediaTypeFromPath_detects_audio_extensions()
    {
        MediaInference.InferMediaTypeFromPath(@"D:\sounds\track.m4a").Should().Be("audio");
    }
}