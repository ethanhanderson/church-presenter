using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Themes;

/// <summary>
/// Loads and saves the global theme library stored under the content directory.
/// </summary>
public interface IThemeLibraryService
{
    /// <summary>
    /// Loads all global themes from disk.
    /// </summary>
    Task<IReadOnlyList<ThemeTemplate>> LoadAsync(CancellationToken cancellationToken = default);

    Task<ThemeTemplate?> LoadThemeAsync(string themeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the supplied global themes to disk.
    /// </summary>
    Task SaveAsync(IReadOnlyCollection<ThemeTemplate> themes, CancellationToken cancellationToken = default);

    Task SaveThemeAsync(ThemeTemplate theme, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ThemeLibraryService : IThemeLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = PresentationJsonSerialization.CreateOptions();

    private readonly IContentDirectoryService _contentDirectories;
    private readonly IContentStore _contentStore;
    private readonly ILogger<ThemeLibraryService> _logger;

    /// <summary>
    /// Creates the theme library service with the shared content store abstraction.
    /// </summary>
    public ThemeLibraryService(
        IContentDirectoryService contentDirectories,
        IContentStore contentStore,
        ILogger<ThemeLibraryService> logger)
    {
        _contentDirectories = contentDirectories ?? throw new ArgumentNullException(nameof(contentDirectories));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the theme library service with the default file-system content store.
    /// </summary>
    public ThemeLibraryService(
        IContentDirectoryService contentDirectories,
        ILogger<ThemeLibraryService> logger)
        : this(contentDirectories, ContentStoreDefaults.Instance, logger)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThemeTemplate>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var root = _contentDirectories.GetThemesRootDirectory();
        _contentStore.EnsureDirectory(root);

        var index = await _contentStore.ReadJsonAsync<ThemeIndex>(
                _contentDirectories.GetThemesIndexPath(),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        if (index == null)
            return Array.Empty<ThemeTemplate>();

        var themes = new List<ThemeTemplate>(index.Entries.Count);
        foreach (var entry in index.Entries.Where(static entry => !string.IsNullOrWhiteSpace(entry.Id)))
        {
            var theme = await _contentStore.ReadJsonAsync<ThemeTemplate>(
                    _contentDirectories.GetThemeFilePath(entry.Id),
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (theme == null)
            {
                _logger.LogWarning("Theme manifest missing for id '{ThemeId}'.", entry.Id);
                continue;
            }

            theme.Id = string.IsNullOrWhiteSpace(theme.Id) ? entry.Id : theme.Id;
            theme.Name = string.IsNullOrWhiteSpace(theme.Name) ? entry.Name : theme.Name;
            theme.Folder = string.IsNullOrWhiteSpace(theme.Folder) ? entry.Folder : theme.Folder;
            PresentationModelUtilities.NormalizeTheme(theme);
            themes.Add(theme);
        }

        return themes;
    }

    /// <inheritdoc />
    public async Task<ThemeTemplate?> LoadThemeAsync(string themeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return null;

        ThemeTemplate? theme = await _contentStore.ReadJsonAsync<ThemeTemplate>(
                _contentDirectories.GetThemeFilePath(themeId.Trim()),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        if (theme == null)
            return null;

        theme.Id = string.IsNullOrWhiteSpace(theme.Id) ? themeId.Trim() : theme.Id.Trim();
        PresentationModelUtilities.NormalizeTheme(theme);
        return theme;
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyCollection<ThemeTemplate> themes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(themes);

        var root = _contentDirectories.GetThemesRootDirectory();
        _contentStore.EnsureDirectory(root);

        var materialized = themes.Select(PresentationModelUtilities.CloneTheme).ToList();
        foreach (var theme in materialized)
        {
            theme.Id = string.IsNullOrWhiteSpace(theme.Id) ? Guid.NewGuid().ToString("N") : theme.Id.Trim();
            theme.Name = string.IsNullOrWhiteSpace(theme.Name) ? theme.Id : theme.Name.Trim();
            theme.CreatedAt ??= DateTimeOffset.UtcNow.ToString("O");
            theme.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
            PresentationModelUtilities.NormalizeTheme(theme);
        }

        foreach (var theme in materialized)
        {
            await _contentStore.WriteJsonAsync(
                    _contentDirectories.GetThemeFilePath(theme.Id),
                    theme,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await PruneDeletedThemesAsync(materialized, cancellationToken).ConfigureAwait(false);

        var index = new ThemeIndex
        {
            Entries = materialized
                .OrderBy(static theme => theme.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static theme => theme.Id, StringComparer.OrdinalIgnoreCase)
                .Select(theme => new ThemeIndexEntry
                {
                    Id = theme.Id,
                    Name = theme.Name,
                    Folder = string.IsNullOrWhiteSpace(theme.Folder) ? null : theme.Folder.Trim(),
                    CreatedAt = theme.CreatedAt ?? theme.UpdatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
                    UpdatedAt = theme.UpdatedAt,
                })
                .ToList(),
        };

        await _contentStore.WriteJsonAsync(
                _contentDirectories.GetThemesIndexPath(),
                index,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Saved {ThemeCount} global themes to {Root}.",
            materialized.Count,
            root);
    }

    /// <inheritdoc />
    public async Task SaveThemeAsync(ThemeTemplate theme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var themes = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var existingIndex = themes.FindIndex(candidate =>
            string.Equals(candidate.Id, theme.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            themes[existingIndex] = theme;
        else
            themes.Add(theme);

        await SaveAsync(themes, cancellationToken).ConfigureAwait(false);
    }

    private async Task PruneDeletedThemesAsync(IReadOnlyCollection<ThemeTemplate> activeThemes, CancellationToken cancellationToken)
    {
        var activeIds = activeThemes
            .Select(static theme => theme.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingIndex = await _contentStore.ReadJsonAsync<ThemeIndex>(
                _contentDirectories.GetThemesIndexPath(),
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingIndex == null)
            return;

        foreach (var orphanId in existingIndex.Entries
                     .Select(static entry => entry.Id)
                     .Where(id => !string.IsNullOrWhiteSpace(id) && !activeIds.Contains(id)))
        {
            _contentStore.TryDeleteFile(_contentDirectories.GetThemeFilePath(orphanId));
        }
    }
}