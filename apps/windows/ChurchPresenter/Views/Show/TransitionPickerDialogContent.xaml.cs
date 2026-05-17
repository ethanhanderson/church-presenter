using System.Collections.ObjectModel;


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Windows.Foundation;
namespace ChurchPresenter.Views;

/// <summary>
/// ContentDialog content for choosing a presentation-wide default transition with hover preview.
/// </summary>
public sealed partial class TransitionPickerDialogContent : UserControl
{
    private const string PreviewHintText = "Drag across the preview to scrub the transition";

    private ShowViewModel? _viewModel;
    private TransitionPickerItem? _selectedItem;
    private readonly ObservableCollection<TransitionPickerItem> _allItems = new();
    private readonly ObservableCollection<TransitionPickerItem> _favItems = new();
    private readonly ObservableCollection<TransitionPickerItem> _recentItems = new();
    private bool _isHovering;
    private double _lastPreviewProgress;

    public TransitionPickerDialogContent()
    {
        InitializeComponent();
        AllList.ItemsSource = _allItems;
        FavoritesList.ItemsSource = _favItems;
        RecentsList.ItemsSource = _recentItems;
    }

    /// <summary>Bind the picker to the current ViewModel (called before showing).</summary>
    /// <param name="viewModel">
    /// Optional show-page view model used for favorites/recents.
    /// Pass <c>null</c> when editing a per-slide override (favorites are a show-page concern).
    /// </param>
    /// <param name="existingTransition">The currently set transition to pre-select, or <c>null</c>.</param>
    public void Initialize(ShowViewModel? viewModel, SlideTransition? existingTransition = null)
    {
        _viewModel = viewModel;
        PopulateItems();

        var existing = existingTransition;
        DurationSlider.Value = existing is { Duration: > 0 } ? existing.Duration : 400;
        var selectedKey = NormalizeSelectedKey(existing);
        if (selectedKey == null)
            ClearSelection();
        else
            SelectItemByKey(selectedKey);

        if (existing != null)
        {
            var dir = existing.GetParameter("direction", "fromLeft");
            foreach (ComboBoxItem cbi in DirectionCombo.Items)
            {
                if (string.Equals(cbi.Tag as string, dir, StringComparison.OrdinalIgnoreCase))
                {
                    DirectionCombo.SelectedItem = cbi;
                    break;
                }
            }

            var easingValue = existing.Easing?.Trim().ToLowerInvariant() ?? "";
            var easingSelected = false;
            foreach (ComboBoxItem cbi in EasingCombo.Items)
            {
                if (string.Equals(cbi.Tag as string, easingValue, StringComparison.OrdinalIgnoreCase))
                {
                    EasingCombo.SelectedItem = cbi;
                    easingSelected = true;
                    break;
                }
            }
            if (!easingSelected) EasingCombo.SelectedIndex = 0;
        }
        else
        {
            if (DirectionCombo.SelectedIndex < 0) DirectionCombo.SelectedIndex = 0;
            if (EasingCombo.SelectedIndex < 0) EasingCombo.SelectedIndex = 0;
        }

        UpdateDurationLabel();
        ResetPreviewPosition();
    }

    /// <summary>Returns the configured <see cref="SlideTransition"/> or null to clear the default.</summary>
    public SlideTransition? BuildTransition()
    {
        if (_selectedItem == null)
            return null;

        var isCut = string.Equals(_selectedItem.Definition.Key, "cut", StringComparison.OrdinalIgnoreCase);

        var transition = new SlideTransition
        {
            Type = _selectedItem.Definition.Key,
            // Cut has no meaningful duration; preserve 0 so the engine treats it as instant.
            Duration = isCut ? 0 : (int)DurationSlider.Value,
        };

        if (!isCut)
        {
            // Easing: null tag means "default" — don't persist an empty string.
            var easingTag = (EasingCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            transition.Easing = string.IsNullOrEmpty(easingTag) ? null : easingTag;
        }

        if (DirectionRow.Visibility == Visibility.Visible
            && DirectionCombo.SelectedItem is ComboBoxItem dirItem)
        {
            transition.Parameters = new Dictionary<string, string>
            {
                ["direction"] = dirItem.Tag as string ?? "fromLeft",
            };
        }

        return transition;
    }

    // ── Population ───────────────────────────────────────────────────────────

    private void PopulateItems()
    {
        _allItems.Clear();
        _favItems.Clear();
        _recentItems.Clear();

        var favorites = _viewModel?.GetFavoriteTransitions() ?? new HashSet<string>();
        var recents = _viewModel?.GetRecentTransitions() ?? Array.Empty<string>();

        foreach (var def in TransitionCatalog.All)
        {
            var item = new TransitionPickerItem
            {
                Definition = def,
                IsFavorite = favorites.Contains(def.Key),
            };
            _allItems.Add(item);
            if (item.IsFavorite)
                _favItems.Add(item);
        }

        foreach (var key in recents)
        {
            var item = _allItems.FirstOrDefault(i => i.Definition.Key == key);
            if (item != null)
                _recentItems.Add(item);
        }

        FavoritesHeader.Visibility = _favItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentsHeader.Visibility = _recentItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectItemByKey(string key)
    {
        var item = _allItems.FirstOrDefault(i => string.Equals(i.Definition.Key, key, StringComparison.OrdinalIgnoreCase));

        if (item != null)
            SelectItem(item);
    }

    private void ClearSelection()
    {
        if (_selectedItem != null)
            _selectedItem.IsSelected = false;

        _selectedItem = null;
        FavoriteButton.Visibility = Visibility.Collapsed;
        FavoriteButton.IsEnabled = false;
        PreviewHint.Text = "Select a transition to preview it";
        ResetPreviewPosition();
    }

    private void SelectItem(TransitionPickerItem item)
    {
        if (_selectedItem != null)
            _selectedItem.IsSelected = false;

        _selectedItem = item;
        _selectedItem.IsSelected = true;

        var isCut = string.Equals(item.Definition.Key, "cut", StringComparison.OrdinalIgnoreCase);
        var hasDirParam = item.Definition.Params.Any(p => p.Key == "direction");

        // "cut" has no animation parameters — hide duration and easing rows.
        DurationSlider.IsEnabled = !isCut;
        DurationLabel.Opacity = isCut ? 0.4 : 1;
        EasingRow.Visibility = isCut ? Visibility.Collapsed : Visibility.Visible;

        DirectionRow.Visibility = hasDirParam ? Visibility.Visible : Visibility.Collapsed;
        if (hasDirParam && DirectionCombo.SelectedIndex < 0)
            DirectionCombo.SelectedIndex = 0;

        FavoriteButton.Visibility = Visibility.Visible;
        FavoriteButton.IsEnabled = true;
        FavoriteIcon.Glyph = item.IsFavorite ? "\uE735" : "\uE734";
        PreviewHint.Text = PreviewHintText;
        ResetPreviewPosition();
    }

    // ── Preview animation ────────────────────────────────────────────────────

    private void PreviewTracker_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovering = true;
        PreviewHint.Visibility = Visibility.Collapsed;
        PreviewPlayhead.Opacity = 0.95;
        UpdatePlayheadPosition(_lastPreviewProgress);
        AnimatePreviewToProgress(_lastPreviewProgress);
    }

    private void PreviewTracker_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovering = false;
        PreviewHint.Visibility = Visibility.Visible;
        PreviewPlayhead.Opacity = 0;
        ResetPreviewPosition();
    }

    private void PreviewTracker_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isHovering || _selectedItem == null)
            return;

        var point = e.GetCurrentPoint(PreviewHost);
        if (PreviewHost.ActualWidth <= 0)
            return;

        var progress = Math.Clamp(point.Position.X / PreviewHost.ActualWidth, 0.0, 1.0);
        _lastPreviewProgress = progress;
        UpdatePlayheadPosition(progress);
        AnimatePreviewToProgress(progress);
    }

    private void AnimatePreviewToProgress(double progress)
    {
        ResetPreviewVisuals();

        if (_selectedItem == null)
            return;

        var key = _selectedItem.Definition.Key;
        var dir = DirectionCombo.SelectedItem is ComboBoxItem di ? di.Tag as string : "fromLeft";

        switch (key)
        {
            case "cut":
                UpdateToSlideClip("fromLeft", 1);
                ToSlide.Opacity = progress < 0.5 ? 0 : 1;
                break;

            case "fade":
                UpdateToSlideClip("fromLeft", 1);
                ToSlide.Opacity = progress;
                break;

            case "wipe":
                ToSlide.Opacity = 1;
                UpdateToSlideClip(dir ?? "fromLeft", progress);
                break;

            case "slide":
                UpdateToSlideClip("fromLeft", 1);
                ToSlide.Opacity = 1;
                var horizontalOffset = dir == "fromRight"
                    ? GetPreviewContentWidth() * (1 - progress)
                    : -GetPreviewContentWidth() * (1 - progress);
                var verticalOffset = dir == "fromBottom"
                    ? GetPreviewContentHeight() * (1 - progress)
                    : -GetPreviewContentHeight() * (1 - progress);

                ToSlideTransform.TranslateX = dir is "fromLeft" or "fromRight" ? horizontalOffset : 0;
                ToSlideTransform.TranslateY = dir is "fromTop" or "fromBottom" ? verticalOffset : 0;
                FromSlideTransform.TranslateX = dir is "fromLeft" or "fromRight" ? -horizontalOffset * 0.18 : 0;
                FromSlideTransform.TranslateY = dir is "fromTop" or "fromBottom" ? -verticalOffset * 0.18 : 0;
                break;

            case "zoom-in":
                UpdateToSlideClip("fromLeft", 1);
                ToSlide.Opacity = progress;
                ToSlideTransform.ScaleX = 0.7 + 0.3 * progress;
                ToSlideTransform.ScaleY = 0.7 + 0.3 * progress;
                break;

            case "zoom-out":
                UpdateToSlideClip("fromLeft", 1);
                ToSlide.Opacity = progress;
                FromSlide.Opacity = 1 - (0.2 * progress);
                FromSlideTransform.ScaleX = 1 + (0.15 * progress);
                FromSlideTransform.ScaleY = 1 + (0.15 * progress);
                break;

            default:
                UpdateToSlideClip("fromLeft", 1);
                ToSlide.Opacity = progress;
                break;
        }
    }

    private void ResetPreviewPosition()
    {
        ResetPreviewVisuals();
        PreviewPlayhead.Opacity = _isHovering ? 0.95 : 0;
        UpdatePlayheadPosition(_lastPreviewProgress);
    }

    private void ResetPreviewVisuals()
    {
        FromSlide.Opacity = 1;
        ToSlide.Opacity = 0;
        ResetTransform(FromSlideTransform);
        ResetTransform(ToSlideTransform);
        UpdateToSlideClip("fromLeft", 1);
    }

    private void UpdatePlayheadPosition(double progress)
    {
        var availableWidth = Math.Max(PreviewHost.ActualWidth - PreviewPlayhead.Width, 0);
        PreviewPlayheadTransform.X = availableWidth * Math.Clamp(progress, 0, 1);
    }

    private void UpdateToSlideClip(string direction, double progress)
    {
        var width = GetPreviewContentWidth();
        var height = GetPreviewContentHeight();
        var clampedProgress = Math.Clamp(progress, 0, 1);

        if (width <= 0 || height <= 0)
        {
            ToSlideClip.Rect = Rect.Empty;
            return;
        }

        Rect rect = direction switch
        {
            "fromRight" => new Rect(width * (1 - clampedProgress), 0, width * clampedProgress, height),
            "fromTop" => new Rect(0, 0, width, height * clampedProgress),
            "fromBottom" => new Rect(0, height * (1 - clampedProgress), width, height * clampedProgress),
            _ => new Rect(0, 0, width * clampedProgress, height),
        };

        ToSlideClip.Rect = rect;
    }

    private double GetPreviewContentWidth() =>
        Math.Max(ToSlide.ActualWidth, Math.Max(PreviewHost.ActualWidth - 32, 0));

    private double GetPreviewContentHeight() =>
        Math.Max(ToSlide.ActualHeight, Math.Max(PreviewHost.ActualHeight - 32, 0));

    private static void ResetTransform(CompositeTransform transform)
    {
        transform.TranslateX = 0;
        transform.TranslateY = 0;
        transform.ScaleX = 1;
        transform.ScaleY = 1;
        transform.Rotation = 0;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void TransitionRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TransitionPickerItem item)
            SelectItem(item);
    }

    private void TransitionFav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TransitionPickerItem item)
            ToggleFavorite(item);
    }

    private void ToggleFavorite(TransitionPickerItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        FavoriteIcon.Glyph = _selectedItem?.IsFavorite == true ? "\uE735" : "\uE734";

        if (item.IsFavorite && !_favItems.Contains(item))
            _favItems.Add(item);
        else if (!item.IsFavorite)
            _favItems.Remove(item);

        FavoritesHeader.Visibility = _favItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_viewModel != null)
        {
            _ = _viewModel.SetFavoriteTransitionAsync(item.Definition.Key, item.IsFavorite);
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
            ToggleFavorite(_selectedItem);
    }

    private void DurationSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateDurationLabel();
    }

    private void DirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isHovering)
            AnimatePreviewToProgress(_lastPreviewProgress);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim().ToLowerInvariant();
        RefreshFilteredList(query);
    }

    private void RefreshFilteredList(string query)
    {
        _allItems.Clear();
        foreach (var def in TransitionCatalog.All)
        {
            if (!string.IsNullOrEmpty(query)
                && !def.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !def.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existing = _favItems.FirstOrDefault(i => i.Definition.Key == def.Key)
                ?? _recentItems.FirstOrDefault(i => i.Definition.Key == def.Key)
                ?? new TransitionPickerItem { Definition = def, IsFavorite = false };

            existing.IsSelected = _selectedItem?.Definition.Key == def.Key;
            _allItems.Add(existing);
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        DurationSlider.Value = 400;
        ClearSelection();
        DirectionCombo.SelectedIndex = 0;
        EasingCombo.SelectedIndex = 0;
        UpdateDurationLabel();
    }

    private void UpdateDurationLabel()
    {
        if (DurationLabel != null)
            DurationLabel.Text = $"{(int)DurationSlider.Value} ms";
    }

    private static string? NormalizeSelectedKey(SlideTransition? transition)
    {
        if (transition == null
            || string.IsNullOrWhiteSpace(transition.Type))
        {
            return null;
        }

        return TransitionCatalog.Find(transition.Type)?.Key
            ?? TransitionCatalog.FindOrDefault(transition.Type).Key;
    }
}