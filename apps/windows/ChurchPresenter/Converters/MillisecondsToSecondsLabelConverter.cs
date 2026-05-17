using System.Globalization;

using Microsoft.UI.Xaml.Data;

namespace ChurchPresenter.Converters;

/// <summary>Formats a duration in milliseconds as a short seconds label (e.g. <c>0.2 s</c>, <c>3 s</c>).</summary>
public sealed class MillisecondsToSecondsLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double ms = value switch
        {
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            _ => 0,
        };
        var s = ms / 1000.0;
        var text = s.ToString("0.###", CultureInfo.CurrentCulture) + " s";
        return text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
