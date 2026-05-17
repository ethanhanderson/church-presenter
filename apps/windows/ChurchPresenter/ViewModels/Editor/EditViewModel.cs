using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Storage.Pickers;

namespace ChurchPresenter.ViewModels;

public partial class EditViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = PresentationJsonSerialization.CreateOptions();

    private readonly IPresentationProjectService _projects;
    private readonly ISlideItemActionService _slideActions;
    private readonly IThemeLibraryService _themeLibrary;
    private readonly IThemeApplicationService _themeApplier;
    private readonly IThemeResolutionService _themeResolution;
    private readonly IWorkspaceService _workspace;
    private readonly IActivePresentationService _activePresentation;
    private readonly ISettingsService _settings;
    private readonly ILogger<EditViewModel> _logger;

    private string? _currentPath;
    private PresentationProject? _project;
    private PresentationSlide? _selectedSlide;
    private SlideLayer? _selectedLayer;
    private string _statusMessage = "";
    private bool _isDirty;
    private CancellationTokenSource? _autoSaveCts;

    public EditViewModel(
        IPresentationProjectService projects,
        ISlideItemActionService slideActions,
        IThemeLibraryService themeLibrary,
        IThemeApplicationService themeApplier,
        IThemeResolutionService themeResolution,
        IWorkspaceService workspace,
        IActivePresentationService activePresentation,
        ISettingsService settings,
        ILogger<EditViewModel> logger)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _slideActions = slideActions ?? throw new ArgumentNullException(nameof(slideActions));
        _themeLibrary = themeLibrary ?? throw new ArgumentNullException(nameof(themeLibrary));
        _themeApplier = themeApplier ?? throw new ArgumentNullException(nameof(themeApplier));
        _themeResolution = themeResolution ?? throw new ArgumentNullException(nameof(themeResolution));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _activePresentation = activePresentation ?? throw new ArgumentNullException(nameof(activePresentation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Slides = new ObservableCollection<PresentationSlide>();
        Layers = new ObservableCollection<SlideLayer>();
        AvailableThemes = new ObservableCollection<ThemeTemplate>();
        AvailableThemeSlides = new ObservableCollection<ThemeTemplateSlide>();
        SectionOptions = PresentationModelUtilities.SongSections.ToList();
        ShapeTypeOptions = new List<string> { "rectangle", "ellipse", "line", "triangle" };
    }

    public ObservableCollection<PresentationSlide> Slides { get; }

    public ObservableCollection<SlideLayer> Layers { get; }

    public ObservableCollection<ThemeTemplate> AvailableThemes { get; }

    public ObservableCollection<ThemeTemplateSlide> AvailableThemeSlides { get; }

    public IReadOnlyList<string> SectionOptions { get; }

    public IReadOnlyList<string> ShapeTypeOptions { get; }

    public IReadOnlyList<string> AspectRatioChoices { get; } = new[] { "16:9", "4:3", "16:10" };

    public PresentationProject? Project
    {
        get => _project;
        private set
        {
            if (SetProperty(ref _project, value))
            {
                OnPropertyChanged(nameof(HasProject));
                OnPropertyChanged(nameof(PresentationTitle));
                OnPropertyChanged(nameof(PresentationAspectRatio));
                OnPropertyChanged(nameof(ProjectPreview));
            }
        }
    }

    public PresentationSlide? SelectedSlide
    {
        get => _selectedSlide;
        private set
        {
            if (SetProperty(ref _selectedSlide, value))
            {
                RefreshLayersCollection();
                OnPropertyChanged(nameof(HasSelectedSlide));
                OnPropertyChanged(nameof(SelectedSlideTitle));
                OnPropertyChanged(nameof(SelectedSlideTransitionLabel));
                OnPropertyChanged(nameof(HasSelectedSlideTransition));
                OnPropertyChanged(nameof(SelectedSlideSection));
                OnPropertyChanged(nameof(SelectedSlideSectionLabel));
                OnPropertyChanged(nameof(SelectedSlideNotes));
                OnPropertyChanged(nameof(SelectedSlidePreview));
                OnPropertyChanged(nameof(SelectedSlideMediaLayers));
                RefreshThemeState();
            }
        }
    }

    public SlideLayer? SelectedLayer
    {
        get => _selectedLayer;
        private set
        {
            if (SetProperty(ref _selectedLayer, value))
            {
                NotifyLayerState();
            }
        }
    }

    public bool HasProject => Project != null;

    public PresentationProject? ProjectPreview => Project;

    public bool HasSelectedSlide => SelectedSlide != null;

    public PresentationSlide? SelectedSlidePreview => SelectedSlide;

    public MediaLayersState SelectedSlideMediaLayers => SlideMediaLayerBuilder.Build(SelectedSlide);

    public bool HasSelectedSlideTransition =>
        !string.IsNullOrWhiteSpace(SelectedSlide?.Animations?.Transition?.Type);

    public string SelectedSlideTransitionLabel
    {
        get
        {
            var transition = SelectedSlide?.Animations?.Transition;
            if (transition == null || string.IsNullOrWhiteSpace(transition.Type))
                return "Uses presentation/global fallback";

            var label = MediaCueTransitionFormatter.FormatLabel(transition);
            return string.IsNullOrWhiteSpace(label) ? "Custom transition" : label;
        }
    }

    public bool HasSelectedLayer => SelectedLayer != null;

    public bool HasSelectedTextLayer => SelectedLayer is TextLayer;

    public bool HasSelectedShapeLayer => SelectedLayer is ShapeLayer;

    public string PresentationTitle
    {
        get => Project?.Manifest.Title ?? string.Empty;
        set
        {
            if (Project == null || string.Equals(Project.Manifest.Title, value, StringComparison.Ordinal))
                return;

            Project.Manifest.Title = value;
            MarkDirty($"Updated title to {value}");
            OnPropertyChanged();
        }
    }

    public string PresentationAspectRatio
    {
        get => Project?.Manifest.AspectRatio ?? "16:9";
        set
        {
            if (Project == null)
                return;

            string normalized = NormalizeAspectRatio(value);
            if (string.Equals(Project.Manifest.AspectRatio, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            Project.Manifest.AspectRatio = normalized;
            Project.Manifest.SlideSize = PresentationModelUtilities.GetBaseSlideSize(normalized);
            foreach (PresentationSlide slide in Project.Slides)
                PresentationModelUtilities.NormalizeSlide(slide, Project.Manifest.SlideSize);

            PresentationModelUtilities.ReconcileArrangement(Project);
            RefreshSlidesCollection();
            OnPropertyChanged(nameof(ProjectPreview));
            OnPropertyChanged(nameof(SelectedSlidePreview));
            MarkDirty($"Updated aspect ratio to {normalized}");
        }
    }

    public string SelectedSlideTitle => SelectedSlide?.SectionLabel ?? SelectedSlide?.Type ?? "Slide";

    public string SelectedSlideSection
    {
        get => SelectedSlide?.Section ?? string.Empty;
        set => CommitSlideMutation(slide =>
        {
            slide.Section = string.IsNullOrWhiteSpace(value) ? null : value;
            slide.SectionLabel = string.IsNullOrWhiteSpace(slide.Section)
                ? slide.SectionLabel
                : PresentationModelUtilities.FormatSectionLabel(slide.Section, slide.SectionIndex);
        });
    }

    public string SelectedSlideSectionLabel
    {
        get => SelectedSlide?.SectionLabel ?? string.Empty;
        set => CommitSlideMutation(slide => slide.SectionLabel = value);
    }

    public string SelectedSlideNotes
    {
        get => SelectedSlide?.Notes ?? string.Empty;
        set => CommitSlideMutation(slide => slide.Notes = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public ThemeTemplate? SelectedThemeForSlide => AvailableThemes.FirstOrDefault(theme =>
        string.Equals(theme.Id, SelectedSlide?.ThemeBinding?.ThemeId ?? Project?.Manifest.ThemeBinding?.ThemeId ?? Project?.Manifest.ThemeId, StringComparison.OrdinalIgnoreCase));

    public ThemeTemplateSlide? SelectedThemeSlideForSlide => AvailableThemeSlides.FirstOrDefault(slide =>
        string.Equals(slide.Id, SelectedSlide?.ThemeBinding?.ThemeSlideId, StringComparison.OrdinalIgnoreCase))
        ?? AvailableThemeSlides.FirstOrDefault();

    public bool SelectedSlideUpdatesWithTheme
    {
        get => !string.Equals(SelectedSlide?.ThemeBinding?.Mode, ThemeBindingModes.Detached, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(SelectedSlide?.ThemeBinding?.Mode, ThemeBindingModes.Materialized, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (SelectedSlide == null)
                return;

            if (value)
            {
                CommitSlideMutation(slide =>
                {
                    slide.ThemeBinding ??= new PresentationThemeBinding();
                    slide.ThemeBinding.Mode = ThemeBindingModes.Linked;
                });
            }
            else if (SelectedThemeForSlide != null && SelectedThemeSlideForSlide != null && Project != null)
            {
                DetachSelectedSlideTheme();
            }
        }
    }

    public string ThemeUpdateMessage
    {
        get
        {
            var state = _themeResolution.GetSourceUpdateState(SelectedSlide?.ThemeBinding ?? Project?.Manifest.ThemeBinding, SelectedThemeForSlide);
            return state.Message;
        }
    }

    public bool HasThemeUpdate =>
        _themeResolution.GetSourceUpdateState(SelectedSlide?.ThemeBinding ?? Project?.Manifest.ThemeBinding, SelectedThemeForSlide).CanUpdate;

    public string SelectedLayerName
    {
        get => SelectedLayer?.Name ?? string.Empty;
        set => CommitLayerMutation(layer => layer.Name = value);
    }

    public double SelectedLayerX
    {
        get => SelectedLayer?.Transform.X ?? 0;
        set => CommitLayerMutation(layer => layer.Transform.X = value);
    }

    public double SelectedLayerY
    {
        get => SelectedLayer?.Transform.Y ?? 0;
        set => CommitLayerMutation(layer => layer.Transform.Y = value);
    }

    public double SelectedLayerWidth
    {
        get => SelectedLayer?.Transform.Width ?? 0;
        set => CommitLayerMutation(layer => layer.Transform.Width = Math.Max(1, value));
    }

    public double SelectedLayerHeight
    {
        get => SelectedLayer?.Transform.Height ?? 0;
        set => CommitLayerMutation(layer => layer.Transform.Height = Math.Max(1, value));
    }

    public double SelectedLayerRotation
    {
        get => SelectedLayer?.Transform.Rotation ?? 0;
        set => CommitLayerMutation(layer => layer.Transform.Rotation = value);
    }

    public double SelectedLayerOpacity
    {
        get => SelectedLayer?.Transform.Opacity ?? 1;
        set => CommitLayerMutation(layer => layer.Transform.Opacity = Math.Clamp(value, 0, 1));
    }

    public string SelectedTextContent
    {
        get
        {
            if (SelectedSlide != null && SelectedLayer is TextLayer textLayer)
                return PresentationModelUtilities.ResolveTextBlock(SelectedSlide, textLayer.TextBinding)?.Text ?? textLayer.Content;

            return string.Empty;
        }
        set
        {
            if (SelectedLayer is not TextLayer selectedTextLayer)
                return;

            string selectedLayerId = selectedTextLayer.Id;
            CommitSlideMutation(slide =>
            {
                TextLayer? textLayer = slide.Layers.OfType<TextLayer>().FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, selectedLayerId, StringComparison.OrdinalIgnoreCase));
                if (textLayer == null)
                    return;

                textLayer.Content = value;
                SlideTextBlock? block = PresentationModelUtilities.ResolveTextBlock(slide, textLayer.TextBinding);
                if (block == null)
                {
                    block = PresentationModelUtilities.CreateTextBlock(value, textLayer.TextBinding?.Role, textLayer.Name, textLayer.Id);
                    slide.TextBlocks.Add(block);
                    textLayer.TextBinding = new ThemeTextBinding
                    {
                        TextBlockId = block.Id,
                        Role = block.Role,
                        FallbackIndex = slide.TextBlocks.Count - 1,
                        PlaceholderText = value,
                    };
                }
                else
                {
                    block.Text = value;
                    block.UpdatedAt = DateTime.UtcNow.ToString("O");
                }
            });
        }
    }

    public string SelectedTextFontFamily
    {
        get => (SelectedLayer as TextLayer)?.Style?.Font.Family ?? string.Empty;
        set => CommitLayerMutation(layer =>
        {
            if (layer is TextLayer textLayer)
                (textLayer.Style ??= PresentationModelUtilities.CreateDefaultTextStyle()).Font.Family = value;
        });
    }

    public double SelectedTextFontSize
    {
        get => (SelectedLayer as TextLayer)?.Style?.Font.Size ?? 0;
        set => CommitLayerMutation(layer =>
        {
            if (layer is TextLayer textLayer)
                (textLayer.Style ??= PresentationModelUtilities.CreateDefaultTextStyle()).Font.Size = Math.Max(1, value);
        });
    }

    public string SelectedTextColor
    {
        get => (SelectedLayer as TextLayer)?.Fills?.FirstOrDefault()?.Color
               ?? (SelectedLayer as TextLayer)?.Style?.Color
               ?? "#FFFFFF";
        set => CommitLayerMutation(layer =>
        {
            if (layer is not TextLayer textLayer)
                return;

            var fills = textLayer.Fills ??= new List<LayerFillModel>();
            if (fills.Count == 0)
            {
                fills.Add(new LayerFillModel
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Color = value,
                    Opacity = 1,
                    Enabled = true,
                });
            }
            else
            {
                fills[0].Color = value;
            }

            (textLayer.Style ??= PresentationModelUtilities.CreateDefaultTextStyle()).Color = value;
        });
    }

    public string SelectedShapeType
    {
        get => (SelectedLayer as ShapeLayer)?.ShapeType ?? "rectangle";
        set => CommitLayerMutation(layer =>
        {
            if (layer is ShapeLayer shapeLayer)
                shapeLayer.ShapeType = value;
        });
    }

    public string SelectedShapeFillColor
    {
        get => (SelectedLayer as ShapeLayer)?.Fills?.FirstOrDefault()?.Color
               ?? (SelectedLayer as ShapeLayer)?.Style?.Fill
               ?? "#3B82F6";
        set => CommitLayerMutation(layer =>
        {
            if (layer is not ShapeLayer shapeLayer)
                return;

            var fills = shapeLayer.Fills ??= new List<LayerFillModel>();
            if (fills.Count == 0)
            {
                fills.Add(new LayerFillModel
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Color = value,
                    Opacity = 1,
                    Enabled = true,
                });
            }
            else
            {
                fills[0].Color = value;
            }

            shapeLayer.Style.Fill = value;
        });
    }

    public string SelectedShapeStrokeColor
    {
        get => (SelectedLayer as ShapeLayer)?.Strokes?.FirstOrDefault()?.Color
               ?? (SelectedLayer as ShapeLayer)?.Style?.Stroke
               ?? "#1D4ED8";
        set => CommitLayerMutation(layer =>
        {
            if (layer is not ShapeLayer shapeLayer)
                return;

            var strokes = shapeLayer.Strokes ??= new List<LayerStrokeModel>();
            if (strokes.Count == 0)
            {
                strokes.Add(new LayerStrokeModel
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Color = value,
                    Opacity = 1,
                    Width = 2,
                    Position = "inside",
                    Sides = "all",
                    Enabled = true,
                });
            }
            else
            {
                strokes[0].Color = value;
            }

            shapeLayer.Style.Stroke = value;
        });
    }

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

    public string? CurrentPath => _currentPath;

    public async Task InitializeAsync()
    {
        await _workspace.LoadAsync().ConfigureAwait(true);
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
        Layers.Clear();
        SelectedSlide = null;
        SelectedLayer = null;
        StatusMessage = "Open a presentation from Show to edit it here.";
    }

    public async Task<bool> ConfirmNavigateAwayAsync()
    {
        if (!IsDirty)
            return true;

        var root = App.MainWindow?.Content as FrameworkElement;
        if (root?.XamlRoot == null)
            return true;

        var dialog = new ContentDialog
        {
            Title = "Unsaved changes",
            Content = "Save your presentation changes before leaving the editor?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            return await SaveCoreAsync().ConfigureAwait(true);
        if (result == ContentDialogResult.Secondary)
            return true;

        return false;
    }

    public void SelectSlideById(string? slideId)
    {
        if (string.IsNullOrWhiteSpace(slideId))
        {
            SelectedSlide = null;
            SelectedLayer = null;
            _activePresentation.SetSelectedSlideId(null);
            return;
        }

        SelectedSlide = Slides.FirstOrDefault(slide => string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
        SelectedLayer = SelectedSlide?.Layers.FirstOrDefault();
        _activePresentation.SetSelectedSlideId(SelectedSlide?.Id);
    }

    public SlideTransition? GetSelectedSlideTransitionForPicker()
    {
        var transition = SelectedSlide?.Animations?.Transition;
        return transition == null ? null : PresentationModelUtilities.DeepClone(transition);
    }

    public void SetSelectedSlideTransition(SlideTransition? transition)
    {
        CommitSlideMutation(slide =>
        {
            if (transition == null)
            {
                if (slide.Animations != null)
                    slide.Animations.Transition = null;
                return;
            }

            slide.Animations ??= new SlideAnimations();
            slide.Animations.Transition = TransitionStorageNormalizer.NormalizeForStorage(transition) ?? transition;
        });
    }

    public void SelectLayerById(string? layerId)
    {
        if (SelectedSlide == null || string.IsNullOrWhiteSpace(layerId))
        {
            SelectedLayer = null;
            return;
        }

        SelectedLayer = SelectedSlide.Layers.FirstOrDefault(layer => string.Equals(layer.Id, layerId, StringComparison.OrdinalIgnoreCase));
    }

    public void SelectThemeForSlide(string? themeId)
    {
        if (Project == null || SelectedSlide == null || string.IsNullOrWhiteSpace(themeId))
            return;

        ThemeTemplate? theme = AvailableThemes.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, themeId, StringComparison.OrdinalIgnoreCase));
        if (theme == null)
            return;

        ThemeTemplateSlide? themeSlide = theme.Slides.FirstOrDefault();
        CommitSlideMutation(slide => _themeApplier.ApplyLinkedTheme(Project, slide, theme, themeSlide));
        RefreshThemeState();
    }

    public void SelectThemeSlideForSlide(string? themeSlideId)
    {
        if (Project == null || SelectedSlide == null || SelectedThemeForSlide == null || string.IsNullOrWhiteSpace(themeSlideId))
            return;

        ThemeTemplateSlide? themeSlide = SelectedThemeForSlide.Slides.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, themeSlideId, StringComparison.OrdinalIgnoreCase));
        if (themeSlide == null)
            return;

        CommitSlideMutation(slide => _themeApplier.ApplyLinkedTheme(Project, slide, SelectedThemeForSlide, themeSlide));
        RefreshThemeState();
    }

    [RelayCommand]
    private void DetachSelectedSlideTheme()
    {
        if (Project == null || SelectedSlide == null || SelectedThemeForSlide == null || SelectedThemeSlideForSlide == null)
            return;

        var options = new ThemeApplyOptions
        {
            ScaleMode = string.Equals(SelectedThemeForSlide.AspectRatio, Project.Manifest.AspectRatio, StringComparison.OrdinalIgnoreCase) ? "none" : "fit",
            SourceSize = SelectedThemeForSlide.BaseSize,
            TargetSize = Project.Manifest.SlideSize ?? PresentationModelUtilities.GetBaseSlideSize(Project.Manifest.AspectRatio),
        };
        CommitSlideMutation(slide => _themeApplier.DetachSlideTheme(slide, SelectedThemeSlideForSlide, options));
        RefreshThemeState();
    }

    [RelayCommand]
    private void AddSlide()
    {
        if (Project == null)
            return;

        var newSlide = PresentationModelUtilities.CreateSlide("blank", slideSize: Project.Manifest.SlideSize);
        var insertIndex = SelectedSlide == null
            ? Project.Slides.Count
            : Project.Slides.FindIndex(slide => string.Equals(slide.Id, SelectedSlide.Id, StringComparison.OrdinalIgnoreCase)) + 1;
        insertIndex = Math.Clamp(insertIndex, 0, Project.Slides.Count);
        Project.Slides.Insert(insertIndex, newSlide);
        PresentationModelUtilities.ReconcileArrangement(Project);
        RefreshSlidesCollection();
        SelectSlideById(newSlide.Id);
        MarkDirty("Added slide", createdChange: true);
    }

    [RelayCommand]
    private async Task DuplicateSlide()
    {
        if (Project == null || SelectedSlide == null || !await PersistBeforeSharedSlideMutationAsync().ConfigureAwait(true) || string.IsNullOrWhiteSpace(_currentPath))
            return;

        var result = await _slideActions.DuplicateSlideAsync(_currentPath!, SelectedSlide.Id).ConfigureAwait(true);
        LoadProject(result.Project, result.PresentationPath);
        SelectSlideById(result.SelectedSlideId);
        StatusMessage = "Duplicated slide";
    }

    [RelayCommand]
    private async Task DeleteSlide()
    {
        if (Project == null || SelectedSlide == null || !await PersistBeforeSharedSlideMutationAsync().ConfigureAwait(true) || string.IsNullOrWhiteSpace(_currentPath))
            return;

        var result = await _slideActions.DeleteSlideAsync(_currentPath!, SelectedSlide.Id).ConfigureAwait(true);
        LoadProject(result.Project, result.PresentationPath);
        SelectSlideById(result.SelectedSlideId);
        StatusMessage = "Deleted slide";
    }

    [RelayCommand]
    private async Task MoveSlideUp()
    {
        await MoveSelectedSlideAsync(-1).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task MoveSlideDown()
    {
        await MoveSelectedSlideAsync(1).ConfigureAwait(true);
    }

    [RelayCommand]
    private void AddTextLayer()
    {
        if (Project == null || SelectedSlide == null)
            return;

        CommitSlideMutation(slide =>
        {
            TextLayer layer = PresentationModelUtilities.CreateTextLayer("New text", slideSize: Project.Manifest.SlideSize);
            SlideTextBlock block = PresentationModelUtilities.CreateTextBlock("New text", "body", layer.Name, layer.Id);
            layer.TextBinding = new ThemeTextBinding
            {
                TextBlockId = block.Id,
                Role = block.Role,
                FallbackIndex = slide.TextBlocks.Count,
                PlaceholderText = "New text",
            };
            slide.TextBlocks.Add(block);
            slide.Layers.Add(layer);
        });

        SelectedLayer = SelectedSlide?.Layers.LastOrDefault();
    }

    [RelayCommand]
    private void AddShapeLayer()
    {
        if (Project == null || SelectedSlide == null)
            return;

        CommitSlideMutation(slide =>
        {
            slide.Layers.Add(PresentationModelUtilities.CreateShapeLayer(slideSize: Project.Manifest.SlideSize));
        });

        SelectedLayer = SelectedSlide?.Layers.LastOrDefault();
    }

    [RelayCommand]
    private void DeleteLayer()
    {
        if (SelectedSlide == null || SelectedLayer == null)
            return;

        var layerId = SelectedLayer.Id;
        CommitSlideMutation(slide =>
        {
            slide.Layers.RemoveAll(layer => string.Equals(layer.Id, layerId, StringComparison.OrdinalIgnoreCase));
        });

        SelectedLayer = SelectedSlide?.Layers.FirstOrDefault();
    }

    [RelayCommand]
    private async Task Save()
    {
        await SaveCoreAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        await SaveCoreAsync(forceSaveAs: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await InitializeAsync().ConfigureAwait(true);
    }

    private void LoadProject(PresentationProject project, string? path)
    {
        _currentPath = string.IsNullOrWhiteSpace(path) ? project.SourcePath : path;
        _autoSaveCts?.Cancel();
        Project = project;
        RefreshSlidesCollection();
        SelectSlideById(_activePresentation.SelectedSlideId ?? Slides.FirstOrDefault()?.Id);
        IsDirty = false;
        StatusMessage = $"Editing {Project?.Manifest.Title}";
        RefreshThemeState();
    }

    private async Task LoadThemesAsync()
    {
        var themes = await _themeLibrary.LoadAsync().ConfigureAwait(true);
        AvailableThemes.Clear();
        foreach (var theme in themes)
            AvailableThemes.Add(theme);
    }

    private async Task MoveSelectedSlideAsync(int delta)
    {
        if (Project == null || SelectedSlide == null || !await PersistBeforeSharedSlideMutationAsync().ConfigureAwait(true) || string.IsNullOrWhiteSpace(_currentPath))
            return;

        var result = await _slideActions.MoveSlideAsync(_currentPath!, SelectedSlide.Id, delta).ConfigureAwait(true);
        LoadProject(result.Project, result.PresentationPath);
        SelectSlideById(result.SelectedSlideId);
        StatusMessage = "Reordered slides";
    }

    private void CommitSlideMutation(Action<PresentationSlide> mutation)
    {
        if (Project == null || SelectedSlide == null)
            return;

        var slideIndex = Project.Slides.FindIndex(slide => string.Equals(slide.Id, SelectedSlide.Id, StringComparison.OrdinalIgnoreCase));
        if (slideIndex < 0)
            return;

        var currentSlide = Project.Slides[slideIndex];
        var updatedSlide = PresentationModelUtilities.CloneSlide(currentSlide);
        mutation(updatedSlide);
        PresentationModelUtilities.NormalizeSlide(updatedSlide, Project.Manifest.SlideSize);
        if (AreEquivalent(currentSlide, updatedSlide))
            return;

        updatedSlide.UpdatedAt = DateTime.UtcNow.ToString("O");
        Project.Slides[slideIndex] = updatedSlide;
        PresentationModelUtilities.ReconcileArrangement(Project);
        RefreshSlidesCollection();
        var selectedLayerId = SelectedLayer?.Id;
        SelectSlideById(updatedSlide.Id);
        if (!string.IsNullOrWhiteSpace(selectedLayerId))
            SelectLayerById(selectedLayerId);
        MarkDirty("Updated slide");
    }

    private void CommitLayerMutation(Action<SlideLayer> mutation)
    {
        if (SelectedLayer == null)
            return;

        var selectedLayerId = SelectedLayer.Id;
        CommitSlideMutation(slide =>
        {
            var layer = slide.Layers.FirstOrDefault(candidate => string.Equals(candidate.Id, selectedLayerId, StringComparison.OrdinalIgnoreCase));
            if (layer == null)
                return;

            mutation(layer);
            PresentationModelUtilities.NormalizeLayer(layer, Project?.Manifest.SlideSize);
        });
    }

    private void RefreshSlidesCollection()
    {
        Slides.Clear();
        if (Project == null)
            return;

        foreach (var slide in Project.Slides)
            Slides.Add(slide);

        OnPropertyChanged(nameof(PresentationTitle));
        OnPropertyChanged(nameof(PresentationAspectRatio));
    }

    private static string NormalizeAspectRatio(string? aspectRatio) =>
        aspectRatio?.Trim() switch
        {
            "4:3" => "4:3",
            "16:10" => "16:10",
            _ => "16:9",
        };

    private void RefreshLayersCollection()
    {
        Layers.Clear();
        if (SelectedSlide == null)
        {
            SelectedLayer = null;
            return;
        }

        foreach (var layer in SelectedSlide.Layers)
            Layers.Add(layer);

        if (SelectedLayer != null)
            SelectedLayer = Layers.FirstOrDefault(layer => string.Equals(layer.Id, SelectedLayer.Id, StringComparison.OrdinalIgnoreCase));
        else
            SelectedLayer = Layers.FirstOrDefault();
    }

    private void RefreshThemeState()
    {
        AvailableThemeSlides.Clear();
        foreach (ThemeTemplateSlide slide in SelectedThemeForSlide?.Slides ?? [])
            AvailableThemeSlides.Add(slide);

        OnPropertyChanged(nameof(SelectedThemeForSlide));
        OnPropertyChanged(nameof(SelectedThemeSlideForSlide));
        OnPropertyChanged(nameof(SelectedSlideUpdatesWithTheme));
        OnPropertyChanged(nameof(ThemeUpdateMessage));
        OnPropertyChanged(nameof(HasThemeUpdate));
    }

    private void NotifyLayerState()
    {
        OnPropertyChanged(nameof(HasSelectedLayer));
        OnPropertyChanged(nameof(HasSelectedTextLayer));
        OnPropertyChanged(nameof(HasSelectedShapeLayer));
        OnPropertyChanged(nameof(SelectedLayerName));
        OnPropertyChanged(nameof(SelectedLayerX));
        OnPropertyChanged(nameof(SelectedLayerY));
        OnPropertyChanged(nameof(SelectedLayerWidth));
        OnPropertyChanged(nameof(SelectedLayerHeight));
        OnPropertyChanged(nameof(SelectedLayerRotation));
        OnPropertyChanged(nameof(SelectedLayerOpacity));
        OnPropertyChanged(nameof(SelectedTextContent));
        OnPropertyChanged(nameof(SelectedTextFontFamily));
        OnPropertyChanged(nameof(SelectedTextFontSize));
        OnPropertyChanged(nameof(SelectedTextColor));
        OnPropertyChanged(nameof(SelectedShapeType));
        OnPropertyChanged(nameof(SelectedShapeFillColor));
        OnPropertyChanged(nameof(SelectedShapeStrokeColor));
    }

    private void MarkDirty(string message, bool createdChange = false)
    {
        IsDirty = true;
        StatusMessage = message;
        ScheduleAutoSave(createdChange);
    }

    private void ScheduleAutoSave(bool createdChange)
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts = null;

        if (!_settings.Settings.Editor.AutoSaveEnabled)
            return;
        if (createdChange && !_settings.Settings.Editor.AutoSaveOnCreate)
            return;

        var delaySeconds = Math.Max(5, _settings.Settings.Editor.AutosaveInterval);
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        _ = AutoSaveAfterDelayAsync(TimeSpan.FromSeconds(delaySeconds), token);
    }

    private async Task AutoSaveAfterDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!IsDirty)
            return;

        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher == null)
            return;

        dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await SaveCoreAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-save failed for editor presentation.");
            }
        });
    }

    private async Task<bool> SaveCoreAsync(bool forceSaveAs = false)
    {
        if (Project == null)
            return false;

        string? targetPath = _currentPath;
        if (forceSaveAs || string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = await PromptForSavePathAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(targetPath))
                return false;
        }

        Project.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");
        _projects.Save(Project, targetPath);
        _currentPath = targetPath;
        _activePresentation.SetCurrentPresentation(Project, _currentPath);
        _workspace.Update(ws =>
        {
            ws.SelectedPresentationPath = _currentPath;
        });
        await _workspace.SaveAsync().ConfigureAwait(true);
        _autoSaveCts?.Cancel();
        IsDirty = false;
        StatusMessage = forceSaveAs ? $"Saved a copy of {Project.Manifest.Title}" : $"Saved {Project.Manifest.Title}";
        RefreshSlidesCollection();
        SelectSlideById(SelectedSlide?.Id ?? Slides.FirstOrDefault()?.Id);
        return true;
    }

    private async Task<string?> PromptForSavePathAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = string.IsNullOrWhiteSpace(Project?.Manifest.Title) ? "Presentation" : Project.Manifest.Title,
        };
        picker.FileTypeChoices.Add("Church Presenter Presentation", new List<string> { ".cpres" });

        if (App.MainWindow != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task<bool> PersistBeforeSharedSlideMutationAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
            return await SaveCoreAsync(forceSaveAs: true).ConfigureAwait(true);
        if (!IsDirty)
            return true;

        return await SaveCoreAsync().ConfigureAwait(true);
    }

    private static void RegenerateSlideIds(PresentationSlide slide)
    {
        slide.Id = Guid.NewGuid().ToString("N");
        slide.CreatedAt = DateTime.UtcNow.ToString("O");
        slide.UpdatedAt = slide.CreatedAt;

        foreach (var layer in slide.Layers)
        {
            layer.Id = Guid.NewGuid().ToString("N");

            if (layer.Fills != null)
            {
                foreach (var fill in layer.Fills)
                    fill.Id = Guid.NewGuid().ToString("N");
            }

            if (layer.Strokes != null)
            {
                foreach (var stroke in layer.Strokes)
                    stroke.Id = Guid.NewGuid().ToString("N");
            }

            if (layer.Effects != null)
            {
                foreach (var effect in layer.Effects)
                    effect.Id = Guid.NewGuid().ToString("N");
            }
        }

        if (slide.MediaCues != null)
        {
            foreach (var cue in slide.MediaCues)
                cue.Id = Guid.NewGuid().ToString("N");
        }

        if (slide.Animations != null)
        {
            foreach (var buildStep in slide.Animations.BuildIn)
                buildStep.Id = Guid.NewGuid().ToString("N");
            foreach (var buildStep in slide.Animations.BuildOut)
                buildStep.Id = Guid.NewGuid().ToString("N");
        }
    }

    private static bool AreEquivalent<T>(T current, T updated)
    {
        return string.Equals(
            JsonSerializer.Serialize(current, JsonOptions),
            JsonSerializer.Serialize(updated, JsonOptions),
            StringComparison.Ordinal);
    }
}