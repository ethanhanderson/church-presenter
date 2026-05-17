using System.Globalization;
using System.Resources;

namespace ChurchPresenter.Core.Resources;

/// <summary>
/// Localized error strings for <see cref="ChurchPresenter.Core.Cpres.CpresException"/> and bundle I/O. Keys match <c>ErrorMessages.resx</c>.
/// </summary>
internal static class ErrorMessageResources
{
    private static readonly ResourceManager Manager = new(
        "ChurchPresenter.Core.Resources.ErrorMessages",
        typeof(ErrorMessageResources).Assembly);

    /// <summary>Returns the message when a file path argument is null or whitespace.</summary>
    internal static string PathRequired =>
        Manager.GetString(nameof(PathRequired), CultureInfo.CurrentCulture) ?? "Path is required.";

    /// <summary>Returns the message when a target path has no valid parent directory.</summary>
    internal static string InvalidPath =>
        Manager.GetString(nameof(InvalidPath), CultureInfo.CurrentCulture) ?? "Invalid path.";

    /// <summary>Returns the message for a missing entry inside a <c>.cpres</c> bundle.</summary>
    /// <param name="name">The expected entry path.</param>
    internal static string MissingBundleFile(string name) =>
        string.Format(
            CultureInfo.CurrentCulture,
            Manager.GetString(nameof(MissingBundleFile), CultureInfo.CurrentCulture) ?? "Missing file in bundle: {0}",
            name);

    /// <summary>Returns the message when <c>manifest.json</c> is structurally invalid.</summary>
    internal static string InvalidManifest =>
        Manager.GetString(nameof(InvalidManifest), CultureInfo.CurrentCulture)
        ?? "Invalid bundle: manifest must include formatVersion and presentationId.";
}