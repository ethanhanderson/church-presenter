
namespace ChurchPresenter.ViewModels;

/// <summary>Output view model for the audience feed, filtered by the active backend Look route.</summary>
public sealed class AudienceOutputViewModel(AudienceOutputFrameFacade frames)
    : OutputViewModel(frames);

/// <summary>Output view model for the stage feed, filtered by the active backend Look route.</summary>
public sealed class StageOutputViewModel(StageOutputFrameFacade frames)
    : OutputViewModel(frames);
