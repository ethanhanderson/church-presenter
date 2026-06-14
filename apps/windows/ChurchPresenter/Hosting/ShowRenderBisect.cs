namespace ChurchPresenter.Hosting;

/// <summary>
/// Debug-only flags for isolating Show render surfaces during transient navigation bisect.
/// Set via environment variables, <c>%LOCALAPPDATA%/ChurchPresenter/show-render-bisect.env</c>, or static properties.
/// </summary>
internal static class ShowRenderBisect
{
    private static bool? _suppressOperatorPreviewAttach;
    private static bool? _suppressSlideDeckThumbnails;
    private static bool? _delayThumbnailUntilShowSettled;
    private static bool _fileFlagsLoaded;

    /// <summary>Skips operator preview mirror attach/teardown.</summary>
    public static bool SuppressOperatorPreviewAttach =>
        _suppressOperatorPreviewAttach ?? ReadFlag("CHURCHPRESENTER_SUPPRESS_PREVIEW_ATTACH");

    /// <summary>Skips slide deck thumbnail rasterization.</summary>
    public static bool SuppressSlideDeckThumbnails =>
        _suppressSlideDeckThumbnails ?? ReadFlag("CHURCHPRESENTER_SUPPRESS_DECK_THUMBNAILS");

    /// <summary>Defers deck thumbnail requests until Show revisit compositor has settled.</summary>
    public static bool DelayThumbnailUntilShowSettled =>
        _delayThumbnailUntilShowSettled ?? ReadFlag("CHURCHPRESENTER_DELAY_DECK_THUMBNAILS");

    public static void SetSuppressOperatorPreviewAttach(bool value) =>
        _suppressOperatorPreviewAttach = value;

    public static void SetSuppressSlideDeckThumbnails(bool value) =>
        _suppressSlideDeckThumbnails = value;

    public static void SetDelayThumbnailUntilShowSettled(bool value) =>
        _delayThumbnailUntilShowSettled = value;

    public static void ResetOverrides()
    {
        _suppressOperatorPreviewAttach = null;
        _suppressSlideDeckThumbnails = null;
        _delayThumbnailUntilShowSettled = null;
    }

    public static string DescribeActiveFlags()
    {
        var parts = new List<string>();
        if (SuppressOperatorPreviewAttach)
            parts.Add("SuppressOperatorPreviewAttach");
        if (SuppressSlideDeckThumbnails)
            parts.Add("SuppressSlideDeckThumbnails");
        if (DelayThumbnailUntilShowSettled)
            parts.Add("DelayThumbnailUntilShowSettled");
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    private static bool ReadFlag(string name)
    {
        EnsureFileFlagsLoaded();
        string? value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
            return value is "1" or "true" or "TRUE" or "yes" or "YES";

        return false;
    }

    private static void EnsureFileFlagsLoaded()
    {
        if (_fileFlagsLoaded)
            return;

        _fileFlagsLoaded = true;
        string path = ResolveBisectFilePath();
        if (!File.Exists(path))
            return;

        try
        {
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (!IsTruthy(value))
                    continue;

                switch (key)
                {
                    case "CHURCHPRESENTER_SUPPRESS_PREVIEW_ATTACH":
                        _suppressOperatorPreviewAttach = true;
                        break;
                    case "CHURCHPRESENTER_SUPPRESS_DECK_THUMBNAILS":
                        _suppressSlideDeckThumbnails = true;
                        break;
                    case "CHURCHPRESENTER_DELAY_DECK_THUMBNAILS":
                        _delayThumbnailUntilShowSettled = true;
                        break;
                }
            }
        }
        catch
        {
            // Bisect flags must never affect navigation.
        }
    }

    internal static string ResolveBisectFilePath()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChurchPresenter");
        return Path.Combine(root, "show-render-bisect.env");
    }

    private static bool IsTruthy(string value) =>
        value is "1" or "true" or "TRUE" or "yes" or "YES";
}
