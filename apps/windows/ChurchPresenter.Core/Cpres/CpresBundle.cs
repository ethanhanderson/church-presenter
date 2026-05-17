namespace ChurchPresenter.Core.Cpres;

/// <summary>
/// Represents a successfully opened <c>.cpres</c> bundle with JSON payloads and embedded theme files.
/// </summary>
public sealed class ParsedBundle
{
    /// <summary>Gets the raw <c>manifest.json</c> text.</summary>
    public required string ManifestJson { get; init; }

    /// <summary>Gets the raw <c>slides.json</c> text.</summary>
    public required string SlidesJson { get; init; }

    /// <summary>Gets the raw <c>arrangement.json</c> text.</summary>
    public required string ArrangementJson { get; init; }

    /// <summary>Gets theme JSON entries read from the <c>themes/</c> folder inside the archive.</summary>
    public required IReadOnlyList<ThemeFileEntry> Themes { get; init; }
}

/// <summary>
/// A single theme JSON file stored under <c>themes/</c> in the bundle.
/// </summary>
public sealed class ThemeFileEntry
{
    /// <summary>Gets the path inside the archive (forward slashes).</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the UTF-8 theme JSON content.</summary>
    public required string Content { get; init; }
}

/// <summary>
/// Serializable state used when writing a new or updated <c>.cpres</c> bundle.
/// </summary>
public sealed class BundleSaveState
{
    /// <summary>Gets or sets the <c>manifest.json</c> payload.</summary>
    public required string ManifestJson { get; init; }

    /// <summary>Gets or sets the <c>slides.json</c> payload.</summary>
    public required string SlidesJson { get; init; }

    /// <summary>Gets or sets the <c>arrangement.json</c> payload.</summary>
    public required string ArrangementJson { get; init; }

    /// <summary>Gets or sets theme files to place under <c>themes/</c>.</summary>
    public required IReadOnlyList<ThemeFileEntry> Themes { get; init; }

    /// <summary>Gets or sets media files to embed (resolved from disk or existing bundle).</summary>
    public required IReadOnlyList<MediaFileRef> Media { get; init; }

    /// <summary>Gets or sets font files to embed.</summary>
    public required IReadOnlyList<FontFileRef> Fonts { get; init; }
}

/// <summary>
/// Reference to a media file to copy into the bundle.
/// </summary>
public sealed class MediaFileRef
{
    /// <summary>Gets the stable identifier for the media asset.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the source path on disk or a <c>bundle:</c> inner path.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Gets the destination path inside the archive.</summary>
    public required string BundlePath { get; init; }
}

/// <summary>
/// Reference to a font file to copy into the bundle.
/// </summary>
public sealed class FontFileRef
{
    /// <summary>Gets the stable identifier for the font.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the source path on disk or a <c>bundle:</c> inner path.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Gets the destination path inside the archive.</summary>
    public required string BundlePath { get; init; }
}

/// <summary>
/// Exception thrown when bundle contents are invalid or I/O fails in a bundle-specific way.
/// </summary>
public sealed class CpresException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CpresException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public CpresException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="CpresException"/> class with an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    public CpresException(string message, Exception inner) : base(message, inner) { }
}