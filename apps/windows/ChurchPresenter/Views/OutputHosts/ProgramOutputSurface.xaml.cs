
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

/// <summary>
/// Shared live program-output surface used by audience output and the operator preview panel.
/// </summary>
public sealed partial class ProgramOutputSurface : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(OutputViewModel),
        typeof(ProgramOutputSurface),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PlaybackRegistrationModeProperty = DependencyProperty.Register(
        nameof(PlaybackRegistrationMode),
        typeof(MediaPlaybackRegistrationMode),
        typeof(ProgramOutputSurface),
        new PropertyMetadata(MediaPlaybackRegistrationMode.Authority));

    public ProgramOutputSurface()
    {
        InitializeComponent();
    }

    public OutputViewModel ViewModel
    {
        get => (OutputViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MediaPlaybackRegistrationMode PlaybackRegistrationMode
    {
        get => (MediaPlaybackRegistrationMode)GetValue(PlaybackRegistrationModeProperty);
        set => SetValue(PlaybackRegistrationModeProperty, value);
    }
}
