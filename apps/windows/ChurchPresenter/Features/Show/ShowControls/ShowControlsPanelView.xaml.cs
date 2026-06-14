using System.ComponentModel;

using ChurchPresenter.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Features.Show.ShowControls;

public sealed partial class ShowControlsPanelView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(ShowViewModel),
        typeof(ShowControlsPanelView),
        new PropertyMetadata(null, OnViewModelChanged));

    public ShowControlsPanelView() => InitializeComponent();

    public ShowViewModel? ViewModel
    {
        get => (ShowViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ShowControlsPanelView view)
            return;

        if (e.OldValue is ShowViewModel oldVm)
            oldVm.PropertyChanged -= view.ViewModel_PropertyChanged;

        if (e.NewValue is ShowViewModel newVm)
            newVm.PropertyChanged += view.ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
    }
}
