namespace ChurchPresenter.Core.Cpres;

/// <summary>
/// <c>manifest.bundleMode</c> values for <c>.cpres</c> archives.
/// </summary>
public static class CpresBundleModes
{
    /// <summary>Slide JSON and manifest inventories without embedded media or font binaries.</summary>
    public const string Document = "document";

    /// <summary>Full portable bundle with embedded media and font payloads.</summary>
    public const string Bundle = "bundle";
}
