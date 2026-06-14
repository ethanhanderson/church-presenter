using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;


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
    // ── Deck toolbar handlers ─────────────────────────────────────────────────

    private void ThumbnailViewToggle_Click(object sender, RoutedEventArgs e)
    {
        Vm.DeckViewMode = "thumbnail";
    }

    private void TextViewToggle_Click(object sender, RoutedEventArgs e)
    {
        Vm.DeckViewMode = "text";
    }

    private void ListViewToggle_Click(object sender, RoutedEventArgs e)
    {
        Vm.DeckViewMode = "list";
    }

    private void GlobalSlideCut_Click(object sender, RoutedEventArgs e) =>
        Vm.GlobalSlideTransitionModeIndex = 0;

    private void GlobalSlideDissolve_Click(object sender, RoutedEventArgs e) =>
        Vm.GlobalSlideTransitionModeIndex = 1;

    private void GlobalMediaCut_Click(object sender, RoutedEventArgs e) =>
        Vm.GlobalMediaTransitionModeIndex = 0;

    private void GlobalMediaDissolve_Click(object sender, RoutedEventArgs e) =>
        Vm.GlobalMediaTransitionModeIndex = 1;

    // GlobalSlideCustom_Click lives in ShowDeckView.Flyouts.partial.cs

    private void DeckScaleSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        Vm.DeckScaleStep = (int)Math.Round(e.NewValue);
    }

    private bool _colorPickerInitializing;

    private void SlideBackgroundSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts)
            Vm.TransparentThumbnailBackgroundEnabled = ts.IsOn;
    }

    private void ThumbnailBgColorFlyout_Opening(object? sender, object e)
    {
        _colorPickerInitializing = true;
        ThumbnailBgColorPicker.Color = Vm.TransparentThumbnailColorWinUI;
        DeckOpacitySlider.Value = Vm.TransparentThumbnailOpacity;
        _colorPickerInitializing = false;
    }

    private void ThumbnailBgColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_colorPickerInitializing)
            return;

        var c = args.NewColor;
        Vm.TransparentThumbnailColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void DeckOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_colorPickerInitializing)
            return;

        Vm.TransparentThumbnailOpacity = (int)Math.Round(e.NewValue);
    }

    // ── Auto-advance flyout ──────────────────────────────────────────────────

    // ── Browse-stack auto-advance button (no inline flyout; section-aware) ───

    private async void BrowseStackArrangementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.Tag is not ShowPresentationDeckSection section)
            return;
        if (e.AddedItems.Count == 0)
            return;
        if (section.ArrangementPickerSelectedItem is not NamedArrangement selected)
            return;
        await Vm.SetBrowseStackArrangementAsync(section, selected).ConfigureAwait(true);
    }

    /// <summary>Browse-stack and single-deck: sync toggle visuals from source of truth, then show the interval menu.</summary>
    private void AutoAdvanceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;

        if (btn.Tag is ShowPresentationDeckSection section)
        {
            btn.IsChecked = section.IsAutoAdvanceEnabled;
            _autoAdvanceFlyoutSection = section;
            var flyout = CreateBrowseStackAutoAdvanceMenuFlyout();
            ApplyAutoAdvanceTickMarks(flyout, section.AutoAdvanceSeconds);
            flyout.ShowAt(btn);
            return;
        }

        btn.IsChecked = Vm.IsAutoAdvanceEnabled;
        _autoAdvanceFlyoutSection = null;
        var menu = CreateBrowseStackAutoAdvanceMenuFlyout();
        ApplyAutoAdvanceTickMarks(menu, Vm.AutoAdvanceSeconds);
        menu.ShowAt(btn);
    }

    private MenuFlyout CreateBrowseStackAutoAdvanceMenuFlyout()
    {
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        var off = new MenuFlyoutItem { Text = "Off", Tag = "0" };
        off.Click += AutoAdvance_Off_Click;
        flyout.Items.Add(off);
        flyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var (label, secs) in new[] { ("5 seconds", 5), ("10 seconds", 10), ("15 seconds", 15),
                                               ("30 seconds", 30), ("1 minute", 60), ("2 minutes", 120) })
        {
            var item = new MenuFlyoutItem { Text = label, Tag = secs.ToString() };
            item.Click += AutoAdvancePreset_Click;
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        var custom = new MenuFlyoutItem { Text = "Custom…" };
        custom.Click += AutoAdvanceCustom_Click;
        flyout.Items.Add(custom);
        return flyout;
    }

    private static void ApplyAutoAdvanceTickMarks(MenuFlyout flyout, int currentSecs)
    {
        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
        {
            if (item.Tag is not string tag || !int.TryParse(tag, out var tagSecs))
                continue;
            var baseLabel = tagSecs switch
            {
                0 => "Off",
                5 => "5 seconds",
                10 => "10 seconds",
                15 => "15 seconds",
                30 => "30 seconds",
                60 => "1 minute",
                120 => "2 minutes",
                _ => "Off",
            };
            item.Text = tagSecs == currentSecs ? $"{baseLabel} ✓" : baseLabel;
        }
    }

    private void AutoAdvance_Off_Click(object sender, RoutedEventArgs e)
    {
        var path = _autoAdvanceFlyoutSection?.PresentationPath;
        _ = string.IsNullOrEmpty(path)
            ? Vm.SetAutoAdvanceAsync(0)
            : Vm.SetAutoAdvanceForPathAsync(path, 0);
    }

    private void AutoAdvancePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string tag } || !int.TryParse(tag, out var secs)) return;
        var path = _autoAdvanceFlyoutSection?.PresentationPath;
        _ = string.IsNullOrEmpty(path)
            ? Vm.SetAutoAdvanceAsync(secs)
            : Vm.SetAutoAdvanceForPathAsync(path, secs);
    }

    private async void AutoAdvanceCustom_Click(object sender, RoutedEventArgs e)
    {
        var currentSecs = _autoAdvanceFlyoutSection?.AutoAdvanceSeconds ?? Vm.AutoAdvanceSeconds;
        var numberBox = new NumberBox
        {
            Minimum = 1,
            Maximum = 3600,
            Value = currentSecs > 0 ? currentSecs : 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Header = "Seconds per slide",
            MinWidth = 200,
        };

        var dialog = new ContentDialog
        {
            Title = "Custom Auto-Advance",
            Content = numberBox,
            PrimaryButtonText = "Set",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !double.IsNaN(numberBox.Value))
        {
            var secs = (int)Math.Clamp(numberBox.Value, 1, 3600);
            var path = _autoAdvanceFlyoutSection?.PresentationPath;
            _ = string.IsNullOrEmpty(path)
                ? Vm.SetAutoAdvanceAsync(secs)
                : Vm.SetAutoAdvanceForPathAsync(path, secs);
        }
    }
}
