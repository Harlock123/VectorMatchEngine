using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using VectorMatchEngine.Data;
using VectorMatchEngine.UI.Navigation;

namespace VectorMatchEngine.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly DataService _dataService;
    private readonly INavigationService _navigation;

    [ObservableProperty] private string _connectionString = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isSuccess;
    [ObservableProperty] private bool _isBusy;

    public SettingsViewModel(AppSettings settings, DataService dataService, INavigationService navigation)
    {
        _settings = settings;
        _dataService = dataService;
        _navigation = navigation;
        ConnectionString = settings.ConnectionString;
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            IsSuccess = false;
            StatusMessage = "Enter a connection string first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing connection...";

        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            IsSuccess = true;
            StatusMessage = $"Connection successful. Server: {connection.DataSource}, database: {connection.Database}.";
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            IsSuccess = false;
            StatusMessage = "Enter a connection string first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Saving settings and applying database migrations...";

        try
        {
            // AppSettings is the singleton the DbContext factory reads on every call, so updating
            // it here is what re-points the whole app at the new server.
            _settings.ConnectionString = ConnectionString.Trim();
            _settings.Save();

            await _dataService.MigrateAsync();

            IsSuccess = true;
            StatusMessage = "Settings saved. Database schema is up to date.";
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand] private void GoHome() => _navigation.NavigateTo<HomeViewModel>();
}
