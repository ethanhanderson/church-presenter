using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Controls.Rendering;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Controls;

/// <summary>
/// Active editor preview canvas backed by the host-neutral slide scene compiler.
/// </summary>
public sealed class EditorSceneCanvas : UserControl
{
    /// <summary>Dependency property for <see cref="Project"/>.</summary>
    public static readonly DependencyProperty ProjectProperty =
        DependencyProperty.Register(
            nameof(Project),
            typeof(PresentationProject),
            typeof(EditorSceneCanvas),
            new PropertyMetadata(null, OnSceneInputChanged));

    /// <summary>Dependency property for <see cref="Slide"/>.</summary>
    public static readonly DependencyProperty SlideProperty =
        DependencyProperty.Register(
            nameof(Slide),
            typeof(PresentationSlide),
            typeof(EditorSceneCanvas),
            new PropertyMetadata(null, OnSceneInputChanged));

    private readonly Grid _root = new();
    private readonly ISlideSceneCompiler _compiler;
    private readonly IWinUiSceneHost _sceneHost = new WinUiSceneHost();

    /// <summary>Creates the editor scene canvas.</summary>
    public EditorSceneCanvas()
    {
        _compiler = App.Services.GetService<ISlideSceneCompiler>() ?? new SlideSceneCompiler();
        Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = _root,
        };
        Loaded += (_, _) => Refresh();
    }

    /// <summary>Presentation project to render.</summary>
    public PresentationProject? Project
    {
        get => (PresentationProject?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    /// <summary>Slide to render.</summary>
    public PresentationSlide? Slide
    {
        get => (PresentationSlide?)GetValue(SlideProperty);
        set => SetValue(SlideProperty, value);
    }

    private static void OnSceneInputChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        if (dependencyObject is EditorSceneCanvas canvas)
            canvas.Refresh();
    }

    private void Refresh()
    {
        if (!IsLoaded || Project == null || Slide == null)
        {
            _root.Children.Clear();
            _root.Width = 1920;
            _root.Height = 1080;
            return;
        }

        SceneCompileResult result = _compiler.Compile(new SceneCompileRequest
        {
            Project = Project,
            Slide = Slide,
            Intent = RenderIntent.Editor,
        });

        _root.Children.Clear();
        _root.Width = result.Scene.RenderSize.Width;
        _root.Height = result.Scene.RenderSize.Height;
        _sceneHost.Apply(_root, result.Scene, new WinUiSceneHostOptions());
    }
}
