namespace ChurchPresenter.Models.Show;

/// <summary>How inactive output-layer clear buttons render while still using <c>AccentButtonStyle</c>.</summary>
public enum OutputClearInactiveAppearance
{
    /// <summary>WinUI disabled accent treatment (<c>AccentFillColorDisabledBrush</c>).</summary>
    DisabledAccent = 0,

    /// <summary>Transparent idle fill with standard icon/border brushes.</summary>
    Transparent = 1,

    /// <summary>Standard control fill brushes aliased onto accent button resources.</summary>
    ControlFill = 2,
}

public static class OutputClearInactiveAppearanceExtensions
{
    public static string ToSettingValue(this OutputClearInactiveAppearance value) =>
        value switch
        {
            OutputClearInactiveAppearance.Transparent => "transparent",
            OutputClearInactiveAppearance.ControlFill => "controlFill",
            _ => "disabledAccent",
        };

    public static OutputClearInactiveAppearance FromSettingValue(string? value) =>
        value switch
        {
            "transparent" => OutputClearInactiveAppearance.Transparent,
            "controlFill" => OutputClearInactiveAppearance.ControlFill,
            _ => OutputClearInactiveAppearance.DisabledAccent,
        };

    public static string GetDisplayName(this OutputClearInactiveAppearance value) =>
        value switch
        {
            OutputClearInactiveAppearance.Transparent => "Transparent",
            OutputClearInactiveAppearance.ControlFill => "Control fill",
            _ => "Disabled accent",
        };
}
