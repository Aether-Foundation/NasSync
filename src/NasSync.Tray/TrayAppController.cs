using System.Diagnostics;
using NasSync.Core;

namespace NasSync.Tray;

/// <summary>
/// System tray application controller. Manages the <see cref="NotifyIcon"/>,
/// context menu, status icons, and <see cref="SyncEngine"/> lifecycle.
/// </summary>
internal sealed class TrayAppController : ApplicationContext
{
    private readonly SyncEngine _engine;
    private readonly AppSettings _settings;
    private readonly NotifyIcon _trayIcon;
    private readonly NamedPipeServer _pipeServer;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private readonly ToolStripMenuItem _queueItem;
    private DateTime _lastBalloon = DateTime.MinValue;

    /// <summary>
    /// Creates a new tray controller and starts the sync engine.
    /// </summary>
    /// <param name="engine">The sync engine to manage.</param>
    /// <param name="settings">Application settings for configuration display.</param>
    public TrayAppController(SyncEngine engine, AppSettings settings)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pipeServer = new NamedPipeServer(engine);

        // Build context menu
        _statusItem = new ToolStripMenuItem("Status: Starting...") { Enabled = false };
        _queueItem = new ToolStripMenuItem("Queue: 0 pending") { Enabled = false };
        _pauseResumeItem = new ToolStripMenuItem("Pause Sync", null, OnPauseResume);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(_statusItem);
        contextMenu.Items.Add(_queueItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_pauseResumeItem);
        contextMenu.Items.Add(new ToolStripMenuItem("Open Sync Folder", null, OnOpenFolder));
        contextMenu.Items.Add(new ToolStripMenuItem("Full Scan", null, OnFullScan));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Settings...", null, OnOpenSettings));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

        // Create tray icon with status-aware icon
        _trayIcon = new NotifyIcon
        {
            Icon = TrayIconManager.GetIcon(SyncState.Starting),
            Text = "NAS Cloud Sync — Starting...",
            ContextMenuStrip = contextMenu,
            Visible = true,
        };

        _trayIcon.DoubleClick += (_, _) => OnOpenFolder(null, null);

        // Subscribe to engine events
        _engine.StateChanged += OnStateChanged;
        _engine.SyncOperation += OnSyncOperation;

        // Start the engine asynchronously
        _ = StartEngineAsync();
    }

    /// <summary>
    /// Starts the sync engine and updates the tray icon accordingly.
    /// </summary>
    private async Task StartEngineAsync()
    {
        try
        {
            await _engine.StartAsync();
            _pipeServer.Start();
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(5000, "NAS Cloud Sync Error",
                $"Failed to start: {ex.Message}", ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Handles sync state changes — updates icon, status text, and menu items.
    /// </summary>
    private void OnStateChanged(object? sender, SyncStateChangedEventArgs e)
    {
        string statusText = e.NewState switch
        {
            SyncState.Idle => "Status: Idle",
            SyncState.Starting => "Status: Starting...",
            SyncState.Syncing => "Status: Syncing...",
            SyncState.Idle_Synced => "Status: Connected ✓",
            SyncState.Paused => "Status: Paused ⏸",
            SyncState.Offline => "Status: Offline",
            SyncState.Error => $"Status: Error — {e.Message ?? "Unknown"}",
            _ => $"Status: {e.NewState}",
        };

        _statusItem.Text = statusText;
        _trayIcon.Text = $"NAS Cloud Sync — {e.NewState}";
        _trayIcon.Icon = TrayIconManager.GetIcon(e.NewState);

        _pauseResumeItem.Text = e.NewState == SyncState.Paused ? "Resume Sync" : "Pause Sync";
    }

    /// <summary>
    /// Handles sync operation events — shows balloon notifications for completions.
    /// </summary>
    private void OnSyncOperation(object? sender, SyncOperationEventArgs e)
    {
        // Update queue count
        var queue = _engine.GetQueueStatus();
        _queueItem.Text = $"Queue: {queue.PendingCount} pending, {queue.InProgressCount} active";

        // Show balloon for completions/failures (debounced: max 1 per 5 seconds)
        if (e.Status == SyncOperationStatus.Completed || e.Status == SyncOperationStatus.Failed)
        {
            if (DateTime.UtcNow - _lastBalloon < TimeSpan.FromSeconds(5))
                return;

            string fileName = Path.GetFileName(e.LocalPath);
            string action = e.OperationType switch
            {
                SyncOperationType.Upload => "Uploaded",
                SyncOperationType.Download => "Downloaded",
                SyncOperationType.Delete => "Deleted",
                SyncOperationType.Rename => "Renamed",
                SyncOperationType.CreatePlaceholder => "Added",
                SyncOperationType.Dehydrate => "Released",
                _ => "Synced",
            };

            if (e.Status == SyncOperationStatus.Failed)
            {
                _trayIcon.ShowBalloonTip(3000, "Sync Error",
                    $"{action} failed: {fileName}\n{e.Error?.Message}", ToolTipIcon.Warning);
            }
            else
            {
                _trayIcon.ShowBalloonTip(2000, "NAS Cloud Sync",
                    $"{action}: {fileName}", ToolTipIcon.Info);
            }

            _lastBalloon = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Toggles between paused and resumed states.
    /// </summary>
    private void OnPauseResume(object? sender, EventArgs e)
    {
        if (_engine.State == SyncState.Paused)
            _engine.Resume();
        else
            _engine.Pause();
    }

    /// <summary>
    /// Opens the sync root folder in File Explorer.
    /// </summary>
    private void OnOpenFolder(object? sender, EventArgs? e)
    {
        try
        {
            Process.Start("explorer.exe", _engine.SyncRootPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Tray] Failed to open folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Triggers a full sync scan.
    /// </summary>
    private async void OnFullScan(object? sender, EventArgs? e)
    {
        try
        {
            _trayIcon.ShowBalloonTip(2000, "NAS Cloud Sync",
                "Starting full scan...", ToolTipIcon.Info);
            await _engine.FullScanAsync();
            _trayIcon.ShowBalloonTip(2000, "NAS Cloud Sync",
                "Full scan complete.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(5000, "Scan Error",
                ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Launches the WinUI 3 settings window as a separate process.
    /// </summary>
    private void OnOpenSettings(object? sender, EventArgs? e)
    {
        string uiExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NasSync.UI.exe");

        if (!File.Exists(uiExe))
        {
            _trayIcon.ShowBalloonTip(5000, "Settings Unavailable",
                "NasSync.UI.exe not found. Settings window is not installed.",
                ToolTipIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uiExe,
                Arguments = $"--config \"{AppSettings.GetConfigPath()}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Tray] Failed to launch settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the engine and exits the application.
    /// </summary>
    private async void OnExit(object? sender, EventArgs? e)
    {
        _trayIcon.Visible = false;
        _pipeServer.Stop();

        try
        {
            await _engine.StopAsync(unregisterSyncRoot: false);
        }
        catch
        {
            // Best effort on exit
        }

        await _engine.DisposeAsync();
        _pipeServer.Dispose();
        Application.Exit();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
