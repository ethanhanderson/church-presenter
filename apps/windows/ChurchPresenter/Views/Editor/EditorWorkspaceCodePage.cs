using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed class EditorWorkspaceCodePage : Page
{
    public EditorWorkspaceCodePage()
    {
        Content = new Grid
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"],
            Children =
            {
                new TextBlock
                {
                    Text = "Pure code editor diagnostic page",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 24,
                },
            },
        };
    }
}