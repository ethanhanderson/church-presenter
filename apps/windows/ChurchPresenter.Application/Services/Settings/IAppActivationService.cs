namespace ChurchPresenter.Services.Settings;

/// <summary>
/// Holds a file path from protocol or file activation until the main window consumes it.
/// </summary>
public interface IAppActivationService
{
    /// <summary>Gets the path waiting to be opened, if any.</summary>
    string? PendingPresentationPath { get; }

    /// <summary>Sets the path to open on next navigation (e.g. from a second-instance redirect).</summary>
    /// <param name="path">Presentation file path, or null to clear.</param>
    void SetPendingPresentationPath(string? path);

    /// <summary>Reads and clears the pending presentation path.</summary>
    /// <returns>The path, or null if none was pending.</returns>
    string? ConsumePendingPresentationPath();
}

/// <summary>Thread-safe enough for UI-thread activation in the single-instance WinUI host.</summary>
public sealed class AppActivationService : IAppActivationService
{
    private string? _path;

    /// <inheritdoc />
    public string? PendingPresentationPath => _path;

    /// <inheritdoc />
    public void SetPendingPresentationPath(string? path) => _path = path;

    /// <inheritdoc />
    public string? ConsumePendingPresentationPath()
    {
        var p = _path;
        _path = null;
        return p;
    }
}