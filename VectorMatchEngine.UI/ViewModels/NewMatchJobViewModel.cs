using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VectorMatchEngine.Core.Models;
using VectorMatchEngine.Data;
using VectorMatchEngine.UI.Navigation;

namespace VectorMatchEngine.UI.ViewModels;

public partial class NewMatchJobViewModel : ObservableObject, IActivatable
{
    private readonly DataService _dataService;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunMatchJobCommand))]
    private DatasetInfo? _selectedDatasetA;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunMatchJobCommand))]
    private DatasetInfo? _selectedDatasetB;

    partial void OnSelectedDatasetAChanged(DatasetInfo? value) => RebuildFields();
    partial void OnSelectedDatasetBChanged(DatasetInfo? value) => RebuildFields();

    // ── Fields ────────────────────────────────────────────────────────────

    /// <summary>Dataset A's vectorized columns, each with the Dataset B column it's paired with.</summary>
    public ObservableCollection<FieldPairItem> FieldRows { get; } = new();

    /// <summary>Both datasets have field vectors, so fields can be paired.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsNote), nameof(ShowFieldsNote))]
    private bool _fieldsAvailable;

    /// <summary>Score as the weighted average of the paired fields, not the whole rows' cosine.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreWholeRow))]
    private bool _scoreByField;

    /// <summary>The inverse of <see cref="ScoreByField"/>, for the other radio button.</summary>
    public bool ScoreWholeRow
    {
        get => !ScoreByField;
        set => ScoreByField = !value;
    }

    /// <summary>Keep each record's best match only.</summary>
    [ObservableProperty] private bool _oneToOne;

    /// <summary>Compare only pairs that share an LSH bucket (or a block field's value).</summary>
    [ObservableProperty] private bool _approximateBlocking;

    public string FieldsNote => SelectedDatasetA is null || SelectedDatasetB is null
        ? "Choose both datasets to pair their fields."
        : FieldsAvailable
            ? string.Empty
            : $"'{(SelectedDatasetA.HasFieldVectors ? SelectedDatasetB.Name : SelectedDatasetA.Name)}' was imported " +
              "before field vectors, so its fields can't be paired: import it again to score, explain or block by " +
              "field. It can still be matched whole-row, one match per record, or with LSH blocking.";

    public bool ShowFieldsNote => FieldsNote.Length > 0;

    private void RebuildFields()
    {
        FieldRows.Clear();
        var a = SelectedDatasetA;
        var b = SelectedDatasetB;
        FieldsAvailable = a is not null && b is not null && a.HasFieldVectors && b.HasFieldVectors;
        if (!FieldsAvailable)
        {
            ScoreByField = false;
            return;
        }

        // Paired by name where the names agree, otherwise by position when both have as many columns.
        var columnsB = b!.VectorizedColumns;
        bool samePositions = a!.VectorizedColumns.Count == columnsB.Count;
        for (int i = 0; i < a.VectorizedColumns.Count; i++)
        {
            var name = a.VectorizedColumns[i];
            int paired = columnsB.FindIndex(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (paired < 0 && samePositions)
                paired = i;
            FieldRows.Add(new FieldPairItem(name, columnsB, paired));
        }
    }

    private MatchSettings BuildSettings() => new()
    {
        ScoreByField = FieldsAvailable && ScoreByField,
        OneToOne = OneToOne,
        ApproximateBlocking = ApproximateBlocking,
        Fields = FieldsAvailable
            ? FieldRows.Where(f => f.IsPaired).Select(f => new FieldPairing
            {
                NameA = f.NameA,
                NameB = f.NameB!,
                Weight = (double)(f.Weight ?? 0),
                MustMatch = f.MustMatch,
                Block = f.Block
            }).ToList()
            : new List<FieldPairing>()
    };

    [ObservableProperty] private double _threshold = 0.85;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunMatchJobCommand))]
    private bool _isRunning;

    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isLoading;

    /// <summary>Compare inside SQL Server when it has the VECTOR type; the app does it otherwise.</summary>
    [ObservableProperty] private bool _useDatabaseVectors = true;

    /// <summary>Null until the server has been asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatabaseVectorsNote))]
    private bool? _serverSupportsVectors;

    public string DatabaseVectorsNote => ServerSupportsVectors switch
    {
        true => "This server has the VECTOR type, so SQL Server can score the pairs itself; no vectors are sent to this computer. " +
                "Field-by-field scoring, must-match fields, one match per record and blocking need the app's own matcher, so with any of " +
                "them the app compares; a plain whole-row job compared here has no runner-up or field score columns.",
        false => "This server has no VECTOR type (it needs SQL Server 2025 or Azure SQL), so the app compares the vectors itself.",
        null => "Checking whether this server has the VECTOR type..."
    };

    public ObservableCollection<DatasetInfo> AvailableDatasets { get; } = new();

    public NewMatchJobViewModel(DataService dataService, INavigationService navigation)
    {
        _dataService = dataService;
        _navigation = navigation;
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public string ThresholdDisplay => Threshold.ToString("0.00");
    partial void OnThresholdChanged(double value) => OnPropertyChanged(nameof(ThresholdDisplay));

    public bool CanRun => SelectedDatasetA is not null && SelectedDatasetB is not null && !IsRunning;

    public async Task OnActivatedAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var datasets = await Task.Run(() => _dataService.GetDatasetsAsync());

            AvailableDatasets.Clear();
            foreach (var dataset in datasets)
                AvailableDatasets.Add(dataset);

            if (AvailableDatasets.Count < 2)
                StatusMessage = "Import at least two datasets before running a match job.";

            ServerSupportsVectors = await Task.Run(() => _dataService.SupportsDatabaseVectorsAsync());
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunMatchJobAsync()
    {
        IsRunning = true;
        ErrorMessage = null;
        ProgressValue = 0;
        StatusMessage = "Comparing records...";

        var settings = BuildSettings();
        if (settings.ScoreByField && settings.Fields.All(f => f.Weight <= 0))
        {
            ErrorMessage = "To score field by field, pair at least one field and give it a weight above 0.";
            StatusMessage = string.Empty;
            IsRunning = false;
            return;
        }

        var request = new MatchJobRequest
        {
            DatasetAId = SelectedDatasetA!.Id,
            DatasetBId = SelectedDatasetB!.Id,
            SimilarityThreshold = Threshold,
            UseDatabaseVectors = UseDatabaseVectors,
            Settings = settings
        };

        var progress = new Progress<int>(percent =>
        {
            ProgressValue = percent;
            StatusMessage = percent >= 100 ? "Saving results..." : "Comparing records...";
        });

        try
        {
            var job = await Task.Run(() => _dataService.RunMatchJobAsync(request, progress));

            StatusMessage = $"Found {job.TotalMatchesFound:N0} matches.";
            IsRunning = false;

            _navigation.NavigateTo<MatchResultsViewModel>(vm => vm.MatchJobId = job.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = string.Empty;
            IsRunning = false;
        }
    }

    [RelayCommand] private void GoHome() => _navigation.NavigateTo<HomeViewModel>();
    [RelayCommand] private void GoMatchJobs() => _navigation.NavigateTo<MatchJobsViewModel>();
}
