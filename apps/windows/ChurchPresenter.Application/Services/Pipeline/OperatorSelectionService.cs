using ChurchPresenter.Models.Media;

namespace ChurchPresenter.Services.Pipeline;

/// <summary>
/// In-memory operator selection service extracted from the legacy playback engine.
/// </summary>
public sealed class OperatorSelectionService : IOperatorSelectionService
{
    private SelectionCursor _cursor = SelectionCursor.Empty;
    private bool _userOverrideSelection;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public SelectionCursor SelectionCursor => _cursor;

    /// <inheritdoc />
    public bool UserOverrideSelection => _userOverrideSelection;

    /// <inheritdoc />
    public void SelectSlide(string? presentationPath, string slideId, string? instanceKey, SelectionSource source = SelectionSource.Operator)
    {
        string? normalizedId = string.IsNullOrWhiteSpace(slideId) ? null : slideId;
        if (normalizedId == null)
        {
            ClearSelection();
            return;
        }

        _cursor = new SelectionCursor
        {
            PresentationPath = string.IsNullOrWhiteSpace(presentationPath) ? null : presentationPath,
            SlideId = normalizedId,
            InstanceKey = string.IsNullOrWhiteSpace(instanceKey) ? normalizedId : instanceKey,
            Source = source,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ClearSelection()
    {
        if (!_cursor.HasSelection)
            return;

        _cursor = SelectionCursor.Empty;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SetUserOverrideSelection(bool value)
    {
        if (_userOverrideSelection == value)
            return;

        _userOverrideSelection = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
