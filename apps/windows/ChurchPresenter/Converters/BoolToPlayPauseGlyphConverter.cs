using Microsoft.UI.Xaml.Data;

namespace ChurchPresenter.Converters;

/// <summary>
/// Returns the Segoe MDL2 pause glyph (&#xE769;) when <c>true</c> (playing),
/// or the play glyph (&#xE768;) when <c>false</c> (paused/stopped).
/// </summary>
public sealed class BoolToPlayPauseGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? "\uE769" : "\uE768";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}