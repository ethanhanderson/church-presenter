using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Show;

/// <inheritdoc />
public sealed class WorkspaceService(
    IContentDirectoryService paths,
    ILogger<WorkspaceService> logger) : IWorkspaceService
{
    private const string FileName = "workspace.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly IContentDirectoryService _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILogger<WorkspaceService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public WorkspaceDto Workspace { get; private set; } = new();

    /// <inheritdoc />
    public async Task LoadAsync()
    {
        var dir = _paths.GetAppDataDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FileName);
        if (!File.Exists(path))
        {
            Workspace = Normalize(new WorkspaceDto());
            _logger.LogDebug("No workspace file at {Path}; using defaults.", path);
            return;
        }

        try
        {
            await using var fs = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<WorkspaceDto>(fs, JsonOptions).ConfigureAwait(false);
            Workspace = Normalize(loaded);
            _logger.LogInformation("Workspace loaded from {Path}.", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Workspace = Normalize(new WorkspaceDto());
            _logger.LogWarning(ex, "Could not load workspace from {Path}; using defaults.", path);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        var dir = _paths.GetAppDataDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FileName);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, Workspace, JsonOptions).ConfigureAwait(false);
        _logger.LogDebug("Workspace saved to {Path}.", path);
    }

    /// <inheritdoc />
    public void Update(Action<WorkspaceDto> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        mutator(Workspace);
    }

    private static WorkspaceDto Normalize(WorkspaceDto? workspace)
    {
        var normalized = workspace ?? new WorkspaceDto();
        if (string.IsNullOrWhiteSpace(normalized.ActivePage))
            normalized.ActivePage = "show";

        normalized.ShowOutputPanelWidth = WorkspaceDto.NormalizeStoredShowOutputPanelWidth(normalized.ShowOutputPanelWidth);

        return normalized;
    }
}