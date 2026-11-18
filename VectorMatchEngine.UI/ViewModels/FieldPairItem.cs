using CommunityToolkit.Mvvm.ComponentModel;

namespace VectorMatchEngine.UI.ViewModels;

/// <summary>
/// One row of the New Match Job field table: one of Dataset A's vectorized columns, the Dataset B column it's paired
/// with (or none), its weight in a field-by-field score, and whether it must match or is a block field.
/// </summary>
public partial class FieldPairItem : ObservableObject
{
    public const string NotPaired = "(not paired)";

    public FieldPairItem(string nameA, IReadOnlyList<string> columnsB, int pairedIndex)
    {
        NameA = nameA;
        OptionsB = new[] { NotPaired }.Concat(columnsB).ToList();
        _selectedIndexB = pairedIndex + 1;
    }

    public string NameA { get; }

    /// <summary>"(not paired)", then Dataset B's vectorized columns.</summary>
    public IReadOnlyList<string> OptionsB { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaired))]
    private int _selectedIndexB;

    [ObservableProperty] private decimal? _weight = 1;
    [ObservableProperty] private bool _mustMatch;
    [ObservableProperty] private bool _block;

    public bool IsPaired => SelectedIndexB > 0 && SelectedIndexB < OptionsB.Count;

    public string? NameB => IsPaired ? OptionsB[SelectedIndexB] : null;

    partial void OnSelectedIndexBChanged(int value)
    {
        if (!IsPaired)
        {
            MustMatch = false;
            Block = false;
        }
    }

    partial void OnMustMatchChanged(bool value)
    {
        if (value && !IsPaired)
            MustMatch = false;
    }

    partial void OnBlockChanged(bool value)
    {
        if (value && !IsPaired)
            Block = false;
    }
}
