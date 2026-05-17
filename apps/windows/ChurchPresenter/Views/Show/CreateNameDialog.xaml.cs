using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

public sealed partial class CreateNameDialog : ContentDialog
{
    private readonly string _validationMessage;

    internal CreateNameDialog(
        string title,
        string fieldHeader,
        string placeholderText,
        string primaryButtonText,
        string validationMessage)
    {
        _validationMessage = validationMessage;

        InitializeComponent();

        Title = title;
        PrimaryButtonText = primaryButtonText;
        NameBox.Header = fieldHeader;
        NameBox.PlaceholderText = placeholderText;
        Loaded += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    internal string? Result { get; private set; }

    private void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NormalizeDialogValue(NameBox.Text);
        if (name == null)
        {
            ValidationText.Text = _validationMessage;
            ValidationText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        Result = name;
    }

    private static string? NormalizeDialogValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
