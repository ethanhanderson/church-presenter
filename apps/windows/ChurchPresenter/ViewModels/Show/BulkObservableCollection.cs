using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChurchPresenter.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> extended with <see cref="ReplaceAll"/> — a batch
/// update that populates the underlying list without firing per-item notifications, then
/// emits a single <see cref="NotifyCollectionChangedAction.Reset"/> event.
/// <para>
/// Use <see cref="ReplaceAll"/> instead of <c>Clear()</c> + a loop of <c>Add()</c> calls
/// whenever the entire contents are replaced at once.  The UI receives one notification instead
/// of (1 + N), which eliminates the "flash blank then fill" artifact in bound ItemsControls.
/// </para>
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Replaces all items with <paramref name="newItems"/> and fires a single
    /// <see cref="NotifyCollectionChangedAction.Reset"/> notification.
    /// When <paramref name="newItems"/> is the same sequence already in the collection
    /// (all references equal in order) the method returns without firing any event.
    /// </summary>
    public void ReplaceAll(IEnumerable<T>? newItems)
    {
        var incoming = newItems == null ? [] : newItems.ToList();

        // Fast path: same reference order — nothing changed, skip all notifications.
        if (Count == incoming.Count)
        {
            var allSame = true;
            for (var i = 0; i < Count; i++)
            {
                if (!ReferenceEquals(this[i], incoming[i])) { allSame = false; break; }
            }
            if (allSame) return;
        }

        CheckReentrancy();
        Items.Clear();
        foreach (var item in incoming)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}