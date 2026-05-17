using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Media;

public sealed partial class MediaLibraryService
{
    // ── Items ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<MediaLibraryItem> AddItemAsync(string playlistId, string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var manifest = await GetPlaylistAsync(playlistId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Media playlist '{playlistId}' not found.");

        var item = await CreateItemFromImportedFileAsync(filePath, ct).ConfigureAwait(false);

        manifest.Items.Add(item);
        manifest.UpdatedAt = DateTime.UtcNow.ToString("o");
        await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
        PublishMediaChange(ContentChangeKind.MediaAssetAdded, item);
        return item;
    }

    /// <inheritdoc />
    public async Task<MediaLibraryItem> AddRootItemAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var item = await CreateItemFromImportedFileAsync(filePath, ct).ConfigureAwait(false);
        index.Items.Add(item);
        await WriteIndexAsync(index, ct).ConfigureAwait(false);
        PublishMediaChange(ContentChangeKind.MediaAssetAdded, item);
        return item;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveItemAsync(string playlistId, string itemId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var manifest = await GetPlaylistAsync(playlistId, ct).ConfigureAwait(false);
        if (manifest == null)
            return false;

        var existing = manifest.Items.FirstOrDefault(i =>
            string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            return false;

        var storedPath = existing.Path;
        manifest.Items.RemoveAll(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));

        manifest.UpdatedAt = DateTime.UtcNow.ToString("o");
        await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
        TryDeleteManagedMediaFile(storedPath);
        PublishMediaChange(ContentChangeKind.MediaAssetDeleted, existing);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveRootItemAsync(string itemId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var existing = index.Items.FirstOrDefault(i =>
            string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            return false;

        var storedPath = existing.Path;
        index.Items.RemoveAll(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        await WriteIndexAsync(index, ct).ConfigureAwait(false);
        TryDeleteManagedMediaFile(storedPath);
        PublishMediaChange(ContentChangeKind.MediaAssetDeleted, existing);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RenameItemAsync(string? playlistId, string itemId, string newName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var trimmedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            var index = await ReadIndexAsync(ct).ConfigureAwait(false);
            var item = index.Items.FirstOrDefault(existing =>
                string.Equals(existing.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return false;

            item.Name = trimmedName;
            await WriteIndexAsync(index, ct).ConfigureAwait(false);
            PublishMediaChange(ContentChangeKind.MediaAssetUpdated, item);
            return true;
        }

        var manifest = await GetPlaylistAsync(playlistId, ct).ConfigureAwait(false);
        if (manifest == null)
            return false;

        var playlistItem = manifest.Items.FirstOrDefault(existing =>
            string.Equals(existing.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (playlistItem == null)
            return false;

        playlistItem.Name = trimmedName;
        manifest.UpdatedAt = DateTime.UtcNow.ToString("o");
        await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
        PublishMediaChange(ContentChangeKind.MediaAssetUpdated, playlistItem);
        return true;
    }

    /// <inheritdoc />
    public async Task<MediaLibraryItem?> DuplicateItemAsync(string? playlistId, string itemId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        if (string.IsNullOrWhiteSpace(playlistId))
        {
            var index = await ReadIndexAsync(ct).ConfigureAwait(false);
            var sourceItem = index.Items.FirstOrDefault(existing =>
                string.Equals(existing.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (sourceItem == null)
                return null;

            var duplicate = await CreateDuplicateItemAsync(sourceItem, index.Items.Select(existing => existing.Name), ct).ConfigureAwait(false);
            index.Items.Add(duplicate);
            await WriteIndexAsync(index, ct).ConfigureAwait(false);
            PublishMediaChange(ContentChangeKind.MediaAssetAdded, duplicate);
            return CloneItem(duplicate);
        }

        var manifest = await GetPlaylistAsync(playlistId, ct).ConfigureAwait(false);
        if (manifest == null)
            return null;

        var playlistItem = manifest.Items.FirstOrDefault(existing =>
            string.Equals(existing.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (playlistItem == null)
            return null;

        var duplicatedItem = await CreateDuplicateItemAsync(playlistItem, manifest.Items.Select(existing => existing.Name), ct).ConfigureAwait(false);
        manifest.Items.Add(duplicatedItem);
        manifest.UpdatedAt = DateTime.UtcNow.ToString("o");
        await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
        PublishMediaChange(ContentChangeKind.MediaAssetAdded, duplicatedItem);
        return CloneItem(duplicatedItem);
    }

    private void PublishMediaChange(ContentChangeKind kind, MediaLibraryItem item)
    {
        _contentChanges?.Publish(new ContentChangeEvent
        {
            Kind = kind,
            SubjectId = string.IsNullOrWhiteSpace(item.Id) ? item.Path : item.Id,
            Source = nameof(MediaLibraryService),
        });
    }
}