#nullable enable

using ChurchPresenter.Backend.Rendering;

namespace ChurchPresenter.Backend.Pipeline.Cues;

/// <summary>
/// A resolvable mask asset: vector geometry compiled to a scene, or an alpha image path.
/// </summary>
public sealed record MaskAsset
{
    /// <summary>Stable mask id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Operator-facing display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Mask source kind: "vector" or "image".</summary>
    public string MaskKind { get; init; } = "vector";

    /// <summary>Compiled vector mask scene, when <see cref="MaskKind"/> is "vector".</summary>
    public SlideScene? Scene { get; init; }

    /// <summary>Alpha image source path, when <see cref="MaskKind"/> is "image".</summary>
    public string? SourcePath { get; init; }

    /// <summary>Whether the mask alpha is inverted.</summary>
    public bool Invert { get; init; }
}

/// <summary>
/// Resolves mask assets for the <see cref="CueKind.Mask"/> prepare path and Look-routed masks.
/// </summary>
public interface IMaskAssetCatalog
{
    /// <summary>Resolves a mask asset by id, or null when no such mask exists.</summary>
    MaskAsset? Resolve(string maskId);
}

/// <summary>
/// In-memory v1 mask asset catalog. Provides a full-frame rectangle vector mask so the mask
/// cue prepare path is wired and testable; richer vector authoring and alpha-image masks attach
/// to this catalog later. Unknown ids fall back to the full-frame mask so a routed mask always
/// resolves to a valid payload.
/// </summary>
public sealed class MaskAssetCatalog : IMaskAssetCatalog
{
    /// <summary>Id of the built-in full-frame (no-op alpha) mask.</summary>
    public const string FullFrameMaskId = "mask:full-frame";

    private readonly Dictionary<string, MaskAsset> _masks;

    /// <summary>Initializes the catalog with the built-in v1 masks.</summary>
    public MaskAssetCatalog()
    {
        _masks = new Dictionary<string, MaskAsset>(StringComparer.OrdinalIgnoreCase)
        {
            [FullFrameMaskId] = new MaskAsset
            {
                Id = FullFrameMaskId,
                DisplayName = "Full Frame",
                MaskKind = "vector",
                Scene = BuildFullFrameScene(),
            },
        };
    }

    /// <inheritdoc />
    public MaskAsset? Resolve(string maskId)
    {
        if (string.IsNullOrWhiteSpace(maskId))
            return null;

        string normalized = maskId.Trim();
        if (_masks.TryGetValue(normalized, out MaskAsset? mask))
            return mask;

        // Fall back to a full-frame mask renamed to the requested id so a Look-routed mask id
        // always resolves to a valid, renderable payload in v1.
        return _masks[FullFrameMaskId] with { Id = normalized, DisplayName = normalized };
    }

    private static SlideScene BuildFullFrameScene()
    {
        PixelSize size = PixelSize.FullHd;
        return new SlideScene
        {
            Id = "scene:mask:full-frame",
            Version = "1",
            RenderSize = size,
            Background = new SceneBackground { AlphaMode = RenderAlphaMode.StraightAlpha, Opacity = 0 },
            Nodes =
            [
                new ShapeSceneNode
                {
                    Id = "mask-rect",
                    Name = "Mask",
                    ShapeType = "rectangle",
                    ZOrder = 0,
                    Transform = new SceneNodeTransform { X = 0, Y = 0, Width = size.Width, Height = size.Height },
                    Fills = [new SceneFill { Id = "fill", Color = "#FFFFFF", Opacity = 1 }],
                },
            ],
        };
    }
}
