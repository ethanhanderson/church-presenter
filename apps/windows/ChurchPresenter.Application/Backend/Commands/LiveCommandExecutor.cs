using ChurchPresenter.Backend.Output;
using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Backend.Commands;

/// <summary>
/// Expands live commands into normalized action batches and applies them.
/// </summary>
public interface ILiveCommandExecutor
{
    /// <summary>Expands a command into deterministic actions.</summary>
    ActionBatch Expand(LiveCommand command);

    /// <summary>Expands multiple commands into one deterministic action batch.</summary>
    ActionBatch Expand(IEnumerable<LiveCommand> commands, LiveCommandSource? source = null, string? macroId = null);

    /// <summary>Expands a macro into one deterministic action batch.</summary>
    ActionBatch ExpandMacro(LiveMacroDefinition macro, LiveCommandSource? source = null);

    /// <summary>Executes a command against the supplied live render state.</summary>
    ActionResult Execute(LiveRenderSessionState state, LiveCommand command);

    /// <summary>Executes a pre-expanded action batch against the supplied live render state.</summary>
    ActionResult Execute(LiveRenderSessionState state, ActionBatch batch);
}

/// <summary>
/// Foundation command executor shared by local UI, slide actions, macros, and future remotes.
/// </summary>
public sealed class LiveCommandExecutor(IBackendRenderEngine renderEngine) : ILiveCommandExecutor
{
    /// <inheritdoc />
    public ActionBatch Expand(LiveCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        LiveAction action = command.Kind switch
        {
            LiveCommandKind.SetLayerPayload => new LiveAction
            {
                Kind = LiveActionKind.SetLayerPayload,
                Target = command.Target,
                Payload = command.Payload,
                Transition = command.Transition,
            },
            LiveCommandKind.Clear => new LiveAction
            {
                Kind = LiveActionKind.ClearLayers,
                Target = command.Target,
                Clear = command.Clear,
            },
            LiveCommandKind.SetLook => new LiveAction
            {
                Kind = LiveActionKind.SetLook,
                Target = command.Target,
                Look = command.Look,
            },
            LiveCommandKind.SetStageLayout => new LiveAction
            {
                Kind = LiveActionKind.SetStageLayout,
                Target = command.Target,
                StageLayoutId = command.StageLayoutId,
                DeliveryMode = command.DeliveryMode,
            },
            LiveCommandKind.SetOverlayState => new LiveAction
            {
                Kind = LiveActionKind.SetOverlayState,
                Target = command.Target,
                Overlay = command.Overlay,
            },
            LiveCommandKind.SetTimerState => new LiveAction
            {
                Kind = LiveActionKind.SetTimerState,
                Target = command.Target,
                Timer = command.Timer,
            },
            LiveCommandKind.SetCaptureSessionState => new LiveAction
            {
                Kind = LiveActionKind.SetCaptureSessionState,
                Target = command.Target,
                CaptureSession = command.CaptureSession,
            },
            _ => throw new NotSupportedException($"Unsupported live command kind '{command.Kind}'."),
        };

        return new ActionBatch
        {
            SourceCommandId = command.Id,
            CorrelationId = command.CorrelationId,
            Source = command.Source,
            Actions = [WithCommandMetadata(action, command)],
        };
    }

    /// <inheritdoc />
    public ActionBatch Expand(IEnumerable<LiveCommand> commands, LiveCommandSource? source = null, string? macroId = null)
    {
        ArgumentNullException.ThrowIfNull(commands);

        LiveCommand[] materializedCommands = commands.ToArray();
        if (materializedCommands.Length == 0)
        {
            return new ActionBatch
            {
                Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Automation },
                MacroId = macroId,
            };
        }

        List<LiveAction> actions = [];
        foreach (LiveCommand command in materializedCommands)
        {
            ActionBatch expanded = Expand(command);
            actions.AddRange(expanded.Actions.Select(action => action with { Source = source ?? action.Source }));
        }

        return new ActionBatch
        {
            SourceCommandId = materializedCommands.Length == 1 ? materializedCommands[0].Id : Guid.NewGuid(),
            CorrelationId = materializedCommands.Length == 1 ? materializedCommands[0].CorrelationId : null,
            Source = source ?? materializedCommands[0].Source,
            MacroId = macroId,
            Actions = actions,
        };
    }

    /// <inheritdoc />
    public ActionBatch ExpandMacro(LiveMacroDefinition macro, LiveCommandSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(macro);

        return Expand(
            macro.Commands,
            source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Macro, Id = macro.Id },
            macro.Id);
    }

    /// <inheritdoc />
    public ActionResult Execute(LiveRenderSessionState state, LiveCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        return Execute(state, Expand(command));
    }

    /// <inheritdoc />
    public ActionResult Execute(LiveRenderSessionState state, ActionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(batch);

        RenderEngineResult result = renderEngine.Apply(state, batch);

        return new ActionResult
        {
            Succeeded = result.Diagnostics.Count == 0,
            Batch = batch,
            State = result.State,
            Frames = result.Frames,
            Diagnostics = result.Diagnostics,
        };
    }

    /// <summary>Creates a command that sets a layer payload.</summary>
    public static LiveCommand SetLayerPayload(
        OutputLayerKind layerKind,
        RenderPayloadDescriptor payload,
        LiveCommandSource? source = null,
        LayerTransitionState? transition = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new LiveCommand
        {
            Kind = LiveCommandKind.SetLayerPayload,
            Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            Target = LiveCommandTarget.Layer(layerKind),
            Payload = payload,
            Transition = transition,
        };
    }

    /// <summary>Creates a command that clears specific layers.</summary>
    public static LiveCommand ClearLayers(
        IEnumerable<OutputLayerKind> layers,
        LiveCommandSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(layers);

        return new LiveCommand
        {
            Kind = LiveCommandKind.Clear,
            Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            Clear = new ClearCommand { Layers = layers.ToHashSet() },
        };
    }

    /// <summary>Creates a command that clears a configured clear group.</summary>
    public static LiveCommand ClearGroup(string clearGroupId, LiveCommandSource? source = null)
    {
        return new LiveCommand
        {
            Kind = LiveCommandKind.Clear,
            Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            Clear = new ClearCommand { ClearGroupId = clearGroupId },
        };
    }

    /// <summary>Creates a command that activates a Look.</summary>
    public static LiveCommand SetLook(LookPreset look, LiveCommandSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(look);

        return new LiveCommand
        {
            Kind = LiveCommandKind.SetLook,
            Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            Look = look,
        };
    }

    /// <summary>Creates a command that assigns a stage layout to a stage screen.</summary>
    public static LiveCommand SetStageLayout(
        string screenId,
        string stageLayoutId,
        StageAudienceCommandMode deliveryMode = StageAudienceCommandMode.StageAndAudience,
        LiveCommandSource? source = null)
    {
        return new LiveCommand
        {
            Kind = LiveCommandKind.SetStageLayout,
            Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            Target = LiveCommandTarget.Screen(screenId),
            StageLayoutId = stageLayoutId,
            DeliveryMode = deliveryMode,
        };
    }

    /// <summary>Creates a command that updates generated overlay state.</summary>
    public static LiveCommand SetOverlay(
        OverlayContentState overlay,
        LiveCommandSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        return new LiveCommand
        {
            Kind = LiveCommandKind.SetOverlayState,
            Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            Overlay = overlay,
        };
    }

    /// <summary>Creates a command that updates timer state.</summary>
    public static LiveCommand SetTimer(
        TimerSnapshot timer,
        LiveCommandSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(timer);

        return new LiveCommand
        {
            Kind = LiveCommandKind.SetTimerState,
            Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            Timer = timer,
        };
    }

    /// <summary>Creates a command that updates capture-session state.</summary>
    public static LiveCommand SetCaptureSession(
        CaptureSessionState captureSession,
        LiveCommandSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(captureSession);

        return new LiveCommand
        {
            Kind = LiveCommandKind.SetCaptureSessionState,
            Source = source ?? new LiveCommandSource { Kind = LiveCommandSourceKind.Operator },
            CaptureSession = captureSession,
        };
    }

    private static LiveAction WithCommandMetadata(LiveAction action, LiveCommand command)
    {
        return action with
        {
            SourceCommandId = command.Id,
            CorrelationId = command.CorrelationId,
            Source = command.Source,
        };
    }
}