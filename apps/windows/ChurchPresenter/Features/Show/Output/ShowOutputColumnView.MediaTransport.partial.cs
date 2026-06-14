using ChurchPresenter.Services.Pipeline;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace ChurchPresenter.Features.Show.Output;

public sealed partial class ShowOutputColumnView
{
    private bool _mediaTransportScrubbing;
    private bool _mediaTransportTargetSelectionSync;
    private bool _showControlsPanelSelectionSync;
    private PointerEventHandler? _mediaTransportSeekPointerPressedHandler;

    private void MediaTransportTargetSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_mediaTransportTargetSelectionSync)
            return;

        if (sender.SelectedItem is not FrameworkElement { Tag: string tag } ||
            !TryParseMediaPlaybackTarget(tag, out var target))
        {
            SyncMediaTransportTargetSelector();
            return;
        }

        _mediaTransportScrubbing = false;
        ViewModel.PlaybackCoordinator.SelectedTransportTarget = target;
    }

    private void ShowControlsSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_showControlsPanelSelectionSync)
            return;

        if (sender.SelectedItem is not FrameworkElement { Tag: string tag })
        {
            SyncShowControlsPanelSelector();
            return;
        }

        ViewModel.SelectedShowControlsPanel = tag;
    }

    private void SyncShowControlsPanelSelector()
    {
        if (ShowControlsSelectorBar == null)
            return;

        SelectorBarItem targetItem = ViewModel.SelectedShowControlsPanel switch
        {
            "stage" => StageScreensSelectorItem,
            "timers" => TimersSelectorItem,
            "messages" => MessagesSelectorItem,
            "props" => PropsSelectorItem,
            "macros" => MacrosSelectorItem,
            _ => AudioBinSelectorItem,
        };

        if (ReferenceEquals(ShowControlsSelectorBar.SelectedItem, targetItem))
            return;

        _showControlsPanelSelectionSync = true;
        try
        {
            ShowControlsSelectorBar.SelectedItem = targetItem;
        }
        finally
        {
            _showControlsPanelSelectionSync = false;
        }
    }

    private void SyncMediaTransportTargetSelector()
    {
        if (MediaTransportTargetSelectorBar == null)
            return;

        var targetItem = ViewModel.PlaybackCoordinator.SelectedTransportTarget switch
        {
            MediaPlaybackTarget.AudioFiles => AudioFilesTransportSelectorItem,
            MediaPlaybackTarget.Announcements => AnnouncementsTransportSelectorItem,
            _ => MediaFilesTransportSelectorItem,
        };

        if (ReferenceEquals(MediaTransportTargetSelectorBar.SelectedItem, targetItem))
            return;

        _mediaTransportTargetSelectionSync = true;
        try
        {
            MediaTransportTargetSelectorBar.SelectedItem = targetItem;
        }
        finally
        {
            _mediaTransportTargetSelectionSync = false;
        }
    }

    private static bool TryParseMediaPlaybackTarget(string tag, out MediaPlaybackTarget target)
    {
        target = tag switch
        {
            "AudioFiles" => MediaPlaybackTarget.AudioFiles,
            "Announcements" => MediaPlaybackTarget.Announcements,
            "MediaFiles" => MediaPlaybackTarget.MediaFiles,
            _ => MediaPlaybackTarget.MediaFiles,
        };
        return tag is "MediaFiles" or "AudioFiles" or "Announcements";
    }

    private void MediaTransportSeekSlider_Loaded(object sender, RoutedEventArgs e)
    {
        _mediaTransportSeekPointerPressedHandler = new PointerEventHandler(MediaTransportSeek_PointerPressed);
        MediaTransportSeekSlider.AddHandler(
            UIElement.PointerPressedEvent,
            _mediaTransportSeekPointerPressedHandler,
            handledEventsToo: true);
    }

    private void MediaTransportSeekSlider_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_mediaTransportSeekPointerPressedHandler is null)
            return;

        MediaTransportSeekSlider.RemoveHandler(
            UIElement.PointerPressedEvent,
            _mediaTransportSeekPointerPressedHandler);
        _mediaTransportSeekPointerPressedHandler = null;
    }

    private void MediaTransportSeek_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        _mediaTransportScrubbing = true;
        ViewModel.PlaybackCoordinator.BeginScrub();
        ViewModel.PlaybackCoordinator.UpdateScrubPosition(slider.Value);
    }

    private void MediaTransportSeek_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_mediaTransportScrubbing)
            return;

        ViewModel.PlaybackCoordinator.UpdateScrubPosition(e.NewValue);
    }

    private void MediaTransportSeek_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        CommitMediaTransportScrub(sender as Slider);

    private void MediaTransportSeek_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        CommitMediaTransportScrub(sender as Slider);

    private void CommitMediaTransportScrub(Slider? slider)
    {
        if (!_mediaTransportScrubbing)
            return;

        _mediaTransportScrubbing = false;
        if (slider == null)
        {
            ViewModel.PlaybackCoordinator.CancelScrub();
            return;
        }

        ViewModel.PlaybackCoordinator.CommitScrubPosition(slider.Value);
    }
}
