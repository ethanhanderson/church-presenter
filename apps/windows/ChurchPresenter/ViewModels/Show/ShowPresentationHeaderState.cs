namespace ChurchPresenter.ViewModels;

/// <summary>
/// Per-presentation UI state for browse-stack header actions that should survive section rebuilds.
/// </summary>
internal sealed class ShowPresentationHeaderState
{
    public bool ArrangementSectionExpanded { get; set; }
}