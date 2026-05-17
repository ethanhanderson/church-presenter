using System.Text.Json;


using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class ContentStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public void GetStamp_returns_missing_failure_for_absent_file()
    {
        var store = new ContentStore(NullLogger<ContentStore>.Instance);
        var path = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"), "missing.json");

        var result = store.GetStamp(path);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Kind.Should().Be(ContentAccessFailureKind.Missing);
        result.Stamp.Should().NotBeNull();
        result.Stamp!.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task TryReadJsonAsync_classifies_corrupt_json()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "bad.json");
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new ContentStore(NullLogger<ContentStore>.Instance);

        var result = await store.TryReadJsonAsync<TestRecord>(path, JsonOptions);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Kind.Should().Be(ContentAccessFailureKind.Corrupt);
        result.Stamp.Should().NotBeNull();
        result.Stamp!.Exists.Should().BeTrue();
    }

    [Fact]
    public async Task TryWriteJsonAsync_writes_atomically_and_returns_stamp()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "state.json");
        var store = new ContentStore(NullLogger<ContentStore>.Instance);

        var result = await store.TryWriteJsonAsync(path, new TestRecord { Name = "Alpha" }, JsonOptions);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Exists.Should().BeTrue();
        File.Exists(path).Should().BeTrue();

        var loaded = await store.TryReadJsonAsync<TestRecord>(path, JsonOptions);
        loaded.Succeeded.Should().BeTrue();
        loaded.Value!.Name.Should().Be("Alpha");
    }

    [Fact]
    public void TryCopyFile_returns_destination_stamp()
    {
        var root = CreateTempRoot();
        var source = Path.Combine(root, "source.txt");
        var destination = Path.Combine(root, "nested", "destination.txt");
        File.WriteAllText(source, "content");
        var store = new ContentStore(NullLogger<ContentStore>.Instance);

        var result = store.TryCopyFile(source, destination);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Path.Should().Be(Path.GetFullPath(destination));
        result.Value.Length.Should().Be(7);
    }

    [Fact]
    public void TryEnumerateFiles_reports_missing_directory()
    {
        var store = new ContentStore(NullLogger<ContentStore>.Instance);
        var path = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));

        var result = store.TryEnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Kind.Should().Be(ContentAccessFailureKind.Missing);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class TestRecord
    {
        public string Name { get; set; } = string.Empty;
    }
}
