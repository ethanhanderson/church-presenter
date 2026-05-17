using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ChurchPresenter.Converters;

/// <summary>Maps <see cref="bool"/> to a uniform <see cref="Thickness"/> (e.g. selection ring width).</summary>
public sealed class BoolToThicknessConverter : IValueConverter
{
    public double TrueThickness { get; set; } = 2;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var t = value is true ? TrueThickness : 0;
        return new Thickness(t);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}