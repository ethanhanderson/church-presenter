using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Media;
using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Show;

/// <inheritdoc />
public sealed class ShowControlsService(
    ISharedConfigService sharedConfig,
    ISettingsService settings,
    IMediaLibraryService mediaLibrary,
    ICuePreparationService cuePreparation,
    IPlaybackEngine playback,
    ILiveProductionFacade liveProduction,
    ILiveProductionQueryService liveProductionQuery,
    IOutputTopologyService topology,
    IStageLayoutRegistryService stageLayouts) : IShowControlsService
{
    private readonly ISharedConfigService _sharedConfig = sharedConfig ?? throw new ArgumentNullException(nameof(sharedConfig));
    private readonly ISettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IMediaLibraryService _mediaLibrary = mediaLibrary ?? throw new ArgumentNullException(nameof(mediaLibrary));
    private readonly ICuePreparationService _cuePreparation = cuePreparation ?? throw new ArgumentNullException(nameof(cuePreparation));
    private readonly IPlaybackEngine _playback = playback ?? throw new ArgumentNullException(nameof(playback));
    private readonly ILiveProductionFacade _liveProduction = liveProduction ?? throw new ArgumentNullException(nameof(liveProduction));
    private readonly ILiveProductionQueryService _liveProductionQuery = liveProductionQuery ?? throw new ArgumentNullException(nameof(liveProductionQuery));
    private readonly IOutputTopologyService _topology = topology ?? throw new ArgumentNullException(nameof(topology));
    private readonly IStageLayoutRegistryService _stageLayouts = stageLayouts ?? throw new ArgumentNullException(nameof(stageLayouts));

    /// <inheritdoc />
    public async Task<ShowControlsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _settings.LoadAsync().ConfigureAwait(false);

        IReadOnlyList<MediaPlaylistManifest> mediaPlaylists = await _mediaLibrary.GetPlaylistsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MediaLibraryItem> rootItems = await _mediaLibrary.GetRootItemsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, StageLayout> layouts = _stageLayouts.GetLayouts().ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        LiveProductionQuerySnapshot live = _liveProductionQuery.Current;
        GeneratedStateSnapshot generated = _liveProduction.Current.SessionState.GeneratedState;

        return new ShowControlsSnapshot
        {
            AudioPlaylists = ProjectAudioPlaylists(mediaPlaylists),
            AudioItems = ProjectAudioItems(rootItems, mediaPlaylists),
            StageScreens = ProjectStageScreens(live, layouts),
            StageLayouts = layouts.Values
                .OrderBy(static layout => layout.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static layout => new ShowStageLayoutPanelItem
                {
                    Id = layout.Id,
                    Name = string.IsNullOrWhiteSpace(layout.Name) ? layout.Id : layout.Name,
                    ElementCount = layout.Elements.Count,
                })
                .ToArray(),
            Timers = _settings.Settings.Show.Timers
                .OrderBy(static timer => timer.Name, StringComparer.OrdinalIgnoreCase)
                .Select(timer => ProjectTimer(timer, generated))
                .ToArray(),
            Messages = _sharedConfig.Show.Messages
                .OrderBy(static message => message.Name, StringComparer.OrdinalIgnoreCase)
                .Select(message => ProjectMessage(message, generated))
                .ToArray(),
            Props = _sharedConfig.Show.Props
                .OrderBy(static prop => prop.Name, StringComparer.OrdinalIgnoreCase)
                .Select(prop => ProjectProp(prop, generated))
                .ToArray(),
            Macros = _sharedConfig.Show.Macros
                .OrderBy(static macro => macro.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static macro => new ShowMacroPanelItem
                {
                    Id = macro.Id,
                    Name = string.IsNullOrWhiteSpace(macro.Name) ? "Macro" : macro.Name,
                    CollectionId = macro.CollectionId,
                    IconKey = string.IsNullOrWhiteSpace(macro.IconKey) ? "\uE756" : macro.IconKey!,
                    AccentColor = macro.AccentColor,
                    ActionCount = macro.Commands.Count + macro.CommandIds.Count,
                })
                .ToArray(),
        };
    }

    /// <inheritdoc />
    public async Task<ShowAudioPlaylistDefinition> SaveAudioPlaylistAsync(ShowAudioPlaylistDefinition playlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        ShowAudioPlaylistDefinition normalized = CloneAudioPlaylist(playlist);
        if (string.IsNullOrWhiteSpace(normalized.Id))
            normalized.Id = Guid.NewGuid().ToString("N");
        normalized.Name = NormalizeName(normalized.Name, "Audio Playlist");
        normalized.TransitionSeconds = Math.Max(0, normalized.TransitionSeconds);

        Upsert(_sharedConfig.Show.AudioPlaylists, normalized, static item => item.Id);
        await _sharedConfig.SaveAsync(cancellationToken).ConfigureAwait(false);
        return CloneAudioPlaylist(normalized);
    }

    /// <inheritdoc />
    public async Task<bool> TriggerAudioCueAsync(string cueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cueId);
        MediaLibraryItem? item = await FindMediaItemAsync(cueId, cancellationToken).ConfigureAwait(false);
        if (item == null)
            return false;

        item = CloneMediaItem(item);
        item.Type = "audio";
        item.CueDefaults.Target = MediaPlaybackLayerTargetNames.Audio;
        item.CueDefaults.Muted = false;
        item.CueDefaults.Autoplay = true;

        PreparedMediaCue? cue = _cuePreparation.PrepareMediaCue(item);
        if (cue == null)
            return false;

        _playback.EnterPreparedMediaCue(cue);
        _liveProduction.ReleaseClearedLayers([BackendOutputLayerKind.Audio]);
        return true;
    }

    /// <inheritdoc />
    public async Task<ShowMessageDefinition> SaveMessageAsync(ShowMessageDefinition message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        ShowMessageDefinition normalized = CloneMessage(message);
        if (string.IsNullOrWhiteSpace(normalized.Id))
            normalized.Id = Guid.NewGuid().ToString("N");
        normalized.Name = NormalizeName(normalized.Name, "Message");
        normalized.Dismiss ??= new ShowMessageDismissDefinition();
        normalized.Tokens ??= new List<ShowMessageTokenDefinition>();
        Upsert(_sharedConfig.Show.Messages, normalized, static item => item.Id);
        await _sharedConfig.SaveAsync(cancellationToken).ConfigureAwait(false);
        return CloneMessage(normalized);
    }

    /// <inheritdoc />
    public async Task<bool> ShowMessageAsync(string messageId, IEnumerable<ShowMessageRuntimeTokenValue>? tokens = null, CancellationToken cancellationToken = default)
    {
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        ShowMessageDefinition? message = _sharedConfig.Show.Messages.FirstOrDefault(item => IdEquals(item.Id, messageId));
        if (message == null)
            return false;

        Dictionary<string, string> values = ResolveTokenValues(message, tokens);
        string text = ResolveTemplateText(message.Template, message.Tokens, values);
        _liveProduction.SetOverlay(new OverlayContentState
        {
            Id = message.Id,
            Name = message.Name,
            Kind = OverlayContentKind.Message,
            IsVisible = true,
            Text = text,
            Tokens = values,
        });
        _liveProduction.ReleaseClearedLayers([BackendOutputLayerKind.Messages]);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> HideMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        ShowMessageDefinition? message = _sharedConfig.Show.Messages.FirstOrDefault(item => IdEquals(item.Id, messageId));
        if (message == null)
            return false;

        _liveProduction.SetOverlay(new OverlayContentState
        {
            Id = message.Id,
            Name = message.Name,
            Kind = OverlayContentKind.Message,
            IsVisible = false,
            Text = message.Template,
        });
        return true;
    }

    /// <inheritdoc />
    public async Task<ShowPropDefinition> SavePropAsync(ShowPropDefinition prop, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prop);
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        ShowPropDefinition normalized = CloneProp(prop);
        if (string.IsNullOrWhiteSpace(normalized.Id))
            normalized.Id = Guid.NewGuid().ToString("N");
        normalized.Name = NormalizeName(normalized.Name, "Prop");
        Upsert(_sharedConfig.Show.Props, normalized, static item => item.Id);
        await _sharedConfig.SaveAsync(cancellationToken).ConfigureAwait(false);
        return CloneProp(normalized);
    }

    /// <inheritdoc />
    public async Task<bool> TogglePropAsync(string propId, CancellationToken cancellationToken = default)
    {
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        ShowPropDefinition? prop = _sharedConfig.Show.Props.FirstOrDefault(item => IdEquals(item.Id, propId));
        if (prop == null)
            return false;

        bool currentlyVisible = _liveProduction.Current.SessionState.GeneratedState.Props.TryGetValue(prop.Id, out OverlayContentState? live)
            && live.IsVisible;
        _liveProduction.SetOverlay(new OverlayContentState
        {
            Id = prop.Id,
            Name = prop.Name,
            Kind = OverlayContentKind.Prop,
            IsVisible = !currentlyVisible,
            Text = prop.Text,
            Payload = BuildPropPayload(prop),
        });
        if (!currentlyVisible)
            _liveProduction.ReleaseClearedLayers([BackendOutputLayerKind.Props]);
        return true;
    }

    /// <inheritdoc />
    public async Task<ShowMacroDefinition> SaveMacroAsync(ShowMacroDefinition macro, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        ShowMacroDefinition normalized = CloneMacro(macro);
        if (string.IsNullOrWhiteSpace(normalized.Id))
            normalized.Id = Guid.NewGuid().ToString("N");
        normalized.Name = NormalizeName(normalized.Name, "Macro");
        Upsert(_sharedConfig.Show.Macros, normalized, static item => item.Id);
        await _sharedConfig.SaveAsync(cancellationToken).ConfigureAwait(false);
        return CloneMacro(normalized);
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteMacroAsync(string macroId, CancellationToken cancellationToken = default)
    {
        await _sharedConfig.LoadAsync(cancellationToken).ConfigureAwait(false);
        ShowMacroDefinition? macro = _sharedConfig.Show.Macros.FirstOrDefault(item => IdEquals(item.Id, macroId));
        if (macro == null)
            return false;

        LiveMacroDefinition liveMacro = new()
        {
            Id = macro.Id,
            Name = macro.Name,
            CollectionId = macro.CollectionId,
            IconKey = macro.IconKey,
            AccentColor = macro.AccentColor,
            Commands = BuildMacroCommands(macro),
        };
        return _liveProduction.ExecuteMacro(liveMacro).Succeeded;
    }

    /// <inheritdoc />
    public Task<bool> SetStageLayoutAsync(string screenId, string layoutId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(screenId) || string.IsNullOrWhiteSpace(layoutId))
            return Task.FromResult(false);

        return Task.FromResult(_liveProduction.SetStageLayout(screenId, layoutId).Succeeded);
    }

    /// <inheritdoc />
    public Task<bool> SetStageMessageAsync(string text, bool visible, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _liveProduction.SetOverlay(new OverlayContentState
        {
            Id = "stage-message",
            Name = "Stage Message",
            Kind = OverlayContentKind.StageMessage,
            IsVisible = visible,
            Text = visible ? text : string.Empty,
        });
        return Task.FromResult(true);
    }

    private IReadOnlyList<ShowAudioPlaylistPanelItem> ProjectAudioPlaylists(IReadOnlyList<MediaPlaylistManifest> mediaPlaylists)
    {
        List<ShowAudioPlaylistPanelItem> result = _sharedConfig.Show.AudioPlaylists
            .Select(playlist => new ShowAudioPlaylistPanelItem
            {
                Id = playlist.Id,
                Name = NormalizeName(playlist.Name, "Audio Playlist"),
                Count = playlist.ItemIds.Count,
                Shuffle = playlist.Shuffle,
                TransitionSeconds = playlist.TransitionSeconds,
            })
            .ToList();

        foreach (MediaPlaylistManifest playlist in mediaPlaylists)
        {
            int count = playlist.Items.Count(IsAudioItem);
            if (count == 0 || result.Any(existing => IdEquals(existing.Id, playlist.Id)))
                continue;

            result.Add(new ShowAudioPlaylistPanelItem
            {
                Id = playlist.Id,
                Name = playlist.Name,
                Count = count,
                TransitionSeconds = 0.5,
            });
        }

        return result.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<ShowAudioCuePanelItem> ProjectAudioItems(
        IReadOnlyList<MediaLibraryItem> rootItems,
        IReadOnlyList<MediaPlaylistManifest> mediaPlaylists)
    {
        List<ShowAudioCuePanelItem> items = [];
        foreach (MediaLibraryItem item in rootItems.Where(IsAudioItem))
            items.Add(ProjectAudioItem(item, string.Empty));

        foreach (MediaPlaylistManifest playlist in mediaPlaylists)
        {
            foreach (MediaLibraryItem item in playlist.Items.Where(IsAudioItem))
                items.Add(ProjectAudioItem(item, playlist.Id));
        }

        return items
            .GroupBy(static item => string.Concat(item.PlaylistId, "::", item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ShowAudioCuePanelItem ProjectAudioItem(MediaLibraryItem item, string playlistId)
    {
        string resolvedPath = _mediaLibrary.ResolveStoredMediaPath(item.Path);
        string audioKind = (item.ExtensionData != null
                && item.ExtensionData.TryGetValue("audioKind", out System.Text.Json.JsonElement audioKindElement)
                && audioKindElement.ValueKind == System.Text.Json.JsonValueKind.String)
            ? audioKindElement.GetString() ?? "track"
            : item.Duration is < 10 ? "soundEffect" : "track";
        return new ShowAudioCuePanelItem
        {
            Id = item.Id,
            PlaylistId = playlistId,
            Name = string.IsNullOrWhiteSpace(item.Name) ? Path.GetFileNameWithoutExtension(item.Path) : item.Name,
            Path = item.Path,
            IsAvailable = !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath),
            AudioKind = audioKind,
            DurationLabel = item.Duration is double seconds && seconds > 0
                ? TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
                : string.Empty,
        };
    }

    private static IReadOnlyList<ShowStageScreenPanelItem> ProjectStageScreens(
        LiveProductionQuerySnapshot live,
        IReadOnlyDictionary<string, StageLayout> layouts)
    {
        return live.StageScreens
            .OrderBy(static screen => screen.ScreenName, StringComparer.OrdinalIgnoreCase)
            .Select(screen =>
            {
                string? layoutId = screen.StageLayoutId;
                string layoutName = !string.IsNullOrWhiteSpace(layoutId) && layouts.TryGetValue(layoutId, out StageLayout? layout)
                    ? layout.Name
                    : "No layout";
                return new ShowStageScreenPanelItem
                {
                    ScreenId = screen.ScreenId,
                    Name = screen.ScreenName,
                    ActiveLayoutId = layoutId,
                    ActiveLayoutName = layoutName,
                    CommandMode = screen.CommandMode,
                    HasResolvedFrame = screen.FrameSequence != null,
                };
            })
            .ToArray();
    }

    private static ShowTimerPanelItem ProjectTimer(ShowTimerDefinition timer, GeneratedStateSnapshot generated)
    {
        generated.Timers.TryGetValue(timer.Id, out TimerSnapshot? snapshot);
        return new ShowTimerPanelItem
        {
            Id = timer.Id,
            Name = NormalizeName(timer.Name, "Timer"),
            Kind = ShowControlsModelHelpers.ParseTimerKind(timer.Kind),
            Status = snapshot?.Status ?? GeneratedTimerStatus.Stopped,
            DisplayValue = snapshot?.DisplayValue ?? FormatSeconds(timer.DurationSeconds),
            AllowsOverrun = timer.AllowsOverrun,
            DurationSeconds = timer.DurationSeconds,
        };
    }

    private static ShowMessagePanelItem ProjectMessage(ShowMessageDefinition message, GeneratedStateSnapshot generated)
    {
        generated.Messages.TryGetValue(message.Id, out OverlayContentState? live);
        return new ShowMessagePanelItem
        {
            Id = message.Id,
            Name = NormalizeName(message.Name, "Message"),
            Template = message.Template,
            Tokens = message.Tokens.ToArray(),
            IsVisible = live?.IsVisible ?? false,
            PreviewText = live?.Text ?? message.Template,
        };
    }

    private static ShowPropPanelItem ProjectProp(ShowPropDefinition prop, GeneratedStateSnapshot generated)
    {
        generated.Props.TryGetValue(prop.Id, out OverlayContentState? live);
        return new ShowPropPanelItem
        {
            Id = prop.Id,
            Name = NormalizeName(prop.Name, "Prop"),
            AssetReference = prop.AssetReference,
            Text = prop.Text,
            IsVisible = live?.IsVisible ?? false,
        };
    }

    private async Task<MediaLibraryItem?> FindMediaItemAsync(string cueId, CancellationToken cancellationToken)
    {
        foreach (MediaLibraryItem item in await _mediaLibrary.GetRootItemsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (IdEquals(item.Id, cueId))
                return item;
        }

        foreach (MediaPlaylistManifest playlist in await _mediaLibrary.GetPlaylistsAsync(cancellationToken).ConfigureAwait(false))
        {
            MediaLibraryItem? item = playlist.Items.FirstOrDefault(candidate => IdEquals(candidate.Id, cueId));
            if (item != null)
                return item;
        }

        return null;
    }

    private static bool IsAudioItem(MediaLibraryItem item) =>
        string.Equals(item.Type, "audio", StringComparison.OrdinalIgnoreCase)
        || MediaInference.ResolveEffectiveMediaType(item.Type, item.Path).Equals("audio", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ResolveTokenValues(
        ShowMessageDefinition message,
        IEnumerable<ShowMessageRuntimeTokenValue>? runtimeTokens)
    {
        Dictionary<string, string> values = message.Tokens.ToDictionary(
            static token => token.Id,
            static token => token.DefaultValue ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
        if (runtimeTokens != null)
        {
            foreach (ShowMessageRuntimeTokenValue token in runtimeTokens)
                values[token.TokenId] = token.Value;
        }

        foreach (ShowMessageTokenDefinition token in message.Tokens.Where(static token => string.Equals(token.Kind, "systemClock", StringComparison.OrdinalIgnoreCase)))
            values[token.Id] = DateTime.Now.ToShortTimeString();

        return values;
    }

    private static string ResolveTemplateText(
        string template,
        IReadOnlyList<ShowMessageTokenDefinition> tokenDefinitions,
        IReadOnlyDictionary<string, string> values)
    {
        string result = template ?? string.Empty;
        foreach (ShowMessageTokenDefinition token in tokenDefinitions)
        {
            string value = values.TryGetValue(token.Id, out string? resolved) ? resolved : token.DefaultValue ?? string.Empty;
            result = result.Replace($"[{token.Name}]", value, StringComparison.OrdinalIgnoreCase)
                .Replace($"[{token.Id}]", value, StringComparison.OrdinalIgnoreCase)
                .Replace($"{{{token.Id}}}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static RenderPayloadDescriptor? BuildPropPayload(ShowPropDefinition prop)
    {
        if (string.IsNullOrWhiteSpace(prop.AssetReference))
            return null;

        return new RenderPayloadDescriptor
        {
            Id = $"prop:{prop.Id}",
            Kind = RenderPayloadKind.Overlay,
            DisplayName = string.IsNullOrWhiteSpace(prop.Name) ? "Prop" : prop.Name,
            SourceReference = prop.AssetReference,
            Detail = new OverlayRenderPayload
            {
                OverlayId = prop.Id,
                OverlayKind = OverlayContentKind.Prop.ToString(),
            },
        };
    }

    private IReadOnlyList<LiveCommand> BuildMacroCommands(ShowMacroDefinition macro)
    {
        List<LiveCommand> commands = [];
        foreach (ShowMacroCommandDefinition command in macro.Commands)
        {
            LiveCommand? liveCommand = BuildMacroCommand(command);
            if (liveCommand != null)
                commands.Add(liveCommand);
        }

        foreach (string commandId in macro.CommandIds)
        {
            ShowMacroCommandDefinition command = ResolveLegacyCommandId(commandId);
            LiveCommand? liveCommand = BuildMacroCommand(command);
            if (liveCommand != null)
                commands.Add(liveCommand);
        }

        return commands;
    }

    private LiveCommand? BuildMacroCommand(ShowMacroCommandDefinition command)
    {
        LiveCommandSource source = new() { Kind = LiveCommandSourceKind.Macro, Id = command.Id };
        return command.Kind switch
        {
            "clearGroup" when !string.IsNullOrWhiteSpace(command.TargetId) =>
                LiveCommandExecutor.ClearGroup(command.TargetId, source),
            "clearLayer" when ShowControlsModelHelpers.ParseLayerKind(command.TargetId) is BackendOutputLayerKind layer =>
                LiveCommandExecutor.ClearLayers([layer], source),
            "message" when !string.IsNullOrWhiteSpace(command.TargetId) =>
                BuildOverlayCommand(command.TargetId, OverlayContentKind.Message, source),
            "prop" when !string.IsNullOrWhiteSpace(command.TargetId) =>
                BuildOverlayCommand(command.TargetId, OverlayContentKind.Prop, source),
            "stageLayout" when !string.IsNullOrWhiteSpace(command.ScreenId) && !string.IsNullOrWhiteSpace(command.TargetId) =>
                LiveCommandExecutor.SetStageLayout(command.ScreenId, command.TargetId, source: source),
            "timer" when !string.IsNullOrWhiteSpace(command.TargetId) =>
                BuildTimerCommand(command.TargetId, source),
            _ => null,
        };
    }

    private LiveCommand? BuildOverlayCommand(string overlayId, OverlayContentKind kind, LiveCommandSource source)
    {
        if (kind == OverlayContentKind.Message)
        {
            ShowMessageDefinition? message = _sharedConfig.Show.Messages.FirstOrDefault(item => IdEquals(item.Id, overlayId));
            if (message == null)
                return null;

            return LiveCommandExecutor.SetOverlay(new OverlayContentState
            {
                Id = message.Id,
                Name = message.Name,
                Kind = OverlayContentKind.Message,
                IsVisible = true,
                Text = ResolveTemplateText(message.Template, message.Tokens, ResolveTokenValues(message, null)),
            }, source);
        }

        ShowPropDefinition? prop = _sharedConfig.Show.Props.FirstOrDefault(item => IdEquals(item.Id, overlayId));
        if (prop == null)
            return null;

        return LiveCommandExecutor.SetOverlay(new OverlayContentState
        {
            Id = prop.Id,
            Name = prop.Name,
            Kind = OverlayContentKind.Prop,
            IsVisible = true,
            Text = prop.Text,
            Payload = BuildPropPayload(prop),
        }, source);
    }

    private LiveCommand? BuildTimerCommand(string timerId, LiveCommandSource source)
    {
        ShowTimerDefinition? timer = _settings.Settings.Show.Timers.FirstOrDefault(item => IdEquals(item.Id, timerId));
        if (timer == null)
            return null;

        return LiveCommandExecutor.SetTimer(CreateTimerSnapshot(timer, GeneratedTimerStatus.Running), source);
    }

    private static ShowMacroCommandDefinition ResolveLegacyCommandId(string commandId)
    {
        string normalized = commandId.Trim();
        if (normalized.StartsWith("clear:", StringComparison.OrdinalIgnoreCase))
            return new ShowMacroCommandDefinition { Id = normalized, Kind = "clearLayer", TargetId = normalized[6..] };
        if (normalized.StartsWith("clearGroup:", StringComparison.OrdinalIgnoreCase))
            return new ShowMacroCommandDefinition { Id = normalized, Kind = "clearGroup", TargetId = normalized[11..] };
        if (normalized.StartsWith("message:", StringComparison.OrdinalIgnoreCase))
            return new ShowMacroCommandDefinition { Id = normalized, Kind = "message", TargetId = normalized[8..] };
        if (normalized.StartsWith("prop:", StringComparison.OrdinalIgnoreCase))
            return new ShowMacroCommandDefinition { Id = normalized, Kind = "prop", TargetId = normalized[5..] };
        if (normalized.StartsWith("timer:", StringComparison.OrdinalIgnoreCase))
            return new ShowMacroCommandDefinition { Id = normalized, Kind = "timer", TargetId = normalized[6..] };

        return new ShowMacroCommandDefinition { Id = normalized, Kind = "clearGroup", TargetId = normalized };
    }

    private static TimerSnapshot CreateTimerSnapshot(ShowTimerDefinition timer, GeneratedTimerStatus status)
    {
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, timer.DurationSeconds));
        return new TimerSnapshot
        {
            Id = timer.Id,
            Name = timer.Name,
            Kind = ShowControlsModelHelpers.ParseTimerKind(timer.Kind),
            Status = status,
            Remaining = duration,
            DisplayValue = FormatDuration(duration),
            IsOverrun = status == GeneratedTimerStatus.Overrun,
        };
    }

    private static string FormatSeconds(int seconds) => FormatDuration(TimeSpan.FromSeconds(Math.Max(0, seconds)));

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"mm\:ss");

    private static string NormalizeName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    private static bool IdEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void Upsert<T>(IList<T> items, T item, Func<T, string> idSelector)
    {
        string id = idSelector(item);
        int index = Enumerable.Range(0, items.Count).FirstOrDefault(i => IdEquals(idSelector(items[i]), id), -1);
        if (index >= 0)
            items[index] = item;
        else
            items.Add(item);
    }

    private static ShowAudioPlaylistDefinition CloneAudioPlaylist(ShowAudioPlaylistDefinition playlist) =>
        new()
        {
            Id = playlist.Id,
            Name = playlist.Name,
            FolderId = playlist.FolderId,
            ItemIds = playlist.ItemIds.ToList(),
            Shuffle = playlist.Shuffle,
            TransitionSeconds = playlist.TransitionSeconds,
        };

    private static ShowMessageDefinition CloneMessage(ShowMessageDefinition message) =>
        new()
        {
            Id = message.Id,
            Name = message.Name,
            Template = message.Template,
            ThemeId = message.ThemeId,
            Transition = message.Transition,
            ClearGroupId = message.ClearGroupId,
            Dismiss = new ShowMessageDismissDefinition
            {
                Mode = message.Dismiss?.Mode ?? "manual",
                Seconds = message.Dismiss?.Seconds,
            },
            Tokens = message.Tokens.Select(static token => new ShowMessageTokenDefinition
            {
                Id = token.Id,
                Name = token.Name,
                Kind = token.Kind,
                SourceId = token.SourceId,
                DefaultValue = token.DefaultValue,
            }).ToList(),
        };

    private static ShowPropDefinition CloneProp(ShowPropDefinition prop) =>
        new()
        {
            Id = prop.Id,
            Name = prop.Name,
            AssetReference = prop.AssetReference,
            Text = prop.Text,
            Transition = prop.Transition,
            AccentColor = prop.AccentColor,
            ClearGroupId = prop.ClearGroupId,
        };

    private static ShowMacroDefinition CloneMacro(ShowMacroDefinition macro) =>
        new()
        {
            Id = macro.Id,
            Name = macro.Name,
            CollectionId = macro.CollectionId,
            IconKey = macro.IconKey,
            AccentColor = macro.AccentColor,
            CommandIds = macro.CommandIds.ToList(),
            Commands = macro.Commands.Select(static command => new ShowMacroCommandDefinition
            {
                Id = command.Id,
                Kind = command.Kind,
                TargetId = command.TargetId,
                ScreenId = command.ScreenId,
            }).ToList(),
        };

    private static MediaLibraryItem CloneMediaItem(MediaLibraryItem item) =>
        new()
        {
            Id = item.Id,
            Name = item.Name,
            Path = item.Path,
            Type = item.Type,
            Mime = item.Mime,
            Duration = item.Duration,
            Width = item.Width,
            Height = item.Height,
            AddedAt = item.AddedAt,
            CueDefaults = new MediaCueDefaults
            {
                Target = item.CueDefaults.Target,
                Fit = item.CueDefaults.Fit,
                Autoplay = item.CueDefaults.Autoplay,
                Loop = item.CueDefaults.Loop,
                Muted = item.CueDefaults.Muted,
                Transition = item.CueDefaults.Transition,
            },
            ExtensionData = item.ExtensionData == null ? null : new Dictionary<string, System.Text.Json.JsonElement>(item.ExtensionData, StringComparer.OrdinalIgnoreCase),
        };
}
