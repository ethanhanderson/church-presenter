using ChurchPresenter.Services.Media;
using ChurchPresenter.Services.Pipeline;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Dispatching;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// Observable transport state for the Show output panel, backed by the pipeline media binding.
/// </summary>
public sealed partial class MediaTransportPresenter : ObservableObject
{
    private readonly IMediaTransportService _transport;
    private readonly CuePipelineRuntime _runtime;
    private readonly DispatcherQueue? _dispatcherQueue;
    private MediaPlaybackTarget _selectedTransportTarget = MediaPlaybackTarget.MediaFiles;
    private bool _isScrubbing;

    public MediaTransportPresenter(IMediaTransportService transport, CuePipelineRuntime runtime)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatcherQueue = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        _runtime.Changed += (_, _) => Refresh();
        Refresh();
    }

    public MediaPlaybackTarget SelectedTransportTarget
    {
        get => _selectedTransportTarget;
        set
        {
            if (SetProperty(ref _selectedTransportTarget, value))
                Refresh();
        }
    }

    public bool IsScrubbing => _isScrubbing;

    [ObservableProperty]
    private bool _hasActiveCue;

    [ObservableProperty]
    private bool _canControlTransport;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _transportCueName = "No media";

    [ObservableProperty]
    private string _transportPositionLabel = "0:00";

    [ObservableProperty]
    private string _transportRemainingLabel = "0:00";

    [ObservableProperty]
    private double _transportSliderMaximum = 1;

    [ObservableProperty]
    private double _transportSliderValue;

    public void Refresh()
    {
        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            RefreshCore();
            return;
        }

        _ = _dispatcherQueue.TryEnqueue(RefreshCore);
    }

    private void RefreshCore()
    {
        TransportDisplaySnapshot display = _transport.GetDisplay(SelectedTransportTarget);
        HasActiveCue = display.HasActiveCue;
        CanControlTransport = display.IsEnabled;
        IsPlaying = display.IsPlaying;
        TransportCueName = string.IsNullOrWhiteSpace(display.Name) ? ResolveEmptyCueName(SelectedTransportTarget) : display.Name;
        TransportPositionLabel = display.PositionLabel;
        TransportRemainingLabel = display.DurationSeconds.HasValue
            ? FormatRemaining(display.DurationSeconds.Value - display.PositionSeconds)
            : "0:00";
        TransportSliderMaximum = display.DurationSeconds.GetValueOrDefault() > 0
            ? display.DurationSeconds.GetValueOrDefault()
            : 1;
        if (!_isScrubbing)
            TransportSliderValue = display.PositionSeconds;
    }

    public void BeginScrub()
    {
        _isScrubbing = true;
        _transport.BeginScrub(SelectedTransportTarget);
    }

    public void UpdateScrubPosition(double positionSeconds)
    {
        _transport.UpdateScrub(positionSeconds);
        TransportSliderValue = Math.Max(0, positionSeconds);
    }

    public void CommitScrubPosition(double positionSeconds)
    {
        _transport.UpdateScrub(positionSeconds);
        _ = _transport.CommitScrubAsync(SelectedTransportTarget);
        _isScrubbing = false;
        Refresh();
    }

    public void CancelScrub()
    {
        _isScrubbing = false;
        Refresh();
    }

    private static string FormatRemaining(double remainingSeconds)
    {
        int seconds = Math.Max(0, (int)Math.Floor(remainingSeconds));
        int minutes = seconds / 60;
        seconds %= 60;
        return $"-{minutes}:{seconds:00}";
    }

    private static string ResolveEmptyCueName(MediaPlaybackTarget target) =>
        target switch
        {
            MediaPlaybackTarget.AudioFiles => "No audio",
            MediaPlaybackTarget.Announcements => "No announcement",
            _ => "No presentation media",
        };
}
