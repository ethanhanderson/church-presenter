namespace ChurchPresenter.Services.Output;

/// <summary>Controls fullscreen audience windows for the configured logical audience screen.</summary>
public interface IAudienceWindowService
{
    /// <summary>Opens the audience screen on mapped local display endpoints.</summary>
    void Open();

    /// <summary>Opens the audience screen and waits for mapped local display endpoints to be ready.</summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes all audience windows.</summary>
    void CloseAll();
}