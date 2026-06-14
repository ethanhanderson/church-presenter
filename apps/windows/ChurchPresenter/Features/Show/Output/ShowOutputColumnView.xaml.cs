using ChurchPresenter.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Features.Show.Output;

public sealed partial class ShowOutputColumnView : UserControl
{
    private readonly IOutputRoutingService _outputRouting;

    private Flyout? _clearGroupsFlyout;
    private ClearGroupsFlyoutContent? _clearGroupsFlyoutContent;

    public ShowOutputColumnView()
    {
        InitializeComponent();
        _outputRouting = App.Services.GetRequiredService<IOutputRoutingService>();
        ActualThemeChanged += (_, _) => ApplyPanelChromeState();
    }

    /// <summary>Parent grid column whose width tracks output panel resize.</summary>
    public ColumnDefinition? HostColumnDefinition { get; set; }

    /// <summary>Coordinate root for splitter pointer math (parent layout grid).</summary>
    public FrameworkElement? LayoutCoordinateRoot { get; set; }

    public ShowViewModel ViewModel
    {
        get => (ShowViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ShowViewModel),
            typeof(ShowOutputColumnView),
            new PropertyMetadata(null, OnViewModelChanged));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ShowOutputColumnView view)
            return;

        if (e.OldValue is ShowViewModel oldVm)
            oldVm.PropertyChanged -= view.ViewModel_OutputColumnPropertyChanged;

        if (e.NewValue is ShowViewModel newVm)
            newVm.PropertyChanged += view.ViewModel_OutputColumnPropertyChanged;

        view.SyncOutputPanelColumnWidth();
        view.SyncMediaTransportTargetSelector();
        view.SyncShowControlsPanelSelector();
        view.SyncProgramOutputSurface();
    }

    public ProgramOutputSurface? PreviewHost => ProgramOutputSurfaceHost;

    internal void SyncProgramOutputSurface()
    {
        if (ProgramOutputSurfaceHost is null || ViewModel is null)
            return;

        ProgramOutputSurfaceHost.ViewModel = ViewModel.ProgramOutput;
    }

    internal void ApplyPanelChromeState()
    {
        var normalBrush = ResolveThemeBrush("CardStrokeColorDefaultBrush", "ShowChromeDividerBrush");
        if (normalBrush == null)
            return;

        if (OutputPanelChromeBorder != null)
            OutputPanelChromeBorder.BorderBrush = normalBrush;
    }

    internal void SyncOutputPanelColumnWidth()
    {
        if (HostColumnDefinition == null || ViewModel == null)
            return;

        var w = WorkspaceDto.NormalizeStoredShowOutputPanelWidth(ViewModel.OutputPanelWidth);
        HostColumnDefinition.Width = new GridLength(w);
    }

    private void ViewModel_OutputColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShowViewModel.OutputPanelWidth))
            SyncOutputPanelColumnWidth();
    }

    private Brush? ResolveThemeBrush(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (Resources.TryGetValue(key, out var local) && local is Brush lb)
                return lb;
            if (Application.Current?.Resources.TryGetValue(key, out var app) == true && app is Brush ab)
                return ab;
        }

        return null;
    }
}
