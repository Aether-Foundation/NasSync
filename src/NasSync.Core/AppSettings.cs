using System.Text.Json;
using System.Text.Json.Serialization;

namespace NasSync.Core;

/// <summary>
/// Application settings with JSON persistence at <c>~/.nassync/config.json</c>.
/// Stores user-configurable paths and sync engine parameters.
/// Uses atomic write (write to .tmp then rename) to prevent corruption.
/// </summary>
public sealed class AppSettings
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nassync");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Gets or sets the path to the NAS/server directory (data source).
    /// For LocalFolderAdapter, this is the local folder acting as the NAS.
    /// </summary>
    public string ServerDirectory { get; set; } = "";

    /// <summary>
    /// Gets or sets the path where cloud placeholder files are created (sync root).
    /// This directory appears in File Explorer's navigation pane.
    /// </summary>
    public string SyncRootPath { get; set; } = "";

    /// <summary>
    /// Gets or sets the storage provider identifier for sync root registration.
    /// </summary>
    public string ProviderId { get; set; } = "NasSync";

    /// <summary>
    /// Gets or sets the account identifier for this NAS connection.
    /// </summary>
    public string AccountId { get; set; } = "default";

    /// <summary>
    /// Gets or sets the display name shown in File Explorer's navigation pane.
    /// </summary>
    public string DisplayName { get; set; } = "NAS Cloud Sync";

    /// <summary>
    /// Gets or sets the path to the icon resource for the navigation pane and tray.
    /// </summary>
    public string IconResource { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this is the first run (no config file existed).
    /// Set to false after the first successful save.
    /// Not persisted to JSON.
    /// </summary>
    [JsonIgnore]
    public bool IsFirstRun { get; set; } = true;

    /// <summary>
    /// Loads settings from <c>~/.nassync/config.json</c>.
    /// Returns a new instance with <see cref="IsFirstRun"/> = true if the file doesn't exist.
    /// </summary>
    public static AppSettings Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppSettings { IsFirstRun = true };
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? new AppSettings();
            settings.IsFirstRun = false;
            return settings;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] Failed to load config: {ex.Message}");
            return new AppSettings { IsFirstRun = true };
        }
    }

    /// <summary>
    /// Saves settings to <c>~/.nassync/config.json</c> using atomic write.
    /// Creates the config directory if it doesn't exist.
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);

        IsFirstRun = false;
        string json = JsonSerializer.Serialize(this, JsonOptions);

        // Atomic write: write to temp file, then rename
        string tmpPath = ConfigPath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, ConfigPath, overwrite: true);
    }

    /// <summary>
    /// Converts these settings to a <see cref="SyncEngineConfiguration"/> for engine initialization.
    /// </summary>
    public SyncEngineConfiguration ToEngineConfiguration()
    {
        return new SyncEngineConfiguration(
            EngineId: "nassync-default",
            ProviderId: ProviderId,
            AccountId: AccountId,
            SyncRootPath: SyncRootPath,
            DisplayName: DisplayName,
            IconResource: IconResource);
    }

    /// <summary>
    /// Gets the full path to the configuration file.
    /// </summary>
    public static string GetConfigPath() => ConfigPath;
}
