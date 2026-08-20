using System.Text.Json;

namespace VectorMatchEngine.UI;

/// <summary>User settings persisted to %APPDATA%/VectorMatchEngine/settings.json.</summary>
public class AppSettings
{
    public string ConnectionString { get; set; } =
        "Server=.;Database=VectorMatchDb;Trusted_Connection=True;TrustServerCertificate=True;";

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VectorMatchEngine");

    public static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                    return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable settings file must not stop the app from starting.
            System.Diagnostics.Debug.WriteLine($"Could not load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
