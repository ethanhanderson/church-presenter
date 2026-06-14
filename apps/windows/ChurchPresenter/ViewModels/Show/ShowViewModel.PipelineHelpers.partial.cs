namespace ChurchPresenter.ViewModels;

public partial class ShowViewModel
{
    private string? GetLiveSlideId() => _liveSessionQuery.Current.LiveSlideId;

    private string? GetLivePresentationPath() => _liveSessionQuery.Current.LivePresentationPath;

    private string? GetLiveSlideInstanceKey()
    {
        string? instanceKey = _liveSessionQuery.Current.ActiveLayers
            .FirstOrDefault(static layer => layer.Kind == Backend.Rendering.OutputLayerKind.Slide && layer.IsLive)
            ?.PayloadInstanceKey;
        return !string.IsNullOrWhiteSpace(instanceKey)
            ? instanceKey
            : _operatorSelection.SelectionCursor.InstanceKey;
    }
}
