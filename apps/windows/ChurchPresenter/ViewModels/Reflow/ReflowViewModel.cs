using System.Collections.ObjectModel;


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChurchPresenter.ViewModels;

public partial class ReflowViewModel : ObservableObject
{
    private readonly IPresentationProjectService _projects;
    private readonly IThemeLibraryService _themeLibrary;
    private readonly IThemeApplicationService _themeApplier;
    private readonly IWorkspaceService _workspace;
    private readonly IActivePresentationService _activePresentation;
    private readonly ISettingsService _settings;

    private string? _currentPath;
    private string _statusMessage = "";
    private bool _isDirty;

    public ReflowViewModel(
        IPresentationProjectService projects,
        IThemeLibraryService themeLibrary,
        IThemeApplicationService themeApplier,
        IWorkspaceService workspace,
        IActivePresentationService activePresentation,
        ISettingsService settings)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _themeLibrary = themeLibrary ?? throw new ArgumentNullException(nameof(themeLibrary));
        _themeApplier = themeApplier ?? throw new ArgumentNullException(nameof(themeApplier));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _activePresentation = activePresentation ?? throw new ArgumentNullException(nameof(activePresentation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Slides = new ObservableCollection<ReflowSlideItem>();
        AvailableThemes = new ObservableCollection<ThemeTemplate>();
        AvailableThemeSlides = new ObservableCollection<ThemeTemplateSlide>();
        SectionOptions = PresentationModelUtilities.SongSections.ToList();
    }

    public string Hint { get; } = "Review slide text, sections, and visual flow across the active presentation.";

    public ObservableCollection<ReflowSlideItem> Slides { get; }

    public ObservableCollection<ThemeTemplate> AvailableThemes { get; }

    public ObservableCollection<ThemeTemplateSlide> AvailableThemeSlides { get; }

    public IReadOnlyList<string> SectionOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(PresentationTitle))]
    [NotifyPropertyChangedFor(nameof(ProjectPreview))]
    [NotifyPropertyChangedFor(nameof(SelectedSlidePreview))]
    private PresentationProject? _project;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSlidePreview))]
    [NotifyPropertyChangedFor(nameof(SelectedSlideMediaLayers))]
    [NotifyPropertyChangedFor(nameof(SelectedSlideSection))]
    [NotifyPropertyChangedFor(nameof(SelectedSlideNotes))]
    [NotifyPropertyChangedFor(nameof(SelectedSlideText))]
    [NotifyPropertyChangedFor(nameof(SelectedSlideHeader))]
    private ReflowSlideItem? _selectedSlideItem;

    partial void OnSelectedSlideItemChanged(ReflowSlideItem? value)
    {
        RefreshThemeState();
    }

    public bool HasProject => Project != null;

    public string PresentationTitle => Project?.Manifest.Title ?? "Reflow";

    public PresentationProject? ProjectPreview => Project;

    public PresentationSlide? SelectedSlidePreview => SelectedSlideItem?.Slide;

    public MediaLayersState SelectedSlideMediaLayers => SlideMediaLayerBuilder.Build(SelectedSlideItem?.Slide);

    public string CurrentPath => _currentPath ?? string.Empty;

    public double TextSize => _settings.Settings.Reflow.TextSize;

    public string DensityLabel => _settings.Settings.Reflow.PreviewDensity == "compact" ? "Compact preview density" : "Comfortable preview density";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public string SelectedSlideHeader => SelectedSlideItem == null ? "No slide selected" : $"Slide {SelectedSlideItem.Index}";

    public string SelectedSlideSection
    {
        get => SelectedSlideItem?.Slide.Section ?? string.Empty;
        set
        {
            if (SelectedSlideItem?.Slide == null)
                return;

            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (string.Equals(SelectedSlideItem.Slide.Section, normalized, StringComparison.Ordinal))
                return;

            SelectedSlideItem.Slide.Section = normalized;
            SelectedSlideItem.Slide.SectionLabel = normalized == null
                ? null
                : PresentationModelUtilities.FormatSectionLabel(normalized, SelectedSlideItem.Slide.SectionIndex);
            SelectedSlideItem.Refresh();
            MarkDirty($"Updated section for slide {SelectedSlideItem.Index}");
            OnPropertyChanged();
        }
    }

    public string SelectedSlideNotes
    {
        get => SelectedSlideItem?.Slide.Notes ?? string.Empty;
        set
        {
            if (SelectedSlideItem?.Slide == null)
                return;

            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (string.Equals(SelectedSlideItem.Slide.Notes, normalized, StringComparison.Ordinal))
                return;

            SelectedSlideItem.Slide.Notes = normalized;
            MarkDirty($"Updated notes for slide {SelectedSlideItem.Index}");
            OnPropertyChanged();
        }
    }

    public string SelectedSlideText => SelectedSlideItem?.BodyText ?? string.Empty;

    public ThemeTemplate? PresentationTheme => AvailableThemes.FirstOrDefault(theme =>
        string.Equals(theme.Id, Project?.Manifest.ThemeBinding?.ThemeId ?? Project?.Manifest.ThemeId, StringComparison.OrdinalIgnoreCase));

    public ThemeTemplateSlide? SelectedSlideStyle => AvailableThemeSlides.FirstOrDefault(slide =>
        string.Equals(slide.Id, SelectedSlideItem?.Slide.ThemeBinding?.ThemeSlideId, StringComparison.OrdinalIgnoreCase))
        ?? AvailableThemeSlides.FirstOrDefault();

    public async Task InitializeAsync()
    {
        await _workspace.LoadAsync().ConfigureAwait(true);
        await _settings.LoadAsync().ConfigureAwait(true);
        await LoadThemesAsync().ConfigureAwait(true);

        var activeProject = _activePresentation.CurrentProject;
        var activePath = _activePresentation.CurrentPath ?? activeProject?.SourcePath;
        if (activeProject != null && !string.IsNullOrWhiteSpace(activePath))
        {
            LoadProject(PresentationModelUtilities.CloneProject(activeProject), activePath);
            return;
        }

        Project = null;
        Slides.Clear();
        SelectedSlideItem = null;
        StatusMessage = "Open a presentation from Show to review it in Reflow.";
    }

    public void SelectSlideById(string? slideId)
    {
        if (string.IsNullOrWhiteSpace(slideId))
        {
            SelectedSlideItem = null;
            _activePresentation.SetSelectedSlideId(null);
            return;
        }

        SelectedSlideItem = Slides.FirstOrDefault(item => string.Equals(item.Slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
        _activePresentation.SetSelectedSlideId(SelectedSlideItem?.Slide.Id);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Project == null || string.IsNullOrWhiteSpace(_currentPath))
            return;

        Project.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");
        PresentationModelUtilities.ReconcileArrangement(Project);
        _projects.Save(Project, _currentPath);
        _activePresentation.SetCurrentPresentation(Project, _currentPath);
        _workspace.Update(ws => ws.SelectedPresentationPath = _currentPath);
        await _workspace.SaveAsync().ConfigureAwait(true);
        IsDirty = false;
        StatusMessage = $"Saved {Project.Manifest.Title}";
    }

    private void LoadProject(PresentationProject project, string? path)
    {
        _currentPath = string.IsNullOrWhiteSpace(path) ? project.SourcePath : path;
        Project = project;
        RebuildSlides();
        SelectSlideById(_activePresentation.SelectedSlideId ?? Slides.FirstOrDefault()?.Slide.Id);
        IsDirty = false;
        StatusMessage = $"Reviewing {project.Manifest.Title}";
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(TextSize));
        OnPropertyChanged(nameof(DensityLabel));
        RefreshThemeState();
    }

    public void SelectPresentationTheme(string? themeId)
    {
        if (Project == null || string.IsNullOrWhiteSpace(themeId))
            return;

        ThemeTemplate? theme = AvailableThemes.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, themeId, StringComparison.OrdinalIgnoreCase));
        if (theme == null)
            return;

        _themeApplier.ApplyLinkedTheme(Project, null, theme, theme.Slides.FirstOrDefault());
        RefreshThemeState();
        MarkDirty($"Using {theme.Name}");
    }

    public void SelectSlideStyle(string? themeSlideId)
    {
        if (Project == null || SelectedSlideItem?.Slide == null || PresentationTheme == null || string.IsNullOrWhiteSpace(themeSlideId))
            return;

        ThemeTemplateSlide? themeSlide = PresentationTheme.Slides.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, themeSlideId, StringComparison.OrdinalIgnoreCase));
        if (themeSlide == null)
            return;

        _themeApplier.ApplyLinkedTheme(Project, SelectedSlideItem.Slide, PresentationTheme, themeSlide);
        SelectedSlideItem.Refresh();
        RefreshThemeState();
        MarkDirty($"Updated slide {SelectedSlideItem.Index} style");
    }

    private async Task LoadThemesAsync()
    {
        AvailableThemes.Clear();
        foreach (ThemeTemplate theme in await _themeLibrary.LoadAsync().ConfigureAwait(true))
            AvailableThemes.Add(theme);
    }

    private void RefreshThemeState()
    {
        AvailableThemeSlides.Clear();
        foreach (ThemeTemplateSlide slide in PresentationTheme?.Slides ?? [])
            AvailableThemeSlides.Add(slide);

        OnPropertyChanged(nameof(PresentationTheme));
        OnPropertyChanged(nameof(SelectedSlideStyle));
    }

    private void RebuildSlides()
    {
        Slides.Clear();
        if (Project == null)
            return;

        for (var index = 0; index < Project.Slides.Count; index++)
            Slides.Add(new ReflowSlideItem(Project.Slides[index], index + 1));
    }

    private void MarkDirty(string message)
    {
        IsDirty = true;
        StatusMessage = message;
    }
}

public sealed partial class ReflowSlideItem : ObservableObject
{
    public ReflowSlideItem(PresentationSlide slide, int index)
    {
        Slide = slide;
        Index = index;
        Refresh();
    }

    public PresentationSlide Slide { get; }

    public int Index { get; }

    [ObservableProperty]
    private string _header = string.Empty;

    [ObservableProperty]
    private string _bodyText = string.Empty;

    [ObservableProperty]
    private string _sectionSummary = string.Empty;

    public void Refresh()
    {
        Header = $"Slide {Index}";
        BodyText = BuildSlideText(Slide);
        SectionSummary = string.IsNullOrWhiteSpace(Slide.SectionLabel)
            ? "No section"
            : Slide.SectionLabel;
    }

    private static string BuildSlideText(PresentationSlide slide)
    {
        var text = PresentationModelUtilities.BuildSlideText(slide);
        return string.IsNullOrWhiteSpace(text) ? "No text" : text;
    }
}