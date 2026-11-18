using CommunityToolkit.Mvvm.ComponentModel;
using VectorMatchEngine.Core.Models;

namespace VectorMatchEngine.UI.ViewModels;

/// <summary>
/// One row of the Step 2 column configuration list. Ticking Vectorize unticks Preserve (a vectorized column is
/// usually the identifying kind, which shouldn't be kept readable); Sounds alike and a field type other than Text
/// need Vectorize, and unticking Vectorize clears both.
/// </summary>
public partial class ColumnSelectionItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isVectorized;
    [ObservableProperty] private bool _isPreserved;

    /// <summary>Encode this column's words by how they sound (Metaphone), so Smith and Smyth are the same.</summary>
    [ObservableProperty] private bool _isSoundsAlike;

    /// <summary>The field type's position in <see cref="FieldKinds.All"/> (the combo box binds by index).</summary>
    [ObservableProperty] private int _kindIndex;

    public FieldKind Kind => KindIndex >= 0 && KindIndex < FieldKinds.All.Count ? FieldKinds.All[KindIndex] : FieldKind.Text;

    /// <summary>The field types, for the combo box.</summary>
    public static IReadOnlyList<string> KindLabels => FieldKinds.Labels;

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
        {
            IsSoundsAlike = false;
            KindIndex = 0;
        }
    }

    partial void OnKindIndexChanged(int value)
    {
        if (Kind != FieldKind.Text)
            IsVectorized = true;
    }

    partial void OnIsSoundsAlikeChanged(bool value)
    {
        if (value)
            IsVectorized = true;
    }
}
