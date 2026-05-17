using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Media;

public sealed partial class MediaLibraryService
{
    // ── Cue defaults ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<bool> UpdateItemCueDefaultsAsync(
        string? playlistId,
        string itemId,
        MediaCueDefaults defaults,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(defaults);

        if (string.IsNullOrWhiteSpace(playlistId))
        {
            var index = await ReadIndexAsync(ct).ConfigureAwait(false);
            var rootItem = index.Items.FirstOrDefault(i =>
                string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (rootItem == null)
                return false;

            rootItem.CueDefaults = defaults;
            await WriteIndexAsync(index, ct).ConfigureAwait(false);
            return true;
        }

        var manifest = await GetPlaylistAsync(playlistId, ct).ConfigureAwait(false);
        if (manifest == null)
            return false;

        var item = manifest.Items.FirstOrDefault(i =>
            string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return false;

        item.CueDefaults = defaults;
        manifest.UpdatedAt = DateTime.UtcNow.ToString("o");
        await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateMediaItemFileMetadataAsync(
        string itemId,
        double? durationSeconds,
        int? width,
        int? height,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        static bool ApplyIfMissing(MediaLibraryItem item, double? durationSeconds, int? width, int? height)
        {
            var changed = false;
            if (durationSeconds is > 0 && (item.Duration is not { } d || d <= 0))
            {
                item.Duration = durationSeconds;
                changed = true;
            }

            if (width is > 0 && (item.Width is not { } w || w <= 0))
            {
                item.Width = width;
                changed = true;
            }

            if (height is > 0 && (item.Height is not { } h || h <= 0))
            {
                item.Height = height;
                changed = true;
            }

            return changed;
        }

        var index = await ReadIndexAsync(ct).ConfigureAwait(false);
        var rootItem = index.Items.FirstOrDefault(i =>
            string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (rootItem != null)
        {
            if (!ApplyIfMissing(rootItem, durationSeconds, width, height))
            {
                return false;
            }

            await WriteIndexAsync(index, ct).ConfigureAwait(false);
            return true;
        }

        foreach (var entry in index.Playlists)
        {
            var manifest = await GetPlaylistAsync(entry.Id, ct).ConfigureAwait(false);
            if (manifest == null)
            {
                continue;
            }

            var playlistItem = manifest.Items.FirstOrDefault(i =>
                string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (playlistItem == null)
            {
                continue;
            }

            if (!ApplyIfMissing(playlistItem, durationSeconds, width, height))
            {
                return false;
            }

            manifest.UpdatedAt = DateTime.UtcNow.ToString("o");
            await WritePlaylistAsync(manifest, ct).ConfigureAwait(false);
            return true;
        }

        return false;
    }
}