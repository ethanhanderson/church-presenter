using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ChurchPresenter.Converters;

/// <summary>Maps <see cref="bool"/> to <see cref="Visibility"/>.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
        var flag = value is bool b && b;
        if (invert)
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => DependencyProperty.UnsetValue;
}