using System.Text;
using System.Text.Json;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Content;

/// <summary>
/// Stores content maintenance events as JSON lines under local app data.
/// </summary>
public sealed class ContentMaintenanceLogService(
    IContentDirectoryService paths,
    ILogger<ContentMaintenanceLogService> logger) : IContentMaintenanceLogService
{
    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILogger<ContentMaintenanceLogService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    /// <inheritdoc />
    public string GetLogPath() =>
        Path.Combine(_paths.GetAppDataDirectory(), "logs", "content-maintenance.jsonl");

    /// <inheritdoc />
    public async Task AppendEntriesAsync(IEnumerable<ContentMaintenanceLogEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var materialized = entries
            .Where(static entry => entry != null)
            .ToList();
        if (materialized.Count == 0)
            return;

        var path = GetLogPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        foreach (var entry in materialized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContentMaintenanceLogEntry>> ReadRecentEntriesAsync(int maxEntries = 50, CancellationToken cancellationToken = default)
    {
        var safeMaxEntries = Math.Clamp(maxEntries, 1, 200);
        var path = GetLogPath();
        if (!File.Exists(path))
            return [];

        var entries = new List<ContentMaintenanceLogEntry>();
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = JsonSerializer.Deserialize<ContentMaintenanceLogEntry>(line, JsonOptions);
                if (entry != null)
                    entries.Add(entry);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Skipping malformed maintenance log line from {Path}.", path);
            }
        }

        return entries
            .OrderByDescending(static entry => entry.Timestamp, StringComparer.Ordinal)
            .Take(safeMaxEntries)
            .ToList();
    }
}