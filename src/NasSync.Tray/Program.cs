using System.Windows.Forms;

namespace NasSync.Tray;

/// <summary>
/// Entry point for the NAS Cloud Sync system tray application.
/// Manages the system tray icon, status display, and quick actions menu.
/// </summary>
internal static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    internal static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // TODO: Initialize sync engine, create tray icon, and run message loop
        MessageBox.Show(
            "NAS Cloud Sync is starting...\n\n" +
            "This is a development build. Full functionality coming soon.",
            "NAS Cloud Sync",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
