using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using ChurchPresenter.ViewModels;

namespace ChurchPresenter.Features.Show.Output;

public sealed partial class ShowOutputColumnView
{
    private const string PreviewTargetRadioGroupName = "PreviewTargetScreen";

    private void PreviewTargetFlyout_Opening(object sender, object e)
    {
        _ = e;
        if (sender is not MenuFlyout flyout || ViewModel?.ProgramOutput is not OutputViewModel programOutput)
            return;

        ApplyPreviewTargetFlyoutWidth(flyout);
        PopulatePreviewTargetFlyout(flyout, programOutput);
    }

    private void PopulatePreviewTargetFlyout(MenuFlyout flyout, OutputViewModel programOutput)
    {
        flyout.Items.Clear();

        IReadOnlyList<PreviewTargetOption> options = programOutput.PreviewTargets;
        List<PreviewTargetOption> audienceOptions = options.Where(static option => !option.IsStage).ToList();
        List<PreviewTargetOption> stageOptions = options.Where(static option => option.IsStage).ToList();
        string? selectedScreenId = programOutput.SelectedPreviewScreenId;

        bool audienceAdded = AppendPreviewTargetGroup(flyout, "Audience", audienceOptions, selectedScreenId);
        if (audienceAdded && stageOptions.Count > 0)
            flyout.Items.Add(new MenuFlyoutSeparator());

        AppendPreviewTargetGroup(flyout, "Stage", stageOptions, selectedScreenId);
    }

    private bool AppendPreviewTargetGroup(
        MenuFlyout flyout,
        string header,
        IReadOnlyList<PreviewTargetOption> options,
        string? selectedScreenId)
    {
        if (options.Count == 0)
            return false;

        flyout.Items.Add(CreatePreviewTargetGroupHeader(header));
        foreach (PreviewTargetOption option in options)
            flyout.Items.Add(CreatePreviewTargetRadioItem(option, selectedScreenId));

        return true;
    }

    private MenuFlyoutItem CreatePreviewTargetGroupHeader(string title) =>
        new()
        {
            Text = title,
            Style = (Style)Resources["PreviewTargetGroupHeaderMenuFlyoutItemStyle"],
        };

    private RadioMenuFlyoutItem CreatePreviewTargetRadioItem(PreviewTargetOption option, string? selectedScreenId)
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = option.Name,
            Tag = option.ScreenId,
            GroupName = PreviewTargetRadioGroupName,
            IsChecked = string.Equals(option.ScreenId, selectedScreenId, StringComparison.OrdinalIgnoreCase),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        item.Click += PreviewTargetItem_Click;
        return item;
    }

    private void ApplyPreviewTargetFlyoutWidth(MenuFlyout flyout)
    {
        double width = PreviewTargetPicker.ActualWidth;
        if (width <= 0)
            return;

        flyout.MenuFlyoutPresenterStyle = BuildPreviewTargetMenuFlyoutPresenterStyle(width);
    }

    private static Style BuildPreviewTargetMenuFlyoutPresenterStyle(double width) =>
        new(typeof(MenuFlyoutPresenter))
        {
            Setters =
            {
                new Setter(FrameworkElement.MinWidthProperty, width),
                new Setter(FrameworkElement.WidthProperty, width),
            },
        };

    private void PreviewTargetItem_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is MenuFlyoutItem { Tag: string screenId })
            ViewModel?.ProgramOutput.SelectPreviewTarget(screenId);
    }
}
