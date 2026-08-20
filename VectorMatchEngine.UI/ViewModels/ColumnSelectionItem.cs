using CommunityToolkit.Mvvm.ComponentModel;

namespace VectorMatchEngine.UI.ViewModels;

/// <summary>One row of the Step 2 column configuration list.</summary>
public partial class ColumnSelectionItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isVectorized;
    [ObservableProperty] private bool _isPreserved;

    public ColumnSelectionItem() { }

    public ColumnSelectionItem(string name, bool isVectorized = false, bool isPreserved = true)
    {
        _name = name;
        _isVectorized = isVectorized;
        _isPreserved = isPreserved;
    }
}
