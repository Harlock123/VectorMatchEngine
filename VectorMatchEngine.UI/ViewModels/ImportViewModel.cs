using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VectorMatchEngine.Data;
using VectorMatchEngine.UI.Navigation;
using VectorMatchEngine.UI.Services;

namespace VectorMatchEngine.UI.ViewModels;

/// <summary>Three-step import wizard: pick file, configure columns, process.</summary>
public partial class ImportViewModel : ObservableObject
{
    private readonly DataService _dataService;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3))]
    private int _currentStep = 1;

    // ── Step 1 ────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty] private bool _isLoadingColumns;

    // ── Step 2 ────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    private string _datasetName = string.Empty;

    public ObservableCollection<ColumnSelectionItem> ColumnSelections { get; } = new();

    // ── Step 3 ────────────────────────────────────────────────────────────
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressMessage = string.Empty;
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private string _successSummary = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isProcessing;

    public ImportViewModel(DataService dataService, INavigationService navigation, IDialogService dialogs)
    {
        _dataService = dataService;
        _navigation = navigation;
        _dialogs = dialogs;
    }

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    public bool CanGoNext => !string.IsNullOrWhiteSpace(SelectedFilePath) && !IsLoadingColumns;

    /// <summary>At least one column vectorized and a non-blank dataset name.</summary>
    public bool CanProcess =>
        !string.IsNullOrWhiteSpace(DatasetName)
        && ColumnSelections.Any(column => column.IsVectorized)
        && !IsProcessing;

    // ── Step 1 commands ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        try
        {
            var path = await _dialogs.OpenExcelFileAsync();
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedFilePath = path;
                ErrorMessage = string.Empty;

                // Offer the file name as a sensible default dataset name.
                if (string.IsNullOrWhiteSpace(DatasetName))
                    DatasetName = Path.GetFileNameWithoutExtension(path);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open the file picker: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        IsLoadingColumns = true;
        ErrorMessage = string.Empty;
        NextCommand.NotifyCanExecuteChanged();

        try
        {
            var columns = await _dataService.GetColumnNamesAsync(SelectedFilePath);

            if (columns.Count == 0)
            {
                ErrorMessage = "No column headers were found in the first worksheet.";
                return;
            }

            ColumnSelections.Clear();
            foreach (var column in columns)
            {
                // Preserve everything by default; the user opts columns into vectorization.
                var item = new ColumnSelectionItem(column, isVectorized: false, isPreserved: true);
                item.PropertyChanged += OnColumnSelectionChanged;
                ColumnSelections.Add(item);
            }

            CurrentStep = 2;
            ProcessCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingColumns = false;
            NextCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnColumnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ColumnSelectionItem.IsVectorized))
        {
            OnPropertyChanged(nameof(CanProcess));
            ProcessCommand.NotifyCanExecuteChanged();
        }
    }

    // ── Step 2 commands ───────────────────────────────────────────────────

    [RelayCommand]
    private void GoBack()
    {
        CurrentStep = 1;
        ErrorMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task ProcessAsync()
    {
        var vectorized = ColumnSelections.Where(c => c.IsVectorized).Select(c => c.Name).ToList();
        var preserved = ColumnSelections.Where(c => c.IsPreserved).Select(c => c.Name).ToList();
        var filePath = SelectedFilePath;
        var name = DatasetName;

        CurrentStep = 3;
        IsProcessing = true;
        IsDone = false;
        ErrorMessage = string.Empty;
        SuccessSummary = string.Empty;
        ProgressValue = 0;
        ProgressMessage = "Starting...";
        ProcessCommand.NotifyCanExecuteChanged();

        // Progress<T> captures the UI SynchronizationContext here, so callbacks marshal back
        // to the UI thread automatically.
        var progress = new Progress<(int current, int total, string message)>(update =>
        {
            ProgressMessage = update.message;
            ProgressValue = update.total > 0 ? update.current * 100.0 / update.total : 0;
        });

        try
        {
            var dataset = await Task.Run(() => _dataService.IngestExcelAsync(
                filePath, name, vectorized, preserved, progress));

            ProgressValue = 100;
            ProgressMessage = "Complete.";
            SuccessSummary =
                $"{dataset.RowCount:N0} records ingested. Vector dimensions: {dataset.VectorDimensions}.";
            IsDone = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsProcessing = false;
            ProcessCommand.NotifyCanExecuteChanged();
        }
    }

    // ── Step 3 commands ───────────────────────────────────────────────────

    [RelayCommand] private void GoToDatasets() => _navigation.NavigateTo<DatasetsViewModel>();

    [RelayCommand]
    private void ImportAnother()
    {
        foreach (var item in ColumnSelections)
            item.PropertyChanged -= OnColumnSelectionChanged;

        ColumnSelections.Clear();
        SelectedFilePath = string.Empty;
        DatasetName = string.Empty;
        ProgressValue = 0;
        ProgressMessage = string.Empty;
        SuccessSummary = string.Empty;
        ErrorMessage = string.Empty;
        IsDone = false;
        IsProcessing = false;
        CurrentStep = 1;
    }

    [RelayCommand] private void GoHome() => _navigation.NavigateTo<HomeViewModel>();
}
