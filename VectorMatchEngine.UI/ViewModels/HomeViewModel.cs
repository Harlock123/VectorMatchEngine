using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VectorMatchEngine.Data;
using VectorMatchEngine.UI.Navigation;

namespace VectorMatchEngine.UI.ViewModels;

public partial class HomeViewModel : ObservableObject, IActivatable
{
    private readonly DataService _dataService;
    private readonly INavigationService _navigation;

    [ObservableProperty] private int _datasetCount;
    [ObservableProperty] private int _matchJobCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public HomeViewModel(DataService dataService, INavigationService navigation)
    {
        _dataService = dataService;
        _navigation = navigation;
    }

    public async Task OnActivatedAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var datasets = await _dataService.GetDatasetsAsync();
            var jobs = await _dataService.GetMatchJobsAsync();

            DatasetCount = datasets.Count;
            MatchJobCount = jobs.Count;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load counts: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand] private void GoImport() => _navigation.NavigateTo<ImportViewModel>();
    [RelayCommand] private void GoDatasets() => _navigation.NavigateTo<DatasetsViewModel>();
    [RelayCommand] private void GoNewMatchJob() => _navigation.NavigateTo<NewMatchJobViewModel>();
    [RelayCommand] private void GoMatchJobs() => _navigation.NavigateTo<MatchJobsViewModel>();
    [RelayCommand] private void GoSettings() => _navigation.NavigateTo<SettingsViewModel>();
}
