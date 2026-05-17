using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Views;

public sealed partial class SettingsDetailHeader : UserControl
{
    public static readonly DependencyProperty ParentSegmentTextProperty = DependencyProperty.Register(
        nameof(ParentSegmentText),
        typeof(string),
        typeof(SettingsDetailHeader),
        new PropertyMetadata("Settings", OnSegmentPropertyChanged));

    public static readonly DependencyProperty CurrentSegmentTextProperty = DependencyProperty.Register(
        nameof(CurrentSegmentText),
        typeof(string),
        typeof(SettingsDetailHeader),
        new PropertyMetadata(string.Empty, OnSegmentPropertyChanged));

    public string ParentSegmentText
    {
        get => (string)GetValue(ParentSegmentTextProperty);
        set => SetValue(ParentSegmentTextProperty, value);
    }

    public string CurrentSegmentText
    {
        get => (string)GetValue(CurrentSegmentTextProperty);
        set => SetValue(CurrentSegmentTextProperty, value);
    }

    public SettingsDetailHeader()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ParentLink.PointerEntered += ParentLink_PointerEntered;
        ParentLink.PointerExited += ParentLink_PointerExited;
        RefreshAutomationName();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ParentLink.PointerEntered -= ParentLink_PointerEntered;
        ParentLink.PointerExited -= ParentLink_PointerExited;
    }

    private static void OnSegmentPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsDetailHeader header)
            header.RefreshAutomationName();
    }

    private void RefreshAutomationName()
    {
        string parent = ParentSegmentText ?? string.Empty;
        string current = CurrentSegmentText ?? string.Empty;
        AutomationProperties.SetName(this, string.IsNullOrEmpty(current) ? parent : $"{parent} > {current}");
    }

    private void ParentLink_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Same as current-page title + shell nav: theme primary text (dark in light, light in dark).
        ParentLink.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    private void ParentLink_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ParentLink.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    private void ParentLink_Click(object sender, RoutedEventArgs e)
    {
        Frame? frame = FindParentFrame();
        SettingsNavigation.GoBack(frame);
    }

    private Frame? FindParentFrame()
    {
        DependencyObject? d = this;
        while (d != null)
        {
            if (d is Page page)
                return page.Frame;
            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }
}