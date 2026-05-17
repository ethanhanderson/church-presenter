using Windows.ApplicationModel;

namespace ChurchPresenter;

/// <summary>
/// Display strings from the app package (MSIX / Microsoft Store) when available, with fallbacks for unpackaged runs.
/// </summary>
internal static class AppMetadata
{
    public static string GetDisplayName()
    {
        try
        {
            Package? p = Package.Current;
            if (p is not null && !string.IsNullOrWhiteSpace(p.DisplayName))
                return p.DisplayName.Trim();
        }
        catch
        {
            // Unpackaged or restricted context.
        }

        return "Church Presenter";
    }

    public static string GetPublisherDisplayName()
    {
        try
        {
            Package? p = Package.Current;
            if (p is not null && !string.IsNullOrWhiteSpace(p.PublisherDisplayName))
                return p.PublisherDisplayName.Trim();
        }
        catch
        {
        }

        return "";
    }

    /// <summary>
    /// Major and minor version only (Identity Version in the manifest for packaged apps), excluding build and revision.
    /// </summary>
    public static string GetVersionMajorMinor()
    {
        try
        {
            Package? p = Package.Current;
            if (p?.Id is not null)
            {
                PackageVersion v = p.Id.Version;
                return $"{v.Major}.{v.Minor}";
            }
        }
        catch
        {
        }

        Version? av = typeof(App).Assembly.GetName().Version;
        return av is not null ? $"{av.Major}.{av.Minor}" : "0.0";
    }
}