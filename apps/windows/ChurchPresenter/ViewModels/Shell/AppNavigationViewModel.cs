using System.Collections.ObjectModel;


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;

using Windows.Storage.Pickers;

namespace ChurchPresenter.ViewModels;

/// <summary>Application navigation and startup state for the main window chrome.</summary>
public partial class AppNavigationViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ICatalogService _catalog;
    private readonly ICpresDocumentService _cpres;
    private readonly IMonitorService _monitors;
    private readonly IOutputWindowService _outputWindows;
    private readonly IPlaybackEngine _live;
    private readonly IContentDirectoryService _content;
    private readonly IWorkspaceService _workspace;
    private readonly IContentStartupMaintenanceService _contentStartupMaintenance;
    private bool _initialized;

    public AppNavigationViewModel(
        ISettingsService settings,
        ICatalogService catalog,
        ICpresDocumentService cpres,
        IMonitorService monitors,
        IOutputWindowService outputWindows,
        IPlaybackEngine live,
        IContentDirectoryService content,
        IWorkspaceService workspace,
        IContentStartupMaintenanceService contentStartupMaintenance)
    {
        _settings = settings;
        _catalog = catalog;
        _cpres = cpres;
        _monitors = monitors;
        _outputWindows = outputWindows;
        _live = live;
        _content = content;
        _workspace = workspace;
        _contentStartupMaintenance = contentStartupMaintenance;
    }

    [ObservableProperty]
    private string _selectedTag = "Show";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _manifestPreview = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _monitorLines = [];

    public AppSettingsDto Settings => _settings.Settings;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;
        await _contentStartupMaintenance.StartAsync().ConfigureAwait(true);
        await _workspace.LoadAsync().ConfigureAwait(true);

        StatusMessage =
            $"Content: {_content.GetDocumentsDataDirectory()} | Libraries: {_catalog.Catalog.Libraries.Count} | Playlists: {_catalog.Catalog.Playlists.Count}";
    }

    [RelayCommand]
    private async Task OpenPresentationAsync(CancellationToken cancellationToken)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".cpres");

        Window? window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file == null)
            return;

        try
        {
            var parsed = _cpres.Open(file.Path);
            ManifestPreview = parsed.ManifestJson[..Math.Min(800, parsed.ManifestJson.Length)];
            StatusMessage = $"Opened {file.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Open failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        MonitorLines.Clear();
        foreach (var monitor in _monitors.GetMonitors())
        {
            MonitorLines.Add(
                $"{monitor.Index}: {monitor.Name} {monitor.Width}x{monitor.Height} @ ({monitor.X},{monitor.Y}) primary={monitor.IsPrimary}");
        }
    }

    [RelayCommand]
    private void ApplyAudienceOutput()
    {
        if (!_live.IsAudienceEnabled)
        {
            _outputWindows.CloseAll();
            _live.SetAudienceEnabled(false);
            StatusMessage = "Audience output off";
            return;
        }

        _outputWindows.OpenAudience();
        _live.SetAudienceEnabled(true);
        StatusMessage = "Audience output updated";
    }
}
