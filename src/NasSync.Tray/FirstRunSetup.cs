using System.Windows.Forms;
using NasSync.Core;

namespace NasSync.Tray;

/// <summary>
/// First-run setup wizard that prompts the user to select their NAS directory
/// and sync root folder using standard <see cref="FolderBrowserDialog"/> dialogs.
/// </summary>
internal static class FirstRunSetup
{
    /// <summary>
    /// Shows the first-run setup dialogs and populates the settings.
    /// </summary>
    /// <param name="settings">The settings object to populate with user selections.</param>
    /// <returns>True if the user completed both steps; false if cancelled.</returns>
    public static bool Show(AppSettings settings)
    {
        MessageBox.Show(
            "Welcome to NAS Cloud Sync!\n\n" +
            "This wizard will help you configure your sync folders.\n" +
            "Click OK to begin.",
            "NAS Cloud Sync Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        // Step 1: Choose NAS directory (server / data source)
        using var dlg1 = new FolderBrowserDialog
        {
            Description = "Step 1: Choose your NAS folder\n\n" +
                          "This is the directory that acts as your NAS storage.\n" +
                          "Files here will be synced to your local cloud folder.",
            SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
        };

        if (dlg1.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        settings.ServerDirectory = dlg1.SelectedPath;

        // Step 2: Choose sync root (where cloud placeholders live)
        string defaultSyncRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "NasSync");

        using var dlg2 = new FolderBrowserDialog
        {
            Description = "Step 2: Choose your sync folder\n\n" +
                          "This is where cloud placeholder files will appear.\n" +
                          "The folder will show in File Explorer's navigation pane.",
            SelectedPath = defaultSyncRoot,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
        };

        if (dlg2.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        settings.SyncRootPath = dlg2.SelectedPath;

        return true;
    }
}
