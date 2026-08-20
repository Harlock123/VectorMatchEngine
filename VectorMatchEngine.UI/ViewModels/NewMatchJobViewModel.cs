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

    [ObservableProperty] private double _threshold = 0.85;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunMatchJobCommand))]
    private bool _isRunning;

    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isLoading;

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

        var request = new MatchJobRequest
        {
            DatasetAId = SelectedDatasetA!.Id,
            DatasetBId = SelectedDatasetB!.Id,
            SimilarityThreshold = Threshold
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
