using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// Edits active-Look clear groups from the Show output-panel flyout.
/// </summary>
public sealed partial class ShowClearGroupsFlyoutViewModel(IOutputRoutingService routing) : ObservableObject
{
    private readonly IOutputRoutingService _routing = routing ?? throw new ArgumentNullException(nameof(routing));
    private readonly SemaphoreSlim _persistGate = new(1, 1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedClearGroup))]
    private ClearGroupSettingsItem? _selectedClearGroup;

    /// <summary>Configured custom clear groups for the active output Look.</summary>
    public ObservableCollection<ClearGroupSettingsItem> ClearGroups { get; } = new();

    /// <summary>Built-in icon choices shown in the flyout picker.</summary>
    public IReadOnlyList<ClearGroupIconOption> IconOptions { get; } =
    [
        new("Clear", "\uE894"),
        new("Slide", "\uE8B9"),
        new("Media", "\uE91B"),
        new("Audio", "\uE189"),
        new("Messages", "\uE724"),
        new("Props", "\uE7C3"),
        new("Announcements", "\uE789"),
        new("Video input", "\uE714"),
        new("Mask", "\uE72E"),
    ];

    /// <summary>Simple tint choices shown in the flyout picker.</summary>
    public IReadOnlyList<ClearGroupTintOption> TintOptions { get; } =
    [
        new("No tint", string.Empty),
        new("Red", "#C42B1C"),
        new("Orange", "#F7630C"),
        new("Gold", "#FFB900"),
        new("Green", "#107C10"),
        new("Blue", "#0078D4"),
        new("Purple", "#8764B8"),
    ];

    /// <summary>Whether a group is selected for editing.</summary>
    public bool HasSelectedClearGroup => SelectedClearGroup != null;

    /// <summary>Loads the active Look's clear groups into the flyout editor.</summary>
    public void Load()
    {
        ClearGroups.Clear();
        foreach (OutputLookClearGroupDefinition group in _routing.ActiveLook.ClearGroups)
            ClearGroups.Add(ClearGroupSettingsItem.FromDefinition(group));

        SelectedClearGroup = ClearGroups.FirstOrDefault();
    }

    /// <summary>Adds a new custom clear group and persists it to the active output Look.</summary>
    public async Task AddClearGroupAsync(CancellationToken cancellationToken = default)
    {
        var item = ClearGroupSettingsItem.FromDefinition(new OutputLookClearGroupDefinition
        {
            Id = $"clear-{Guid.NewGuid():N}",
            Name = CreateNewGroupName(),
            Icon = "\uE894",
            TintEnabled = false,
            Scopes = [OutputClearScope.Presentation],
            Layers = OutputRoutingDefaults.CreateClearGroupLayers(OutputClearScope.Presentation),
        });

        ClearGroups.Add(item);
        SelectedClearGroup = item;
        await PersistClearGroupsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Deletes a custom clear group and persists the active output Look.</summary>
    public async Task DeleteClearGroupAsync(ClearGroupSettingsItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        int index = ClearGroups.IndexOf(item);
        if (index < 0)
            return;

        bool wasSelected = ReferenceEquals(SelectedClearGroup, item);
        ClearGroups.RemoveAt(index);
        if (wasSelected)
        {
            SelectedClearGroup = ClearGroups.Count == 0
                ? null
                : ClearGroups[Math.Clamp(index, 0, ClearGroups.Count - 1)];
        }

        await PersistClearGroupsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Persists the edited clear groups to the active output Look.</summary>
    public async Task PersistClearGroupsAsync(CancellationToken cancellationToken = default)
    {
        await _persistGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await _routing.SetClearGroupsAsync(
                ClearGroups.Select(static group => group.ToDefinition()),
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private string CreateNewGroupName()
    {
        const string baseName = "New Clear Group";
        HashSet<string> existingNames = ClearGroups
            .Select(static group => group.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName))
            return baseName;

        for (int index = 2; index < 100; index++)
        {
            string candidate = $"{baseName} {index}";
            if (!existingNames.Contains(candidate))
                return candidate;
        }

        return $"{baseName} {ClearGroups.Count + 1}";
    }
}

/// <summary>One selectable icon for a clear group.</summary>
public sealed record ClearGroupIconOption(string Name, string Glyph);

/// <summary>One selectable icon tint for a clear group.</summary>
public sealed record ClearGroupTintOption(string Name, string Color)
{
    /// <summary>Brush used by the flyout swatch preview.</summary>
    public Brush PreviewBrush => TryParseHexColor(Color, out Color color)
        ? new SolidColorBrush(color)
        : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    private static bool TryParseHexColor(string value, out Color color)
    {
        color = default;
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

        color = Windows.UI.Color.FromArgb(a, r, g, b);
        return true;
    }
}
