using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Features.Show.Deck;

public sealed partial class ShowDeckView
{
    private async Task<string?> PromptForNameAsync(string title, string header, string defaultValue)
    {
        var textBox = new TextBox
        {
            Header = header,
            Text = defaultValue,
        };

        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = textBox,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? NormalizeDialogValue(textBox.Text)
            : null;
    }

    private static string? NormalizeDialogValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task OpenPresentationInEditorAsync(
        string presentationPath,
        string? libraryId,
        string? playlistId,
        string? slideId = null)
    {
        if (ViewModel is null)
            return;

        await ViewModel.RefreshCatalogAsync();
        if (!string.IsNullOrWhiteSpace(libraryId))
            ViewModel.SelectLibraryCommand.Execute(libraryId);
        else if (!string.IsNullOrWhiteSpace(playlistId))
            ViewModel.SelectPlaylistCommand.Execute(playlistId);

        if (!string.IsNullOrWhiteSpace(presentationPath))
        {
            await ViewModel.OpenPresentationFromPathAsync(presentationPath);
            if (!string.IsNullOrWhiteSpace(slideId))
                await ViewModel.ActivateSlideSelectionAsync(presentationPath, slideId);
        }

        if (App.MainWindow is MainWindow window)
            window.NavigateToEditorPage();
    }
}
