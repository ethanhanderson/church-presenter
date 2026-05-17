using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChurchPresenter.Views;

internal static class ContentDialogSetup
{
    public static T ConfigureForPage<T>(this T dialog, Page owner)
        where T : ContentDialog
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(owner);

        dialog.XamlRoot = owner.XamlRoot;
        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style)
            && style is Style defaultStyle)
        {
            dialog.Style = defaultStyle;
        }

        dialog.RequestedTheme = owner.ActualTheme;

        return dialog;
    }
}
