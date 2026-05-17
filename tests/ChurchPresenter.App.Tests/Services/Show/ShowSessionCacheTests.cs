using System.Text.Json;


using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace ChurchPresenter.App.Tests.Services.Show;

/// <summary>
/// Regression tests for <see cref="ShowSessionCache"/>:
/// caching, path normalisation, prefetch scheduling, and invalidation.
/// </summary>
public sealed class ShowSessionCacheTests
{
    private static PresentationDocument MakeDoc(string path) => new()
    {
        SourcePath = path,
        Manifest = new PresentationManifestDto { Title = System.IO.Path.GetFileNameWithoutExtension(path) },
        Slides = new List<SlideDto>
        {
            new() { Id = "s1", Type = "blank", Layers = JsonSerializer.SerializeToElement(Array.Empty<object>()) },
        },
    };

    private static (ShowSessionCache cache, Mock<IPresentationDocumentService> docsMock)
        CreateCache(params (string raw, string resolved)[] paths)
    {
        var (cache, docsMock, _) = CreateCacheWithAvailability(paths);
        return (cache, docsMock);
    }

    private static (ShowSessionCache cache, Mock<IPresentationDocumentService> docsMock, HashSet<string> existingPaths)
        CreateCacheWithAvailability(params (string raw, string resolved)[] paths)
    {
        var docsMock = new Mock<IPresentationDocumentService>();
        var contentMock = new Mock<IContentDirectoryService>();
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (raw, resolved) in paths)
        {
            contentMock.Setup(c => c.ResolvePresentationPath(raw)).Returns(resolved);
            contentMock.Setup(c => c.ResolvePresentationPath(resolved)).Returns(resolved);
            docsMock.Setup(d => d.Open(resolved)).Returns(MakeDoc(resolved));
            existingPaths.Add(resolved);
        }

        var cache = new ShowSessionCache(
            docsMock.Object,
            contentMock.Object,
            NullLogger<ShowSessionCache>.Instance,
            existingPaths.Contains);

        return (cache, docsMock, existingPaths);
    }

    // ── GetOrLoad ────────────────────────────────────────────────────────────

    [Fact]
    public void GetOrLoad_loads_and_caches_document()
    {
        var (cache, docsMock) = CreateCache((@"relative\a.cpres", @"C:\abs\a.cpres"));

        var doc1 = cache.GetOrLoad(@"relative\a.cpres");
        var doc2 = cache.GetOrLoad(@"relative\a.cpres");

        doc1.Should().NotBeNull();
        doc1.Should().BeSameAs(doc2, "second call should return cached instance");
        docsMock.Verify(d => d.Open(@"C:\abs\a.cpres"), Times.Once, "disk read should happen only once");
    }

    [Fact]
    public void TryGet_returns_null_before_load()
    {
        var (cache, _) = CreateCache((@"a.cpres", @"C:\a.cpres"));

        cache.TryGet(@"a.cpres").Should().BeNull("cache is empty before first load");
    }

    [Fact]
    public void TryGet_returns_doc_after_GetOrLoad()
    {
        var (cache, _) = CreateCache((@"a.cpres", @"C:\a.cpres"));
        cache.GetOrLoad(@"a.cpres");

        cache.TryGet(@"a.cpres").Should().NotBeNull();
    }

    // ── UpdateEntry ──────────────────────────────────────────────────────────

    [Fact]
    public void UpdateEntry_replaces_cached_document()
    {
        var (cache, _) = CreateCache((@"a.cpres", @"C:\a.cpres"));
        cache.GetOrLoad(@"a.cpres");

        var updated = MakeDoc(@"C:\a.cpres");
        cache.UpdateEntry(@"C:\a.cpres", updated);

        cache.TryGet(@"C:\a.cpres").Should().BeSameAs(updated);
    }

    // ── Invalidate ───────────────────────────────────────────────────────────

    [Fact]
    public void Invalidate_removes_cached_entry()
    {
        var (cache, docsMock) = CreateCache((@"a.cpres", @"C:\a.cpres"));
        cache.GetOrLoad(@"a.cpres");
        cache.Invalidate(@"C:\a.cpres");

        cache.TryGet(@"C:\a.cpres").Should().BeNull("entry should be removed after invalidation");

        // Re-loading should go to disk again.
        cache.GetOrLoad(@"C:\a.cpres");
        docsMock.Verify(d => d.Open(@"C:\a.cpres"), Times.Exactly(2));
    }

    [Fact]
    public void TryGet_prunes_cached_entry_when_bundle_file_was_deleted()
    {
        var (cache, docsMock, existingPaths) = CreateCacheWithAvailability((@"a.cpres", @"C:\a.cpres"));
        cache.GetOrLoad(@"a.cpres");
        existingPaths.Remove(@"C:\a.cpres");

        cache.TryGet(@"a.cpres").Should().BeNull("deleted bundles should not be served from memory");
        cache.GetOrLoad(@"a.cpres").Should().BeNull("deleted bundles should not be re-opened");
        docsMock.Verify(d => d.Open(@"C:\a.cpres"), Times.Once);
    }

    [Fact]
    public void TryGet_prunes_cached_entry_when_bundle_file_was_replaced_at_same_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var resolvedPath = Path.Combine(root, "a.cpres");
        File.WriteAllText(resolvedPath, "first");

        var docsMock = new Mock<IPresentationDocumentService>();
        var original = MakeDoc(resolvedPath);
        original.Manifest.Title = "Original";
        var replacement = MakeDoc(resolvedPath);
        replacement.Manifest.Title = "Replacement";
        docsMock.SetupSequence(d => d.Open(resolvedPath))
            .Returns(original)
            .Returns(replacement);

        var contentMock = new Mock<IContentDirectoryService>();
        contentMock.Setup(c => c.ResolvePresentationPath("a.cpres")).Returns(resolvedPath);
        contentMock.Setup(c => c.ResolvePresentationPath(resolvedPath)).Returns(resolvedPath);

        var cache = new ShowSessionCache(
            docsMock.Object,
            contentMock.Object,
            NullLogger<ShowSessionCache>.Instance,
            contentStore: new ContentStore(NullLogger<ContentStore>.Instance));

        cache.GetOrLoad("a.cpres").Should().BeSameAs(original);
        File.WriteAllText(resolvedPath, "replacement-content");

        cache.TryGet("a.cpres").Should().BeNull("same-path replacements should invalidate cached documents");
        cache.GetOrLoad("a.cpres").Should().BeSameAs(replacement);
        docsMock.Verify(d => d.Open(resolvedPath), Times.Exactly(2));
    }

    // ── ClearSession ─────────────────────────────────────────────────────────

    [Fact]
    public void ClearSession_empties_cache_and_session_paths()
    {
        var (cache, _) = CreateCache((@"a.cpres", @"C:\a.cpres"));
        cache.GetOrLoad(@"a.cpres");

        cache.ClearSession();

        cache.SessionPaths.Should().BeEmpty();
        cache.TryGet(@"C:\a.cpres").Should().BeNull();
    }

    [Fact]
    public async Task PruneMissingFiles_removes_deleted_entries_from_cache_and_session_order()
    {
        var (cache, _, existingPaths) = CreateCacheWithAvailability(
            (@"a.cpres", @"C:\a.cpres"),
            (@"b.cpres", @"C:\b.cpres"));

        await cache.LoadSessionAsync(new[]
        {
            new PresentationRefDto { Path = @"a.cpres" },
            new PresentationRefDto { Path = @"b.cpres" },
        });
        existingPaths.Remove(@"C:\b.cpres");

        var pruned = cache.PruneMissingFiles();

        pruned.Should().ContainSingle().Which.Should().Be(@"C:\b.cpres");
        cache.SessionPaths.Should().Equal(@"C:\a.cpres");
        cache.TryGet(@"C:\b.cpres").Should().BeNull();
    }

    // ── SetSessionOrder ──────────────────────────────────────────────────────

    [Fact]
    public void SetSessionOrder_sets_resolved_paths_without_loading_documents()
    {
        var (cache, docsMock) = CreateCache(
            (@"a.cpres", @"C:\a.cpres"),
            (@"b.cpres", @"C:\b.cpres"));

        cache.SetSessionOrder(new[]
        {
            new PresentationRefDto { Path = @"b.cpres" },
            new PresentationRefDto { Path = @"a.cpres" },
        });

        cache.SessionPaths.Should().Equal(@"C:\b.cpres", @"C:\a.cpres");
        docsMock.Verify(d => d.Open(It.IsAny<string>()), Times.Never);
    }

    // ── LoadSessionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task LoadSessionAsync_loads_first_item_eagerly()
    {
        var (cache, docsMock) = CreateCache(
            (@"a.cpres", @"C:\a.cpres"),
            (@"b.cpres", @"C:\b.cpres"));

        var items = new List<PresentationRefDto>
        {
            new() { Path = @"a.cpres", Title = "A" },
            new() { Path = @"b.cpres", Title = "B" },
        };

        await cache.LoadSessionAsync(items);

        cache.TryGet(@"C:\a.cpres").Should().NotBeNull("first item loaded eagerly");
        cache.SessionPaths.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadSessionAsync_loads_all_items_in_background()
    {
        var (cache, docsMock) = CreateCache(
            (@"a.cpres", @"C:\a.cpres"),
            (@"b.cpres", @"C:\b.cpres"),
            (@"c.cpres", @"C:\c.cpres"));

        var items = new List<PresentationRefDto>
        {
            new() { Path = @"a.cpres", Title = "A" },
            new() { Path = @"b.cpres", Title = "B" },
            new() { Path = @"c.cpres", Title = "C" },
        };

        await cache.LoadSessionAsync(items);
        await Task.Delay(50); // Allow background loads to complete.

        cache.TryGet(@"C:\b.cpres").Should().NotBeNull("remaining items loaded in background");
        cache.TryGet(@"C:\c.cpres").Should().NotBeNull();
    }

    [Fact]
    public async Task LoadSessionAsync_preserves_previously_loaded_docs_across_sessions()
    {
        // Once loaded, a presentation stays in the cache regardless of which session is active.
        var (cache, _) = CreateCache(
            (@"a.cpres", @"C:\a.cpres"),
            (@"b.cpres", @"C:\b.cpres"));

        await cache.LoadSessionAsync(new[] { new PresentationRefDto { Path = @"a.cpres" } });
        cache.TryGet(@"C:\a.cpres").Should().NotBeNull();

        await cache.LoadSessionAsync(new[] { new PresentationRefDto { Path = @"b.cpres" } });

        // a.cpres remains cached — it was previously loaded and is never evicted.
        cache.TryGet(@"C:\a.cpres").Should().NotBeNull("loaded docs are retained across sessions");
        cache.TryGet(@"C:\b.cpres").Should().NotBeNull("new session item should be loaded");
        // SessionPaths reflects only the active session's items.
        cache.SessionPaths.Should().HaveCount(1);
        cache.SessionPaths.Should().Contain(@"C:\b.cpres");
    }

    // ── SchedulePrefetch ─────────────────────────────────────────────────────

    [Fact]
    public async Task SchedulePrefetch_loads_next_items_in_background()
    {
        var (cache, docsMock) = CreateCache(
            (@"a.cpres", @"C:\a.cpres"),
            (@"b.cpres", @"C:\b.cpres"),
            (@"c.cpres", @"C:\c.cpres"));

        // Set up session order manually via LoadSessionAsync.
        await cache.LoadSessionAsync(new[]
        {
            new PresentationRefDto { Path = @"a.cpres" },
            new PresentationRefDto { Path = @"b.cpres" },
            new PresentationRefDto { Path = @"c.cpres" },
        });
        await Task.Delay(50);

        // All items should be cached now (background load).
        cache.TryGet(@"C:\b.cpres").Should().NotBeNull();
        cache.TryGet(@"C:\c.cpres").Should().NotBeNull();

        // Verify that SchedulePrefetch doesn't reload already-warm items.
        docsMock.Invocations.Clear();
        cache.SchedulePrefetch(@"C:\a.cpres", lookAhead: 2);
        await Task.Delay(50);

        docsMock.Verify(d => d.Open(It.IsAny<string>()), Times.Never,
            "prefetch should skip already-cached items");
    }
}