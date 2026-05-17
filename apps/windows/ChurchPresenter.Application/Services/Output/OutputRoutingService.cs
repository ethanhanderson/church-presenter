using ChurchPresenter.Backend.Rendering;

using Microsoft.Extensions.Logging;

namespace ChurchPresenter.Services.Output;

/// <summary>
/// Resolves portable Looks with machine-local activation state for title-bar routing.
/// </summary>
public sealed class OutputRoutingService(
    IMachineStateService machineState,
    ISharedConfigService sharedConfig,
    IOutputTopologyService topology,
    ILogger<OutputRoutingService> logger) : IOutputRoutingService
{
    private readonly IMachineStateService _machineState = machineState ?? throw new ArgumentNullException(nameof(machineState));
    private readonly ISharedConfigService _sharedConfig = sharedConfig ?? throw new ArgumentNullException(nameof(sharedConfig));
    private readonly IOutputTopologyService _topology = topology ?? throw new ArgumentNullException(nameof(topology));
    private readonly ILogger<OutputRoutingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<OutputFeedDefinition> Feeds => _topology.AudienceScreens;

    /// <inheritdoc />
    public IReadOnlyList<OutputLookDefinition> Looks => BuildLooks();

    /// <inheritdoc />
    public string ActiveLookId => NormalizeActiveLookId(_machineState.OutputBinding.ActiveLookId, Looks);

    /// <inheritdoc />
    public OutputLookDefinition ActiveLook =>
        Looks.FirstOrDefault(look => string.Equals(look.Id, ActiveLookId, StringComparison.OrdinalIgnoreCase))
            ?.Clone()
        ?? OutputRoutingDefaults.CreateDefaultLook(Feeds);

    /// <inheritdoc />
    public bool RoutesLayer(string feedId, OutputLayerKind layerKind)
    {
        if (string.IsNullOrWhiteSpace(feedId))
            return true;

        var route = ActiveLook.ResolveRouting(feedId);
        return route.Routes(layerKind);
    }

    /// <inheritdoc />
    public async Task SetActiveLookAsync(string lookId, CancellationToken cancellationToken = default)
    {
        var effectiveLookId = NormalizeActiveLookId(lookId, Looks);
        _machineState.UpdateOutputBinding(binding => binding.ActiveLookId = effectiveLookId);
        await _machineState.SaveAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Activated output look {LookId}.", effectiveLookId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task ResetToDefaultAsync(CancellationToken cancellationToken = default) =>
        SetActiveLookAsync(OutputLookIds.Default, cancellationToken);

    /// <inheritdoc />
    public async Task SetLayerRoutingAsync(string feedId, OutputLayerKind layerKind, bool enabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedId))
            return;

        OutputLookDefinition editableLook = null!;
        _sharedConfig.UpdateOutput(output =>
        {
            output.Looks ??= new List<OutputLookDefinition>();
            var activeLookId = NormalizeActiveLookId(_machineState.OutputBinding.ActiveLookId, BuildLooks(output, _machineState.OutputBinding));
            editableLook = EnsureEditableLook(output, activeLookId);
            var route = editableLook.Routes.FirstOrDefault(candidate =>
                string.Equals(candidate.FeedId, feedId, StringComparison.OrdinalIgnoreCase));

            if (route == null)
            {
                route = new OutputLookFeedRouting { FeedId = feedId, Slide = true, Media = true };
                OutputRoutingDefaults.EnsureLayerRoutes(route);
                editableLook.Routes.Add(route);
            }

            route.SetRoute(layerKind, enabled);

            UpsertLook(output, editableLook);
        });
        _machineState.UpdateOutputBinding(binding => binding.ActiveLookId = editableLook.Id);

        await _sharedConfig.SaveAsync(cancellationToken).ConfigureAwait(false);
        await _machineState.SaveAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Updated output routing for feed {FeedId} layer {LayerKind} -> {Enabled}.", feedId, layerKind, enabled);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task SetRoutesAsync(IEnumerable<OutputLookFeedRouting> routes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routes);

        OutputLookDefinition editableLook = null!;
        List<OutputLookFeedRouting> normalizedRoutes = NormalizeRoutes(routes, Feeds).ToList();

        _sharedConfig.UpdateOutput(output =>
        {
            output.Looks ??= new List<OutputLookDefinition>();
            var activeLookId = NormalizeActiveLookId(_machineState.OutputBinding.ActiveLookId, BuildLooks(output, _machineState.OutputBinding));
            editableLook = EnsureEditableLook(output, activeLookId);
            editableLook.Routes = normalizedRoutes.Select(static route => route.Clone()).ToList();
            EnsureMaskDefinitions(output, normalizedRoutes);
            UpsertLook(output, editableLook);
        });
        _machineState.UpdateOutputBinding(binding => binding.ActiveLookId = editableLook.Id);

        await _sharedConfig.SaveAsync(cancellationToken).ConfigureAwait(false);
        await _machineState.SaveAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Updated {RouteCount} output routes for look {LookId}.", normalizedRoutes.Count, editableLook.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task SetClearGroupsAsync(IEnumerable<OutputLookClearGroupDefinition> clearGroups, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clearGroups);

        OutputLookDefinition editableLook = null!;
        List<OutputLookClearGroupDefinition> normalizedGroups = clearGroups
            .Select(NormalizeClearGroup)
            .Where(static group => !string.IsNullOrWhiteSpace(group.Id) && ResolveClearLayers(group).Count > 0)
            .ToList();

        _sharedConfig.UpdateOutput(output =>
        {
            output.Looks ??= new List<OutputLookDefinition>();
            var activeLookId = NormalizeActiveLookId(_machineState.OutputBinding.ActiveLookId, BuildLooks(output, _machineState.OutputBinding));
            editableLook = EnsureEditableLook(output, activeLookId);
            editableLook.ClearGroups = normalizedGroups.Select(static group => group.Clone()).ToList();
            UpsertLook(output, editableLook);
        });
        _machineState.UpdateOutputBinding(binding => binding.ActiveLookId = editableLook.Id);

        await _sharedConfig.SaveAsync(cancellationToken).ConfigureAwait(false);
        await _machineState.SaveAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Updated {ClearGroupCount} clear groups for output look {LookId}.", normalizedGroups.Count, editableLook.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<OutputLookDefinition> BuildLooks() => BuildLooks(_sharedConfig.Output, _machineState.OutputBinding);

    private IReadOnlyList<OutputLookDefinition> BuildLooks(OutputConfig output, OutputBinding binding)
    {
        IReadOnlyList<OutputFeedDefinition> feeds = Feeds;
        var looks = new List<OutputLookDefinition>
        {
            OutputRoutingDefaults.CreateDefaultLook(feeds),
        };

        var portableLooks = output.Looks;
        if (portableLooks is { Count: > 0 })
        {
            looks.AddRange(portableLooks
                .Where(look => !string.Equals(look.Id, OutputLookIds.Default, StringComparison.OrdinalIgnoreCase))
                .Select(look => NormalizeLook(look, feeds)));
        }
        else if (binding.Looks is { Count: > 0 })
        {
            looks.AddRange(binding.Looks
                .Where(look => !string.Equals(look.Id, OutputLookIds.Default, StringComparison.OrdinalIgnoreCase))
                .Select(look => NormalizeLook(look, feeds)));
        }

        return looks;
    }

    private OutputLookDefinition EnsureEditableLook(OutputConfig output, string activeLookId)
    {
        var current = BuildLooks(output, _machineState.OutputBinding)
            .FirstOrDefault(look => string.Equals(look.Id, activeLookId, StringComparison.OrdinalIgnoreCase))
            ?? OutputRoutingDefaults.CreateDefaultLook(Feeds);

        if (string.Equals(current.Id, OutputLookIds.Default, StringComparison.OrdinalIgnoreCase) || current.IsBuiltIn)
        {
            var custom = output.Looks?.FirstOrDefault(look =>
                string.Equals(look.Id, OutputLookIds.Custom, StringComparison.OrdinalIgnoreCase));
            if (custom != null)
                return NormalizeLook(custom, Feeds);

            return OutputRoutingDefaults.CreateCustomLook(current, Feeds);
        }

        return NormalizeLook(current, Feeds);
    }

    private static void UpsertLook(OutputConfig output, OutputLookDefinition look)
    {
        output.Looks ??= new List<OutputLookDefinition>();
        var existingIndex = output.Looks.FindIndex(candidate =>
            string.Equals(candidate.Id, look.Id, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            output.Looks[existingIndex] = look.Clone();
        else
            output.Looks.Add(look.Clone());
    }

    private static string NormalizeActiveLookId(string? lookId, IReadOnlyList<OutputLookDefinition> looks)
    {
        var requested = string.IsNullOrWhiteSpace(lookId) ? OutputLookIds.Default : lookId.Trim();
        return looks.Any(look => string.Equals(look.Id, requested, StringComparison.OrdinalIgnoreCase))
            ? requested
            : OutputLookIds.Default;
    }

    private static OutputLookDefinition NormalizeLook(
        OutputLookDefinition look,
        IReadOnlyList<OutputFeedDefinition> feeds)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentNullException.ThrowIfNull(feeds);

        OutputLookDefinition clone = look.Clone();
        clone.Routes = NormalizeRoutes(clone.Routes, feeds).ToList();

        clone.ClearGroups = clone.ClearGroups
            .Select(NormalizeClearGroup)
            .Where(static group => ResolveClearLayers(group).Count > 0)
            .ToList();

        return clone;
    }

    private static IEnumerable<OutputLookFeedRouting> NormalizeRoutes(
        IEnumerable<OutputLookFeedRouting> routes,
        IReadOnlyList<OutputFeedDefinition> feeds)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(feeds);

        OutputLookFeedRouting[] routeArray = routes.Select(static route => route.Clone()).ToArray();
        foreach (OutputFeedDefinition feed in feeds)
        {
            OutputLookFeedRouting route = routeArray.FirstOrDefault(candidate =>
                    string.Equals(candidate.FeedId, feed.Id, StringComparison.OrdinalIgnoreCase))
                ?? new OutputLookFeedRouting
                {
                    FeedId = feed.Id,
                    Slide = true,
                    Media = true,
                };

            route.FeedId = feed.Id;
            NormalizeLayerRouteValues(route);
            OutputRoutingDefaults.EnsureLayerRoutes(route);
            yield return route;
        }
    }

    private static void NormalizeLayerRouteValues(OutputLookFeedRouting route)
    {
        foreach (OutputLayerRouteDefinition layer in route.Layers)
        {
            layer.ThemeVariantId = string.IsNullOrWhiteSpace(layer.ThemeVariantId) ? null : layer.ThemeVariantId.Trim();
            layer.MaskId = string.IsNullOrWhiteSpace(layer.MaskId) ? null : layer.MaskId.Trim();
        }
    }

    private static void EnsureMaskDefinitions(OutputConfig output, IEnumerable<OutputLookFeedRouting> routes)
    {
        output.Masks ??= new List<OutputMaskDefinition>();
        HashSet<string> knownIds = output.Masks
            .Select(static mask => mask.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string maskId in routes
                     .Select(route => route.ResolveLayerRoute(OutputLayerKind.Mask)?.MaskId)
                     .Where(static maskId => !string.IsNullOrWhiteSpace(maskId))
                     .Select(static maskId => maskId!.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (knownIds.Contains(maskId))
                continue;

            output.Masks.Add(new OutputMaskDefinition
            {
                Id = maskId,
                Name = maskId,
            });
            knownIds.Add(maskId);
        }
    }

    private static OutputLookClearGroupDefinition NormalizeClearGroup(OutputLookClearGroupDefinition clearGroup)
    {
        ArgumentNullException.ThrowIfNull(clearGroup);

        OutputLookClearGroupDefinition clone = clearGroup.Clone();
        clone.Id = string.IsNullOrWhiteSpace(clone.Id)
            ? $"clear-{Guid.NewGuid():N}"
            : clone.Id.Trim();
        clone.Name = string.IsNullOrWhiteSpace(clone.Name)
            ? "Clear Group"
            : clone.Name.Trim();
        clone.Icon = string.IsNullOrWhiteSpace(clone.Icon)
            ? "\uE894"
            : clone.Icon.Trim();
        clone.TintColor = string.IsNullOrWhiteSpace(clone.TintColor)
            ? null
            : clone.TintColor.Trim();
        clone.Scopes = clone.Scopes.Distinct().ToList();
        clone.Layers = ResolveClearLayers(clone)
            .Select(OutputRoutingDefaults.GetLayerId)
            .ToList();
        return clone;
    }

    private static IReadOnlySet<OutputLayerKind> ResolveClearLayers(OutputLookClearGroupDefinition clearGroup) =>
        OutputRoutingDefaults.ResolveClearGroupLayers(clearGroup);
}