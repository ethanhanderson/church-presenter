using System.Collections.ObjectModel;
using System.Globalization;

using ChurchPresenter.Backend.Rendering;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace ChurchPresenter.ViewModels;

public partial class ShowViewModel
{
    private void LiveProductionQuery_Changed(object? sender, LiveProductionQueryChangedEventArgs args)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher == null || dispatcher.HasThreadAccess)
        {
            RefreshLiveProductionState(args.Snapshot);
            return;
        }

        dispatcher.TryEnqueue(() => RefreshLiveProductionState(args.Snapshot));
    }

    private void RefreshLiveProductionState(LiveProductionQuerySnapshot snapshot)
    {
        RefreshOutputClearActions(snapshot);
        RefreshAllSlideDeckState();
        _ = LoadShowControlsAsync();
    }

    private void RefreshOutputClearActions(LiveProductionQuerySnapshot snapshot)
    {
        HashSet<OutputLayerKind> liveLayers = snapshot.ActiveLayers
            .Where(static layer => layer.IsLive)
            .Select(static layer => layer.Kind)
            .ToHashSet();

        ShowClearActionViewModel[] primaryActions = OutputRoutingDefaults.Layers
            .Where(static layer => layer.IsClearable)
            .Select(layer => new ShowClearActionViewModel
            {
                Id = $"clear-layer-{layer.Id}",
                Label = layer.DisplayName,
                IconGlyph = ResolveLayerIconGlyph(layer.Kind),
                Layers = [layer.Kind],
                IsActive = liveLayers.Contains(layer.Kind),
                ExecuteRequested = ClearOutputAction,
                CanExecuteRequested = CanClearOutputAction,
            })
            .ToArray();

        LiveClearGroupQuery[] configuredGroups = snapshot.Generated.ClearGroups.ToArray();

        ShowClearActionViewModel[] secondaryActions = configuredGroups
            .Select(group => new ShowClearActionViewModel
            {
                Id = group.Id,
                Label = group.Name,
                IconGlyph = ResolveClearActionIconGlyph(group.Id, group.Name, group.Icon, group.Layers),
                ClearGroupId = group.Id,
                Layers = group.Layers.ToArray(),
                IsActive = group.Layers.Any(liveLayers.Contains),
                TintEnabled = group.TintEnabled,
                TintColor = group.TintColor,
                ExecuteRequested = ClearOutputAction,
                CanExecuteRequested = CanClearOutputAction,
            })
            .OrderBy(static action => GetClearActionSortRank(action))
            .ToArray();

        ApplyLeadingDividers(primaryActions);
        ApplyLeadingDividers(secondaryActions);

        ReplaceActions(PrimaryOutputClearActions, primaryActions);
        ReplaceActions(SecondaryOutputClearActions, secondaryActions);
        ReplaceActions(OutputClearActions, primaryActions.Concat(secondaryActions));
        OnPropertyChanged(nameof(HasSecondaryOutputClearActions));
    }

    /// <summary>Whether the secondary custom clear-groups bar has any visible saved groups.</summary>
    public bool HasSecondaryOutputClearActions => SecondaryOutputClearActions.Count > 0;

    private static void ApplyLeadingDividers(IReadOnlyList<ShowClearActionViewModel> actions)
    {
        for (int i = 0; i < actions.Count; i++)
            actions[i].HasLeadingDivider = i > 0;
    }

    private static void ReplaceActions(
        ObservableCollection<ShowClearActionViewModel> target,
        IEnumerable<ShowClearActionViewModel> actions)
    {
        target.Clear();
        foreach (ShowClearActionViewModel action in actions)
            target.Add(action);
    }

    private void ClearOutputAction(ShowClearActionViewModel? action)
    {
        if (action == null)
            return;

        if (!string.IsNullOrWhiteSpace(action.ClearGroupId))
        {
            _liveProduction.ClearGroup(action.ClearGroupId);
            return;
        }

        if (action.Layers.Count > 0)
            _liveProduction.ClearLayers(action.Layers);
    }

    private bool CanClearOutputAction(ShowClearActionViewModel? action)
    {
        if (action == null)
            return false;

        HashSet<OutputLayerKind> targetLayers = action.Layers.ToHashSet();
        if (targetLayers.Count == 0)
            return false;

        return _liveProductionQuery.Current.ActiveLayers.Any(layer =>
            targetLayers.Contains(layer.Kind) && layer.IsLive);
    }

    private static int GetClearActionSortRank(ShowClearActionViewModel action)
    {
        HashSet<OutputLayerKind> layers = action.Layers.ToHashSet();

        if (layers.SetEquals([OutputLayerKind.Slide]))
            return 0;

        if (layers.SetEquals([OutputLayerKind.Media, OutputLayerKind.Audio]))
            return 1;

        if (IsClearAllAction(action.Id, layers))
            return 99;

        if (layers.Contains(OutputLayerKind.Messages) || layers.Contains(OutputLayerKind.Props))
            return 2;

        if (layers.Contains(OutputLayerKind.Announcements))
            return 3;

        if (layers.Contains(OutputLayerKind.LiveVideo))
            return 4;

        return 10;
    }

    private static string ResolveClearActionIconGlyph(string id, string name, string icon, IReadOnlyList<OutputLayerKind> layers)
    {
        HashSet<OutputLayerKind> layerSet = layers.ToHashSet();
        string normalizedId = NormalizeClearActionText(id);
        string normalizedName = NormalizeClearActionText(name);

        if (!string.IsNullOrWhiteSpace(icon))
            return icon;

        if (IsClearAllAction(id, layerSet))
            return "\uE894";

        if (layerSet.SetEquals([OutputLayerKind.Slide]))
            return ResolveLayerIconGlyph(OutputLayerKind.Slide);

        if (layerSet.SetEquals([OutputLayerKind.Media, OutputLayerKind.Audio]) || layerSet.Contains(OutputLayerKind.Media))
            return ResolveLayerIconGlyph(OutputLayerKind.Media);

        if (layerSet.SetEquals([OutputLayerKind.Audio]))
            return ResolveLayerIconGlyph(OutputLayerKind.Audio);

        if (layerSet.Contains(OutputLayerKind.Messages) || layerSet.Contains(OutputLayerKind.Props))
            return ResolveLayerIconGlyph(OutputLayerKind.Messages);

        if (layerSet.Contains(OutputLayerKind.Announcements))
            return ResolveLayerIconGlyph(OutputLayerKind.Announcements);

        if (layerSet.Contains(OutputLayerKind.LiveVideo))
            return ResolveLayerIconGlyph(OutputLayerKind.LiveVideo);

        if (layerSet.Contains(OutputLayerKind.Mask))
            return ResolveLayerIconGlyph(OutputLayerKind.Mask);

        if (normalizedId.Contains("overlay", StringComparison.Ordinal) || normalizedName.Contains("overlay", StringComparison.Ordinal))
            return ResolveLayerIconGlyph(OutputLayerKind.Messages);

        return "\uE894";
    }

    private static string ResolveLayerIconGlyph(OutputLayerKind layerKind) =>
        layerKind switch
        {
            OutputLayerKind.Slide => "\uE8B9",
            OutputLayerKind.Media => "\uE91B",
            OutputLayerKind.Audio => "\uE189",
            OutputLayerKind.Messages => "\uE724",
            OutputLayerKind.Props => "\uE7C3",
            OutputLayerKind.Announcements => "\uE789",
            OutputLayerKind.LiveVideo => "\uE714",
            OutputLayerKind.Mask => "\uE72E",
            _ => "\uE894",
        };

    private static bool IsClearAllAction(string id, IReadOnlySet<OutputLayerKind> layers)
    {
        if (string.Equals(id, "clear-all", StringComparison.OrdinalIgnoreCase))
            return true;

        return layers.Count >= OutputRoutingDefaults.Layers.Count(static layer => layer.IsClearable)
            && OutputRoutingDefaults.Layers
                .Where(static layer => layer.IsClearable)
                .All(layer => layers.Contains(layer.Kind));
    }

    private static string NormalizeClearActionText(string value) =>
        value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}

/// <summary>Operator-facing clear action shown in the Show output panel.</summary>
public sealed partial class ShowClearActionViewModel : ObservableObject
{
    /// <summary>Stable action id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display label.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Symbol-font glyph shown for this clear action.</summary>
    public string IconGlyph { get; init; } = "\uE894";

    /// <summary>Optional configured clear group id.</summary>
    public string? ClearGroupId { get; init; }

    /// <summary>Backend layers affected by the clear action.</summary>
    public IReadOnlyList<OutputLayerKind> Layers { get; init; } = Array.Empty<OutputLayerKind>();

    /// <summary>Whether any target layer currently contributes output.</summary>
    public bool IsActive { get; init; }

    /// <summary>Whether this action should draw a divider before itself.</summary>
    public bool HasLeadingDivider { get; set; }

    /// <summary>Whether the action uses a configured tint.</summary>
    public bool TintEnabled { get; init; }

    /// <summary>Optional configured tint color.</summary>
    public string? TintColor { get; init; }

    /// <summary>Optional button background brush when a configured tint is enabled.</summary>
    public Brush? ButtonBackgroundBrush => TintEnabled && TryParseHexColor(TintColor, out Color color)
        ? new SolidColorBrush(Color.FromArgb(ResolveTintBackgroundAlpha(color.A, IsActive), color.R, color.G, color.B))
        : null;

    /// <summary>Compatibility brush for any legacy icon-container tint visuals.</summary>
    public Brush? IconBackgroundBrush => ButtonBackgroundBrush;

    private static byte ResolveTintBackgroundAlpha(byte sourceAlpha, bool isActive)
    {
        double opacity = isActive ? 0.55 : 0.25;
        return (byte)Math.Round(sourceAlpha * opacity, MidpointRounding.AwayFromZero);
    }

    /// <summary>Callback invoked when the operator requests this action.</summary>
    public Action<ShowClearActionViewModel>? ExecuteRequested { get; init; }

    /// <summary>Callback used to determine whether this action can run.</summary>
    public Func<ShowClearActionViewModel, bool>? CanExecuteRequested { get; init; }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private void Execute() => ExecuteRequested?.Invoke(this);

    private bool CanExecute() => CanExecuteRequested?.Invoke(this) == true;

    private static bool TryParseHexColor(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string hex = value.Trim().TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;

        if (hex.Length != 8
            || !byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a)
            || !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }
}
