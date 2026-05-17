using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;


using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Media;

/// <summary>
/// Sunday Manager integration surface for song groups and related music data.
/// </summary>
public interface IMusicManagerService
{
    /// <summary>Returns music group names for the UI.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Group display names.</returns>
    Task<IReadOnlyList<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Supabase-backed Sunday Manager music service.</summary>
public sealed class MusicManagerService(HttpClient httpClient, ISettingsService settings, ILogger<MusicManagerService> logger) : IMusicManagerService
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<MusicManagerService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default)
    {
        var integration = _settings.Settings.Integrations.MusicManager;
        if (string.IsNullOrWhiteSpace(integration.SupabaseUrl) || string.IsNullOrWhiteSpace(integration.PublishableKey))
        {
            _logger.LogInformation("Music manager is not configured.");
            return Array.Empty<string>();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{integration.SupabaseUrl.TrimEnd('/')}/rest/v1/music_groups?select=name&order=name");

        request.Headers.Add("apikey", integration.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", integration.PublishableKey);
        request.Headers.Add("Accept-Profile", "music");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Music Manager request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var groups = await JsonSerializer.DeserializeAsync<List<MusicGroupNameRow>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? new List<MusicGroupNameRow>();

        return groups
            .Select(group => group.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();
    }

    private sealed class MusicGroupNameRow
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}