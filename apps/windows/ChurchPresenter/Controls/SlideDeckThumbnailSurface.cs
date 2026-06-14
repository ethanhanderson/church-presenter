#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
namespace ChurchPresenter.Controls;

/// <summary>
/// Deck slide thumbnail: checkerboard and optional tint sit behind a crossfading slide image.
/// Background chrome updates without re-raster; only slide content changes swap the image.
/// </summary>
public sealed class SlideDeckThumbnailSurface : Grid
{
    private static readonly TimeSpan CrossfadeDuration = TimeSpan.FromMilliseconds(150);

    public static readonly DependencyProperty UseInstantImageSwapProperty =
        DependencyProperty.Register(
            nameof(UseInstantImageSwap),
            typeof(bool),
            typeof(SlideDeckThumbnailSurface),
            new PropertyMetadata(false));

    private readonly Border _checkerboardHost;
    private readonly Border _backdropHost;
    private readonly Image _imageA;
    private readonly Image _imageB;
    private bool _frontIsA = true;
    private int _crossfadeGeneration;

    public SlideDeckThumbnailSurface()
    {
        _checkerboardHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _backdropHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _imageA = CreateImage();
        _imageB = CreateImage();

        Children.Add(_checkerboardHost);
        Children.Add(_backdropHost);
        Children.Add(_imageA);
        Children.Add(_imageB);
    }

    public static readonly DependencyProperty SlideImageProperty =
        DependencyProperty.Register(
            nameof(SlideImage),
            typeof(ImageSource),
            typeof(SlideDeckThumbnailSurface),
            new PropertyMetadata(null, OnSlideImageChanged));

    public static readonly DependencyProperty CheckerboardBrushProperty =
        DependencyProperty.Register(
            nameof(CheckerboardBrush),
            typeof(Brush),
            typeof(SlideDeckThumbnailSurface),
            new PropertyMetadata(null, OnCheckerboardBrushChanged));

    public static readonly DependencyProperty BackdropBrushProperty =
        DependencyProperty.Register(
            nameof(BackdropBrush),
            typeof(Brush),
            typeof(SlideDeckThumbnailSurface),
            new PropertyMetadata(null, OnBackdropBrushChanged));

    public static readonly DependencyProperty ShowsCheckerboardProperty =
        DependencyProperty.Register(
            nameof(ShowsCheckerboard),
            typeof(bool),
            typeof(SlideDeckThumbnailSurface),
            new PropertyMetadata(true, OnShowsCheckerboardChanged));

    public static readonly DependencyProperty ShowsBackdropProperty =
        DependencyProperty.Register(
            nameof(ShowsBackdrop),
            typeof(bool),
            typeof(SlideDeckThumbnailSurface),
            new PropertyMetadata(false, OnShowsBackdropChanged));

    public static readonly DependencyProperty ImageStretchProperty =
        DependencyProperty.Register(
            nameof(ImageStretch),
            typeof(Stretch),
            typeof(SlideDeckThumbnailSurface),
            new PropertyMetadata(Stretch.Uniform, OnImageStretchChanged));

    public ImageSource? SlideImage
    {
        get => (ImageSource?)GetValue(SlideImageProperty);
        set => SetValue(SlideImageProperty, value);
    }

    public Brush? CheckerboardBrush
    {
        get => (Brush?)GetValue(CheckerboardBrushProperty);
        set => SetValue(CheckerboardBrushProperty, value);
    }

    public Brush? BackdropBrush
    {
        get => (Brush?)GetValue(BackdropBrushProperty);
        set => SetValue(BackdropBrushProperty, value);
    }

    public bool ShowsCheckerboard
    {
        get => (bool)GetValue(ShowsCheckerboardProperty);
        set => SetValue(ShowsCheckerboardProperty, value);
    }

    public bool ShowsBackdrop
    {
        get => (bool)GetValue(ShowsBackdropProperty);
        set => SetValue(ShowsBackdropProperty, value);
    }

    public Stretch ImageStretch
    {
        get => (Stretch)GetValue(ImageStretchProperty);
        set => SetValue(ImageStretchProperty, value);
    }

    /// <summary>When true, slide image updates swap immediately (no crossfade). Used for Quick Edit typing preview.</summary>
    public bool UseInstantImageSwap
    {
        get => (bool)GetValue(UseInstantImageSwapProperty);
        set => SetValue(UseInstantImageSwapProperty, value);
    }

    private static Image CreateImage() =>
        new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Stretch = Stretch.Uniform,
            Opacity = 1,
        };

    private static void OnSlideImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (SlideDeckThumbnailSurface)d;
        surface.ApplySlideImage(e.NewValue as ImageSource);
    }

    private static void OnCheckerboardBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (SlideDeckThumbnailSurface)d;
        surface._checkerboardHost.Background = e.NewValue as Brush;
    }

    private static void OnBackdropBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (SlideDeckThumbnailSurface)d;
        surface._backdropHost.Background = e.NewValue as Brush;
    }

    private static void OnShowsCheckerboardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (SlideDeckThumbnailSurface)d;
        surface._checkerboardHost.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnShowsBackdropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (SlideDeckThumbnailSurface)d;
        surface._backdropHost.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnImageStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (SlideDeckThumbnailSurface)d;
        var stretch = (Stretch)e.NewValue;
        surface._imageA.Stretch = stretch;
        surface._imageB.Stretch = stretch;
    }

    private void ApplySlideImage(ImageSource? newSource)
    {
        Image front = _frontIsA ? _imageA : _imageB;
        Image back = _frontIsA ? _imageB : _imageA;

        if (newSource is null)
        {
            _crossfadeGeneration++;
            front.Source = null;
            back.Source = null;
            front.Opacity = 1;
            back.Opacity = 0;
            return;
        }

        if (front.Source is null && back.Source is null)
        {
            front.Source = newSource;
            front.Opacity = 1;
            back.Opacity = 0;
            return;
        }

        if (ReferenceEquals(front.Source, newSource))
            return;

        if (ReferenceEquals(back.Source, newSource) && back.Opacity > 0.99)
        {
            _frontIsA = !_frontIsA;
            return;
        }

        if (UseInstantImageSwap)
        {
            SwapInstant(newSource);
            return;
        }

        _ = CrossfadeAsync(newSource);
    }

    private void SwapInstant(ImageSource newSource)
    {
        _crossfadeGeneration++;
        Image front = _frontIsA ? _imageA : _imageB;
        Image back = _frontIsA ? _imageB : _imageA;
        back.Source = null;
        back.Opacity = 0;
        front.Source = newSource;
        front.Opacity = 1;
    }

    private async Task CrossfadeAsync(ImageSource newSource)
    {
        int generation = ++_crossfadeGeneration;
        Image front = _frontIsA ? _imageA : _imageB;
        Image back = _frontIsA ? _imageB : _imageA;

        back.Source = newSource;
        back.Opacity = 0;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = CrossfadeDuration,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, back);
        Storyboard.SetTargetProperty(animation, nameof(Opacity));
        storyboard.Children.Add(animation);

        var tcs = new TaskCompletionSource<bool>();
        storyboard.Completed += (_, _) => tcs.TrySetResult(true);
        storyboard.Begin();

        try
        {
            await tcs.Task.ConfigureAwait(true);
        }
        catch (Exception)
        {
            return;
        }

        if (generation != _crossfadeGeneration)
            return;

        _frontIsA = !_frontIsA;
        front.Source = null;
        front.Opacity = 0;
        back.Opacity = 1;
    }
}
