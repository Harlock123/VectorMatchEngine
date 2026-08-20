using System.Collections.ObjectModel;
using System.Dynamic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VectorMatchEngine.Core.Models;
using VectorMatchEngine.Data;
using VectorMatchEngine.UI.Navigation;
using VectorMatchEngine.UI.Services;

namespace VectorMatchEngine.UI.ViewModels;

/// <summary>
/// Side-by-side match viewer. The grid's column set is not known until the job is loaded,
/// so rows are ExpandoObjects and the view builds DataGridColumns from <see cref="GridColumns"/>.
/// </summary>
public partial class MatchResultsViewModel : ObservableObject, IActivatable
{
    private readonly DataService _dataService;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private int _matchJobId;
    [ObservableProperty] private string _headerText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isExporting;

    [ObservableProperty] private ObservableCollection<ExpandoObject> _gridRows = new();

    public MatchResultsViewModel(DataService dataService, INavigationService navigation, IDialogService dialogs)
    {
        _dataService = dataService;
        _navigation = navigation;
        _dialogs = dialogs;
    }

    /// <summary>Preserved column names for Dataset A (display names, as imported).</summary>
    public List<string> ColumnsA { get; private set; } = new();

    /// <summary>Preserved column names for Dataset B (display names, as imported).</summary>
    public List<string> ColumnsB { get; private set; } = new();

    /// <summary>The raw pairs behind the grid.</summary>
    public List<MatchedPair> MatchedPairs { get; private set; } = new();

    /// <summary>
    /// Binding key + header text for every grid column, in display order.
    ///
    /// Keys are synthetic ("A0", "B3") rather than the Excel header text for two reasons:
    /// the two datasets frequently share column names (both have "FNAME"), which would collide
    /// in a single ExpandoObject; and header text like "First Name" is not a parseable binding path.
    /// </summary>
    public List<GridColumnDescriptor> GridColumns { get; private set; } = new();

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    public bool IsEmpty => !IsLoading && GridRows.Count == 0;

    public bool CanExport => !IsLoading && !IsExporting && MatchedPairs.Count > 0;
    partial void OnIsExportingChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();

    public async Task OnActivatedAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var jobs = await Task.Run(() => _dataService.GetMatchJobsAsync());
            var job = jobs.FirstOrDefault(j => j.Id == MatchJobId);

            if (job is null)
            {
                ErrorMessage = $"Match job #{MatchJobId} could not be found.";
                return;
            }

            ColumnsA = DataService.DeserializeColumns(job.DatasetA?.PreservedColumnsJson);
            ColumnsB = DataService.DeserializeColumns(job.DatasetB?.PreservedColumnsJson);

            MatchedPairs = await Task.Run(() => _dataService.GetMatchResultsAsync(MatchJobId));

            HeaderText =
                $"Job #{job.Id} - {job.DatasetAName} vs {job.DatasetBName} - " +
                $"Threshold {job.Threshold:0.00} - {MatchedPairs.Count:N0} matches";

            GridColumns = BuildColumnDescriptors();

            // Build the rows off the UI thread, then hand over a finished collection: assigning
            // GridRows is also the signal the view listens for to rebuild its columns.
            var rows = await Task.Run(() => BuildRows(MatchedPairs, GridColumns));
            GridRows = new ObservableCollection<ExpandoObject>(rows);

            if (MatchedPairs.Count == 0)
                StatusMessage = "No record pairs met the similarity threshold for this job.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    private List<GridColumnDescriptor> BuildColumnDescriptors()
    {
        var columns = new List<GridColumnDescriptor>(ColumnsA.Count + ColumnsB.Count + 1)
        {
            new("Similarity", "Similarity", isSimilarity: true)
        };

        for (int i = 0; i < ColumnsA.Count; i++)
            columns.Add(new GridColumnDescriptor($"A{i}", $"A: {ColumnsA[i]}"));

        for (int i = 0; i < ColumnsB.Count; i++)
            columns.Add(new GridColumnDescriptor($"B{i}", $"B: {ColumnsB[i]}"));

        return columns;
    }

    private List<ExpandoObject> BuildRows(List<MatchedPair> pairs, List<GridColumnDescriptor> columns)
    {
        var rows = new List<ExpandoObject>(pairs.Count);

        foreach (var pair in pairs)
        {
            var row = new ExpandoObject();
            var values = (IDictionary<string, object?>)row;

            values["Similarity"] = pair.SimilarityScore;

            for (int i = 0; i < ColumnsA.Count; i++)
                values[$"A{i}"] = pair.DatasetAPreserved.GetValueOrDefault(ColumnsA[i], string.Empty);

            for (int i = 0; i < ColumnsB.Count; i++)
                values[$"B{i}"] = pair.DatasetBPreserved.GetValueOrDefault(ColumnsB[i], string.Empty);

            rows.Add(row);
        }

        return rows;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        IsExporting = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var suggested = $"MatchResults_Job{MatchJobId}.xlsx";
            var path = await _dialogs.SaveExcelFileAsync(suggested);

            if (string.IsNullOrWhiteSpace(path))
                return;

            await Task.Run(() => _dataService.ExportMatchResultsAsync(MatchJobId, path));
            StatusMessage = $"Exported {MatchedPairs.Count:N0} matches to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand] private void GoBack() => _navigation.NavigateTo<MatchJobsViewModel>();
    [RelayCommand] private void GoHome() => _navigation.NavigateTo<HomeViewModel>();
}

/// <summary>Binding key and header text for one dynamically generated grid column.</summary>
public class GridColumnDescriptor
{
    public GridColumnDescriptor(string key, string header, bool isSimilarity = false)
    {
        Key = key;
        Header = header;
        IsSimilarity = isSimilarity;
    }

    public string Key { get; }
    public string Header { get; }
    public bool IsSimilarity { get; }
}
