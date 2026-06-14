using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChurchPresenter.ViewModels;

public partial class ShowViewModel
{
    private const double ShowControlsFullFooterMinimumWidth = WorkspaceDto.ShowOutputPanelDefaultWidthDpi + 80;
    private const double ShowControlsAudioFullFooterMinimumWidth = WorkspaceDto.ShowOutputPanelMinWidthDpi;

    private bool _showControlsLoading;
    private readonly List<ShowPropPanelItem> _allShowProps = [];
    private readonly List<ShowMacroPanelItem> _allShowMacros = [];

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

    [ObservableProperty]
    private double _audioFadeInSeconds = 0.5;

    [ObservableProperty]
    private double _propDissolveSeconds = 0.3;

    [ObservableProperty]
    private string _selectedPropCollectionId = string.Empty;

    [ObservableProperty]
    private string _selectedMacroCollectionId = string.Empty;

    [ObservableProperty]
    private bool _propsTileViewSelected = true;

    [ObservableProperty]
    private bool _macrosTileViewSelected = true;

    public ObservableCollection<ShowAudioPlaylistPanelItem> ShowAudioPlaylists { get; } = new();

    public ObservableCollection<ShowAudioCuePanelItem> ShowAudioItems { get; } = new();

    public ObservableCollection<ShowStageScreenPanelItem> ShowStageScreens { get; } = new();

    public ObservableCollection<ShowStageLayoutPanelItem> ShowStageLayouts { get; } = new();

    public ObservableCollection<ShowTimerPanelItem> ShowTimers { get; } = new();

    public ObservableCollection<ShowMessagePanelItem> ShowMessages { get; } = new();

    public ObservableCollection<ShowCollectionPanelItem> ShowPropCollections { get; } = new();

    public ObservableCollection<ShowPropPanelItem> ShowProps { get; } = new();

    public ObservableCollection<ShowCollectionPanelItem> ShowMacroCollections { get; } = new();

    public ObservableCollection<ShowMacroPanelItem> ShowMacros { get; } = new();

    public IAsyncRelayCommand CreateAudioPlaylistCommand { get; private set; } = null!;

    public IAsyncRelayCommand CreateTimerCommand { get; private set; } = null!;

    public IAsyncRelayCommand CreateMessageCommand { get; private set; } = null!;

    public IAsyncRelayCommand CreatePropCommand { get; private set; } = null!;

    public IAsyncRelayCommand CreateMacroCommand { get; private set; } = null!;

    public IRelayCommand ShowPropsTileViewCommand { get; private set; } = null!;

    public IRelayCommand ShowPropsListViewCommand { get; private set; } = null!;

    public IRelayCommand ShowMacrosTileViewCommand { get; private set; } = null!;

    public IRelayCommand ShowMacrosListViewCommand { get; private set; } = null!;

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

    public string SelectedPropCollectionName =>
        ResolveCollectionName(ShowPropCollections, SelectedPropCollectionId, "All Props");

    public string SelectedMacroCollectionName =>
        ResolveCollectionName(ShowMacroCollections, SelectedMacroCollectionId, "All Macros");

    public string AudioFadeInDurationLabel => $"{AudioFadeInSeconds:0.#}s";

    public string PropDissolveDurationLabel => $"{PropDissolveSeconds:0.#}s";

    public bool ShowControlsCompactFooter => OutputPanelWidth < ShowControlsFullFooterMinimumWidth;

    public bool ShowControlsCompactAudioFooter => OutputPanelWidth < ShowControlsAudioFullFooterMinimumWidth;

    public bool PropsListViewSelected => !PropsTileViewSelected;

    public bool MacrosListViewSelected => !MacrosTileViewSelected;

    private void InitializeShowControlsCommands()
    {
        CreateAudioPlaylistCommand = new AsyncRelayCommand(CreateAudioPlaylistAsync);
        CreateTimerCommand = new AsyncRelayCommand(CreateTimerAsync);
        CreateMessageCommand = new AsyncRelayCommand(CreateMessageAsync);
        CreatePropCommand = new AsyncRelayCommand(CreatePropAsync);
        CreateMacroCommand = new AsyncRelayCommand(CreateMacroAsync);
        ShowPropsTileViewCommand = new RelayCommand(() => PropsTileViewSelected = true);
        ShowPropsListViewCommand = new RelayCommand(() => PropsTileViewSelected = false);
        ShowMacrosTileViewCommand = new RelayCommand(() => MacrosTileViewSelected = true);
        ShowMacrosListViewCommand = new RelayCommand(() => MacrosTileViewSelected = false);
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
        ApplySelectedAudioCue(value);

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
        ApplySelectedProp(value);

    partial void OnSelectedMacroChanged(ShowMacroPanelItem? value) =>
        OnPropertyChanged(nameof(HasSelectedMacro));

    partial void OnSelectedPropCollectionIdChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedPropCollectionName));
        RefreshVisibleProps();
    }

    partial void OnSelectedMacroCollectionIdChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedMacroCollectionName));
        RefreshVisibleMacros();
    }

    partial void OnPropsTileViewSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(PropsListViewSelected));
    }

    partial void OnMacrosTileViewSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(MacrosListViewSelected));
    }

    partial void OnAudioFadeInSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(AudioFadeInDurationLabel));
    }

    partial void OnPropDissolveSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(PropDissolveDurationLabel));
    }

    partial void OnOutputPanelWidthChanged(double value)
    {
        OnPropertyChanged(nameof(ShowControlsCompactFooter));
        OnPropertyChanged(nameof(ShowControlsCompactAudioFooter));
    }

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
            ReplaceShowControls(ShowPropCollections, WithAllCollection(snapshot.PropCollections, "All Props", snapshot.Props.Count));
            ReplaceShowControls(ShowMacroCollections, WithAllCollection(snapshot.MacroCollections, "All Macros", snapshot.Macros.Count));
            _allShowProps.Clear();
            _allShowProps.AddRange(snapshot.Props);
            _allShowMacros.Clear();
            _allShowMacros.AddRange(snapshot.Macros);
            RefreshVisibleProps();
            RefreshVisibleMacros();
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

        if (await _showControls.TriggerAudioCueAsync(SelectedAudioCue.Id, AudioFadeInSeconds).ConfigureAwait(true))
        {
            _mediaTransportPresenter.Refresh();
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
            CollectionId = string.IsNullOrWhiteSpace(SelectedPropCollectionId) ? null : SelectedPropCollectionId,
            Text = "New prop",
            Transition = new SlideTransition
            {
                Type = "dissolve",
                Duration = (int)Math.Round(PropDissolveSeconds * 1000),
            },
        }).ConfigureAwait(true);
        StatusMessage = $"Created prop \"{prop.Name}\".";
        await LoadShowControlsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleSelectedPropAsync()
    {
        if (SelectedProp == null)
            return;

        if (await _showControls.TogglePropAsync(SelectedProp.Id, PropDissolveSeconds).ConfigureAwait(true))
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
            CollectionId = string.IsNullOrWhiteSpace(SelectedMacroCollectionId) ? null : SelectedMacroCollectionId,
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

    public void SelectPropCollection(string collectionId) =>
        SelectedPropCollectionId = collectionId ?? string.Empty;

    public void SelectMacroCollection(string collectionId) =>
        SelectedMacroCollectionId = collectionId ?? string.Empty;

    private void RefreshVisibleProps()
    {
        IEnumerable<ShowPropPanelItem> visible = string.IsNullOrWhiteSpace(SelectedPropCollectionId)
            ? _allShowProps
            : _allShowProps.Where(prop => string.Equals(prop.CollectionId, SelectedPropCollectionId, StringComparison.OrdinalIgnoreCase));
        ReplaceShowControls(ShowProps, visible.ToArray());
        if (SelectedProp == null || !ShowProps.Any(prop => string.Equals(prop.Id, SelectedProp.Id, StringComparison.OrdinalIgnoreCase)))
            SelectedProp = ShowProps.FirstOrDefault();
    }

    private void RefreshVisibleMacros()
    {
        IEnumerable<ShowMacroPanelItem> visible = string.IsNullOrWhiteSpace(SelectedMacroCollectionId)
            ? _allShowMacros
            : _allShowMacros.Where(macro => string.Equals(macro.CollectionId, SelectedMacroCollectionId, StringComparison.OrdinalIgnoreCase));
        ReplaceShowControls(ShowMacros, visible.ToArray());
        if (SelectedMacro == null || !ShowMacros.Any(macro => string.Equals(macro.Id, SelectedMacro.Id, StringComparison.OrdinalIgnoreCase)))
            SelectedMacro = ShowMacros.FirstOrDefault();
    }

    private void ApplySelectedAudioCue(ShowAudioCuePanelItem? value)
    {
        if (value != null)
            AudioFadeInSeconds = value.FadeInSeconds;
        OnPropertyChanged(nameof(HasSelectedAudioCue));
    }

    private void ApplySelectedProp(ShowPropPanelItem? value)
    {
        if (value != null)
            PropDissolveSeconds = value.DissolveSeconds;
        OnPropertyChanged(nameof(HasSelectedProp));
    }

    private static ShowCollectionPanelItem[] WithAllCollection(
        IReadOnlyList<ShowCollectionPanelItem> source,
        string allName,
        int allCount)
    {
        return
        [
            new ShowCollectionPanelItem { Id = string.Empty, Name = allName, Count = allCount },
            .. source,
        ];
    }

    private static string ResolveCollectionName(
        IEnumerable<ShowCollectionPanelItem> collections,
        string collectionId,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
            return fallback;

        return collections.FirstOrDefault(collection => string.Equals(collection.Id, collectionId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? fallback;
    }
}
