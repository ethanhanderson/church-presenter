using ChurchPresenter.App.Tests.Reference;
using ChurchPresenter.Backend.Pipeline.Cues;
using ChurchPresenter.Backend.Rendering;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Pipeline;

[ReferenceArea(ReferenceAreas.CacheAndRecovery)]
public sealed class PreparedCueCacheTests
{
    [Fact]
    public void Set_and_TryGet_returns_hot_cache_hit()
    {
        PreparedCueCache cache = new();
        CueRequest request = CreateRequest("slide-1");
        PreparedCueRecord record = CreateRecord("slide-1");

        cache.Set(request, record);

        bool hit = cache.TryGet(request, out PreparedCueRecord cached, out CacheHitTier tier);
        hit.Should().BeTrue();
        cached.Should().Be(record);
        tier.Should().Be(CacheHitTier.Hot);
    }

    [Fact]
    public void Invalidate_removes_matching_content()
    {
        PreparedCueCache cache = new();
        CueRequest request = CreateRequest("slide-1");
        PreparedCueRecord record = CreateRecord("slide-1");
        cache.Set(request, record);

        cache.Invalidate(new ContentRef { Kind = ContentRefKind.Slide, Id = "slide-1" });

        cache.TryGet(request, out _, out CacheHitTier tier).Should().BeFalse();
        tier.Should().Be(CacheHitTier.Miss);
    }

    [Fact]
    public void Evicts_least_recently_used_entries_when_byte_budget_exceeded()
    {
        PreparedCueCache cache = new(maxHotBytes: 100);
        CueRequest first = CreateRequest("first");
        CueRequest second = CreateRequest("second");
        cache.Set(first, CreateRecord("first", sizeCostBytes: 60));
        cache.Set(second, CreateRecord("second", sizeCostBytes: 60));

        cache.TryGet(first, out _, out _).Should().BeFalse();
        cache.TryGet(second, out PreparedCueRecord cached, out _).Should().BeTrue();
        cached.ContentRef.Id.Should().Be("second");
    }

    private static CueRequest CreateRequest(string id) =>
        new()
        {
            CueKind = CueKind.Slide,
            Layer = OutputLayerKind.Slide,
            ContentRef = new ContentRef { Kind = ContentRefKind.Slide, Id = id },
        };

    private static PreparedCueRecord CreateRecord(string id, int sizeCostBytes = 1) =>
        new()
        {
            Layer = OutputLayerKind.Slide,
            ContentRef = new ContentRef { Kind = ContentRefKind.Slide, Id = id },
            Payload = new RenderPayloadDescriptor { Id = id, Kind = RenderPayloadKind.Presentation },
            SizeCostBytes = sizeCostBytes,
        };
}
