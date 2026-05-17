using System;
using System.Diagnostics;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Storage.Pickers;

namespace ChurchPresenter.Views;

public sealed partial class SettingsLibraryLocationPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsLibraryLocationPage()
        : this(App.Services)
    {
    }

    private SettingsLibraryLocationPage(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ViewModel = services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        SettingsPageLayout.BindSettingsColumnWidth(this, SettingsColumnRoot);
        await ViewModel.LoadLibraryManagementSectionAsync();
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = CreateFolderPicker();
        var folder = await picker.PickSingleFolderAsync();
        if (folder == null)
            return;

        await ViewModel.ChangeContentLibraryLocationAsync(folder.Path);
    }

    private async void ResetFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ChangeContentLibraryLocationAsync(null);
    }

    private async void RunAudit_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RunAuditAsync();
    }

    private async void ScanNow_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanContentLibraryAsync();
    }

    private async void RefreshLog_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshContentMaintenanceLogAsync();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ViewModel.ContentFolderPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ViewModel.ContentMaintenanceStatus = $"Could not open folder: {ex.Message}";
        }
    }

    private FolderPicker CreateFolderPicker()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        if (App.MainWindow != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        return picker;
    }
}