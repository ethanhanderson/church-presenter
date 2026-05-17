
using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

namespace ChurchPresenter.Views;

/// <summary>View model for a single row in the transition picker list.</summary>
public sealed partial class TransitionPickerItem : ObservableObject
{
    public TransitionDefinition Definition { get; init; } = null!;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>Favorite star glyph — filled when favorite, outlined when not.</summary>
    public string FavGlyph => IsFavorite ? "\uE735" : "\uE734";

    /// <summary>Checkmark visibility for the selected row indicator.</summary>
    public Visibility SelectedVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavGlyph));
    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(SelectedVisibility));
}