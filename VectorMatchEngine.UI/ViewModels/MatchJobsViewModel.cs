using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VectorMatchEngine.Data;
using VectorMatchEngine.Data.Entities;
using VectorMatchEngine.UI.Navigation;
using VectorMatchEngine.UI.Services;

namespace VectorMatchEngine.UI.ViewModels;

public partial class MatchJobsViewModel : ObservableObject, IActivatable
{
    private readonly DataService _dataService;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<MatchJob> MatchJobs { get; } = new();

    public MatchJobsViewModel(DataService dataService, INavigationService navigation, IDialogService dialogs)
    {
        _dataService = dataService;
        _navigation = navigation;
        _dialogs = dialogs;
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public bool IsEmpty => !IsLoading && MatchJobs.Count == 0;

    public Task OnActivatedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var jobs = await Task.Run(() => _dataService.GetMatchJobsAsync());

            MatchJobs.Clear();
            foreach (var job in jobs)
                MatchJobs.Add(job);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand] private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void ViewResults(MatchJob? job)
    {
        if (job is null)
            return;

        _navigation.NavigateTo<MatchResultsViewModel>(vm => vm.MatchJobId = job.Id);
    }

    [RelayCommand]
    private async Task DeleteJobAsync(MatchJob? job)
    {
        if (job is null)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Delete match job",
            $"Delete match job #{job.Id} and its {job.TotalMatchesFound:N0} result(s)? This cannot be undone.");

        if (!confirmed)
            return;

        try
        {
            await _dataService.DeleteMatchJobAsync(job.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand] private void GoNewMatchJob() => _navigation.NavigateTo<NewMatchJobViewModel>();
    [RelayCommand] private void GoHome() => _navigation.NavigateTo<HomeViewModel>();
}
