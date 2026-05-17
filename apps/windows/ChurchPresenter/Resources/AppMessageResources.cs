using System.Globalization;
using System.Resources;

namespace ChurchPresenter.Resources;

/// <summary>
/// User-visible strings from <c>ErrorMessages.resx</c> (including captions that are not strictly errors).
/// </summary>
internal static class AppErrorMessages
{
    private static readonly ResourceManager Manager = new(
        "ChurchPresenter.Resources.ErrorMessages",
        typeof(AppErrorMessages).Assembly);

    /// <summary>Title for full-screen presentation output windows.</summary>
    internal static string OutputWindowTitle =>
        Manager.GetString(nameof(OutputWindowTitle), CultureInfo.CurrentCulture) ?? "Presentation Output";
}

/// <summary>
/// Template strings for structured logging from <c>LogMessages.resx</c>.
/// </summary>
internal static class AppLogMessages
{
    private static readonly ResourceManager Manager = new(
        "ChurchPresenter.Resources.LogMessages",
        typeof(AppLogMessages).Assembly);

    /// <summary>Message logged when <see cref="ChurchPresenter.Adapters.Display.MonitorService"/> cannot enumerate displays.</summary>
    internal static string MonitorEnumerationFailed =>
        Manager.GetString(nameof(MonitorEnumerationFailed), CultureInfo.CurrentCulture)
        ?? "Monitor enumeration failed; returning no displays.";
}