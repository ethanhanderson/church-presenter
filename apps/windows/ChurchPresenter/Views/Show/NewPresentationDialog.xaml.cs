using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed partial class NewPresentationDialog : ContentDialog
{
    private readonly IReadOnlyList<CreatePresentationThemeChoice> _themeChoices;
    private readonly IReadOnlyList<LibraryDto> _libraries;
    private readonly IReadOnlyList<CreatePresentationPlaylistChoice> _playlistChoices;
    private readonly Func<PresentationSizeChoice, Task<PresentationSizeChoice?>> _chooseCustomSize;
    private readonly List<PresentationSizeChoice> _screenSizeChoices;
    private readonly List<PresentationSizeChoice> _commonSizeChoices;
    private readonly List<PresentationSizeChoice> _customSizeChoices = [];

    private CreatePresentationThemeChoice? _selectedThemeChoice;
    private LibraryDto? _selectedLibrary;
    private CreatePresentationPlaylistChoice? _selectedPlaylistChoice;
    private PresentationSizeChoice _selectedSize;

    internal NewPresentationDialog(
        IReadOnlyList<CreatePresentationThemeChoice> themeChoices,
        IReadOnlyList<PresentationSizeChoice> screenSizeChoices,
        IReadOnlyList<PresentationSizeChoice> commonSizeChoices,
        IReadOnlyList<LibraryDto> libraries,
        IReadOnlyList<CreatePresentationPlaylistChoice> playlistChoices,
        LibraryDto selectedLibrary,
        PresentationSizeChoice selectedSize,
        Func<PresentationSizeChoice, Task<PresentationSizeChoice?>> chooseCustomSize)
    {
        _themeChoices = themeChoices;
        _libraries = libraries;
        _playlistChoices = playlistChoices;
        _screenSizeChoices = screenSizeChoices.ToList();
        _commonSizeChoices = commonSizeChoices.ToList();
        _selectedSize = selectedSize;
        _chooseCustomSize = chooseCustomSize;

        InitializeComponent();
        InitializePickers(selectedLibrary, selectedSize);
    }

    internal CreatePresentationDialogResult? Result { get; private set; }

    private void InitializePickers(LibraryDto selectedLibrary, PresentationSizeChoice selectedSize)
    {
        BuildThemeMenu();
        if (_themeChoices.FirstOrDefault() is { } selectedThemeChoice)
            SelectThemeChoice(selectedThemeChoice);

        BuildSizeMenu();
        var selectedSizeChoice = _screenSizeChoices.Concat(_commonSizeChoices)
            .FirstOrDefault(choice => SizeMatches(choice, selectedSize))
            ?? _screenSizeChoices.Concat(_commonSizeChoices).FirstOrDefault();
        if (selectedSizeChoice != null)
            SelectSizeChoice(selectedSizeChoice);

        BuildLibraryMenu();
        SelectLibrary(selectedLibrary);

        BuildPlaylistMenu();
        if (_playlistChoices.FirstOrDefault() is { } selectedPlaylistChoice)
            SelectPlaylist(selectedPlaylistChoice);
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CreatePresentationThemeChoice choice })
            SelectThemeChoice(choice);
    }

    private void SizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PresentationSizeChoice choice })
        {
            SelectSizeChoice(choice);
        }
    }

    private async void CustomSizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var custom = await _chooseCustomSize(_selectedSize);
        if (custom == null)
        {
            return;
        }

        if (!_customSizeChoices.Any(choice => SizeMatches(choice, custom)))
            _customSizeChoices.Add(custom);

        BuildSizeMenu();
        SelectSizeChoice(custom);
    }

    private void LibraryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: LibraryDto library })
            SelectLibrary(library);
    }

    private void PlaylistMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: CreatePresentationPlaylistChoice choice })
            SelectPlaylist(choice);
    }

    private void BuildThemeMenu()
    {
        ThemePickerMenuFlyout.Items.Clear();
        foreach (var choice in _themeChoices)
        {
            var item = new MenuFlyoutItem
            {
                Style = (Style)Resources["ThemeMenuFlyoutItemStyle"],
                Tag = choice,
                Text = choice.Name,
            };
            item.Click += ThemeMenuItem_Click;
            ThemePickerMenuFlyout.Items.Add(item);
        }
    }

    private void BuildSizeMenu()
    {
        SizePickerMenuFlyout.Items.Clear();

        AddSizeMenuGroup(_screenSizeChoices);
        AddSeparatorIfNeeded(SizePickerMenuFlyout, _screenSizeChoices.Count > 0 && _commonSizeChoices.Count > 0);
        AddSizeMenuGroup(_commonSizeChoices);
        AddSeparatorIfNeeded(SizePickerMenuFlyout, _screenSizeChoices.Count > 0 || _commonSizeChoices.Count > 0);
        AddSizeMenuGroup(_customSizeChoices);
        AddSeparatorIfNeeded(SizePickerMenuFlyout, _customSizeChoices.Count > 0);

        var customItem = new MenuFlyoutItem { Text = "Custom size..." };
        customItem.Click += CustomSizeMenuItem_Click;
        SizePickerMenuFlyout.Items.Add(customItem);
        SyncMenuSelection(SizePickerMenuFlyout, _selectedSize);
    }

    private void AddSizeMenuGroup(IEnumerable<PresentationSizeChoice> choices)
    {
        foreach (var choice in choices)
        {
            var item = new MenuFlyoutItem
            {
                Text = choice.Name,
                Tag = choice,
            };
            item.Click += SizeMenuItem_Click;
            SizePickerMenuFlyout.Items.Add(item);
        }
    }

    private void BuildLibraryMenu()
    {
        LibraryPickerMenuFlyout.Items.Clear();
        foreach (var library in _libraries)
        {
            var item = new MenuFlyoutItem
            {
                Text = library.Name,
                Tag = library,
            };
            item.Click += LibraryMenuItem_Click;
            LibraryPickerMenuFlyout.Items.Add(item);
        }
    }

    private void BuildPlaylistMenu()
    {
        PlaylistPickerMenuFlyout.Items.Clear();
        foreach (var choice in _playlistChoices)
        {
            var item = new MenuFlyoutItem
            {
                Text = choice.Name,
                Tag = choice,
            };
            item.Click += PlaylistMenuItem_Click;
            PlaylistPickerMenuFlyout.Items.Add(item);
        }
    }

    private static void AddSeparatorIfNeeded(MenuFlyout flyout, bool shouldAdd)
    {
        if (shouldAdd)
            flyout.Items.Add(new MenuFlyoutSeparator());
    }

    private void SelectThemeChoice(CreatePresentationThemeChoice choice)
    {
        _selectedThemeChoice = choice;
        SelectedThemeText.Text = choice.Name;
        SelectedThemePreview.Project = choice.PreviewProject;
        SelectedThemePreview.Slide = choice.PreviewSlide;
        SelectedThemePreview.Visibility = choice.ThemePreviewVisibility;
        SelectedNoThemePreview.Visibility = choice.NoThemePreviewVisibility;
        SyncMenuSelection(ThemePickerMenuFlyout, choice);
    }

    private void SelectSizeChoice(PresentationSizeChoice choice)
    {
        _selectedSize = choice;
        SelectedSizeText.Text = choice.Name;
        SyncMenuSelection(SizePickerMenuFlyout, choice);
    }

    private void SelectLibrary(LibraryDto library)
    {
        _selectedLibrary = library;
        SelectedLibraryText.Text = library.Name;
        SyncMenuSelection(LibraryPickerMenuFlyout, library);
    }

    private void SelectPlaylist(CreatePresentationPlaylistChoice choice)
    {
        _selectedPlaylistChoice = choice;
        SelectedPlaylistText.Text = choice.Name;
        SyncMenuSelection(PlaylistPickerMenuFlyout, choice);
    }

    private static void SyncMenuSelection(MenuFlyout flyout, object selected)
    {
        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
        {
            item.Icon = IsSelected(item.Tag, selected)
                ? new SymbolIcon(Symbol.Accept)
                : null;
        }
    }

    private static bool IsSelected(object? candidate, object selected) =>
        candidate switch
        {
            PresentationSizeChoice choice when selected is PresentationSizeChoice selectedChoice => SizeMatches(choice, selectedChoice),
            _ => Equals(candidate, selected),
        };

    private static bool SizeMatches(PresentationSizeChoice left, PresentationSizeChoice right) =>
        left.Size.Width == right.Size.Width && left.Size.Height == right.Size.Height;

    private void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NormalizeDialogValue(NameBox.Text);
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowValidation("Enter a presentation name.");
            args.Cancel = true;
            return;
        }

        if (_selectedLibrary is not { } selectedLibrary || string.IsNullOrWhiteSpace(selectedLibrary.Id))
        {
            ShowValidation("Choose a library for this presentation.");
            args.Cancel = true;
            return;
        }

        if (_selectedSize.Size.Width <= 0 || _selectedSize.Size.Height <= 0)
        {
            ShowValidation("Choose a slide size.");
            args.Cancel = true;
            return;
        }

        var selectedTheme = _selectedThemeChoice?.Theme;
        var selectedPlaylist = _selectedPlaylistChoice;

        Result = new CreatePresentationDialogResult(
            name,
            selectedTheme,
            _selectedSize.AspectRatio,
            _selectedSize.Size,
            selectedLibrary.Id,
            selectedPlaylist?.Playlist?.Id);
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private static string? NormalizeDialogValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

}

internal sealed record CreatePresentationDialogResult(
    string Name,
    ThemeTemplate? Theme,
    string AspectRatio,
    SlideSizeDto SlideSize,
    string LibraryId,
    string? PlaylistId);

internal sealed record CreatePresentationThemeChoice(ThemeTemplate? Theme, string Name)
{
    public Visibility ThemePreviewVisibility => Theme == null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NoThemePreviewVisibility => Theme == null ? Visibility.Visible : Visibility.Collapsed;

    public PresentationProject? PreviewProject =>
        Theme == null
            ? null
            : new PresentationProject
            {
                Manifest = new PresentationManifest
                {
                    Title = Theme.Name,
                    AspectRatio = Theme.AspectRatio,
                    OutputScaleMode = PresentationModelUtilities.DefaultOutputScaleMode,
                    SlideSize = PresentationModelUtilities.GetBaseSlideSize(Theme.AspectRatio, Theme.BaseSize),
                },
                Slides = new List<PresentationSlide>(),
                Arrangement = new PresentationArrangement(),
            };

    public PresentationSlide? PreviewSlide =>
        Theme?.Slides.FirstOrDefault() is not { } slide
            ? null
            : new PresentationSlide
            {
                Id = slide.Id,
                Type = "theme",
                LayoutType = slide.LayoutType,
                SectionLabel = slide.Name,
                Background = PresentationModelUtilities.DeepClone(slide.Background),
                Layers = slide.Layers.Select(layer => PresentationModelUtilities.DeepClone(layer) ?? layer).ToList(),
                MediaCues = slide.MediaCues?.Select(cue => PresentationModelUtilities.DeepClone(cue) ?? cue).ToList() ?? new List<SlideMediaCue>(),
            };
}

internal sealed record PresentationSizeChoice(string Name, string SourceLabel, string AspectRatio, SlideSizeDto Size);

internal sealed record CreatePresentationPlaylistChoice(PlaylistDto? Playlist, string Name);
