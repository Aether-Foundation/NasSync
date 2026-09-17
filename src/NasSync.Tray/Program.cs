using System.Windows.Forms;
using NasSync.Core;

namespace NasSync.Tray;

/// <summary>
/// Entry point for the NAS Cloud Sync system tray application.
/// Ensures single instance, loads configuration, and launches the tray controller.
/// </summary>
internal static class Program
{
    /// <summary>Named mutex for single-instance enforcement.</summary>
    private const string MUTEX_NAME = "Global\\NasSync-Tray-SingleInstance";

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    internal static void Main()
    {
        // Initialize file logger FIRST — before anything else
        try { AppLogger.Initialize(); }
        catch { /* best effort */ }

        AppLogger.Info("Program", "=== Application starting ===");

        // Enforce single instance
        using var mutex = new Mutex(true, MUTEX_NAME, out bool createdNew);
        if (!createdNew)
        {
            AppLogger.Warn("Program", "Another instance is already running");
            MessageBox.Show(
                "NAS Cloud Sync is already running.\nCheck the system tray.",
                "NAS Cloud Sync",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Load configuration
        AppSettings settings;
        try
        {
            settings = AppSettings.Load();
            AppLogger.Info("Program",
                $"Config loaded: IsFirstRun={settings.IsFirstRun}, " +
                $"ServerDir='{settings.ServerDirectory}', " +
                $"SyncRoot='{settings.SyncRootPath}'");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Program", "Failed to load config", ex);
            settings = new AppSettings { IsFirstRun = true };
        }

        // First-run setup if no configuration exists
        if (settings.IsFirstRun)
        {
            AppLogger.Info("Program", "First run — showing setup wizard");

            try
            {
                if (!FirstRunSetup.Show(settings))
                {
                    AppLogger.Info("Program", "User cancelled setup");
                    return;
                }

                settings.Save();
                AppLogger.Info("Program",
                    $"Setup complete: ServerDir='{settings.ServerDirectory}', SyncRoot='{settings.SyncRootPath}'");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Program", "Setup wizard failed", ex);
                MessageBox.Show(
                    $"Setup failed: {ex.Message}\n\nCheck nassync.log for details.",
                    "NAS Cloud Sync",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        // Validate paths
        if (string.IsNullOrWhiteSpace(settings.ServerDirectory) ||
            string.IsNullOrWhiteSpace(settings.SyncRootPath))
        {
            AppLogger.Error("Program", "Configuration incomplete — paths are empty");
            MessageBox.Show(
                "Configuration is incomplete.\nPlease delete ~/.nassync/config.json and restart.",
                "NAS Cloud Sync",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Ensure directories exist
        try
        {
            Directory.CreateDirectory(settings.ServerDirectory);
            Directory.CreateDirectory(settings.SyncRootPath);
            AppLogger.Info("Program", "Directories verified");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Program", "Failed to create directories", ex);
            MessageBox.Show(
                $"Failed to create directories:\n{ex.Message}\n\nCheck nassync.log for details.",
                "NAS Cloud Sync",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Create adapter and engine
        try
        {
            var adapter = new LocalFolderAdapter(
                settings.ServerDirectory,
                "local-folder",
                "Local Folder NAS");

            var engine = new SyncEngine(settings.ToEngineConfiguration(), adapter);

            AppLogger.Info("Program", "Launching tray controller");

            // Launch tray app
            Application.Run(new TrayAppController(engine, settings));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Program", "Fatal error during startup", ex);
            MessageBox.Show(
                $"NAS Cloud Sync failed to start:\n\n{ex.Message}\n\n" +
                $"Log: {AppLogger.LogPath ?? "unavailable"}",
                "NAS Cloud Sync — Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
