namespace ChurchPresenter.Core.Cpres;

/// <summary>
/// Controls which binary payloads are retained when rewriting a <c>.cpres</c> bundle for export.
/// </summary>
public sealed class CpresBundleExportOptions
{
    /// <summary>When <see langword="false"/>, <c>media/</c> zip entries are omitted.</summary>
    public bool IncludeMedia { get; init; } = true;

    /// <summary>When <see langword="false"/>, <c>fonts/</c> zip entries are omitted.</summary>
    public bool IncludeFonts { get; init; } = true;

    /// <summary>When <see langword="false"/>, embedded <c>themes/</c> snapshots are omitted.</summary>
    public bool IncludeEmbeddedThemes { get; init; } = true;

    /// <summary>Document mode when no media or font binaries are exported.</summary>
    public string ResolveBundleMode() =>
        IncludeMedia || IncludeFonts ? CpresBundleModes.Bundle : CpresBundleModes.Document;
}
