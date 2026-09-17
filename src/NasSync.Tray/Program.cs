using System.Windows.Forms;
using NasSync.Core;

namespace NasSync.Tray;

/// <summary>
/// Entry point for the NAS Cloud Sync system tray application.
/// Ensures single instance, configures paths, and launches the tray controller.
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

        // Configure paths
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string syncRootPath = Path.Combine(userProfile, "NasSync");
        string serverDirectory = Path.Combine(userProfile, "NasSyncServer");

        // Ensure server directory exists with sample files
        EnsureServerDirectory(serverDirectory);

        // Create adapter and engine
        var adapter = new LocalFolderAdapter(serverDirectory, "local-test", "Local Test NAS");

        var config = new SyncEngineConfiguration(
            EngineId: "nassync-default",
            ProviderId: "NasSync",
            AccountId: "default",
            SyncRootPath: syncRootPath,
            DisplayName: "NAS Cloud Sync",
            IconResource: @"%SystemRoot%\system32\imageres.dll,-1043");

        var engine = new SyncEngine(config, adapter);

        // Launch tray app
        Application.Run(new TrayAppController(engine));
    }

    /// <summary>
    /// Creates the server directory with sample files if it doesn't exist.
    /// </summary>
    private static void EnsureServerDirectory(string serverDirectory)
    {
        if (Directory.Exists(serverDirectory))
        {
            return;
        }

        Directory.CreateDirectory(serverDirectory);
        Directory.CreateDirectory(Path.Combine(serverDirectory, "Documents"));
        Directory.CreateDirectory(Path.Combine(serverDirectory, "Photos"));

        // Create sample files
        File.WriteAllText(
            Path.Combine(serverDirectory, "readme.txt"),
            "Welcome to NAS Cloud Sync!\n\nThis file is stored on the 'NAS' (server directory)\nand synced to your local sync root as a cloud placeholder.\n\nDouble-click the cloud icon to download and view this file.");

        File.WriteAllText(
            Path.Combine(serverDirectory, "Documents", "notes.txt"),
            "Meeting Notes — 2026-09-17\n\n1. Set up CI/CD pipeline ✓\n2. Implement CfAPI wrapper ✓\n3. Build sync engine ✓\n4. Create tray app ✓\n5. Test navigation pane integration");

        File.WriteAllText(
            Path.Combine(serverDirectory, "Documents", "report.pdf"),
            "%PDF-1.4 (sample placeholder — not a real PDF)");

        // Create a small binary file to simulate a photo
        byte[] sampleImage = new byte[1024];
        new Random(42).NextBytes(sampleImage);
        File.WriteAllBytes(Path.Combine(serverDirectory, "Photos", "vacation.jpg"), sampleImage);
    }
}
