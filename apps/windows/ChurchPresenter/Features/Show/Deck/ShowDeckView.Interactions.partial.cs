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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using System.Text.Json;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Services.Pipeline;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Hosting;

namespace ChurchPresenter.Features.Show.Deck;

public sealed partial class ShowDeckView
{
    private bool TryGetSlideDeckItem(object sender, out ShowSlideDeckItem item)
    {
        item = null!;

        // MenuFlyout Opening event: resolve from the target button's Tag.
        if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement flyoutTarget
            && flyoutTarget.Tag is ShowSlideDeckItem flyoutItem)
        {
            item = flyoutItem;
            return true;
        }

        // Direct FrameworkElement (button Click): Tag holds the data item set via x:Bind.
        if (sender is FrameworkElement element && element.Tag is ShowSlideDeckItem elementItem)
        {
            item = elementItem;
            return true;
        }

        // MenuFlyoutItem Click — sender is the menu item itself, which has no direct reference
        // to the card data. Use the item stored when the context menu opened.
        if (_contextMenuSlideItem != null)
        {
            item = _contextMenuSlideItem;
            return true;
        }

        return false;
    }

    private string ResolveSlideDeckPresentationPath(ShowSlideDeckItem item)
    {
        return string.IsNullOrWhiteSpace(item.PresentationPath)
            ? Vm.SelectedPresentationPath ?? item.ThumbnailProject?.SourcePath ?? string.Empty
            : item.PresentationPath;
    }

    private bool TryGetSlideDeckTarget(object sender, out FrameworkElement target, out ShowSlideDeckItem item)
    {
        item = null!;
        target = null!;

        // MenuFlyout Opening event: resolve from the target button's Tag.
        if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement flyoutTarget
            && flyoutTarget.Tag is ShowSlideDeckItem flyoutItem)
        {
            target = flyoutTarget;
            item = flyoutItem;
            return true;
        }

        // Direct FrameworkElement (button Click): Tag holds the data item set via x:Bind.
        if (sender is FrameworkElement element && element.Tag is ShowSlideDeckItem elementItem)
        {
            target = element;
            item = elementItem;
            return true;
        }

        // MenuFlyoutItem Click — fall back to the item and target stored when the context menu opened.
        if (_contextMenuSlideItem != null && _contextMenuSlideTarget != null)
        {
            target = _contextMenuSlideTarget;
            item = _contextMenuSlideItem;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> BuildQuickEditUpdates(IReadOnlyDictionary<string, TextBox> textBoxes)
    {
        return textBoxes.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.OrdinalIgnoreCase);
    }

    private async Task ShowQuickEditFlyoutAsync(FrameworkElement target, ShowSlideDeckItem item)
    {
        var project = _projects.Open(ResolveSlideDeckPresentationPath(item));
        var projectSlide = project.Slides.FirstOrDefault(slide =>
            string.Equals(slide.Id, item.Slide.Id, StringComparison.OrdinalIgnoreCase)) ?? item.Slide;
        var draft = await _quickEditTextLayers.BuildDraftAsync(project, projectSlide);
        var textLayers = draft.TextLayers.ToList();

        _activeQuickEditFlyout?.Hide();
        item.ResetTransientThumbnailPreview();

        var editorStack = new StackPanel { Spacing = 12 };
        var textBoxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);

        void RefreshPreview()
        {
            item.SetTransientThumbnailPreview(textLayers, BuildQuickEditUpdates(textBoxes));
            ScheduleQuickEditLivePreviewRefresh(item);
        }

        for (var index = 0; index < textLayers.Count; index++)
        {
            var layer = textLayers[index];
            var box = new TextBox
            {
                AcceptsReturn = true,
                MinHeight = 120,
                TextWrapping = TextWrapping.Wrap,
                Text = layer.Content,
            };
            box.TextChanged += (_, _) => RefreshPreview();
            editorStack.Children.Add(box);
            textBoxes[layer.Id] = box;
        }

        RefreshPreview();

        var applyButton = new Button
        {
            Content = "Apply",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                cancelButton,
                applyButton,
            },
        };

        var flyout = new Flyout
        {
            Placement = ResolveQuickEditFlyoutPlacement(target),
            Content = new Border
            {
                Width = 420,
                Padding = new Thickness(8),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new ScrollViewer
                        {
                            MaxHeight = 420,
                            Content = editorStack,
                        },
                        buttonRow,
                    },
                },
            },
        };

        var committed = false;
        cancelButton.Click += (_, _) => flyout.Hide();
        applyButton.Click += async (_, _) =>
        {
            committed = true;
            flyout.Hide();

            var result = await _slideItemActions.UpdateSlideAsync(
                ResolveSlideDeckPresentationPath(item),
                item.Slide.Id,
                (slide, mutationProject) =>
                {
                    _quickEditTextLayers.ApplyEdits(
                        mutationProject,
                        slide,
                        draft,
                        BuildQuickEditUpdates(textBoxes));
                });

            await ReloadSlideMutationAsync(result);
            Vm.StatusMessage = "Updated slide text.";
        };

        flyout.Closed += (_, _) =>
        {
            CancelQuickEditLivePreviewRefresh();
            item.CancelPendingThumbnailPreviewRefresh();
            if (!committed)
                item.ResetTransientThumbnailPreview();

            if (ReferenceEquals(_activeQuickEditFlyout, flyout))
                _activeQuickEditFlyout = null;
        };

        _activeQuickEditFlyout = flyout;
        flyout.ShowAt(target);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (textBoxes.Values.FirstOrDefault() is { } firstEditor)
                firstEditor.Focus(FocusState.Programmatic);
        });
    }

    private FlyoutPlacementMode ResolveQuickEditFlyoutPlacement(FrameworkElement target)
    {
        const double estimatedFlyoutHeight = 500;
        try
        {
            var targetTop = target.TransformToVisual(this).TransformPoint(new Point(0, 0)).Y;
            var targetBottom = targetTop + target.ActualHeight;
            var spaceBelow = ActualHeight - targetBottom;
            var spaceAbove = targetTop;

            return spaceBelow < estimatedFlyoutHeight && spaceAbove > spaceBelow
                ? FlyoutPlacementMode.TopEdgeAlignedLeft
                : FlyoutPlacementMode.BottomEdgeAlignedLeft;
        }
        catch (InvalidOperationException)
        {
            return FlyoutPlacementMode.BottomEdgeAlignedLeft;
        }
    }

    private async Task<ThemeSelection?> PromptForThemeSelectionAsync(PresentationProject project)
    {
        var choices = new List<ThemeChoice>();
        var globalThemes = await _themeLibrary.LoadAsync();
        choices.AddRange(globalThemes.Select(theme => new ThemeChoice(theme, theme.Name)));
        choices.AddRange(project.EmbeddedThemes
            .Where(entry => entry.Template != null)
            .Select(entry => new ThemeChoice(entry.Template!, $"{entry.Template!.Name} (Embedded)")));

        if (choices.Count == 0)
        {
            Vm.StatusMessage = "No themes are available for this slide.";
            return null;
        }

        var themeCombo = new ComboBox
        {
            Header = "Theme",
            DisplayMemberPath = nameof(ThemeChoice.Name),
            ItemsSource = choices,
            SelectedItem = choices[0],
        };
        var slideCombo = new ComboBox
        {
            Header = "Theme Slide",
            DisplayMemberPath = nameof(ThemeSlideChoice.Name),
        };

        void RefreshThemeSlides()
        {
            var selectedTheme = themeCombo.SelectedItem as ThemeChoice;
            var slides = selectedTheme?.Theme.Slides.Select((slide, index) =>
                    new ThemeSlideChoice(slide, slide.Name ?? $"Slide {index + 1}"))
                .ToList() ?? new List<ThemeSlideChoice>();
            slideCombo.ItemsSource = slides;
            slideCombo.SelectedItem = slides.FirstOrDefault();
        }

        themeCombo.SelectionChanged += (_, _) => RefreshThemeSlides();
        RefreshThemeSlides();

        var dialog = new ContentDialog
        {
            Title = "Apply Theme",
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    themeCombo,
                    slideCombo,
                },
            },
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || themeCombo.SelectedItem is not ThemeChoice themeChoice
            || slideCombo.SelectedItem is not ThemeSlideChoice slideChoice)
        {
            return null;
        }

        return new ThemeSelection(themeChoice.Theme, slideChoice.Slide);
    }

    private async Task<TransitionDialogResult> PromptForTransitionAsync(
        SlideTransition? currentTransition,
        string? dialogTitle = null)
    {
        var picker = new TransitionPickerDialogContent();
        // Initialize without a viewModel — favorites/recents are show-page concerns, not per-slide.
        picker.Initialize(null, currentTransition);

        var dialog = new ContentDialog
        {
            Title = dialogTitle ?? "Slide Transition Override",
            PrimaryButtonText = "Apply",
            SecondaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                Content = picker,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return new TransitionDialogResult(false, false, null);
        if (result == ContentDialogResult.Secondary)
            return new TransitionDialogResult(true, true, null);

        return new TransitionDialogResult(true, false, picker.BuildTransition());
    }

    private async Task<SelectionDialogResult> PromptForHotKeyAsync(string? currentHotKey)
    {
        var input = new TextBox
        {
            Header = "Hot Key",
            MaxLength = 1,
            PlaceholderText = "A-Z",
            Text = NormalizeHotKeyInput(currentHotKey) ?? string.Empty,
        };
        var helpText = new TextBlock
        {
            Text = "Enter one letter. Pressing that key on the Show page will take this slide live.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        var errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var dialog = new ContentDialog
        {
            Title = "Slide Hot Key",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    input,
                    helpText,
                    errorText,
                },
            },
            XamlRoot = XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (NormalizeHotKeyInput(input.Text) != null)
                return;

            errorText.Text = "Enter a single letter from A to Z.";
            errorText.Visibility = Visibility.Visible;
            args.Cancel = true;
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => new SelectionDialogResult(true, false, NormalizeHotKeyInput(input.Text)),
            ContentDialogResult.Secondary => new SelectionDialogResult(true, true, null),
            _ => new SelectionDialogResult(false, false, null),
        };
    }

    private static string? NormalizeHotKeyInput(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length != 1)
            return null;

        var c = char.ToUpperInvariant(trimmed[0]);
        return c is >= 'A' and <= 'Z' ? c.ToString() : null;
    }

    private async Task<SelectionDialogResult> PromptForTimerAssignmentAsync(string? currentTimerId)
    {
        var timers = (await _timers.GetTimersAsync()).ToList();
        var timerChoices = timers.Select(timer => new TimerChoice(timer)).ToList();
        var timerCombo = new ComboBox
        {
            Header = "Existing Timer",
            DisplayMemberPath = nameof(TimerChoice.Name),
            ItemsSource = timerChoices,
            SelectedItem = timerChoices.FirstOrDefault(choice => string.Equals(choice.Timer.Id, currentTimerId, StringComparison.OrdinalIgnoreCase)),
        };
        var newNameBox = new TextBox
        {
            Header = "Create New Timer",
            PlaceholderText = "Timer name",
        };
        var durationBox = new NumberBox
        {
            Header = "Duration (seconds)",
            Minimum = 0,
            Maximum = 7200,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = 60,
        };

        var dialog = new ContentDialog
        {
            Title = "Go to Next Timer",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    timerCombo,
                    newNameBox,
                    durationBox,
                },
            },
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return new SelectionDialogResult(false, false, null);
        if (result == ContentDialogResult.Secondary)
            return new SelectionDialogResult(true, true, null);

        var newName = NormalizeDialogValue(newNameBox.Text);
        if (!string.IsNullOrWhiteSpace(newName))
        {
            var timer = await _timers.SaveTimerAsync(new ShowTimerDefinition
            {
                Name = newName,
                DurationSeconds = (int)Math.Round(durationBox.Value),
            });
            return new SelectionDialogResult(true, false, timer.Id);
        }

        return new SelectionDialogResult(true, false, (timerCombo.SelectedItem as TimerChoice)?.Timer.Id);
    }

    private async Task<List<SlideActionDefinition>?> PromptForSlideActionsAsync(IReadOnlyList<SlideActionDefinition> currentActions)
    {
        var nextActions = currentActions
            .Select(action => PresentationModelUtilities.DeepClone(action) ?? new SlideActionDefinition())
            .ToList();
        var existingChoices = nextActions.Select(action => new ActionChoice(action)).ToList();
        var removeCombo = new ComboBox
        {
            Header = "Remove Existing Action",
            DisplayMemberPath = nameof(ActionChoice.Name),
            ItemsSource = existingChoices,
        };
        var addCombo = new ComboBox
        {
            Header = "Add Action",
            ItemsSource = new List<string> { "", "clearPresentation", "clearMedia", "blackoutOn", "blackoutOff", "clearAll" },
            SelectedIndex = 0,
        };
        var summary = new TextBlock
        {
            Text = nextActions.Count == 0
                ? "No actions are currently assigned to this slide."
                : string.Join(Environment.NewLine, nextActions.Select(action => $"- {GetSlideActionLabel(action)}")),
            TextWrapping = TextWrapping.Wrap,
        };

        var dialog = new ContentDialog
        {
            Title = "Slide Actions",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    summary,
                    removeCombo,
                    addCombo,
                },
            },
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        if (removeCombo.SelectedItem is ActionChoice removeChoice)
            nextActions.RemoveAll(action => string.Equals(action.Id, removeChoice.Action.Id, StringComparison.OrdinalIgnoreCase));

        var addType = NormalizeDialogValue(addCombo.SelectedItem as string);
        if (!string.IsNullOrWhiteSpace(addType))
            nextActions.Add(CreateSlideAction(addType));

        return nextActions;
    }

    private async Task<MediaConfigurationResult?> PromptForMediaConfigurationAsync(PresentationProject project, PresentationSlide slide)
    {
        var mediaChoices = project.Manifest.Media.Select(entry => new MediaChoice(entry)).ToList();
        var nonAudioChoices = mediaChoices.Where(choice => !string.Equals(choice.Entry.Type, "audio", StringComparison.OrdinalIgnoreCase)).ToList();
        var currentBackground = slide.Background;
        var currentBackgroundMediaId = currentBackground switch
        {
            ImageSlideBackground image => image.MediaId,
            VideoSlideBackground video => video.MediaId,
            _ => null,
        };

        var backgroundMode = new ComboBox
        {
            Header = "Slide Background",
            ItemsSource = new List<string> { "Keep current", "Solid Black", "Transparent", "Image Background", "Video Background" },
            SelectedItem = currentBackground switch
            {
                TransparentSlideBackground => "Transparent",
                ImageSlideBackground => "Image Background",
                VideoSlideBackground => "Video Background",
                _ => "Keep current",
            },
        };
        var backgroundMedia = new ComboBox
        {
            Header = "Background Media",
            DisplayMemberPath = nameof(MediaChoice.Name),
            ItemsSource = nonAudioChoices,
            SelectedItem = nonAudioChoices.FirstOrDefault(choice => string.Equals(choice.Entry.Id, currentBackgroundMediaId, StringComparison.OrdinalIgnoreCase)),
        };
        var backgroundFit = new ComboBox
        {
            Header = "Background Fit",
            ItemsSource = new List<string> { "cover", "contain", "fill", "none" },
            SelectedItem = currentBackground switch
            {
                ImageSlideBackground image => image.Fit,
                VideoSlideBackground video => video.Fit,
                _ => "cover",
            },
        };
        var backgroundOpacity = new NumberBox
        {
            Header = "Background Opacity",
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.05,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = currentBackground switch
            {
                ImageSlideBackground image => image.Opacity,
                VideoSlideBackground video => video.Opacity,
                _ => 1,
            },
        };
        var backgroundLoop = new ToggleSwitch
        {
            Header = "Loop Background Video",
            IsOn = currentBackground is VideoSlideBackground currentVideo && currentVideo.Loop,
        };
        var backgroundMuted = new ToggleSwitch
        {
            Header = "Mute Background Video",
            IsOn = currentBackground is not VideoSlideBackground currentVideoMuted || currentVideoMuted.Muted,
        };

        var existingCueChoices = GetMediaCues(slide)
            .Select(cue => new CueChoice(cue, ResolveCueDisplayName(cue, mediaChoices)))
            .ToList();
        var removeCueCombo = new ComboBox
        {
            Header = "Remove Existing Cue",
            DisplayMemberPath = nameof(CueChoice.Name),
            ItemsSource = existingCueChoices,
        };
        var newCueMedia = new ComboBox
        {
            Header = "Add Cue Media",
            DisplayMemberPath = nameof(MediaChoice.Name),
            ItemsSource = mediaChoices,
        };
        var newCueTarget = new ComboBox
        {
            Header = "Cue Target",
            ItemsSource = new List<string> { "mediaUnderlay", "mediaOverlay", "audio" },
            SelectedItem = "mediaUnderlay",
        };
        var newCueFit = new ComboBox
        {
            Header = "Cue Fit",
            ItemsSource = new List<string> { "cover", "contain", "fill", "none" },
            SelectedItem = "cover",
        };
        var cueLoop = new ToggleSwitch { Header = "Loop Cue Video", IsOn = true };
        var cueMuted = new ToggleSwitch { Header = "Mute Cue Video", IsOn = true };
        var cueAutoplay = new ToggleSwitch { Header = "Autoplay Cue", IsOn = true };

        var dialog = new ContentDialog
        {
            Title = "Slide Media Actions",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 520,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        backgroundMode,
                        backgroundMedia,
                        backgroundFit,
                        backgroundOpacity,
                        backgroundLoop,
                        backgroundMuted,
                        new TextBlock { Text = "Media Cues", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                        removeCueCombo,
                        newCueMedia,
                        newCueTarget,
                        newCueFit,
                        cueLoop,
                        cueMuted,
                        cueAutoplay,
                    },
                },
            },
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var cues = CloneMediaCues(slide);
        if (removeCueCombo.SelectedItem is CueChoice removeCue)
            cues.RemoveAll(cue => string.Equals(cue.Id, removeCue.Cue.Id, StringComparison.OrdinalIgnoreCase));

        if (newCueMedia.SelectedItem is MediaChoice cueMediaChoice)
        {
            cues.Add(new SlideMediaCue
            {
                Id = Guid.NewGuid().ToString("N"),
                MediaId = cueMediaChoice.Entry.Id,
                MediaType = cueMediaChoice.Entry.Type,
                Target = (newCueTarget.SelectedItem as string) ?? "mediaUnderlay",
                Fit = (newCueFit.SelectedItem as string) ?? "cover",
                Loop = cueMediaChoice.Entry.Type == "video" ? cueLoop.IsOn : null,
                Muted = cueMediaChoice.Entry.Type == "video" ? cueMuted.IsOn : null,
                Autoplay = cueMediaChoice.Entry.Type == "image" ? null : cueAutoplay.IsOn,
            });
        }

        var selectedBackgroundMode = backgroundMode.SelectedItem as string;
        SlideBackground? nextBackground = PresentationModelUtilities.DeepClone(currentBackground);
        if (string.Equals(selectedBackgroundMode, "Solid Black", StringComparison.Ordinal))
            nextBackground = new SolidSlideBackground { Color = "#000000" };
        else if (string.Equals(selectedBackgroundMode, "Transparent", StringComparison.Ordinal))
            nextBackground = new TransparentSlideBackground();
        else if (string.Equals(selectedBackgroundMode, "Image Background", StringComparison.Ordinal))
        {
            if (backgroundMedia.SelectedItem is not MediaChoice backgroundMediaChoice)
            {
                Vm.StatusMessage = "Choose background media before saving.";
                return null;
            }

            nextBackground = new ImageSlideBackground
            {
                MediaId = backgroundMediaChoice.Entry.Id,
                Fit = (backgroundFit.SelectedItem as string) ?? "cover",
                Opacity = backgroundOpacity.Value,
            };
        }
        else if (string.Equals(selectedBackgroundMode, "Video Background", StringComparison.Ordinal))
        {
            if (backgroundMedia.SelectedItem is not MediaChoice backgroundMediaChoice)
            {
                Vm.StatusMessage = "Choose background media before saving.";
                return null;
            }

            nextBackground = new VideoSlideBackground
            {
                MediaId = backgroundMediaChoice.Entry.Id,
                Fit = (backgroundFit.SelectedItem as string) ?? "cover",
                Opacity = backgroundOpacity.Value,
                Loop = backgroundLoop.IsOn,
                Muted = backgroundMuted.IsOn,
            };
        }

        return new MediaConfigurationResult(nextBackground, cues);
    }

    private static List<string> BuildHotKeyOptions()
    {
        var keys = new List<string>();
        keys.AddRange(Enumerable.Range(1, 12).Select(index => $"F{index}"));
        keys.AddRange(Enumerable.Range(0, 10).Select(index => $"Number{index}"));
        keys.AddRange(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()));
        return keys;
    }

    private static SlideActionDefinition CreateSlideAction(string type)
    {
        return new SlideActionDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Label = type switch
            {
                "clearPresentation" => "Clear Presentation",
                "clearMedia" => "Clear Media",
                "blackoutOn" => "Blackout On",
                "blackoutOff" => "Blackout Off",
                "clearAll" => "Clear All",
                _ => type,
            },
        };
    }

    private static string GetSlideActionLabel(SlideActionDefinition action) =>
        string.IsNullOrWhiteSpace(action.Label) ? action.Type : action.Label;

    private static string ResolveCueDisplayName(SlideMediaCue cue, IReadOnlyList<MediaChoice> mediaChoices)
    {
        var name = MediaCueDisplayNameResolver.Normalize(cue.DisplayName)
                   ?? mediaChoices.FirstOrDefault(choice => string.Equals(choice.Entry.Id, cue.MediaId, StringComparison.OrdinalIgnoreCase))?.Name
                   ?? MediaCueDisplayNameResolver.ResolveFallback(cue.MediaId);
        return $"{name} ({SlideMediaLayerBuilder.MapCueTarget(cue.Target)})";
    }

    private void SlideDeckItemMenu_Opening(object sender, object e)
    {
        _contextMenuSlideItem = null;
        _contextMenuSlideTarget = null;

        if (sender is not MenuFlyout flyout || !TryGetSlideDeckItem(sender, out var item))
            return;

        _contextMenuSlideItem = item;
        _contextMenuSlideTarget = flyout.Target as FrameworkElement;
        BuildSlideDeckContextMenu(flyout, item);
    }

    private void BuildSlideDeckContextMenu(MenuFlyout flyout, ShowSlideDeckItem item)
    {
        flyout.Items.Clear();

        flyout.Items.Add(CreateSlideMenuItem(
            "Quick Edit",
            string.Empty,
            VirtualKeyModifiers.Control,
            VirtualKey.E,
            true,
            () => ShowQuickEditForSlideAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem(
            "Edit Slide",
            "\uE70F",
            null,
            VirtualKey.F4,
            true,
            () => OpenSlideInEditorAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(CreateSlideMenuItem(
            item.Slide.Disabled ? "Enable" : "Disable",
            string.Empty,
            null,
            VirtualKey.None,
            true,
            () => ToggleSlideDisabledAsync(item)));
        flyout.Items.Add(BuildSlideDeckThemesMenu(item));
        flyout.Items.Add(CreateSlideMenuItem(
            "Transitions...",
            string.Empty,
            null,
            VirtualKey.None,
            true,
            () => EditSlideTransitionAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem(
            "Hot Key...",
            string.Empty,
            null,
            VirtualKey.None,
            true,
            () => EditSlideHotKeyAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem(
            "Go to Next Timer...",
            string.Empty,
            null,
            VirtualKey.None,
            true,
            () => EditTimerActionAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());

        var addActionMenu = new MenuFlyoutSubItem
        {
            Text = "Add Action",
        };
        BuildAddActionMenu(addActionMenu, item);
        flyout.Items.Add(addActionMenu);

        foreach (var dynamicItem in BuildExistingActionMenuItems(item))
            flyout.Items.Add(dynamicItem);
        if (item.Slide.Actions.Count > 0 || GetMediaCues(item.Slide).Count > 0)
            flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(BuildSlideGroupMenu(item));
        flyout.Items.Add(BuildSlideLabelMenu(item));
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(CreateSlideMenuItem(
            "Copy Text Style",
            string.Empty,
            VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift,
            VirtualKey.C,
            item.Slide.Layers.OfType<TextLayer>().Any(),
            () =>
            {
                _textStyleClipboard.SetFromSlide(item.Slide);
                Vm.StatusMessage = "Copied slide text style.";
                return Task.CompletedTask;
            }));
        flyout.Items.Add(CreateSlideMenuItem(
            "Paste Text Style",
            string.Empty,
            VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift,
            VirtualKey.V,
            _textStyleClipboard.HasEntries && item.Slide.Layers.OfType<TextLayer>().Any(),
            () => PasteSlideTextStyleAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(CreateSlideMenuItem("Cut", "\uE8C6", VirtualKeyModifiers.Control, VirtualKey.X, true, () => CutSlideAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem("Copy", "\uE8C8", VirtualKeyModifiers.Control, VirtualKey.C, true, () => CopySlideAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem("Paste", "\uE77F", VirtualKeyModifiers.Control, VirtualKey.V, _slideClipboard.HasSlide, () => PasteSlideAsync(item)));
        flyout.Items.Add(CreateSlideMenuItem("Delete", "\uE74D", null, VirtualKey.Delete, true, () => DeleteSlideAsync(item)));
    }

    private MenuFlyoutItem CreateSlideMenuItem(
        string text,
        string iconGlyph,
        VirtualKeyModifiers? acceleratorModifiers,
        VirtualKey acceleratorKey,
        bool isEnabled,
        Func<Task> execute)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            IsEnabled = isEnabled,
            Icon = string.IsNullOrWhiteSpace(iconGlyph)
                ? null
                : new FontIcon { Glyph = iconGlyph },
        };

        if (acceleratorKey != VirtualKey.None)
        {
            var accelerator = new KeyboardAccelerator
            {
                Key = acceleratorKey,
                Modifiers = acceleratorModifiers ?? VirtualKeyModifiers.None,
                ScopeOwner = KeyboardFocusTarget,
            };
            accelerator.Invoked += async (_, args) =>
            {
                if (!item.IsEnabled)
                    return;

                await ExecuteHandledActionAsync(execute, $"Could not complete \"{text}\".");
                args.Handled = true;
            };
            item.KeyboardAccelerators.Add(accelerator);
        }

        item.Click += async (_, _) =>
        {
            if (item.IsEnabled)
                await ExecuteHandledActionAsync(execute, $"Could not complete \"{text}\".");
        };
        return item;
    }

    private static MenuFlyoutItem CreateDisabledSlideMenuItem(string text) =>
        new()
        {
            Text = text,
            IsEnabled = false,
        };

    private MenuFlyoutSubItem BuildSlideDeckThemesMenu(ShowSlideDeckItem item)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Themes",
        };
        menu.Items.Add(CreateDisabledSlideMenuItem("Loading themes..."));
        _ = PopulateSlideThemeMenuAsync(menu, item);
        return menu;
    }

    private async Task PopulateSlideThemeMenuAsync(MenuFlyoutSubItem menu, ShowSlideDeckItem item)
    {
        try
        {
            var presentationPath = ResolveSlideDeckPresentationPath(item);
            var project = _projects.Open(presentationPath);
            var globalThemes = (await _themeLibrary.LoadAsync()).ToList();
            var choices = BuildThemeMenuChoices(project, globalThemes);

            menu.Items.Clear();
            if (choices.Count == 0)
                menu.Items.Add(CreateNoThemesAvailableMenuItem());

            AddRecentThemeMenuItems(menu, item, choices);
            AddTopLevelThemeMenuItems(menu, item, choices);
            AddFolderThemeMenuItems(menu, item, choices);

            if (menu.Items.Count > 0)
                menu.Items.Add(new MenuFlyoutSeparator());

            var applyMediaActionsItem = new ToggleMenuFlyoutItem
            {
                Text = "Apply Media Actions with Theme Slide",
                IsChecked = _settings.Settings.Show.ApplyMediaActionsWithThemeSlide,
            };
            applyMediaActionsItem.Click += async (_, _) =>
            {
                _settings.Update(settings => settings.Show.ApplyMediaActionsWithThemeSlide = applyMediaActionsItem.IsChecked);
                await _settings.SaveAsync();
            };
            menu.Items.Add(applyMediaActionsItem);

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CreateSlideMenuItem("New Theme...", string.Empty, null, VirtualKey.None, true, () => CreateBlankThemeAsync(item)));
            menu.Items.Add(CreateSlideMenuItem("New Theme from Selection", string.Empty, null, VirtualKey.None, true, () => CreateThemeFromSelectionAsync(item)));
        }
        catch (Exception ex)
        {
            menu.Items.Clear();
            menu.Items.Add(CreateDisabledSlideMenuItem($"Could not load themes: {ex.Message}"));
        }
    }

    private List<ThemeMenuChoice> BuildThemeMenuChoices(PresentationProject project, IReadOnlyList<ThemeTemplate> globalThemes)
    {
        var choices = globalThemes
            .Select(theme => new ThemeMenuChoice(theme, theme.Name, theme.Folder, IsEmbedded: false))
            .ToList();

        choices.AddRange(project.EmbeddedThemes
            .Where(entry => entry.Template != null)
            .Select(entry => new ThemeMenuChoice(entry.Template!, $"{entry.Template!.Name} (Embedded)", entry.Template!.Folder, IsEmbedded: true)));

        return choices
            .Where(choice => !string.IsNullOrWhiteSpace(choice.Theme.Id) && !string.IsNullOrWhiteSpace(choice.Theme.Name))
            .OrderBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void AddRecentThemeMenuItems(MenuFlyoutSubItem menu, ShowSlideDeckItem item, IReadOnlyList<ThemeMenuChoice> choices)
    {
        var recentChoices = _settings.Settings.Show.RecentThemeIds
            .Select(id => choices.FirstOrDefault(choice => string.Equals(choice.Theme.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(choice => choice != null)
            .Cast<ThemeMenuChoice>()
            .Take(MaxTopLevelThemeMenuItems)
            .ToList();
        if (recentChoices.Count == 0)
            return;

        menu.Items.Add(CreateDisabledSlideMenuItem("Recents"));
        foreach (var choice in recentChoices)
            menu.Items.Add(BuildThemeChoiceMenu(item, choice));
        menu.Items.Add(new MenuFlyoutSeparator());
    }

    private void AddTopLevelThemeMenuItems(MenuFlyoutSubItem menu, ShowSlideDeckItem item, IReadOnlyList<ThemeMenuChoice> choices)
    {
        var topLevel = choices
            .Where(choice => string.IsNullOrWhiteSpace(choice.Folder))
            .Take(MaxTopLevelThemeMenuItems)
            .ToList();
        foreach (var choice in topLevel)
            menu.Items.Add(BuildThemeChoiceMenu(item, choice));

        var hasFolderChoices = choices.Any(choice => !string.IsNullOrWhiteSpace(choice.Folder));
        if (topLevel.Count > 0 && hasFolderChoices)
            menu.Items.Add(new MenuFlyoutSeparator());
    }

    private void AddFolderThemeMenuItems(MenuFlyoutSubItem menu, ShowSlideDeckItem item, IReadOnlyList<ThemeMenuChoice> choices)
    {
        foreach (var folderGroup in choices
                     .Where(choice => !string.IsNullOrWhiteSpace(choice.Folder))
                     .GroupBy(choice => choice.Folder!.Trim(), StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var folderMenu = new MenuFlyoutSubItem
            {
                Text = folderGroup.Key,
            };
            foreach (var choice in folderGroup.OrderBy(choice => choice.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                folderMenu.Items.Add(BuildThemeChoiceMenu(item, choice));
            menu.Items.Add(folderMenu);
        }
    }

    private MenuFlyoutSubItem BuildThemeChoiceMenu(ShowSlideDeckItem item, ThemeMenuChoice choice)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = choice.DisplayName,
        };

        var slides = choice.Theme.Slides.Count == 0
            ? new List<ThemeTemplateSlide> { new() { Id = string.Empty, Name = "Default", Background = new SolidSlideBackground { Color = "#000000" } } }
            : choice.Theme.Slides;
        foreach (var slide in slides)
        {
            var slideName = string.IsNullOrWhiteSpace(slide.Name)
                ? $"Theme Slide {choice.Theme.Slides.IndexOf(slide) + 1}"
                : slide.Name!;
            menu.Items.Add(CreateSlideMenuItem(slideName, string.Empty, null, VirtualKey.None, true, () => ApplyThemeToSlideAsync(item, choice, slide)));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateSlideMenuItem("Edit Theme...", string.Empty, null, VirtualKey.None, true, () => EditThemeAsync(choice)));
        menu.Items.Add(CreateSlideMenuItem("Add Selected Slide to Theme", string.Empty, null, VirtualKey.None, true, () => AddSelectedSlideToThemeAsync(item, choice)));
        return menu;
    }

    private MenuFlyoutSubItem BuildSlideGroupMenu(ShowSlideDeckItem item)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Group",
        };

        foreach (var (section, label) in new[]
                 {
                     ("title", "Title"),
                     ("intro", "Intro"),
                     ("verse", "Verse"),
                     ("pre-chorus", "Pre-Chorus"),
                     ("chorus", "Chorus"),
                     ("bridge", "Bridge"),
                     ("tag", "Tag"),
                     ("outro", "Outro"),
                 })
        {
            menu.Items.Add(CreateSlideGroupMenuItem(item, section, label));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateSlideMenuItem("Clear Group", string.Empty, null, VirtualKey.None, !string.IsNullOrWhiteSpace(item.Slide.Section) || !string.IsNullOrWhiteSpace(item.Slide.SectionLabel), () => ClearSlideGroupAsync(item)));
        return menu;
    }

    private MenuFlyoutItem CreateSlideGroupMenuItem(ShowSlideDeckItem item, string section, string label)
    {
        var menuItem = CreateSlideMenuItem(label, string.Empty, null, VirtualKey.None, true, () => SetSlideGroupAsync(item, section));
        var color = ParseMenuColor(SlideGroupThumbnailColors.GetHexColorForSlide(new PresentationSlide
        {
            Section = section,
            SectionLabel = label,
            Type = "content",
        }));
        menuItem.Icon = null;
        menuItem.Style = (Style)Resources["SlideGroupMenuFlyoutItemStyle"];
        menuItem.Tag = new SlideGroupMenuChoice(label, new SolidColorBrush(color));
        return menuItem;
    }

    private MenuFlyoutItem CreateNoThemesAvailableMenuItem() =>
        new()
        {
            Text = "No themes available",
            Tag = "No themes available",
            IsEnabled = false,
            Style = (Style)Resources["SlideContextNoThemeMenuFlyoutItemStyle"],
        };

    private MenuFlyoutSubItem BuildSlideLabelMenu(ShowSlideDeckItem item)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Label",
        };
        menu.Items.Add(CreateSlideMenuItem("Edit Label...", string.Empty, null, VirtualKey.None, true, () => EditSlideLabelAsync(item)));
        menu.Items.Add(CreateSlideMenuItem("Clear Label", string.Empty, null, VirtualKey.None, !string.IsNullOrWhiteSpace(item.Slide.SectionLabel), () => ClearSlideLabelAsync(item)));
        return menu;
    }

    private static Windows.UI.Color ParseMenuColor(string hex)
    {
        hex = (hex ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length != 6)
            return Windows.UI.Color.FromArgb(255, 100, 116, 139);

        return Windows.UI.Color.FromArgb(
            255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    private void BuildAddActionMenu(MenuFlyoutSubItem addActionMenu, ShowSlideDeckItem item)
    {
        addActionMenu.Items.Clear();
        addActionMenu.Items.Add(CreateAddActionCategory("Clear",
        [
            CreateMenuItem("Clear Presentation", async () => await AddSlideActionAsync(item, "clearPresentation", "Clear Presentation")),
            CreateMenuItem("Clear Media", async () => await AddSlideActionAsync(item, "clearMedia", "Clear Media")),
            CreateMenuItem("Clear All", async () => await AddSlideActionAsync(item, "clearAll", "Clear All")),
        ]));
        addActionMenu.Items.Add(CreateAddActionCategory("Audience Look",
        [
            CreateMenuItem("Blackout On", async () => await AddSlideActionAsync(item, "blackoutOn", "Blackout On")),
            CreateMenuItem("Blackout Off", async () => await AddSlideActionAsync(item, "blackoutOff", "Blackout Off")),
        ]));
        addActionMenu.Items.Add(CreateAddActionCategory("Timer",
        [
            CreateMenuItem("Go to Next Timer...", async () => await EditTimerActionAsync(item)),
        ]));
        addActionMenu.Items.Add(CreateAddActionCategory("Video Input",
        [
            CreateMenuItem("Media Action...", async () => await EditSlideMediaActionsAsync(item)),
        ]));
    }

    private List<MenuFlyoutItemBase> BuildExistingActionMenuItems(ShowSlideDeckItem item)
    {
        var items = new List<MenuFlyoutItemBase>();

        foreach (var action in item.Slide.Actions)
        {
            var label = GetSlideActionLabel(action);
            var editMenu = new MenuFlyoutSubItem { Text = $"Edit Action: {label}" };
            editMenu.Items.Add(CreateMenuItem($"Remove Action: {label}", async () => await RemoveSlideActionAsync(item, action.Id, label)));
            items.Add(editMenu);
            items.Add(CreateMenuItem($"Remove Action: {label}", async () => await RemoveSlideActionAsync(item, action.Id, label)));
        }

        foreach (var cue in GetMediaCues(item.Slide))
        {
            var label = ResolveMediaCueActionLabel(item, cue);
            var editMenu = new MenuFlyoutSubItem { Text = $"Edit Action: {label}" };
            editMenu.Items.Add(CreateMenuItem("Inspector...", async () => await EditSlideMediaActionsAsync(item)));
            editMenu.Items.Add(CreateMediaCueTargetMenu(item, cue));
            editMenu.Items.Add(CreateMediaCueFitMenu(item, cue));
            editMenu.Items.Add(CreateMediaCueToggleItem("Playback Behavior", cue.Autoplay ?? false, async isChecked =>
                await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Autoplay = isChecked, $"Updated playback behavior for \"{label}\".")));
            editMenu.Items.Add(CreateMediaCueToggleItem("Loop Behavior", cue.Loop ?? false, async isChecked =>
                await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Loop = isChecked, $"Updated loop behavior for \"{label}\".")));
            editMenu.Items.Add(CreateMenuItem("Open File Location", () => OpenMediaCueFileLocation(item, cue)));
            editMenu.Items.Add(CreateMenuItem($"Remove Action: {label}", async () => await RemoveMediaCueAsync(item, cue.Id, label)));
            items.Add(editMenu);
            items.Add(CreateMenuItem($"Remove Action: {label}", async () => await RemoveMediaCueAsync(item, cue.Id, label)));
        }

        if (GetMediaCues(item.Slide).Count > 0)
            items.Add(CreateMenuItem("Inspector...", async () => await EditSlideMediaActionsAsync(item)));

        return items;
    }

    private MenuFlyoutSubItem CreateMediaCueTargetMenu(ShowSlideDeckItem item, SlideMediaCue cue)
    {
        var menu = new MenuFlyoutSubItem { Text = "Behavior" };
        menu.Items.Add(CreateMenuItem("Media Underlay", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Target = "mediaUnderlay", "Updated media action behavior.")));
        menu.Items.Add(CreateMenuItem("Media Overlay", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Target = "mediaOverlay", "Updated media action behavior.")));
        menu.Items.Add(CreateMenuItem("Audio", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Target = "audio", "Updated media action behavior.")));
        return menu;
    }

    private MenuFlyoutSubItem CreateMediaCueFitMenu(ShowSlideDeckItem item, SlideMediaCue cue)
    {
        var menu = new MenuFlyoutSubItem { Text = "Scaling" };
        menu.Items.Add(CreateMenuItem("Cover", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Fit = "cover", "Updated media action scaling.")));
        menu.Items.Add(CreateMenuItem("Contain", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Fit = "contain", "Updated media action scaling.")));
        menu.Items.Add(CreateMenuItem("Fill", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Fit = "fill", "Updated media action scaling.")));
        menu.Items.Add(CreateMenuItem("None", async () =>
            await UpdateMediaCueAsync(item, cue.Id, targetCue => targetCue.Fit = "none", "Updated media action scaling.")));
        return menu;
    }

    private MenuFlyoutSubItem CreateAddActionCategory(string text, IEnumerable<MenuFlyoutItemBase> children)
    {
        var menu = new MenuFlyoutSubItem { Text = text };
        foreach (var child in children)
            menu.Items.Add(child);
        return menu;
    }

    private MenuFlyoutItem CreateMenuItem(string text, Action onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            try
            {
                onClick();
            }
            catch (Exception ex)
            {
                Vm.StatusMessage = $"Could not complete \"{text}\": {ex.Message}";
            }
        };
        return item;
    }

    private MenuFlyoutItem CreateMenuItem(string text, Func<Task> onClick)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (_, _) =>
        {
            try
            {
                await onClick();
            }
            catch (Exception ex)
            {
                Vm.StatusMessage = $"Could not complete \"{text}\": {ex.Message}";
            }
        };
        return item;
    }

    private ToggleMenuFlyoutItem CreateMediaCueToggleItem(string text, bool isChecked, Func<bool, Task> onClick)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text,
            IsChecked = isChecked,
        };
        item.Click += async (_, _) =>
        {
            try
            {
                await onClick(item.IsChecked);
            }
            catch (Exception ex)
            {
                Vm.StatusMessage = $"Could not complete \"{text}\": {ex.Message}";
            }
        };
        return item;
    }

    private static int FindTopLevelMenuItemIndex(MenuFlyout flyout, string text)
    {
        for (var index = 0; index < flyout.Items.Count; index++)
        {
            switch (flyout.Items[index])
            {
                case MenuFlyoutSubItem subItem when string.Equals(subItem.Text, text, StringComparison.Ordinal):
                    return index;
                case MenuFlyoutItem menuItem when string.Equals(menuItem.Text, text, StringComparison.Ordinal):
                    return index;
            }
        }

        return -1;
    }

    private Task ShowQuickEditForSlideAsync(ShowSlideDeckItem item)
    {
        if (_contextMenuSlideTarget == null)
            return Task.CompletedTask;

        return ShowQuickEditFlyoutAsync(_contextMenuSlideTarget, item);
    }

    private Task OpenSlideInEditorAsync(ShowSlideDeckItem item) =>
        OpenPresentationInEditorAsync(
            ResolveSlideDeckPresentationPath(item),
            Vm.SelectedLibraryId,
            Vm.SelectedPlaylistId,
            item.Slide.Id);

    private async Task ToggleSlideDisabledAsync(ShowSlideDeckItem item)
    {
        var wasDisabled = item.Slide.Disabled;
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.Disabled = !slide.Disabled);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = wasDisabled ? "Enabled slide." : "Disabled slide.";
    }

    private async Task ApplyThemeToSlideAsync(ShowSlideDeckItem item, ThemeMenuChoice choice, ThemeTemplateSlide themeSlide)
    {
        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var applyMediaActions = _settings.Settings.Show.ApplyMediaActionsWithThemeSlide;
        var result = await _slideItemActions.UpdateSlideAsync(
            presentationPath,
            item.Slide.Id,
            (slide, targetProject) =>
            {
                var preservedBackground = PresentationModelUtilities.DeepClone(slide.Background);
                var preservedCues = slide.MediaCues
                    .Select(cue => PresentationModelUtilities.DeepClone(cue) ?? new SlideMediaCue())
                    .ToList();

                _themeApplier.ApplyThemeSlideToSlide(
                    slide,
                    themeSlide,
                    new ThemeApplyOptions
                    {
                        ScaleMode = "fit",
                        SourceSize = choice.Theme.BaseSize,
                        TargetSize = targetProject.Manifest.SlideSize ?? PresentationModelUtilities.GetBaseSlideSize(targetProject.Manifest.AspectRatio),
                    });

                if (!applyMediaActions)
                {
                    slide.Background = preservedBackground;
                    slide.MediaCues = preservedCues;
                }
            });

        await RecordRecentThemeAsync(choice.Theme.Id);
        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = $"Applied theme \"{choice.Theme.Name}\".";
    }

    private async Task RecordRecentThemeAsync(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return;

        _settings.Update(settings =>
        {
            settings.Show.RecentThemeIds.RemoveAll(id => string.Equals(id, themeId, StringComparison.OrdinalIgnoreCase));
            settings.Show.RecentThemeIds.Insert(0, themeId);
            if (settings.Show.RecentThemeIds.Count > MaxTopLevelThemeMenuItems)
                settings.Show.RecentThemeIds.RemoveRange(MaxTopLevelThemeMenuItems, settings.Show.RecentThemeIds.Count - MaxTopLevelThemeMenuItems);
        });
        await _settings.SaveAsync();
    }

    private async Task CreateBlankThemeAsync(ShowSlideDeckItem item)
    {
        var name = await PromptForNameAsync("New Theme", "Theme name", "New Theme");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var now = DateTime.UtcNow.ToString("O");
        var aspectRatio = project.Manifest.AspectRatio ?? "16:9";
        var theme = new ThemeTemplate
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            AspectRatio = aspectRatio,
            BaseSize = PresentationModelUtilities.GetBaseSlideSize(aspectRatio),
            Slides =
            {
                new ThemeTemplateSlide
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Default",
                    Background = new SolidSlideBackground { Color = "#000000" },
                },
            },
        };

        await _themeLibrary.SaveThemeAsync(theme);
        await RecordRecentThemeAsync(theme.Id);
        Vm.StatusMessage = $"Created theme \"{theme.Name}\".";
    }

    private async Task CreateThemeFromSelectionAsync(ShowSlideDeckItem item)
    {
        var defaultName = string.IsNullOrWhiteSpace(item.Slide.SectionLabel) ? "Selected Slide Theme" : item.Slide.SectionLabel!;
        var name = await PromptForNameAsync("New Theme from Selection", "Theme name", defaultName);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var theme = PresentationModelUtilities.CreateThemeFromSlide(name, item.Slide, project.Manifest.AspectRatio ?? "16:9");
        await _themeLibrary.SaveThemeAsync(theme);
        await RecordRecentThemeAsync(theme.Id);
        Vm.StatusMessage = $"Created theme \"{theme.Name}\" from slide {item.Ordinal}.";
    }

    private async Task AddSelectedSlideToThemeAsync(ShowSlideDeckItem item, ThemeMenuChoice choice)
    {
        var themeSlide = PresentationModelUtilities.CreateThemeSlideFromSlide(item.Slide);
        if (choice.IsEmbedded)
        {
            var presentationPath = ResolveSlideDeckPresentationPath(item);
            var project = _projects.Open(presentationPath);
            var embedded = project.EmbeddedThemes.FirstOrDefault(entry =>
                string.Equals(entry.Template?.Id, choice.Theme.Id, StringComparison.OrdinalIgnoreCase));
            if (embedded?.Template == null)
                return;

            embedded.Template.Slides.Add(themeSlide);
            project.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");
            _projects.Save(project, presentationPath);
            await Vm.OpenPresentationFromPathAsync(presentationPath);
        }
        else
        {
            var theme = await _themeLibrary.LoadThemeAsync(choice.Theme.Id) ?? choice.Theme;
            theme.Slides.Add(themeSlide);
            await _themeLibrary.SaveThemeAsync(theme);
        }

        await RecordRecentThemeAsync(choice.Theme.Id);
        Vm.StatusMessage = $"Added slide {item.Ordinal} to theme \"{choice.Theme.Name}\".";
    }

    private Task EditThemeAsync(ThemeMenuChoice choice)
    {
        if (App.MainWindow is MainWindow window)
            window.NavigateToThemeLibraryPage();

        Vm.StatusMessage = $"Opened Theme Library to edit \"{choice.Theme.Name}\".";
        return Task.CompletedTask;
    }

    private async Task EditSlideTransitionAsync(ShowSlideDeckItem item)
    {
        var transitionResult = await PromptForTransitionAsync(item.Slide.Animations?.Transition);
        if (!transitionResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Animations ??= new SlideAnimations();
                slide.Animations.Transition = transitionResult.ClearRequested
                    ? null
                    : TransitionStorageNormalizer.NormalizeForStorage(transitionResult.Transition);
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = transitionResult.ClearRequested
            ? "Cleared slide transition override."
            : "Updated slide transition override.";
    }

    private async Task EditSlideHotKeyAsync(ShowSlideDeckItem item)
    {
        var hotKeyResult = await PromptForHotKeyAsync(item.Slide.HotKey);
        if (!hotKeyResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.HotKey = hotKeyResult.ClearRequested ? null : hotKeyResult.Value);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = hotKeyResult.ClearRequested ? "Cleared slide hot key." : $"Assigned hot key {hotKeyResult.Value}.";
    }

    private async Task SetSlideGroupAsync(ShowSlideDeckItem item, string section)
    {
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Section = section;
                slide.SectionLabel = PresentationModelUtilities.FormatSectionLabel(section, slide.SectionIndex);
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = $"Assigned slide to {PresentationModelUtilities.FormatSectionLabel(section)}.";
    }

    private async Task ClearSlideGroupAsync(ShowSlideDeckItem item)
    {
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Section = null;
                slide.SectionLabel = null;
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Cleared slide group.";
    }

    private async Task EditSlideLabelAsync(ShowSlideDeckItem item)
    {
        var label = await PromptForNameAsync("Slide Label", "Label", item.Slide.SectionLabel ?? string.Empty);
        if (label == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.SectionLabel = label);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Updated slide label.";
    }

    private async Task ClearSlideLabelAsync(ShowSlideDeckItem item)
    {
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.SectionLabel = null);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Cleared slide label.";
    }

    private async Task PasteSlideTextStyleAsync(ShowSlideDeckItem item)
    {
        if (!_textStyleClipboard.HasEntries)
            return;

        var entries = _textStyleClipboard.Entries.ToList();
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, project) =>
            {
                var targetTextLayers = slide.Layers.OfType<TextLayer>().ToList();
                for (var index = 0; index < targetTextLayers.Count && index < entries.Count; index++)
                {
                    var targetLayer = targetTextLayers[index];
                    var source = entries[index];
                    targetLayer.Style = PresentationModelUtilities.DeepClone(source.Style) ?? PresentationModelUtilities.CreateDefaultTextStyle();
                    targetLayer.Fills = source.Fills.Select(fill => PresentationModelUtilities.DeepClone(fill) ?? new LayerFillModel()).ToList();
                    targetLayer.Strokes = source.Strokes.Select(stroke => PresentationModelUtilities.DeepClone(stroke) ?? new LayerStrokeModel()).ToList();
                    targetLayer.Effects = source.Effects.Select(effect => PresentationModelUtilities.DeepClone(effect) ?? new LayerBlurEffectModel()).ToList();
                    targetLayer.Padding = source.Padding;
                    PresentationModelUtilities.NormalizeLayer(targetLayer, project.Manifest.SlideSize);
                }
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Pasted slide text style.";
    }

    private Task CutSlideAsync(ShowSlideDeckItem item)
    {
        _slideClipboard.SetCut(ResolveSlideDeckPresentationPath(item), item.Slide);
        Vm.StatusMessage = $"Cut slide {item.Ordinal}.";
        return Task.CompletedTask;
    }

    private Task CopySlideAsync(ShowSlideDeckItem item)
    {
        _slideClipboard.SetCopy(ResolveSlideDeckPresentationPath(item), item.Slide);
        Vm.StatusMessage = $"Copied slide {item.Ordinal}.";
        return Task.CompletedTask;
    }

    private async Task PasteSlideAsync(ShowSlideDeckItem item)
    {
        if (_slideClipboard.Entry == null)
            return;

        var result = await _slideItemActions.PasteSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            _slideClipboard.Entry,
            SlidePastePosition.After);

        if (_slideClipboard.Entry.IsCut)
            _slideClipboard.Clear();

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Pasted slide.";
    }

    private async Task DeleteSlideAsync(ShowSlideDeckItem item)
    {
        if (!await ConfirmAsync("Delete Slide", $"Delete slide {item.Ordinal} from this presentation?", "Delete"))
            return;

        var result = await _slideItemActions.DeleteSlideAsync(ResolveSlideDeckPresentationPath(item), item.Slide.Id);
        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Deleted slide.";
    }

    private async void SlideDeckQuickEdit_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckTarget(sender, out var target, out var item))
            return;

        await ShowQuickEditFlyoutAsync(target, item);
    }

    private async void SlideDeckEditSlide_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        await OpenPresentationInEditorAsync(
            ResolveSlideDeckPresentationPath(item),
            Vm.SelectedLibraryId,
            Vm.SelectedPlaylistId,
            item.Slide.Id);
    }

    private async void SlideDeckToggleDisable_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.Disabled = !slide.Disabled);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = item.Slide.Disabled ? "Enabled slide." : "Disabled slide.";
    }

    private async void SlideDeckApplyTheme_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var selection = await PromptForThemeSelectionAsync(project);
        if (selection == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            presentationPath,
            item.Slide.Id,
            (slide, targetProject) =>
            {
                _themeApplier.ApplyThemeSlideToSlide(
                    slide,
                    selection.Slide,
                    new ThemeApplyOptions
                    {
                        ScaleMode = "fit",
                        SourceSize = selection.Theme.BaseSize,
                        TargetSize = targetProject.Manifest.SlideSize ?? PresentationModelUtilities.GetBaseSlideSize(targetProject.Manifest.AspectRatio),
                    });
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = $"Applied theme \"{selection.Theme.Name}\".";
    }

    private async void SlideDeckEditTransition_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var transitionResult = await PromptForTransitionAsync(item.Slide.Animations?.Transition);
        if (!transitionResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Animations ??= new SlideAnimations();
                slide.Animations.Transition = transitionResult.ClearRequested
                    ? null
                    : TransitionStorageNormalizer.NormalizeForStorage(transitionResult.Transition);
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = transitionResult.ClearRequested
            ? "Cleared slide transition override."
            : "Updated slide transition override.";
    }

    private async void SlideDeckEditHotKey_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var hotKeyResult = await PromptForHotKeyAsync(item.Slide.HotKey);
        if (!hotKeyResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.HotKey = hotKeyResult.ClearRequested ? null : hotKeyResult.Value);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = hotKeyResult.ClearRequested ? "Cleared slide hot key." : $"Assigned hot key {hotKeyResult.Value}.";
    }

    private async void SlideDeckEditTimer_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        await EditTimerActionAsync(item);
    }

    private async void SlideDeckManageActions_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var actions = await PromptForSlideActionsAsync(item.Slide.Actions);
        if (actions == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.Actions = actions.Select(action => PresentationModelUtilities.DeepClone(action) ?? new SlideActionDefinition()).ToList());

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Updated slide actions.";
    }

    private async void SlideDeckAddClearPresentationAction_Click(object sender, RoutedEventArgs e) =>
        await AddSlideActionAsync(sender, "clearPresentation", "Clear Presentation");

    private async void SlideDeckAddClearMediaAction_Click(object sender, RoutedEventArgs e) =>
        await AddSlideActionAsync(sender, "clearMedia", "Clear Media");

    private async void SlideDeckAddBlackoutOnAction_Click(object sender, RoutedEventArgs e) =>
        await AddSlideActionAsync(sender, "blackoutOn", "Blackout On");

    private async void SlideDeckAddBlackoutOffAction_Click(object sender, RoutedEventArgs e) =>
        await AddSlideActionAsync(sender, "blackoutOff", "Blackout Off");

    private async Task AddSlideActionAsync(object sender, string type, string label)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        await AddSlideActionAsync(item, type, label);
    }

    private async Task AddSlideActionAsync(ShowSlideDeckItem item, string type, string label)
    {
        ArgumentNullException.ThrowIfNull(item);

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Actions ??= new List<SlideActionDefinition>();
                slide.Actions.Add(new SlideActionDefinition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = type,
                    Label = label,
                });
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = $"Added slide action \"{label}\".";
    }

    private async void SlideDeckEditMediaActions_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        await ExecuteHandledActionAsync(
            () => EditSlideMediaActionsAsync(item),
            "Could not update slide media actions.");
    }

    private async Task EditSlideMediaActionsAsync(ShowSlideDeckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        var mediaResult = await PromptForMediaConfigurationAsync(project, item.Slide);
        if (mediaResult == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            presentationPath,
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Background = PresentationModelUtilities.DeepClone(mediaResult.Background);
                slide.MediaCues = mediaResult.Cues.Select(cue => PresentationModelUtilities.DeepClone(cue) ?? new SlideMediaCue()).ToList();
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Updated slide media actions.";
    }

    private async Task EditTimerActionAsync(ShowSlideDeckItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var timerResult = await PromptForTimerAssignmentAsync(item.Slide.GoToNextTimerId);
        if (!timerResult.Submitted)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.GoToNextTimerId = timerResult.ClearRequested ? null : timerResult.Value);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = timerResult.ClearRequested ? "Cleared slide timer action." : "Updated slide timer action.";
    }

    private async Task RemoveSlideActionAsync(ShowSlideDeckItem item, string actionId, string label)
    {
        ArgumentNullException.ThrowIfNull(item);

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.Actions.RemoveAll(action => string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase)));

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = $"Removed action \"{label}\".";
    }

    private async Task UpdateMediaCueAsync(
        ShowSlideDeckItem item,
        string cueId,
        Action<SlideMediaCue> mutate,
        string statusMessage)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(mutate);

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.MediaCues ??= new List<SlideMediaCue>();
                var cue = slide.MediaCues.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, cueId, StringComparison.OrdinalIgnoreCase));
                if (cue != null)
                    mutate(cue);
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = statusMessage;
    }

    private async Task RemoveMediaCueAsync(ShowSlideDeckItem item, string cueId, string label)
    {
        ArgumentNullException.ThrowIfNull(item);
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.MediaCues ??= new List<SlideMediaCue>();
                slide.MediaCues.RemoveAll(cue => string.Equals(cue.Id, cueId, StringComparison.OrdinalIgnoreCase));
            });
        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = $"Removed action \"{label}\".";
    }

    private IReadOnlyList<SlideMediaCue> GetMediaCues(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return slide.MediaCues ?? [];
    }

    private static List<SlideMediaCue> CloneMediaCues(PresentationSlide slide)
    {
        ArgumentNullException.ThrowIfNull(slide);
        return (slide.MediaCues ?? [])
            .Select(cue => PresentationModelUtilities.DeepClone(cue) ?? new SlideMediaCue())
            .ToList();
    }

    private async Task ExecuteHandledActionAsync(Func<Task> action, string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{FailureMessage}", failureMessage);
            Vm.StatusMessage = $"{failureMessage} {ex.Message}";
        }
    }

    private void DismissSlideContextMenu()
    {
        try
        {
            _contextMenuSlideTarget?.ContextFlyout?.Hide();
        }
        catch
        {
            // Best-effort only; the flyout may already be closing.
        }

        _contextMenuSlideItem = null;
        _contextMenuSlideTarget = null;
    }

    private void OpenMediaCueFileLocation(ShowSlideDeckItem item, SlideMediaCue cue)
    {
        try
        {
            var presentationPath = ResolveSlideDeckPresentationPath(item);
            var project = _projects.Open(presentationPath);
            var resolvedPath = _assetCache.ResolveMediaPath(project, cue.MediaId);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                Vm.StatusMessage = "Could not resolve the media file location.";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{resolvedPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Vm.StatusMessage = $"Could not open media file location: {ex.Message}";
        }
    }

    private string ResolveMediaCueActionLabel(ShowSlideDeckItem item, SlideMediaCue cue)
    {
        var cueDisplayName = MediaCueDisplayNameResolver.Normalize(cue.DisplayName);
        if (cueDisplayName != null)
            return cueDisplayName;

        var presentationPath = ResolveSlideDeckPresentationPath(item);
        var project = _projects.Open(presentationPath);
        return MediaCueDisplayNameResolver.Resolve(cue, project);
    }

    private async void SlideDeckSetGroupTitle_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "title");
    private async void SlideDeckSetGroupIntro_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "intro");
    private async void SlideDeckSetGroupVerse_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "verse");
    private async void SlideDeckSetGroupPreChorus_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "pre-chorus");
    private async void SlideDeckSetGroupChorus_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "chorus");
    private async void SlideDeckSetGroupBridge_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "bridge");
    private async void SlideDeckSetGroupTag_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "tag");
    private async void SlideDeckSetGroupOutro_Click(object sender, RoutedEventArgs e) => await SetSlideGroupAsync(sender, "outro");

    private async Task SetSlideGroupAsync(object sender, string section)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Section = section;
                slide.SectionLabel = PresentationModelUtilities.FormatSectionLabel(section, slide.SectionIndex);
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = $"Assigned slide to {PresentationModelUtilities.FormatSectionLabel(section)}.";
    }

    private async void SlideDeckClearGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) =>
            {
                slide.Section = null;
                slide.SectionLabel = null;
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Cleared slide group.";
    }

    private async void SlideDeckEditLabel_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var label = await PromptForNameAsync("Slide Label", "Label", item.Slide.SectionLabel ?? string.Empty);
        if (label == null)
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.SectionLabel = label);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Updated slide label.";
    }

    private async void SlideDeckClearLabel_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, _) => slide.SectionLabel = null);

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Cleared slide label.";
    }

    private void SlideDeckCopyTextStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        _textStyleClipboard.SetFromSlide(item.Slide);
        Vm.StatusMessage = "Copied slide text style.";
    }

    private async void SlideDeckPasteTextStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item) || !_textStyleClipboard.HasEntries)
            return;

        var entries = _textStyleClipboard.Entries.ToList();
        var result = await _slideItemActions.UpdateSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            (slide, project) =>
            {
                var targetTextLayers = slide.Layers.OfType<TextLayer>().ToList();
                for (var index = 0; index < targetTextLayers.Count && index < entries.Count; index++)
                {
                    var targetLayer = targetTextLayers[index];
                    var source = entries[index];
                    targetLayer.Style = PresentationModelUtilities.DeepClone(source.Style) ?? PresentationModelUtilities.CreateDefaultTextStyle();
                    targetLayer.Fills = source.Fills.Select(fill => PresentationModelUtilities.DeepClone(fill) ?? new LayerFillModel()).ToList();
                    targetLayer.Strokes = source.Strokes.Select(stroke => PresentationModelUtilities.DeepClone(stroke) ?? new LayerStrokeModel()).ToList();
                    targetLayer.Effects = source.Effects.Select(effect => PresentationModelUtilities.DeepClone(effect) ?? new LayerBlurEffectModel()).ToList();
                    targetLayer.Padding = source.Padding;
                    PresentationModelUtilities.NormalizeLayer(targetLayer, project.Manifest.SlideSize);
                }
            });

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Pasted slide text style.";
    }

    private void SlideDeckCut_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        _slideClipboard.SetCut(ResolveSlideDeckPresentationPath(item), item.Slide);
        Vm.StatusMessage = $"Cut slide {item.Ordinal}.";
    }

    private void SlideDeckCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        _slideClipboard.SetCopy(ResolveSlideDeckPresentationPath(item), item.Slide);
        Vm.StatusMessage = $"Copied slide {item.Ordinal}.";
    }

    private async void SlideDeckPaste_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item) || _slideClipboard.Entry == null)
            return;

        var result = await _slideItemActions.PasteSlideAsync(
            ResolveSlideDeckPresentationPath(item),
            item.Slide.Id,
            _slideClipboard.Entry,
            SlidePastePosition.After);

        if (_slideClipboard.Entry.IsCut)
            _slideClipboard.Clear();

        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Pasted slide.";
    }

    private async void SlideDeckDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSlideDeckItem(sender, out var item))
            return;

        if (!await ConfirmAsync("Delete Slide", $"Delete slide {item.Ordinal} from this presentation?", "Delete"))
            return;

        var result = await _slideItemActions.DeleteSlideAsync(ResolveSlideDeckPresentationPath(item), item.Slide.Id);
        await ReloadSlideMutationAsync(result);
        Vm.StatusMessage = "Deleted slide.";
    }

    private void ScheduleQuickEditLivePreviewRefresh(ShowSlideDeckItem item)
    {
        if (!item.IsLive)
            return;

        _quickEditLivePreviewItem = item;
        Microsoft.UI.Dispatching.DispatcherQueue? queue =
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue;
        if (queue is null)
        {
            _ = Vm.RefreshLiveSlidePreviewAsync(item);
            return;
        }

        if (_quickEditLivePreviewEnqueuePending)
            return;

        _quickEditLivePreviewEnqueuePending = true;
        queue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, QuickEditLivePreviewRefreshTick);
    }

    private void QuickEditLivePreviewRefreshTick()
    {
        _quickEditLivePreviewEnqueuePending = false;
        if (_quickEditLivePreviewItem is { } item)
            _ = Vm.RefreshLiveSlidePreviewAsync(item);
    }

    private void CancelQuickEditLivePreviewRefresh()
    {
        _quickEditLivePreviewEnqueuePending = false;
        _quickEditLivePreviewItem = null;
    }

    private async Task ReloadSlideMutationAsync(SlideItemMutationResult result)
    {
        if (await Vm.TryApplyInPlaceSlideMutationAsync(result))
        {
            await Vm.RefreshLiveSlideAfterMutationAsync(result);
            if (!string.IsNullOrWhiteSpace(result.SelectedSlideId))
                await Vm.ActivateSlideSelectionAsync(result.PresentationPath, result.SelectedSlideId);
            return;
        }

        var opened = await Vm.OpenPresentationFromPathAsync(result.PresentationPath);
        if (!string.IsNullOrWhiteSpace(result.SelectedSlideId))
            await Vm.ActivateSlideSelectionAsync(result.PresentationPath, result.SelectedSlideId);
    }

    private async void SlideDeckItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ShowSlideDeckItem row)
            return;

        var modifiers = GetCurrentKeyModifiers();
        if (modifiers.HasFlag(VirtualKeyModifiers.Shift))
            await Vm.SelectSlideRangeAsync(row.PresentationPath, row.Slide.Id, row.InstanceKey);
        else if (modifiers.HasFlag(VirtualKeyModifiers.Control))
            await Vm.ActivateSlideSelectionAsync(row.PresentationPath, row.Slide.Id, row.InstanceKey);
        else
            await Vm.TakeSlideLiveAsync(row.PresentationPath, row.Slide.Id, row.InstanceKey);

        BringElementIntoView(fe);
        RestoreKeyboardFocus();
    }

    private async void SlideDeckItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ShowSlideDeckItem row)
            return;

        await Vm.ActivateSlideSelectionAsync(row.PresentationPath, row.Slide.Id, row.InstanceKey);
        BringElementIntoView(fe);
    }

    private static bool IsSlideSeekKey(VirtualKey key) =>
        key is VirtualKey.Right
            or VirtualKey.PageDown
            or VirtualKey.Left
            or VirtualKey.PageUp
            or VirtualKey.Back;

    private async Task<bool> TryHandleSelectedSlideShortcutAsync(VirtualKey key)
    {
        if (Vm.SelectedDeckRowForView is not { } item)
            return false;

        var modifiers = GetCurrentKeyModifiers();
        if (modifiers == VirtualKeyModifiers.Control)
        {
            switch (key)
            {
                case VirtualKey.C:
                    await CopySlideAsync(item);
                    return true;
                case VirtualKey.X:
                    await CutSlideAsync(item);
                    return true;
                case VirtualKey.V when _slideClipboard.HasSlide:
                    await PasteSlideAsync(item);
                    return true;
                case VirtualKey.E:
                    if (FindSelectedSlideDeckItemHost(this) is FrameworkElement target)
                        await ShowQuickEditFlyoutAsync(target, item);
                    return true;
            }
        }

        if (modifiers == (VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift))
        {
            switch (key)
            {
                case VirtualKey.C:
                    _textStyleClipboard.SetFromSlide(item.Slide);
                    Vm.StatusMessage = "Copied slide text style.";
                    return true;
                case VirtualKey.V when _textStyleClipboard.HasEntries:
                    await PasteSlideTextStyleAsync(item);
                    return true;
            }
        }

        switch (key)
        {
            case VirtualKey.Delete:
                await DeleteSlideAsync(item);
                return true;
            case VirtualKey.F4:
                await OpenSlideInEditorAsync(item);
                return true;
        }

        return false;
    }

    private static VirtualKeyModifiers GetCurrentKeyModifiers()
    {
        var modifiers = VirtualKeyModifiers.None;
        if (IsKeyDown(VirtualKey.Control))
            modifiers |= VirtualKeyModifiers.Control;
        if (IsKeyDown(VirtualKey.Shift))
            modifiers |= VirtualKeyModifiers.Shift;
        if (IsKeyDown(VirtualKey.Menu))
            modifiers |= VirtualKeyModifiers.Menu;
        return modifiers;
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private void BringSelectedSlideDeckItemIntoView()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FindSelectedSlideDeckItemHost(this) is not FrameworkElement host)
                return;

            BringElementIntoView(host);
        });
    }

    private static void BringElementIntoView(FrameworkElement element)
    {
        element.StartBringIntoView();
    }

    private FrameworkElement? FindSelectedSlideDeckItemHost(DependencyObject root)
    {
        var selectedItem = Vm.SelectedDeckRowForView;
        return selectedItem == null ? null : FindSelectedSlideDeckItemHost(root, selectedItem);
    }

    private static FrameworkElement? FindSelectedSlideDeckItemHost(DependencyObject root, ShowSlideDeckItem selectedItem)
    {
        // Tag is set to the ShowSlideDeckItem via x:Bind in each card DataTemplate.
        if (root is FrameworkElement { Tag: ShowSlideDeckItem item } element
            && ReferenceEquals(item, selectedItem))
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var match = FindSelectedSlideDeckItemHost(VisualTreeHelper.GetChild(root, index), selectedItem);
            if (match != null)
                return match;
        }

        return null;
    }
}
