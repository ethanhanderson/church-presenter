namespace ChurchPresenter.Services.Output;

/// <summary>Controls fullscreen audience windows for the configured logical audience screen.</summary>
public interface IAudienceWindowService
{
    /// <summary>Opens the audience screen on mapped local display endpoints.</summary>
    void Open();

    /// <summary>Closes all audience windows.</summary>
    void CloseAll();
}