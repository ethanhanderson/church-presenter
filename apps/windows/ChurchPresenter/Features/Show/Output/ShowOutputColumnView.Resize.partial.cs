using ChurchPresenter.ViewModels;

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;

namespace ChurchPresenter.Features.Show.Output;

public sealed partial class ShowOutputColumnView
{
    private bool _outputSplitterDragging;
    private double _outputSplitterStartX;
    private double _outputSplitterStartWidth;

    private void OutputPanelResize_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        if (OutputPanelResizeHandleHoverLine != null)
            OutputPanelResizeHandleHoverLine.Opacity = 0.9;
    }

    private void OutputPanelResize_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_outputSplitterDragging)
            ProtectedCursor = null;
        if (!_outputSplitterDragging && OutputPanelResizeHandleHoverLine != null)
            OutputPanelResizeHandleHoverLine.Opacity = 0;
    }

    private void OutputPanelResize_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _outputSplitterDragging = true;
        _outputSplitterStartX = e.GetCurrentPoint(LayoutCoordinateRoot ?? this).Position.X;
        _outputSplitterStartWidth = HostColumnDefinition?.ActualWidth ?? ActualWidth;
        (sender as UIElement)?.CapturePointer(e.Pointer);
    }

    private void OutputPanelResize_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_outputSplitterDragging)
            return;

        var currentX = e.GetCurrentPoint(LayoutCoordinateRoot ?? this).Position.X;
        var delta = currentX - _outputSplitterStartX;
        ViewModel.OutputPanelWidth = WorkspaceDto.NormalizeStoredShowOutputPanelWidth(
            _outputSplitterStartWidth - delta);
    }

    private void OutputPanelResize_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var endedResizeGesture = _outputSplitterDragging;
        _outputSplitterDragging = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        if (endedResizeGesture)
            _ = ViewModel.SaveWorkspaceUiStateAsync();
    }

    private void OutputPanelResize_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        var endedResizeGesture = _outputSplitterDragging;
        _outputSplitterDragging = false;
        if (sender is FrameworkElement el)
        {
            var pt = e.GetCurrentPoint(el).Position;
            var bounds = new Rect(0, 0, el.ActualWidth, el.ActualHeight);
            var inside = bounds.Contains(pt);
            ProtectedCursor = inside
                ? InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast)
                : null;
            if (OutputPanelResizeHandleHoverLine != null)
                OutputPanelResizeHandleHoverLine.Opacity = inside ? 0.9 : 0;
        }

        if (endedResizeGesture)
            _ = ViewModel.SaveWorkspaceUiStateAsync();
    }

    private void OutputPanelResize_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        _outputSplitterDragging = false;
        if (ProtectedCursor != null)
            ProtectedCursor = null;

        if (OutputPanelResizeHandleHoverLine != null)
            OutputPanelResizeHandleHoverLine.Opacity = 0;

        if (sender is UIElement ue)
            ue.ReleasePointerCaptures();

        ViewModel.OutputPanelWidth = WorkspaceDto.ShowOutputPanelDefaultWidthDpi;
        _ = ViewModel.SaveWorkspaceUiStateAsync();
    }
}
