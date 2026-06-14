#nullable enable



using System.Collections.ObjectModel;

using ChurchPresenter.Adapters.Rendering.Pipeline;
using ChurchPresenter.Backend.Output;

using ChurchPresenter.Backend.Pipeline.Routing;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Models.Output;
using ChurchPresenter.Services.Pipeline;



using CommunityToolkit.Mvvm.ComponentModel;



using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;



namespace ChurchPresenter.ViewModels;



/// <summary>
/// Operator program preview surface backed by the unified output pipeline.
/// </summary>
public partial class OutputViewModel : ObservableObject
{

    private const double DefaultFrameWidth = 1920;

    private const double DefaultFrameHeight = 1080;



    private readonly ILiveSessionQueryService _liveSession;

    private readonly IProgramRenderService _renderService;

    private readonly OperatorPreviewSession _previewSession;

    private readonly CuePipelineRuntime? _pipeline;

    private readonly DispatcherQueue? _dispatcherQueue;



    private string _programTitle = string.Empty;

    private bool _isBlackout;

    private bool _isClear;

    private FrameworkElement? _previewAnchor;

    private string? _selectedPreviewScreenId;



    public OutputViewModel(

        ILiveSessionQueryService liveSession,

        IProgramRenderService renderService,

        OperatorPreviewSession previewSession,

        CuePipelineRuntime pipeline)

    {

        _liveSession = liveSession ?? throw new ArgumentNullException(nameof(liveSession));

        _renderService = renderService ?? throw new ArgumentNullException(nameof(renderService));

        _previewSession = previewSession ?? throw new ArgumentNullException(nameof(previewSession));

        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;


        _liveSession.Changed += (_, args) => RunOnUiThread(() => RefreshFromSnapshot(args.Snapshot), "live-session-changed");

        _pipeline.Changed += (_, _) => RunOnUiThread(OnPipelineChanged, "pipeline-changed");

        RefreshFromSnapshot(_liveSession.Current);

        RefreshPreviewTargets();

    }

    /// <summary>Selectable output screens (audience feeds + stage) the operator preview can mirror.</summary>
    public ObservableCollection<PreviewTargetOption> PreviewTargets { get; } = [];

    /// <summary>Display name of the currently mirrored screen, shown on the preview target picker.</summary>
    public string SelectedPreviewTargetName =>
        ResolveSelectedScreen() is { } screen ? screen.Name : "Audience";

    /// <summary>Id of the currently mirrored screen, used to mark the active preview-target menu item.</summary>
    public string? SelectedPreviewScreenId => ResolveSelectedScreen()?.Id;

    /// <summary>True when more than one output screen exists, so the picker is worth showing.</summary>
    public bool HasMultiplePreviewTargets => PreviewTargets.Count > 1;

    /// <summary>Visibility of the preview-target picker (shown only when there is a choice to make).</summary>
    public Visibility PreviewTargetPickerVisibility =>
        HasMultiplePreviewTargets ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Re-targets the operator preview to mirror the given screen and replays its latest frame.</summary>
    public void SelectPreviewTarget(string screenId)
    {
        if (string.IsNullOrWhiteSpace(screenId))
            return;

        _selectedPreviewScreenId = screenId;

        _previewSession.SetTargetScreen(screenId);
        _previewSession.NudgeCanonicalRender(screenId);
        if (_previewAnchor is not null)
            _previewSession.ShowOver(_previewAnchor, FrameWidth, FrameHeight, OutputStageStretch);

        OnPropertyChanged(nameof(SelectedPreviewTargetName));
        RefreshFrameDimensions();
        _ = _pipeline?.InvalidateTransitionDefaultsAsync();
    }



    [ObservableProperty]

    private Stretch _outputStageStretch = Stretch.Uniform;



    public string ProgramTitle

    {

        get => _programTitle;

        private set => SetProperty(ref _programTitle, value);

    }



    public bool IsBlackout

    {

        get => _isBlackout;

        private set => SetProperty(ref _isBlackout, value);

    }



    public bool IsClear

    {

        get => _isClear;

        private set => SetProperty(ref _isClear, value);

    }



    public double FrameWidth => ResolveSelectedScreen()?.RenderSize.Width ?? DefaultFrameWidth;



    public double FrameHeight => ResolveSelectedScreen()?.RenderSize.Height ?? DefaultFrameHeight;



    /// <summary>Positions the shell-hosted preview mirror over the Show page preview slot.</summary>
    public void AttachPreview(FrameworkElement previewAnchor)
    {
        ArgumentNullException.ThrowIfNull(previewAnchor);

        _previewAnchor = previewAnchor;
        OutputScreen? selected = ResolveSelectedScreen();
        _selectedPreviewScreenId = selected?.Id;
        string screenId = selected?.Id ?? "audience";

        _previewSession.SetTargetScreen(screenId);
        _previewSession.NudgeCanonicalRender(screenId);
        _previewSession.ShowOver(previewAnchor, FrameWidth, FrameHeight, OutputStageStretch);
        Hosting.ShowNavigationDiagnostics.Log(
            "PreviewSession.Attach",
            $"screen={screenId} brushHash={_previewSession.Brush.GetHashCode()}");

        if (_dispatcherQueue == null)
        {
            _ = _pipeline?.InvalidateTransitionDefaultsAsync();
            return;
        }

        _dispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                _ = _pipeline?.InvalidateTransitionDefaultsAsync();
            });
    }

    /// <summary>Hides the shell preview overlay when Show leaves the frame.</summary>
    public void DetachPreviewPage(FrameworkElement previewAnchor)
    {
        ArgumentNullException.ThrowIfNull(previewAnchor);
        if (!ReferenceEquals(_previewAnchor, previewAnchor))
            return;

        _previewAnchor = null;
        _previewSession.HideOverlay();
        Hosting.ShowNavigationDiagnostics.Log(
            "PreviewSession.DetachPage",
            $"brushHash={_previewSession.Brush.GetHashCode()}");
    }



    private void RefreshFromSnapshot(LiveSessionQuerySnapshot snapshot)

    {

        ProgramTitle = BuildProgramTitle(snapshot);

        IsBlackout = snapshot.ActiveLayers.Any(static layer =>

            layer.Kind == OutputLayerKind.Mask && layer.IsLive);

        IsClear = snapshot.ActiveLayers.All(static layer => !layer.IsLive);

        RefreshFrameDimensions();

    }



    private void RefreshFrameDimensions()
    {
        OnPropertyChanged(nameof(FrameWidth));
        OnPropertyChanged(nameof(FrameHeight));
        if (_previewAnchor is not null)
            _previewSession.ShowOver(_previewAnchor, FrameWidth, FrameHeight, OutputStageStretch);
    }

    private void OnPipelineChanged()
    {
        RefreshPreviewTargets();
        RefreshFrameDimensions();
    }

    private void RefreshPreviewTargets()
    {
        LiveSessionSnapshot snapshot = _pipeline?.Snapshot ?? LiveSessionSnapshot.CreateEmpty();
        List<PreviewTargetOption> options = snapshot.SessionState.Screens.Values
            .OrderBy(static screen => screen.Kind)
            .ThenBy(static screen => screen.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static screen => new PreviewTargetOption(screen.Id, screen.Name, screen.Kind))
            .ToList();

        bool changed = options.Count != PreviewTargets.Count
            || options.Where((option, index) => !option.Equals(PreviewTargets[index])).Any();
        if (changed)
        {
            PreviewTargets.Clear();
            foreach (PreviewTargetOption option in options)
                PreviewTargets.Add(option);
            OnPropertyChanged(nameof(HasMultiplePreviewTargets));
            OnPropertyChanged(nameof(PreviewTargetPickerVisibility));
        }

        // Fall back to a valid target if the selected screen disappeared from the topology.
        string? previousScreenId = _selectedPreviewScreenId;
        if (_selectedPreviewScreenId == null
            || !options.Any(option => string.Equals(option.ScreenId, _selectedPreviewScreenId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedPreviewScreenId = ResolveMainScreen()?.Id;
            OnPropertyChanged(nameof(SelectedPreviewTargetName));
        }

        if (!string.Equals(previousScreenId, _selectedPreviewScreenId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_selectedPreviewScreenId))
        {
            _previewSession.SetTargetScreen(_selectedPreviewScreenId);
            _previewSession.NudgeCanonicalRender(_selectedPreviewScreenId);
            if (_previewAnchor is not null)
                _previewSession.ShowOver(_previewAnchor, FrameWidth, FrameHeight, OutputStageStretch);
        }
    }

    private void RunOnUiThread(Action action, string reason)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _ = reason;
        _ = _dispatcherQueue.TryEnqueue(() => action());
    }



    private OutputScreen? ResolveMainScreen()

    {

        LiveSessionSnapshot pipelineSnapshot = _pipeline?.Snapshot ?? LiveSessionSnapshot.CreateEmpty();

        return pipelineSnapshot.SessionState.Screens.Values

            .FirstOrDefault(static screen => screen.Kind == OutputScreenKind.Audience)

            ?? pipelineSnapshot.SessionState.Screens.Values.FirstOrDefault();

    }

    private OutputScreen? ResolveSelectedScreen()
    {
        LiveSessionSnapshot pipelineSnapshot = _pipeline?.Snapshot ?? LiveSessionSnapshot.CreateEmpty();
        if (_selectedPreviewScreenId != null
            && pipelineSnapshot.SessionState.Screens.TryGetValue(_selectedPreviewScreenId, out OutputScreen? selected))
        {
            return selected;
        }

        return ResolveMainScreen();
    }



    private static string BuildProgramTitle(LiveSessionQuerySnapshot snapshot)

    {

        LiveLayerStateQuery? slideLayer = snapshot.ActiveLayers.FirstOrDefault(static layer =>

            layer.Kind == OutputLayerKind.Slide && layer.IsLive);

        if (slideLayer == null)

            return string.Empty;



        return FirstNonEmpty(slideLayer.PayloadName, slideLayer.PayloadPresentationPath, slideLayer.PayloadSlideId);

    }



    private static string FirstNonEmpty(params string?[] values) =>

        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

}



/// <summary>A selectable output screen the operator preview can mirror.</summary>
public sealed record PreviewTargetOption(string ScreenId, string Name, OutputScreenKind Kind)
{
    /// <summary>True when this option mirrors a stage screen.</summary>
    public bool IsStage => Kind == OutputScreenKind.Stage;
}


