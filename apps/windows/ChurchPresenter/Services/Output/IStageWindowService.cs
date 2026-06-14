namespace ChurchPresenter.Services.Output;

/// <summary>Controls fullscreen stage windows for the configured logical stage screen.</summary>
public interface IStageWindowService
{
    /// <summary>Opens the stage screen on mapped local display endpoints.</summary>
    void Open();

    /// <summary>Opens the stage screen and waits for mapped local display endpoints to be ready.</summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes all stage windows.</summary>
    void CloseAll();
}