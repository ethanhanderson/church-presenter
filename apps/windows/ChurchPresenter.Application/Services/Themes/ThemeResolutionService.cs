
namespace ChurchPresenter.Services.Themes;

/// <summary>
/// Resolves presentation and slide theme bindings into concrete theme slides for scene compilation.
/// </summary>
public interface IThemeResolutionService
{
    ThemeResolutionResult ResolveThemeSlide(PresentationProject? project, PresentationSlide? slide, string? themeVariantId = null);

    ThemeSourceUpdateState GetSourceUpdateState(PresentationThemeBinding? binding, ThemeTemplate? sourceTheme);
}

public sealed class ThemeResolutionResult
{
    public ThemeTemplate? Theme { get; init; }

    public ThemeTemplateSlide? ThemeSlide { get; init; }

    public PresentationThemeBinding? Binding { get; init; }

    public bool IsResolved => ThemeSlide != null;

    public string? StatusMessage { get; init; }
}

public sealed class ThemeSourceUpdateState
{
    public bool CanUpdate { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <inheritdoc />
public sealed class ThemeResolutionService : IThemeResolutionService
{
    /// <inheritdoc />
    public ThemeResolutionResult ResolveThemeSlide(PresentationProject? project, PresentationSlide? slide, string? themeVariantId = null)
    {
        if (project == null || slide == null)
            return new ThemeResolutionResult { StatusMessage = "No presentation slide was supplied." };

        PresentationThemeBinding? binding = ResolveBinding(project, slide);
        if (binding == null || IsDetached(binding))
            return new ThemeResolutionResult { Binding = binding };

        ThemeTemplate? theme = ResolveTheme(project, binding);
        if (theme == null)
        {
            return new ThemeResolutionResult
            {
                Binding = binding,
                StatusMessage = "The applied theme could not be found.",
            };
        }

        ThemeTemplateSlide? themeSlide = ResolveThemeSlide(theme, project, slide, binding, themeVariantId);
        return new ThemeResolutionResult
        {
            Theme = theme,
            ThemeSlide = themeSlide,
            Binding = binding,
            StatusMessage = themeSlide == null ? "No matching slide style was found." : null,
        };
    }

    public ThemeSourceUpdateState GetSourceUpdateState(PresentationThemeBinding? binding, ThemeTemplate? sourceTheme)
    {
        if (binding == null || sourceTheme == null)
            return new ThemeSourceUpdateState { Message = "No theme source selected." };

        if (string.Equals(binding.Mode, ThemeBindingModes.Detached, StringComparison.OrdinalIgnoreCase))
            return new ThemeSourceUpdateState { Message = "This slide is customized." };

        if (string.IsNullOrWhiteSpace(binding.ThemeVersion) || string.IsNullOrWhiteSpace(sourceTheme.Version))
            return new ThemeSourceUpdateState { Message = "Theme source is available." };

        bool canUpdate = !string.Equals(binding.ThemeVersion, sourceTheme.Version, StringComparison.OrdinalIgnoreCase);
        return new ThemeSourceUpdateState
        {
            CanUpdate = canUpdate,
            Message = canUpdate ? "An updated theme is available." : "Theme is up to date.",
        };
    }

    private static PresentationThemeBinding? ResolveBinding(PresentationProject project, PresentationSlide slide)
    {
        if (slide.ThemeBinding != null)
            return slide.ThemeBinding;

        if (project.Manifest.ThemeBinding != null)
            return project.Manifest.ThemeBinding;

        return string.IsNullOrWhiteSpace(project.Manifest.ThemeId)
            ? null
            : new PresentationThemeBinding
            {
                ThemeId = project.Manifest.ThemeId,
                Mode = ThemeBindingModes.Linked,
            };
    }

    private static bool IsDetached(PresentationThemeBinding binding) =>
        string.Equals(binding.Mode, ThemeBindingModes.Detached, StringComparison.OrdinalIgnoreCase)
        || string.Equals(binding.Mode, ThemeBindingModes.Materialized, StringComparison.OrdinalIgnoreCase);

    private static ThemeTemplate? ResolveTheme(PresentationProject project, PresentationThemeBinding binding)
    {
        string? themeId = FirstNonWhiteSpace(binding.EmbeddedSnapshotId, binding.ThemeId);
        if (string.IsNullOrWhiteSpace(themeId))
            return null;

        return project.EmbeddedThemes
            .Select(static entry => entry.Template)
            .Where(static template => template != null)
            .FirstOrDefault(template =>
                string.Equals(template!.Id, themeId, StringComparison.OrdinalIgnoreCase))
            ?? project.EmbeddedThemes
                .Select(static entry => entry.Template)
                .Where(static template => template != null)
                .FirstOrDefault(template =>
                    string.Equals(template!.Id, binding.ThemeId, StringComparison.OrdinalIgnoreCase));
    }

    private static ThemeTemplateSlide? ResolveThemeSlide(
        ThemeTemplate theme,
        PresentationProject project,
        PresentationSlide slide,
        PresentationThemeBinding binding,
        string? themeVariantId)
    {
        if (!string.IsNullOrWhiteSpace(themeVariantId))
        {
            ThemeTemplateSlide? variantSlide = FindByRoleOrId(theme, themeVariantId);
            if (variantSlide != null)
                return variantSlide;
        }

        if (!string.IsNullOrWhiteSpace(binding.ThemeSlideId))
        {
            ThemeTemplateSlide? boundSlide = theme.Slides.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, binding.ThemeSlideId, StringComparison.OrdinalIgnoreCase));
            if (boundSlide != null)
                return boundSlide;
        }

        string role = ResolveSlideRole(slide);
        ThemeRoleMapping? mapping = binding.RoleMappings.FirstOrDefault(candidate =>
            string.Equals(PresentationModelUtilities.NormalizeRole(candidate.SlideRole), role, StringComparison.OrdinalIgnoreCase));
        if (mapping != null)
        {
            ThemeTemplateSlide? mappedSlide = theme.Slides.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, mapping.ThemeSlideId, StringComparison.OrdinalIgnoreCase));
            if (mappedSlide != null)
                return mappedSlide;
        }

        return FindByRoleOrId(theme, role)
            ?? FindByRoleOrId(theme, project.Manifest.ThemeBinding?.ThemeSlideId)
            ?? theme.Slides.FirstOrDefault();
    }

    private static ThemeTemplateSlide? FindByRoleOrId(ThemeTemplate theme, string? roleOrId)
    {
        if (string.IsNullOrWhiteSpace(roleOrId))
            return null;

        string normalized = PresentationModelUtilities.NormalizeRole(roleOrId);
        return theme.Slides.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, roleOrId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(PresentationModelUtilities.NormalizeRole(candidate.LayoutType), normalized, StringComparison.OrdinalIgnoreCase)
            || candidate.Roles.Any(role => string.Equals(PresentationModelUtilities.NormalizeRole(role), normalized, StringComparison.OrdinalIgnoreCase))
            || candidate.RoleAliases.Any(alias => string.Equals(PresentationModelUtilities.NormalizeRole(alias), normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ResolveSlideRole(PresentationSlide slide) =>
        PresentationModelUtilities.NormalizeRole(FirstNonWhiteSpace(slide.LayoutType, slide.Section, slide.SectionLabel, "body"));

    private static string FirstNonWhiteSpace(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
