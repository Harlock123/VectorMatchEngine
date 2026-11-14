using CommunityToolkit.Mvvm.ComponentModel;

namespace VectorMatchEngine.UI.ViewModels;

/// <summary>
/// One row of the Step 2 column configuration list. Ticking Vectorize unticks Preserve (a vectorized column is
/// usually the identifying kind, which shouldn't be kept readable); Sounds alike needs Vectorize.
/// </summary>
public partial class ColumnSelectionItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isVectorized;
    [ObservableProperty] private bool _isPreserved;

    /// <summary>Encode this column's words by how they sound (Metaphone), so Smith and Smyth are the same.</summary>
    [ObservableProperty] private bool _isSoundsAlike;

    public ColumnSelectionItem() { }

    public ColumnSelectionItem(string name, bool isVectorized = false, bool isPreserved = true)
    {
        _name = name;
        _isVectorized = isVectorized;
        _isPreserved = isPreserved;
    }

    partial void OnIsVectorizedChanged(bool value)
    {
        if (value)
            IsPreserved = false;
        else
            IsSoundsAlike = false;
    }

    partial void OnIsSoundsAlikeChanged(bool value)
    {
        if (value)
            IsVectorized = true;
    }
}
