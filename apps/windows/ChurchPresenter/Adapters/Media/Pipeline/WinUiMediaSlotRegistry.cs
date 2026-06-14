#nullable enable

using ChurchPresenter.Backend.Pipeline.HostAdapter;
using ChurchPresenter.Services.Media;
using ChurchPresenter.Services.Pipeline;

namespace ChurchPresenter.Adapters.Media.Pipeline;

/// <summary>
/// Tracks active WinUI media slot bindings by layer slot id.
/// </summary>
public sealed class WinUiMediaSlotRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<LayerSlotId, WinUiMediaSlotAdapter> _activeBindings = new();
    private readonly Dictionary<string, WinUiMediaSlotAdapter> _preparedBindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or creates one media slot binding.</summary>
    public WinUiMediaSlotAdapter GetOrCreate(
        LayerSlotId slotId,
        string playerId,
        string? sourcePath = null,
        bool? loop = null)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath) && loop.HasValue)
            {
                string preparedKey = BuildPreparedKey(slotId, sourcePath, loop.Value);
                if (_preparedBindings.TryGetValue(preparedKey, out WinUiMediaSlotAdapter? prepared))
                {
                    _activeBindings[slotId] = prepared;
                    return prepared;
                }
            }

            if (_activeBindings.TryGetValue(slotId, out WinUiMediaSlotAdapter? existing)
                && (string.IsNullOrWhiteSpace(sourcePath)
                    || !loop.HasValue
                    || existing.IsBoundTo(sourcePath, loop.Value)))
            {
                return existing;
            }

            WinUiMediaSlotAdapter created = new(slotId, playerId);
            _activeBindings[slotId] = created;
            if (!string.IsNullOrWhiteSpace(sourcePath) && loop.HasValue)
                _preparedBindings[BuildPreparedKey(slotId, sourcePath, loop.Value)] = created;

            return created;
        }
    }

    /// <summary>Gets or creates a retained binding for startup preloading without making it active.</summary>
    public WinUiMediaSlotAdapter GetOrCreatePrepared(
        LayerSlotId slotId,
        string playerId,
        string sourcePath,
        bool loop)
    {
        lock (_gate)
        {
            string key = BuildPreparedKey(slotId, sourcePath, loop);
            if (_preparedBindings.TryGetValue(key, out WinUiMediaSlotAdapter? existing))
            {
                return existing;
            }

            WinUiMediaSlotAdapter created = new(slotId, playerId);
            _preparedBindings[key] = created;
            return created;
        }
    }

    /// <summary>Gets an existing binding when present.</summary>
    public WinUiMediaSlotAdapter? TryGet(LayerSlotId slotId)
    {
        lock (_gate)
            return _activeBindings.TryGetValue(slotId, out WinUiMediaSlotAdapter? binding) ? binding : null;
    }

    /// <summary>Removes and disposes one binding.</summary>
    public async Task RemoveAsync(LayerSlotId slotId)
    {
        WinUiMediaSlotAdapter? binding;
        WinUiMediaSlotAdapter[] preparedBindings;
        lock (_gate)
        {
            _activeBindings.Remove(slotId, out binding);
            string[] keys = _preparedBindings.Keys
                .Where(key => key.StartsWith(slotId.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            preparedBindings = keys.Select(key => _preparedBindings[key]).Distinct().ToArray();
            foreach (string key in keys)
                _preparedBindings.Remove(key);
        }

        if (binding != null)
            await binding.DisposeAsync().ConfigureAwait(false);

        foreach (WinUiMediaSlotAdapter prepared in preparedBindings)
        {
            if (!ReferenceEquals(prepared, binding))
                await prepared.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Disposes all active bindings.</summary>
    public async Task ClearAsync()
    {
        WinUiMediaSlotAdapter[] bindings;
        lock (_gate)
        {
            bindings = _activeBindings.Values.Concat(_preparedBindings.Values).Distinct().ToArray();
            _activeBindings.Clear();
            _preparedBindings.Clear();
        }

        foreach (WinUiMediaSlotAdapter binding in bindings)
            await binding.DisposeAsync().ConfigureAwait(false);
    }

    private static string BuildPreparedKey(LayerSlotId slotId, string sourcePath, bool loop)
    {
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(sourcePath).Replace('\\', '/');
        }
        catch
        {
            normalizedPath = sourcePath.Trim().Replace('\\', '/');
        }

        return string.Concat(slotId.ToString(), "|", normalizedPath, "|", loop);
    }
}
