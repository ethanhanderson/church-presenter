using ChurchPresenter.Backend.Overlays;
using ChurchPresenter.Backend.Rendering;
using ChurchPresenter.Backend.Stage;

namespace ChurchPresenter.Models.Show;

/// <summary>
/// Snapshot used by the Show Controls panels.
/// </summary>
public sealed record ShowControlsSnapshot
{
    public IReadOnlyList<ShowAudioPlaylistPanelItem> AudioPlaylists { get; init; } = Array.Empty<ShowAudioPlaylistPanelItem>();

    public IReadOnlyList<ShowAudioCuePanelItem> AudioItems { get; init; } = Array.Empty<ShowAudioCuePanelItem>();

    public IReadOnlyList<ShowStageScreenPanelItem> StageScreens { get; init; } = Array.Empty<ShowStageScreenPanelItem>();

    public IReadOnlyList<ShowStageLayoutPanelItem> StageLayouts { get; init; } = Array.Empty<ShowStageLayoutPanelItem>();

    public IReadOnlyList<ShowTimerPanelItem> Timers { get; init; } = Array.Empty<ShowTimerPanelItem>();

    public IReadOnlyList<ShowMessagePanelItem> Messages { get; init; } = Array.Empty<ShowMessagePanelItem>();

    public IReadOnlyList<ShowPropPanelItem> Props { get; init; } = Array.Empty<ShowPropPanelItem>();

    public IReadOnlyList<ShowMacroPanelItem> Macros { get; init; } = Array.Empty<ShowMacroPanelItem>();
}

public sealed record ShowAudioPlaylistPanelItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Count { get; init; }

    public bool Shuffle { get; init; }

    public double TransitionSeconds { get; init; }
}

public sealed record ShowAudioCuePanelItem
{
    public string Id { get; init; } = string.Empty;

    public string PlaylistId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public bool IsAvailable { get; init; }

    public string AudioKind { get; init; } = "track";

    public string DurationLabel { get; init; } = string.Empty;
}

public sealed record ShowStageScreenPanelItem
{
    public string ScreenId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? ActiveLayoutId { get; init; }

    public string ActiveLayoutName { get; init; } = "No layout";

    public StageAudienceCommandMode? CommandMode { get; init; }

    public bool HasResolvedFrame { get; init; }
}

public sealed record ShowStageLayoutPanelItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int ElementCount { get; init; }
}

public sealed record ShowTimerPanelItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public GeneratedTimerKind Kind { get; init; } = GeneratedTimerKind.Countdown;

    public GeneratedTimerStatus Status { get; init; } = GeneratedTimerStatus.Stopped;

    public string DisplayValue { get; init; } = "00:00";

    public bool AllowsOverrun { get; init; }

    public int DurationSeconds { get; init; }
}

public sealed record ShowMessagePanelItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Template { get; init; } = string.Empty;

    public IReadOnlyList<ShowMessageTokenDefinition> Tokens { get; init; } = Array.Empty<ShowMessageTokenDefinition>();

    public bool IsVisible { get; init; }

    public string PreviewText { get; init; } = string.Empty;
}

public sealed record ShowPropPanelItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? AssetReference { get; init; }

    public string? Text { get; init; }

    public bool IsVisible { get; init; }
}

public sealed record ShowMacroPanelItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? CollectionId { get; init; }

    public string IconKey { get; init; } = "\uE756";

    public string? AccentColor { get; init; }

    public int ActionCount { get; init; }
}

/// <summary>
/// Operator-entered token values for one message fire.
/// </summary>
public sealed record ShowMessageRuntimeTokenValue(string TokenId, string Value);

/// <summary>
/// Stable conversion helpers for panel models.
/// </summary>
public static class ShowControlsModelHelpers
{
    public static GeneratedTimerKind ParseTimerKind(string? kind) =>
        kind?.Trim() switch
        {
            "countdownToTime" => GeneratedTimerKind.CountdownToTime,
            "elapsed" => GeneratedTimerKind.ElapsedTime,
            _ => GeneratedTimerKind.Countdown,
        };

    public static string ToTimerKindKey(GeneratedTimerKind kind) =>
        kind switch
        {
            GeneratedTimerKind.CountdownToTime => "countdownToTime",
            GeneratedTimerKind.ElapsedTime => "elapsed",
            _ => "countdown",
        };

    public static OutputLayerKind? ParseLayerKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Enum.TryParse(value, ignoreCase: true, out OutputLayerKind parsed)
            ? parsed
            : null;
    }
}
