namespace ChurchPresenter.Services.Output;

/// <summary>Controls fullscreen stage windows for the configured logical stage screen.</summary>
public interface IStageWindowService
{
    /// <summary>Opens the stage screen on mapped local display endpoints.</summary>
    void Open();

    /// <summary>Closes all stage windows.</summary>
    void CloseAll();
}