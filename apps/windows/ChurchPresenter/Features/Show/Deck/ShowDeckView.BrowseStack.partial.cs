using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using ChurchPresenter.Models.Show;
using ChurchPresenter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace ChurchPresenter.Features.Show.Deck;

public sealed partial class ShowDeckView
{
    private void OnBrowseStackScrollToSelectionRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => ScrollBrowseStackToSelectedPresentation(0));
    }

    private void ScrollBrowseStackToSelectedPresentation(int attempt)
    {
        var path = Vm.SelectedPresentationPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        ShowPresentationDeckSection? targetSection = null;
        foreach (var s in Vm.BrowseStackSections)
        {
            if (Vm.PresentationPathsMatch(s.PresentationPath, path))
            {
                targetSection = s;
                break;
            }
        }

        if (targetSection == null)
            return;

        BrowseStackItemsControl.UpdateLayout();
        var container = BrowseStackItemsControl.ContainerFromItem(targetSection);
        if (container is UIElement uie)
        {
            uie.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.12f });
            return;
        }

        if (attempt < 10)
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ScrollBrowseStackToSelectedPresentation(attempt + 1));
        }
    }
}
