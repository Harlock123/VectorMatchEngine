using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VectorMatchEngine.Core.Services;
using VectorMatchEngine.Data;
using VectorMatchEngine.Data.Repositories;
using VectorMatchEngine.UI.Navigation;
using VectorMatchEngine.UI.Services;
using VectorMatchEngine.UI.ViewModels;
using VectorMatchEngine.UI.Views;

namespace VectorMatchEngine.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        // Must run before any binding is evaluated so the MatchResults grid can read
        // its ExpandoObject rows by key.
        BindingPlugins.PropertyAccessors.Insert(0, new ExpandoPropertyAccessorPlugin());

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        RegisterServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };

            // MainViewModel's constructor already navigated Home. Decide from here whether the
            // user can actually use the app, or has to configure a connection string first.
            _ = InitializeDatabaseAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Applies pending migrations at startup when a connection string is configured; otherwise
    /// sends the user straight to Settings with an explanation.
    /// </summary>
    private static async Task InitializeDatabaseAsync()
    {
        var settings = Services.GetRequiredService<AppSettings>();
        var navigation = Services.GetRequiredService<INavigationService>();

        if (!settings.IsConfigured)
        {
            navigation.NavigateTo<SettingsViewModel>(vm =>
                vm.StatusMessage = "Please configure your SQL Server connection string to get started.");
            return;
        }

        try
        {
            await Services.GetRequiredService<DataService>().MigrateAsync();
        }
        catch (Exception ex)
        {
            // Startup must stay silent-but-honest: surface it on the Settings page rather than
            // crashing before a window is even usable.
            navigation.NavigateTo<SettingsViewModel>(vm =>
            {
                vm.IsSuccess = false;
                vm.StatusMessage = $"Could not reach the database: {ex.Message}";
            });
        }
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Settings
        services.AddSingleton<AppSettings>(AppSettings.Load());

        // Resolves the connection string on every CreateDbContext() call, so changing it in
        // Settings takes effect immediately without rebuilding the container.
        services.AddSingleton<IDbContextFactory<AppDbContext>, AppSettingsDbContextFactory>();

        // Core services
        services.AddSingleton<ExcelService>();
        services.AddSingleton<VectorizationService>();
        services.AddSingleton<SimilarityService>();
        services.AddSingleton<ExcelExportService>();

        // Data
        services.AddSingleton<IDatasetRepository, DatasetRepository>();
        services.AddSingleton<IMatchJobRepository, MatchJobRepository>();
        services.AddSingleton<DataService>();

        // Navigation + dialogs
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();

        // ViewModels
        services.AddTransient<HomeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ImportViewModel>();
        services.AddTransient<DatasetsViewModel>();
        services.AddTransient<NewMatchJobViewModel>();
        services.AddTransient<MatchJobsViewModel>();
        services.AddTransient<MatchResultsViewModel>();
        services.AddSingleton<MainViewModel>();
    }
}
