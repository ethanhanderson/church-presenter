using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChurchPresenter.ViewModels;

public partial class ShowViewModel
{
    private bool _showControlsLoading;

    [ObservableProperty]
    private string _selectedShowControlsPanel = "audio";

    [ObservableProperty]
    private ShowAudioCuePanelItem? _selectedAudioCue;

    [ObservableProperty]
    private ShowStageScreenPanelItem? _selectedStageScreen;

    [ObservableProperty]
    private ShowStageLayoutPanelItem? _selectedStageLayout;

    [ObservableProperty]
    private ShowTimerPanelItem? _selectedTimer;

    [ObservableProperty]
    private ShowMessagePanelItem? _selectedMessage;

    [ObservableProperty]
    private ShowPropPanelItem? _selectedProp;

    [ObservableProperty]
    private ShowMacroPanelItem? _selectedMacro;

    [ObservableProperty]
    private string _stageMessageText = string.Empty;

    [ObservableProperty]
    private string _messageRuntimeText = string.Empty;

    public ObservableCollection<ShowAudioPlaylistPanelItem> ShowAudioPlaylists { get; } = new();

    public ObservableCollection<ShowAudioCuePanelItem> ShowAudioItems { get; } = new();

    public ObservableCollection<ShowStageScreenPanelItem> ShowStageScreens { get; } = new();

    public ObservableCollection<ShowStageLayoutPanelItem> ShowStageLayouts { get; } = new();

    public ObservableCollection<ShowTimerPanelItem> ShowTimers { get; } = new();

    public ObservableCollection<ShowMessagePanelItem> ShowMessages { get; } = new();

    public ObservableCollection<ShowPropPanelItem> ShowProps { get; } = new();

    public ObservableCollection<ShowMacroPanelItem> ShowMacros { get; } = new();

    public IAsyncRelayCommand CreateAudioPlaylistCommand { get; private set; } = null!;

    public IAsyncRelayCommand CreateTimerCommand { get; private set; } = null!;

    public IAsyncRelayCommand CreateMessageCommand { get; private set; } = null!;

    public IAsyncRelayCommand CreatePropCommand { get; private set; } = null!;

    public IAsyncRelayCommand CreateMacroCommand { get; private set; } = null!;

    public bool ShowControlsAudioSelected => SelectedShowControlsPanel == "audio";

    public bool ShowControlsStageSelected => SelectedShowControlsPanel == "stage";

    public bool ShowControlsTimersSelected => SelectedShowControlsPanel == "timers";

    public bool ShowControlsMessagesSelected => SelectedShowControlsPanel == "messages";

    public bool ShowControlsPropsSelected => SelectedShowControlsPanel == "props";

    public bool ShowControlsMacrosSelected => SelectedShowControlsPanel == "macros";

    public bool HasSelectedAudioCue => SelectedAudioCue != null;

    public bool HasSelectedStageAssignment => SelectedStageScreen != null && SelectedStageLayout != null;

    public bool HasSelectedTimer => SelectedTimer != null;

    public bool HasSelectedMessage => SelectedMessage != null;

    public bool HasSelectedProp => SelectedProp != null;

    public bool HasSelectedMacro => SelectedMacro != null;

    private void InitializeShowControlsCommands()
    {
        CreateAudioPlaylistCommand = new AsyncRelayCommand(CreateAudioPlaylistAsync);
        CreateTimerCommand = new AsyncRelayCommand(CreateTimerAsync);
        CreateMessageCommand = new AsyncRelayCommand(CreateMessageAsync);
        CreatePropCommand = new AsyncRelayCommand(CreatePropAsync);
        CreateMacroCommand = new AsyncRelayCommand(CreateMacroAsync);
    }

    partial void OnSelectedShowControlsPanelChanged(string value)
    {
        OnPropertyChanged(nameof(ShowControlsAudioSelected));
        OnPropertyChanged(nameof(ShowControlsStageSelected));
        OnPropertyChanged(nameof(ShowControlsTimersSelected));
        OnPropertyChanged(nameof(ShowControlsMessagesSelected));
        OnPropertyChanged(nameof(ShowControlsPropsSelected));
        OnPropertyChanged(nameof(ShowControlsMacrosSelected));
    }

    partial void OnSelectedAudioCueChanged(ShowAudioCuePanelItem? value) =>
        OnPropertyChanged(nameof(HasSelectedAudioCue));

    partial void OnSelectedStageScreenChanged(ShowStageScreenPanelItem? value) =>
        OnPropertyChanged(nameof(HasSelectedStageAssignment));

    partial void OnSelectedStageLayoutChanged(ShowStageLayoutPanelItem? value) =>
        OnPropertyChanged(nameof(HasSelectedStageAssignment));

    partial void OnSelectedTimerChanged(ShowTimerPanelItem? value) =>
        OnPropertyChanged(nameof(HasSelectedTimer));

    partial void OnSelectedMessageChanged(ShowMessagePanelItem? value)
    {
        MessageRuntimeText = value?.Template ?? string.Empty;
        OnPropertyChanged(nameof(HasSelectedMessage));
    }

    partial void OnSelectedPropChanged(ShowPropPanelItem? value) =>
        OnPropertyChanged(nameof(HasSelectedProp));

    partial void OnSelectedMacroChanged(ShowMacroPanelItem? value) =>
        OnPropertyChanged(nameof(HasSelectedMacro));

    public async Task LoadShowControlsAsync(CancellationToken cancellationToken = default)
    {
        if (_showControlsLoading)
            return;

        _showControlsLoading = true;
        try
        {
            ShowControlsSnapshot snapshot = await _showControls.LoadAsync(cancellationToken).ConfigureAwait(true);
            ReplaceShowControls(ShowAudioPlaylists, snapshot.AudioPlaylists);
            ReplaceShowControls(ShowAudioItems, snapshot.AudioItems);
            ReplaceShowControls(ShowStageScreens, snapshot.StageScreens);
            ReplaceShowControls(ShowStageLayouts, snapshot.StageLayouts);
            ReplaceShowControls(ShowTimers, snapshot.Timers);
            ReplaceShowControls(ShowMessages, snapshot.Messages);
            ReplaceShowControls(ShowProps, snapshot.Props);
            ReplaceShowControls(ShowMacros, snapshot.Macros);
            SelectedAudioCue ??= ShowAudioItems.FirstOrDefault();
            SelectedStageScreen ??= ShowStageScreens.FirstOrDefault();
            SelectedStageLayout ??= ShowStageLayouts.FirstOrDefault();
            SelectedTimer ??= ShowTimers.FirstOrDefault();
            SelectedMessage ??= ShowMessages.FirstOrDefault();
            SelectedProp ??= ShowProps.FirstOrDefault();
            SelectedMacro ??= ShowMacros.FirstOrDefault();
        }
        finally
        {
            _showControlsLoading = false;
        }
    }

    private async Task CreateAudioPlaylistAsync()
    {
        ShowAudioPlaylistDefinition playlist = await _showControls.SaveAudioPlaylistAsync(new ShowAudioPlaylistDefinition
        {
            Name = $"Audio Playlist {ShowAudioPlaylists.Count + 1}",
            TransitionSeconds = 0.5,
        }).ConfigureAwait(true);
        StatusMessage = $"Created audio playlist \"{playlist.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TriggerSelectedAudioCueAsync()
    {
        if (SelectedAudioCue == null)
            return;

        if (await _showControls.TriggerAudioCueAsync(SelectedAudioCue.Id).ConfigureAwait(true))
        {
            _playbackCoordinator.SelectedTransportTarget = MediaPlaybackTarget.AudioFiles;
            StatusMessage = $"Playing audio \"{SelectedAudioCue.Name}\".";
            await LoadShowControlsAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task AssignSelectedStageLayoutAsync()
    {
        if (SelectedStageScreen == null || SelectedStageLayout == null)
            return;

        if (await _showControls.SetStageLayoutAsync(SelectedStageScreen.ScreenId, SelectedStageLayout.Id).ConfigureAwait(true))
        {
            StatusMessage = $"Set {SelectedStageScreen.Name} to {SelectedStageLayout.Name}.";
            await LoadShowControlsAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ShowStageMessageAsync()
    {
        await _showControls.SetStageMessageAsync(StageMessageText, visible: true).ConfigureAwait(true);
        StatusMessage = "Stage message shown.";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task HideStageMessageAsync()
    {
        await _showControls.SetStageMessageAsync(StageMessageText, visible: false).ConfigureAwait(true);
        StatusMessage = "Stage message hidden.";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    private async Task CreateTimerAsync()
    {
        ShowTimerDefinition timer = await _showTimers.SaveTimerAsync(new ShowTimerDefinition
        {
            Name = $"Timer {ShowTimers.Count + 1}",
            Kind = "countdown",
            DurationSeconds = 300,
        }).ConfigureAwait(true);
        StatusMessage = $"Created timer \"{timer.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StartSelectedTimerAsync()
    {
        if (SelectedTimer == null)
            return;

        _showTimers.StartTimer(SelectedTimer.Id);
        StatusMessage = $"Started timer \"{SelectedTimer.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StopSelectedTimerAsync()
    {
        if (SelectedTimer == null)
            return;

        _showTimers.StopTimer(SelectedTimer.Id);
        StatusMessage = $"Stopped timer \"{SelectedTimer.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetSelectedTimerAsync()
    {
        if (SelectedTimer == null)
            return;

        _showTimers.ResetTimer(SelectedTimer.Id);
        StatusMessage = $"Reset timer \"{SelectedTimer.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    private async Task CreateMessageAsync()
    {
        ShowMessageDefinition message = await _showControls.SaveMessageAsync(new ShowMessageDefinition
        {
            Name = $"Message {ShowMessages.Count + 1}",
            Template = "Welcome [name]",
            Tokens =
            [
                new ShowMessageTokenDefinition { Id = "name", Name = "name", DefaultValue = "guest" },
            ],
        }).ConfigureAwait(true);
        StatusMessage = $"Created message \"{message.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ShowSelectedMessageAsync()
    {
        if (SelectedMessage == null)
            return;

        ShowMessageRuntimeTokenValue[] values = SelectedMessage.Tokens.Count == 0
            ? []
            : [new ShowMessageRuntimeTokenValue(SelectedMessage.Tokens[0].Id, MessageRuntimeText)];
        if (await _showControls.ShowMessageAsync(SelectedMessage.Id, values).ConfigureAwait(true))
        {
            StatusMessage = $"Shown message \"{SelectedMessage.Name}\".";
            await LoadShowControlsAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task HideSelectedMessageAsync()
    {
        if (SelectedMessage == null)
            return;

        if (await _showControls.HideMessageAsync(SelectedMessage.Id).ConfigureAwait(true))
        {
            StatusMessage = $"Hidden message \"{SelectedMessage.Name}\".";
            await LoadShowControlsAsync().ConfigureAwait(true);
        }
    }

    private async Task CreatePropAsync()
    {
        ShowPropDefinition prop = await _showControls.SavePropAsync(new ShowPropDefinition
        {
            Name = $"Prop {ShowProps.Count + 1}",
            Text = "New prop",
        }).ConfigureAwait(true);
        StatusMessage = $"Created prop \"{prop.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleSelectedPropAsync()
    {
        if (SelectedProp == null)
            return;

        if (await _showControls.TogglePropAsync(SelectedProp.Id).ConfigureAwait(true))
        {
            StatusMessage = $"Toggled prop \"{SelectedProp.Name}\".";
            await LoadShowControlsAsync().ConfigureAwait(true);
        }
    }

    private async Task CreateMacroAsync()
    {
        ShowMacroDefinition macro = await _showControls.SaveMacroAsync(new ShowMacroDefinition
        {
            Name = $"Macro {ShowMacros.Count + 1}",
            IconKey = "\uE756",
            Commands =
            [
                new ShowMacroCommandDefinition
                {
                    Id = "clear-all",
                    Kind = "clearGroup",
                    TargetId = "clear-all",
                },
            ],
        }).ConfigureAwait(true);
        StatusMessage = $"Created macro \"{macro.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExecuteSelectedMacroAsync()
    {
        if (SelectedMacro == null)
            return;

        if (await _showControls.ExecuteMacroAsync(SelectedMacro.Id).ConfigureAwait(true))
        {
            StatusMessage = $"Ran macro \"{SelectedMacro.Name}\".";
            await LoadShowControlsAsync().ConfigureAwait(true);
        }
    }

    private static void ReplaceShowControls<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (T item in source)
            target.Add(item);
    }
}
