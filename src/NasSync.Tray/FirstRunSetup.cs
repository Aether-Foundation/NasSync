using System.Drawing;
using System.Windows.Forms;
using NasSync.Core;

namespace NasSync.Tray;

/// <summary>
/// First-run setup wizard with a modern flat-design form.
/// Allows the user to configure NAS and sync directories via text input + browse buttons.
/// </summary>
internal sealed class SetupForm : Form
{
    private readonly TextBox _serverDirBox;
    private readonly TextBox _syncRootBox;
    private readonly Label _statusLabel;
    private bool _completed;

    public string ServerDirectory => _serverDirBox.Text.Trim();
    public string SyncRootPath => _syncRootBox.Text.Trim();
    public bool Completed => _completed;

    public SetupForm()
    {
        // Form styling
        Text = "NAS Cloud Sync — Setup";
        Size = new Size(520, 360);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(243, 243, 243);
        Font = new Font("Segoe UI", 9.5f);

        int y = 24;
        int labelX = 24;
        int inputX = 24;
        int inputWidth = 370;
        int browseX = 404;
        int browseWidth = 76;

        // Title
        var titleLabel = new Label
        {
            Text = "Welcome to NAS Cloud Sync",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 212),
            Location = new Point(labelX, y),
            AutoSize = true,
        };
        Controls.Add(titleLabel);
        y += 40;

        // Subtitle
        var subtitleLabel = new Label
        {
            Text = "Configure your sync folders to get started.",
            ForeColor = Color.FromArgb(100, 100, 100),
            Location = new Point(labelX, y),
            AutoSize = true,
        };
        Controls.Add(subtitleLabel);
        y += 36;

        // Server directory
        var serverLabel = new Label
        {
            Text = "NAS Directory (your files source)",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(labelX, y),
            AutoSize = true,
        };
        Controls.Add(serverLabel);
        y += 24;

        _serverDirBox = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 28),
            Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "NasSyncServer"),
        };
        Controls.Add(_serverDirBox);

        var browseServerBtn = new Button
        {
            Text = "Browse",
            Location = new Point(browseX, y),
            Size = new Size(browseWidth, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
        };
        browseServerBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        browseServerBtn.Click += (_, _) => BrowseFolder(_serverDirBox);
        Controls.Add(browseServerBtn);
        y += 44;

        // Sync root directory
        var syncLabel = new Label
        {
            Text = "Sync Folder (cloud placeholders, shows in Explorer)",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(labelX, y),
            AutoSize = true,
        };
        Controls.Add(syncLabel);
        y += 24;

        _syncRootBox = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 28),
            Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "NasSync"),
        };
        Controls.Add(_syncRootBox);

        var browseSyncBtn = new Button
        {
            Text = "Browse",
            Location = new Point(browseX, y),
            Size = new Size(browseWidth, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
        };
        browseSyncBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        browseSyncBtn.Click += (_, _) => BrowseFolder(_syncRootBox);
        Controls.Add(browseSyncBtn);
        y += 44;

        // Status label
        _statusLabel = new Label
        {
            Text = "",
            ForeColor = Color.Red,
            Location = new Point(labelX, y),
            Size = new Size(460, 20),
        };
        Controls.Add(_statusLabel);
        y += 28;

        // Buttons
        var confirmBtn = new Button
        {
            Text = "Start Sync",
            Location = new Point(browseX - 100, y),
            Size = new Size(176, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 212),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        confirmBtn.FlatAppearance.BorderSize = 0;
        confirmBtn.Click += OnConfirm;
        Controls.Add(confirmBtn);

        var cancelBtn = new Button
        {
            Text = "Cancel",
            Location = new Point(labelX, y),
            Size = new Size(80, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
        };
        cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        cancelBtn.Click += (_, _) => { _completed = false; Close(); };
        Controls.Add(cancelBtn);
    }

    private void BrowseFolder(TextBox target)
    {
        using var dlg = new FolderBrowserDialog
        {
            ShowNewFolderButton = true,
            SelectedPath = target.Text,
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dlg.SelectedPath;
        }
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerDirectory))
        {
            _statusLabel.Text = "NAS directory cannot be empty.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SyncRootPath))
        {
            _statusLabel.Text = "Sync folder cannot be empty.";
            return;
        }

        if (ServerDirectory == SyncRootPath)
        {
            _statusLabel.Text = "NAS directory and sync folder must be different.";
            return;
        }

        _completed = true;
        Close();
    }
}

/// <summary>
/// Entry point for first-run setup.
/// </summary>
internal static class FirstRunSetup
{
    /// <summary>
    /// Shows the first-run setup form and populates the settings.
    /// </summary>
    /// <param name="settings">The settings object to populate with user selections.</param>
    /// <returns>True if the user completed the setup; false if cancelled.</returns>
    public static bool Show(AppSettings settings)
    {
        using var form = new SetupForm();
        form.ShowDialog();

        if (!form.Completed)
        {
            return false;
        }

        settings.ServerDirectory = form.ServerDirectory;
        settings.SyncRootPath = form.SyncRootPath;

        return true;
    }
}
