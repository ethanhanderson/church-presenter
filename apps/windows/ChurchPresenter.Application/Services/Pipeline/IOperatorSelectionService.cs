
namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// Operator slide selection state managed by the unified pipeline runtime.
/// </summary>
public interface IOperatorSelectionService
{
    /// <summary>Current operator selection cursor.</summary>
    SelectionCursor SelectionCursor { get; }

    /// <summary>Whether the operator has explicitly overridden program selection.</summary>
    bool UserOverrideSelection { get; }

    /// <summary>Raised when selection state changes.</summary>
    event EventHandler? Changed;

    /// <summary>Updates the operator selection cursor without taking the slide live.</summary>
    void SelectSlide(string? presentationPath, string slideId, string? instanceKey, SelectionSource source = SelectionSource.Operator);

    /// <summary>Clears the operator selection cursor.</summary>
    void ClearSelection();

    /// <summary>Sets whether the operator is intentionally holding selection away from program output.</summary>
    void SetUserOverrideSelection(bool value);
}
