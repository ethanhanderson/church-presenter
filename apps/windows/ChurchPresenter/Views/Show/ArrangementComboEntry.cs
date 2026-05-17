
namespace ChurchPresenter.Views;

/// <summary>Combo row for the arrangements picker.</summary>
public sealed class ArrangementComboEntry
{
    public ArrangementComboEntry(NamedArrangement arrangement)
    {
        Arrangement = arrangement;
    }

    public NamedArrangement Arrangement { get; }

    public string DisplayName => Arrangement.Name;

    /// <summary>Master / natural arrangements cannot be removed from the project.</summary>
    public bool IsDeletable => !Arrangement.IsNatural;
}