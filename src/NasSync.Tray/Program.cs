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
        // Enforce single instance
        using var mutex = new Mutex(true, MUTEX_NAME, out bool createdNew);
        if (!createdNew)
        {
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
        var settings = AppSettings.Load();

        // First-run setup if no configuration exists
        if (settings.IsFirstRun)
        {
            if (!FirstRunSetup.Show(settings))
            {
                return; // User cancelled
            }

            settings.Save();
        }

        // Validate paths
        if (string.IsNullOrWhiteSpace(settings.ServerDirectory) ||
            string.IsNullOrWhiteSpace(settings.SyncRootPath))
        {
            MessageBox.Show(
                "Configuration is incomplete.\nPlease delete ~/.nassync/config.json and restart.",
                "NAS Cloud Sync",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Ensure directories exist
        Directory.CreateDirectory(settings.ServerDirectory);
        Directory.CreateDirectory(settings.SyncRootPath);

        // Create adapter and engine
        var adapter = new LocalFolderAdapter(
            settings.ServerDirectory,
            "local-folder",
            "Local Folder NAS");

        var engine = new SyncEngine(settings.ToEngineConfiguration(), adapter);

        // Launch tray app
        Application.Run(new TrayAppController(engine, settings));
    }
}
