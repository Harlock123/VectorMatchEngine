using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VectorMatchEngine.Core.Models;
using VectorMatchEngine.Data;
using VectorMatchEngine.UI.Navigation;
using VectorMatchEngine.UI.Services;

namespace VectorMatchEngine.UI.ViewModels;

public partial class DatasetsViewModel : ObservableObject, IActivatable
{
    private readonly DataService _dataService;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<DatasetInfo> Datasets { get; } = new();

    public DatasetsViewModel(DataService dataService, INavigationService navigation, IDialogService dialogs)
    {
        _dataService = dataService;
        _navigation = navigation;
        _dialogs = dialogs;
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public bool IsEmpty => !IsLoading && Datasets.Count == 0;

    public Task OnActivatedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Query off the UI thread, then swap the collection contents on it.
            var datasets = await Task.Run(() => _dataService.GetDatasetsAsync());

            Datasets.Clear();
            foreach (var dataset in datasets)
                Datasets.Add(dataset);
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
    private async Task DeleteDatasetAsync(DatasetInfo? dataset)
    {
        if (dataset is null)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Delete dataset",
            $"Delete '{dataset.Name}' and all {dataset.RowCount:N0} of its records? This cannot be undone.");

        if (!confirmed)
            return;

        try
        {
            await _dataService.DeleteDatasetAsync(dataset.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand] private void GoImport() => _navigation.NavigateTo<ImportViewModel>();
    [RelayCommand] private void GoHome() => _navigation.NavigateTo<HomeViewModel>();
}
