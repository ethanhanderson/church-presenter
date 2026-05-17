using System.Collections.ObjectModel;
using System.Text.Json;


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.ViewModels;

public partial class ThemesViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = PresentationJsonSerialization.CreateOptions();

    private readonly IThemeLibraryService _themeLibrary;
    private readonly IWorkspaceService _workspace;
    private readonly IPresentationProjectService _projects;
    private readonly IThemeApplicationService _themeApplier;
    private readonly IActivePresentationService _activePresentation;
    private readonly ISettingsService _settings;
    private readonly ILogger<ThemesViewModel> _logger;

    private ThemeTemplate? _selectedTheme;
    private ThemeTemplateSlide? _selectedThemeSlide;
    private SlideLayer? _selectedLayer;
    private PresentationProject? _currentPresentation;
    private PresentationSlide? _selectedSourceSlide;
    private string? _currentPresentationPath;
    private string _statusMessage = "";
    private bool _isDirty;
    private CancellationTokenSource? _autoSaveCts;

    public ThemesViewModel(
        IThemeLibraryService themeLibrary,
        IWorkspaceService workspace,
        IPresentationProjectService projects,
        IThemeApplicationService themeApplier,
        IActivePresentationService activePresentation,
        ISettingsService settings,
        ILogger<ThemesViewModel> logger)
    {
        _themeLibrary = themeLibrary ?? throw new ArgumentNullException(nameof(themeLibrary));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _themeApplier = themeApplier ?? throw new ArgumentNullException(nameof(themeApplier));
        _activePresentation = activePresentation ?? throw new ArgumentNullException(nameof(activePresentation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Themes = new ObservableCollection<ThemeTemplate>();
        ThemeSlides = new ObservableCollection<ThemeTemplateSlide>();
        Layers = new ObservableCollection<SlideLayer>();
        SourceSlides = new ObservableCollection<PresentationSlide>();
        ShapeTypeOptions = new List<string> { "rectangle", "ellipse", "line", "triangle" };
    }

    public ObservableCollection<ThemeTemplate> Themes { get; }

    public ObservableCollection<ThemeTemplateSlide> ThemeSlides { get; }

    public ObservableCollection<SlideLayer> Layers { get; }

    public ObservableCollection<PresentationSlide> SourceSlides { get; }

    public IReadOnlyList<string> ShapeTypeOptions { get; }

    public IReadOnlyList<string> AspectRatioChoices { get; } = new[] { "16:9", "4:3", "16:10" };

    public ThemeTemplate? SelectedTheme
    {
        get => _selectedTheme;
        private set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                RefreshThemeSlides();
                OnPropertyChanged(nameof(HasSelectedTheme));
                OnPropertyChanged(nameof(ThemeName));
                OnPropertyChanged(nameof(ThemeAspectRatio));
                OnPropertyChanged(nameof(ThemePreviewProject));
            }
        }
    }

    public ThemeTemplateSlide? SelectedThemeSlide
    {
        get => _selectedThemeSlide;
        private set
        {
            if (SetProperty(ref _selectedThemeSlide, value))
            {
                RefreshLayers();
                OnPropertyChanged(nameof(HasSelectedThemeSlide));
                OnPropertyChanged(nameof(SelectedThemeSlideName));
                OnPropertyChanged(nameof(SelectedThemeSlideRole));
                OnPropertyChanged(nameof(ThemePreviewSlide));
                OnPropertyChanged(nameof(ThemePreviewProject));
            }
        }
    }

    public SlideLayer? SelectedLayer
    {
        get => _selectedLayer;
        private set
        {
            if (SetProperty(ref _selectedLayer, value))
                NotifyLayerState();
        }
    }

    public PresentationProject? CurrentPresentation
    {
        get => _currentPresentation;
        private set
        {
            if (SetProperty(ref _currentPresentation, value))
            {
                RefreshSourceSlides();
                OnPropertyChanged(nameof(CurrentPresentationTitle));
                OnPropertyChanged(nameof(HasSourcePresentation));
                OnPropertyChanged(nameof(CurrentPresentationPreview));
            }
        }
    }

    public PresentationSlide? SelectedSourceSlide
    {
        get => _selectedSourceSlide;
        private set => SetProperty(ref _selectedSourceSlide, value);
    }

    public bool HasSelectedTheme => SelectedTheme != null;

    public bool HasSelectedThemeSlide => SelectedThemeSlide != null;

    public bool HasSelectedLayer => SelectedLayer != null;

    public bool HasSelectedTextLayer => SelectedLayer is TextLayer;

    public bool HasSelectedShapeLayer => SelectedLayer is ShapeLayer;

    public bool HasSourcePresentation => CurrentPresentation != null;

    public PresentationProject? CurrentPresentationPreview => CurrentPresentation;

    public PresentationProject? ThemePreviewProject =>
        SelectedTheme == null
            ? null
            : new PresentationProject
            {
                Manifest = new PresentationManifest
                {
                    Title = SelectedTheme.Name,
                    AspectRatio = SelectedTheme.AspectRatio,
                    OutputScaleMode = PresentationModelUtilities.DefaultOutputScaleMode,
                    SlideSize = PresentationModelUtilities.GetBaseSlideSize(SelectedTheme.AspectRatio, SelectedTheme.BaseSize),
                },
                Slides = new List<PresentationSlide>(),
                Arrangement = new PresentationArrangement(),
            };

    public string ThemeName
    {
        get => SelectedTheme?.Name ?? string.Empty;
        set
        {
            if (SelectedTheme == null || string.Equals(SelectedTheme.Name, value, StringComparison.Ordinal))
                return;

            SelectedTheme.Name = value;
            MarkDirty("Updated theme name");
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemePreviewProject));
        }
    }

    public string ThemeAspectRatio
    {
        get => SelectedTheme?.AspectRatio ?? "16:9";
        set
        {
            if (SelectedTheme == null)
                return;

            string normalized = NormalizeAspectRatio(value);
            if (string.Equals(SelectedTheme.AspectRatio, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            SelectedTheme.AspectRatio = normalized;
            SelectedTheme.BaseSize = PresentationModelUtilities.GetBaseSlideSize(normalized);
            PresentationModelUtilities.NormalizeTheme(SelectedTheme);
            MarkDirty($"Updated theme aspect ratio to {normalized}");
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemePreviewProject));
            OnPropertyChanged(nameof(ThemePreviewSlide));
        }
    }

    public string SelectedThemeSlideName
    {
        get => SelectedThemeSlide?.Name ?? string.Empty;
        set => CommitThemeSlideMutation(slide => slide.Name = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public string SelectedThemeSlideLayoutType
    {
        get => SelectedThemeSlide?.LayoutType ?? string.Empty;
        set => CommitThemeSlideMutation(slide => slide.LayoutType = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public string SelectedThemeSlideRole
    {
        get => SelectedThemeSlide?.Roles.FirstOrDefault() ?? SelectedThemeSlide?.LayoutType ?? string.Empty;
        set => CommitThemeSlideMutation(slide =>
        {
            slide.Roles.Clear();
            if (!string.IsNullOrWhiteSpace(value))
                slide.Roles.Add(PresentationModelUtilities.NormalizeRole(value));
        });
    }

    public PresentationSlide? ThemePreviewSlide =>
        SelectedThemeSlide == null
            ? null
            : new PresentationSlide
            {
                Id = SelectedThemeSlide.Id,
                Type = "theme",
                LayoutType = SelectedThemeSlide.LayoutType,
                SectionLabel = SelectedThemeSlide.Name,
                Background = PresentationModelUtilities.DeepClone(SelectedThemeSlide.Background),
                Layers = SelectedThemeSlide.Layers.Select(layer => PresentationModelUtilities.DeepClone(layer) ?? layer).ToList(),
                MediaCues = SelectedThemeSlide.MediaCues?.Select(cue => PresentationModelUtilities.DeepClone(cue) ?? cue).ToList() ?? new List<SlideMediaCue>(),
            };

    public string CurrentPresentationTitle => CurrentPresentation?.Manifest.Title ?? "No presentation selected";

    public string SaveStateLabel => IsDirty ? "Unsaved changes" : "Saved";

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
        get => (SelectedLayer as TextLayer)?.Content ?? string.Empty;
        set => CommitLayerMutation(layer =>
        {
            if (layer is TextLayer textLayer)
                textLayer.Content = value;
        });
    }

    public string SelectedTextBindingRole
    {
        get => (SelectedLayer as TextLayer)?.TextBinding?.Role ?? string.Empty;
        set => CommitLayerMutation(layer =>
        {
            if (layer is TextLayer textLayer)
            {
                textLayer.TextBinding ??= new ThemeTextBinding();
                textLayer.TextBinding.Role = string.IsNullOrWhiteSpace(value)
                    ? null
                    : PresentationModelUtilities.NormalizeRole(value);
            }
        });
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

    public async Task InitializeAsync()
    {
        await _workspace.LoadAsync().ConfigureAwait(true);

        var themes = await _themeLibrary.LoadAsync().ConfigureAwait(true);
        Themes.Clear();
        foreach (var theme in themes)
            Themes.Add(theme);

        SelectedTheme = Themes.FirstOrDefault();
        SelectThemeSlideById(SelectedTheme?.Slides.FirstOrDefault()?.Id);

        var activeProject = _activePresentation.CurrentProject;
        _currentPresentationPath = _activePresentation.CurrentPath
            ?? activeProject?.SourcePath
            ?? _workspace.Workspace.SelectedPresentationPath;
        if (activeProject != null)
        {
            CurrentPresentation = PresentationModelUtilities.CloneProject(activeProject);
            SelectedSourceSlide = SourceSlides.FirstOrDefault();
        }
        else if (!string.IsNullOrWhiteSpace(_currentPresentationPath))
        {
            CurrentPresentation = _projects.Open(_currentPresentationPath);
            SelectedSourceSlide = SourceSlides.FirstOrDefault();
        }
        else
        {
            CurrentPresentation = null;
            SelectedSourceSlide = null;
        }

        StatusMessage = Themes.Count == 0
            ? "Create a theme or import a slide from the open presentation."
            : $"Loaded {Themes.Count} theme(s)";
    }

    public void SelectThemeById(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            SelectedTheme = null;
            SelectedThemeSlide = null;
            return;
        }

        SelectedTheme = Themes.FirstOrDefault(theme => string.Equals(theme.Id, themeId, StringComparison.OrdinalIgnoreCase));
        SelectThemeSlideById(SelectedTheme?.Slides.FirstOrDefault()?.Id);
    }

    public void SelectThemeSlideById(string? slideId)
    {
        if (SelectedTheme == null || string.IsNullOrWhiteSpace(slideId))
        {
            SelectedThemeSlide = null;
            SelectedLayer = null;
            return;
        }

        SelectedThemeSlide = SelectedTheme.Slides.FirstOrDefault(slide => string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
        SelectedLayer = SelectedThemeSlide?.Layers.FirstOrDefault();
    }

    public void SelectLayerById(string? layerId)
    {
        if (SelectedThemeSlide == null || string.IsNullOrWhiteSpace(layerId))
        {
            SelectedLayer = null;
            return;
        }

        SelectedLayer = SelectedThemeSlide.Layers.FirstOrDefault(layer => string.Equals(layer.Id, layerId, StringComparison.OrdinalIgnoreCase));
    }

    public void SelectSourceSlideById(string? slideId)
    {
        if (string.IsNullOrWhiteSpace(slideId))
        {
            SelectedSourceSlide = null;
            return;
        }

        SelectedSourceSlide = SourceSlides.FirstOrDefault(slide => string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void CreateTheme()
    {
        var aspectRatio = CurrentPresentation?.Manifest.AspectRatio ?? "16:9";
        var now = DateTime.UtcNow.ToString("O");
        var theme = new ThemeTemplate
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"Theme {Themes.Count + 1}",
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

        Themes.Add(theme);
        SelectedTheme = theme;
        SelectThemeSlideById(theme.Slides[0].Id);
        MarkDirty("Created theme");
    }

    [RelayCommand]
    private void DuplicateTheme()
    {
        if (SelectedTheme == null)
            return;

        var duplicate = PresentationModelUtilities.CloneTheme(SelectedTheme);
        RegenerateThemeIds(duplicate);
        duplicate.Name = $"{duplicate.Name} Copy";
        Themes.Add(duplicate);
        SelectedTheme = duplicate;
        SelectThemeSlideById(duplicate.Slides.FirstOrDefault()?.Id);
        MarkDirty("Duplicated theme");
    }

    [RelayCommand]
    private void DeleteTheme()
    {
        if (SelectedTheme == null)
            return;

        var removeIndex = Themes.IndexOf(SelectedTheme);
        Themes.Remove(SelectedTheme);
        SelectedTheme = Themes.Count == 0 ? null : Themes[Math.Clamp(removeIndex, 0, Themes.Count - 1)];
        SelectThemeSlideById(SelectedTheme?.Slides.FirstOrDefault()?.Id);
        MarkDirty("Deleted theme");
    }

    [RelayCommand]
    private void CreateThemeFromSourceSlide()
    {
        if (SelectedSourceSlide == null)
            return;

        var theme = PresentationModelUtilities.CreateThemeFromSlide(
            string.IsNullOrWhiteSpace(SelectedSourceSlide.SectionLabel) ? "Imported Theme" : SelectedSourceSlide.SectionLabel!,
            SelectedSourceSlide,
            CurrentPresentation?.Manifest.AspectRatio ?? "16:9");

        Themes.Add(theme);
        SelectedTheme = theme;
        SelectThemeSlideById(theme.Slides.FirstOrDefault()?.Id);
        MarkDirty("Created theme from presentation slide");
    }

    [RelayCommand]
    private void AddThemeSlideFromSource()
    {
        if (SelectedTheme == null || SelectedSourceSlide == null)
            return;

        var slide = PresentationModelUtilities.CreateThemeSlideFromSlide(SelectedSourceSlide);
        SelectedTheme.Slides.Add(slide);
        RefreshThemeSlides();
        SelectThemeSlideById(slide.Id);
        MarkDirty("Added theme slide from presentation");
    }

    [RelayCommand]
    private void DeleteThemeSlide()
    {
        if (SelectedTheme == null || SelectedThemeSlide == null)
            return;

        if (SelectedTheme.Slides.Count == 1)
        {
            SelectedTheme.Slides[0] = new ThemeTemplateSlide
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Default",
                Background = new SolidSlideBackground { Color = "#000000" },
            };
            RefreshThemeSlides();
            SelectThemeSlideById(SelectedTheme.Slides[0].Id);
            MarkDirty("Reset theme to a blank slide");
            return;
        }

        var removeIndex = SelectedTheme.Slides.FindIndex(slide => string.Equals(slide.Id, SelectedThemeSlide.Id, StringComparison.OrdinalIgnoreCase));
        SelectedTheme.Slides.RemoveAt(removeIndex);
        RefreshThemeSlides();
        var nextSlide = SelectedTheme.Slides[Math.Clamp(removeIndex, 0, SelectedTheme.Slides.Count - 1)];
        SelectThemeSlideById(nextSlide.Id);
        MarkDirty("Deleted theme slide");
    }

    [RelayCommand]
    private void AddTextLayer()
    {
        if (SelectedTheme == null || SelectedThemeSlide == null)
            return;

        CommitThemeSlideMutation(slide =>
        {
            slide.Layers.Add(PresentationModelUtilities.CreateTextLayer("Theme text", slideSize: SelectedTheme.BaseSize));
        });

        SelectedLayer = SelectedThemeSlide?.Layers.LastOrDefault();
    }

    [RelayCommand]
    private void AddShapeLayer()
    {
        if (SelectedTheme == null || SelectedThemeSlide == null)
            return;

        CommitThemeSlideMutation(slide =>
        {
            slide.Layers.Add(PresentationModelUtilities.CreateShapeLayer(slideSize: SelectedTheme.BaseSize));
        });

        SelectedLayer = SelectedThemeSlide?.Layers.LastOrDefault();
    }

    [RelayCommand]
    private void DeleteLayer()
    {
        if (SelectedThemeSlide == null || SelectedLayer == null)
            return;

        var layerId = SelectedLayer.Id;
        CommitThemeSlideMutation(slide =>
        {
            slide.Layers.RemoveAll(layer => string.Equals(layer.Id, layerId, StringComparison.OrdinalIgnoreCase));
        });

        SelectedLayer = SelectedThemeSlide?.Layers.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SaveLibraryAsync()
    {
        await _themeLibrary.SaveAsync(Themes.ToList()).ConfigureAwait(true);
        _autoSaveCts?.Cancel();
        IsDirty = false;
        StatusMessage = $"Saved {Themes.Count} theme(s)";
        OnPropertyChanged(nameof(SaveStateLabel));
    }

    [RelayCommand]
    private void ApplyThemeToPresentation()
    {
        if (CurrentPresentation == null || SelectedTheme == null || SelectedThemeSlide == null || string.IsNullOrWhiteSpace(_currentPresentationPath))
            return;

        var targets = CurrentPresentation.Slides
            .Where(slide => !string.IsNullOrWhiteSpace(SelectedThemeSlide.LayoutType)
                            && string.Equals(slide.LayoutType, SelectedThemeSlide.LayoutType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (targets.Count == 0)
            targets = CurrentPresentation.Slides.ToList();

        foreach (var slide in targets)
            _themeApplier.ApplyLinkedTheme(CurrentPresentation, slide, SelectedTheme, SelectedThemeSlide);

        CurrentPresentation.Manifest.UpdatedAt = DateTime.UtcNow.ToString("O");
        _projects.Save(CurrentPresentation, _currentPresentationPath);
        StatusMessage = $"Linked {SelectedTheme.Name} to {targets.Count} slide(s)";
    }

    private void CommitThemeSlideMutation(Action<ThemeTemplateSlide> mutation)
    {
        if (SelectedTheme == null || SelectedThemeSlide == null)
            return;

        var slideIndex = SelectedTheme.Slides.FindIndex(slide => string.Equals(slide.Id, SelectedThemeSlide.Id, StringComparison.OrdinalIgnoreCase));
        if (slideIndex < 0)
            return;

        var currentSlide = SelectedTheme.Slides[slideIndex];
        var clone = PresentationModelUtilities.DeepClone(currentSlide) ?? new ThemeTemplateSlide();
        mutation(clone);
        clone.Layers = PresentationModelUtilities.GetThemeSlideLayers(clone);
        clone.LegacyTextLayers = null;
        if (AreEquivalent(currentSlide, clone))
            return;

        SelectedTheme.Slides[slideIndex] = clone;
        SelectedTheme.UpdatedAt = DateTime.UtcNow.ToString("O");
        RefreshThemeSlides();
        SelectThemeSlideById(clone.Id);
        MarkDirty("Updated theme slide");
    }

    private void CommitLayerMutation(Action<SlideLayer> mutation)
    {
        if (SelectedLayer == null)
            return;

        var layerId = SelectedLayer.Id;
        CommitThemeSlideMutation(slide =>
        {
            var layer = slide.Layers.FirstOrDefault(candidate => string.Equals(candidate.Id, layerId, StringComparison.OrdinalIgnoreCase));
            if (layer == null)
                return;

            mutation(layer);
            PresentationModelUtilities.NormalizeLayer(layer, SelectedTheme?.BaseSize);
        });
    }

    private void RefreshThemeSlides()
    {
        ThemeSlides.Clear();
        if (SelectedTheme == null)
            return;

        foreach (var slide in SelectedTheme.Slides)
            ThemeSlides.Add(slide);
    }

    private void RefreshLayers()
    {
        Layers.Clear();
        if (SelectedThemeSlide == null)
        {
            SelectedLayer = null;
            return;
        }

        foreach (var layer in SelectedThemeSlide.Layers)
            Layers.Add(layer);

        if (SelectedLayer != null)
            SelectedLayer = Layers.FirstOrDefault(layer => string.Equals(layer.Id, SelectedLayer.Id, StringComparison.OrdinalIgnoreCase));
        else
            SelectedLayer = Layers.FirstOrDefault();
    }

    private void RefreshSourceSlides()
    {
        SourceSlides.Clear();
        if (CurrentPresentation == null)
            return;

        foreach (var slide in CurrentPresentation.Slides)
            SourceSlides.Add(slide);
    }

    private void NotifyLayerState()
    {
        OnPropertyChanged(nameof(HasSelectedLayer));
        OnPropertyChanged(nameof(HasSelectedTextLayer));
        OnPropertyChanged(nameof(HasSelectedShapeLayer));
        OnPropertyChanged(nameof(ThemePreviewSlide));
        OnPropertyChanged(nameof(SelectedLayerName));
        OnPropertyChanged(nameof(SelectedLayerX));
        OnPropertyChanged(nameof(SelectedLayerY));
        OnPropertyChanged(nameof(SelectedLayerWidth));
        OnPropertyChanged(nameof(SelectedLayerHeight));
        OnPropertyChanged(nameof(SelectedLayerRotation));
        OnPropertyChanged(nameof(SelectedLayerOpacity));
        OnPropertyChanged(nameof(SelectedTextContent));
        OnPropertyChanged(nameof(SelectedTextBindingRole));
        OnPropertyChanged(nameof(SelectedTextFontFamily));
        OnPropertyChanged(nameof(SelectedTextFontSize));
        OnPropertyChanged(nameof(SelectedTextColor));
        OnPropertyChanged(nameof(SelectedShapeType));
        OnPropertyChanged(nameof(SelectedShapeFillColor));
        OnPropertyChanged(nameof(SelectedShapeStrokeColor));
    }

    private static string NormalizeAspectRatio(string? aspectRatio) =>
        aspectRatio?.Trim() switch
        {
            "4:3" => "4:3",
            "16:10" => "16:10",
            _ => "16:9",
        };

    private void MarkDirty(string message)
    {
        IsDirty = true;
        StatusMessage = message;
        OnPropertyChanged(nameof(SaveStateLabel));
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts = null;

        if (!_settings.Settings.Editor.AutoSaveEnabled)
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
                await SaveLibraryAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Theme library auto-save failed.");
            }
        });
    }

    private static void RegenerateThemeIds(ThemeTemplate theme)
    {
        theme.Id = Guid.NewGuid().ToString("N");
        theme.CreatedAt = DateTime.UtcNow.ToString("O");
        theme.UpdatedAt = theme.CreatedAt;

        foreach (var slide in theme.Slides)
        {
            slide.Id = Guid.NewGuid().ToString("N");
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