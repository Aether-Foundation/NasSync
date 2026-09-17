using NasSync.Core;

namespace NasSync.Tray;

/// <summary>
/// System tray application controller. Manages the <see cref="NotifyIcon"/>,
/// context menu, and <see cref="SyncEngine"/> lifecycle.
/// Extends <see cref="ApplicationContext"/> for WinForms message loop integration.
/// </summary>
internal sealed class TrayAppController : ApplicationContext
{
    private readonly SyncEngine _engine;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseResumeItem;

    /// <summary>
    /// Creates a new tray controller and starts the sync engine.
    /// </summary>
    /// <param name="engine">The sync engine to manage.</param>
    public TrayAppController(SyncEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        // Build context menu
        _statusItem = new ToolStripMenuItem("Status: Starting...") { Enabled = false };
        _pauseResumeItem = new ToolStripMenuItem("Pause Sync", null, OnPauseResume);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(_statusItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_pauseResumeItem);
        contextMenu.Items.Add(new ToolStripMenuItem("Open Sync Folder", null, OnOpenFolder));
        contextMenu.Items.Add(new ToolStripMenuItem("Full Scan", null, OnFullScan));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

        // Create tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // Placeholder icon
            Text = "NAS Cloud Sync",
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
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(5000, "NAS Cloud Sync Error",
                $"Failed to start: {ex.Message}", ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Handles sync state changes and updates the tray status display.
    /// </summary>
    private void OnStateChanged(object? sender, SyncStateChangedEventArgs e)
    {
        string statusText = e.NewState switch
        {
            SyncState.Idle => "Status: Idle",
            SyncState.Starting => "Status: Starting...",
            SyncState.Syncing => "Status: Syncing...",
            SyncState.Idle_Synced => "Status: Connected ✓",
            SyncState.Paused => "Status: Paused",
            SyncState.Offline => "Status: Offline",
            SyncState.Error => $"Status: Error — {e.Message ?? "Unknown"}",
            _ => $"Status: {e.NewState}",
        };

        _statusItem.Text = statusText;
        _trayIcon.Text = $"NAS Cloud Sync — {e.NewState}";

        // Update pause/resume menu text
        _pauseResumeItem.Text = e.NewState == SyncState.Paused ? "Resume Sync" : "Pause Sync";
    }

    /// <summary>
    /// Handles sync operation events (download/upload progress).
    /// </summary>
    private void OnSyncOperation(object? sender, SyncOperationEventArgs e)
    {
        if (e.Status == SyncOperationStatus.Completed)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Tray] Sync {e.OperationType} completed: {e.LocalPath}");
        }
    }

    /// <summary>
    /// Toggles between paused and resumed states.
    /// </summary>
    private void OnPauseResume(object? sender, EventArgs e)
    {
        if (_engine.State == SyncState.Paused)
        {
            _engine.Resume();
        }
        else
        {
            _engine.Pause();
        }
    }

    /// <summary>
    /// Opens the sync root folder in File Explorer.
    /// </summary>
    private void OnOpenFolder(object? sender, EventArgs? e)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", _engine.SyncRootPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Tray] Failed to open folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Triggers a full sync scan.
    /// </summary>
    private async void OnFullScan(object? sender, EventArgs? e)
    {
        try
        {
            await _engine.FullScanAsync();
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(5000, "Scan Error",
                ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Stops the engine and exits the application.
    /// </summary>
    private async void OnExit(object? sender, EventArgs? e)
    {
        _trayIcon.Visible = false;

        try
        {
            await _engine.StopAsync(unregisterSyncRoot: false);
        }
        catch
        {
            // Best effort on exit
        }

        await _engine.DisposeAsync();
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
