using Microsoft.EntityFrameworkCore;
using VectorMatchEngine.Data;

namespace VectorMatchEngine.UI;

/// <summary>
/// Builds an AppDbContext from whatever connection string is configured *right now*.
///
/// AddDbContextFactory bakes its options in once at container-build time, which would leave the
/// app pinned to the startup connection string even after the user changes it in Settings.
/// Reading AppSettings on every call is what makes "Save and Apply" take effect without a restart.
/// </summary>
public class AppSettingsDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly AppSettings _settings;

    public AppSettingsDbContextFactory(AppSettings settings) => _settings = settings;

    public AppDbContext CreateDbContext()
    {
        var connectionString = _settings.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "No SQL Server connection string is configured. Open Settings to add one.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(300))
            .Options;

        return new AppDbContext(options);
    }
}
