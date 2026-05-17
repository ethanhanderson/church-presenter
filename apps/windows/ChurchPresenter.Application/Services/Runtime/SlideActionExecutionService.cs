using System.Text.Json;

using ChurchPresenter.Backend.Commands;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;

using Microsoft.Extensions.Logging;

using BackendOutputLayerKind = ChurchPresenter.Backend.Rendering.OutputLayerKind;

namespace ChurchPresenter.Services.Runtime;

/// <summary>
/// Executes serialized slide actions when a slide is activated.
/// </summary>
public interface ISlideActionExecutionService
{
    /// <summary>
    /// Executes runtime behaviors associated with a slide activation.
    /// </summary>
    void ExecuteForSlide(PresentationSlide? slide);
}

/// <inheritdoc />
public sealed class SlideActionExecutionService(
    ILiveSessionService live,
    IShowTimerService timers,
    ILogger<SlideActionExecutionService> logger,
    ILiveProductionFacade? liveProduction = null) : ISlideActionExecutionService
{
    private static readonly JsonSerializerOptions MacroCommandJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILiveSessionService _live = live ?? throw new ArgumentNullException(nameof(live));
    private readonly IShowTimerService _timers = timers ?? throw new ArgumentNullException(nameof(timers));
    private readonly ILogger<SlideActionExecutionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILiveProductionFacade? _liveProduction = liveProduction;

    /// <inheritdoc />
    public void ExecuteForSlide(PresentationSlide? slide)
    {
        if (slide == null)
            return;

        LiveCommandSource source = new()
        {
            Kind = LiveCommandSourceKind.SlideAction,
            Id = slide.Id,
        };

        if (!string.IsNullOrWhiteSpace(slide.GoToNextTimerId))
            _timers.ActivateTimer(slide.GoToNextTimerId);

        List<LiveCommand> pendingCommands = [];
        foreach (var action in slide.Actions)
        {
            string actionType = NormalizeActionType(action.Type);
            switch (actionType)
            {
                case "clearpresentation":
                    pendingCommands.Add(LiveCommandExecutor.ClearLayers([BackendOutputLayerKind.Slide], source));
                    break;
                case "clearmedia":
                    pendingCommands.Add(LiveCommandExecutor.ClearLayers(
                        [BackendOutputLayerKind.Media, BackendOutputLayerKind.Audio],
                        source));
                    break;
                case "blackouton":
                    pendingCommands.Add(CreateMaskCommand(action, isVisible: true, source));
                    _live.SetBlackout(true);
                    break;
                case "blackoutoff":
                    pendingCommands.Add(CreateMaskCommand(action, isVisible: false, source));
                    _live.SetBlackout(false);
                    break;
                case "clearall":
                    pendingCommands.Add(LiveCommandExecutor.ClearGroup("clear-all", source));
                    break;
                case "clearmessages":
                    pendingCommands.Add(LiveCommandExecutor.ClearLayers([BackendOutputLayerKind.Messages], source));
                    break;
                case "clearprops":
                    pendingCommands.Add(LiveCommandExecutor.ClearLayers([BackendOutputLayerKind.Props], source));
                    break;
                case "clearannouncements":
                    pendingCommands.Add(LiveCommandExecutor.ClearLayers([BackendOutputLayerKind.Announcements], source));
                    break;
                case "message":
                case "setmessage":
                case "showmessage":
                    pendingCommands.Add(CreateOverlayCommand(action, OverlayContentKind.Message, isVisible: true, source));
                    break;
                case "hidemessage":
                case "dismissmessage":
                    pendingCommands.Add(CreateOverlayCommand(action, OverlayContentKind.Message, isVisible: false, source));
                    break;
                case "prop":
                case "setprop":
                case "showprop":
                    pendingCommands.Add(CreateOverlayCommand(action, OverlayContentKind.Prop, isVisible: true, source));
                    break;
                case "hideprop":
                    pendingCommands.Add(CreateOverlayCommand(action, OverlayContentKind.Prop, isVisible: false, source));
                    break;
                case "announcement":
                case "setannouncement":
                case "showannouncement":
                    pendingCommands.Add(CreateOverlayCommand(action, OverlayContentKind.Announcement, isVisible: true, source));
                    break;
                case "hideannouncement":
                    pendingCommands.Add(CreateOverlayCommand(action, OverlayContentKind.Announcement, isVisible: false, source));
                    break;
                case "stagemessage":
                case "setstagemessage":
                case "showstagemessage":
                    pendingCommands.Add(CreateOverlayCommand(action, OverlayContentKind.StageMessage, isVisible: true, source));
                    break;
                case "clearstagemessage":
                case "hidestagemessage":
                    pendingCommands.Add(CreateOverlayCommand(action, OverlayContentKind.StageMessage, isVisible: false, source));
                    break;
                case "timer":
                case "starttimer":
                case "gotonexttimer":
                    FlushPendingCommands(pendingCommands, source);
                    if (!string.IsNullOrWhiteSpace(action.Value))
                        _timers.ActivateTimer(action.Value);
                    break;
                case "cleartimer":
                case "stoptimer":
                    FlushPendingCommands(pendingCommands, source);
                    _timers.ActivateTimer(null);
                    break;
                case "macro":
                case "triggermacro":
                    FlushPendingCommands(pendingCommands, source);
                    ExecuteMacroAction(action, source);
                    break;
                default:
                    _logger.LogDebug("Ignoring unsupported slide action type {ActionType}.", action.Type);
                    break;
            }
        }

        FlushPendingCommands(pendingCommands, source);
    }

    private void FlushPendingCommands(List<LiveCommand> commands, LiveCommandSource source)
    {
        if (commands.Count == 0)
            return;

        _liveProduction?.ExecuteCommands(commands, source);
        commands.Clear();
    }

    private void ExecuteMacroAction(SlideActionDefinition action, LiveCommandSource source)
    {
        if (_liveProduction == null)
            return;

        if (!TryReadMacroCommands(action, out IReadOnlyList<LiveCommand> commands))
        {
            _logger.LogDebug("Ignoring macro slide action {ActionId} because it does not contain commands.", action.Id);
            return;
        }

        string macroId = !string.IsNullOrWhiteSpace(action.Value)
            ? action.Value
            : ResolveActionId(action, "macro");
        _liveProduction.ExecuteMacro(
            new LiveMacroDefinition
            {
                Id = macroId,
                Name = action.Label ?? macroId,
                Commands = commands,
            },
            source);
    }

    private static bool TryReadMacroCommands(
        SlideActionDefinition action,
        out IReadOnlyList<LiveCommand> commands)
    {
        commands = Array.Empty<LiveCommand>();
        if (action.ExtensionData == null)
            return false;

        if (!action.ExtensionData.TryGetValue("commands", out JsonElement commandsElement)
            || commandsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        commands = commandsElement.Deserialize<List<LiveCommand>>(MacroCommandJsonOptions) ?? [];
        return commands.Count > 0;
    }

    private static LiveCommand CreateMaskCommand(
        SlideActionDefinition action,
        bool isVisible,
        LiveCommandSource source)
    {
        return CreateOverlayCommand(action, OverlayContentKind.Mask, isVisible, source);
    }

    private static LiveCommand CreateOverlayCommand(
        SlideActionDefinition action,
        OverlayContentKind kind,
        bool isVisible,
        LiveCommandSource source)
    {
        string fallbackPrefix = kind.ToString().ToLowerInvariant();
        string id = ResolveActionId(action, fallbackPrefix);
        string name = string.IsNullOrWhiteSpace(action.Label)
            ? ToDisplayName(kind)
            : action.Label;
        string? text = isVisible
            ? FirstNonWhiteSpace(action.Value, action.Label, name)
            : null;

        return LiveCommandExecutor.SetOverlay(
            new OverlayContentState
            {
                Id = id,
                Name = name,
                Kind = kind,
                IsVisible = isVisible,
                Text = text,
            },
            source);
    }

    private static string ResolveActionId(SlideActionDefinition action, string fallbackPrefix)
    {
        return FirstNonWhiteSpace(action.Id, action.Value, action.Label)
            ?? $"{fallbackPrefix}-{Guid.NewGuid():N}";
    }

    private static string ToDisplayName(OverlayContentKind kind)
    {
        return kind switch
        {
            OverlayContentKind.StageMessage => "Stage Message",
            _ => kind.ToString(),
        };
    }

    private static string NormalizeActionType(string? actionType)
    {
        return string.IsNullOrWhiteSpace(actionType)
            ? string.Empty
            : actionType.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Trim()
                .ToLowerInvariant();
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }
}