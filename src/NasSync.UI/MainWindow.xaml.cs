using Microsoft.UI.Xaml;
using NasSync.Core;
using Windows.Storage.Pickers;

namespace NasSync.UI;

/// <summary>
/// Main settings window for NAS Cloud Sync. Displays configuration, sync status,
/// and recent operations. Communicates with the tray process via named pipe.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AppStateClient _stateClient;
    private readonly DispatcherTimer _pollTimer;

    /// <summary>
    /// Creates the main settings window and loads configuration.
    /// </summary>
    public MainWindow()
    {
        this.InitializeComponent();

        _settings = AppSettings.Load();
        _stateClient = new AppStateClient();

        // Populate fields from settings
        ServerDirBox.Text = _settings.ServerDirectory;
        SyncRootBox.Text = _settings.SyncRootPath;

        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = $"Version {version}";

        // Set window size to 600×800
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ResizeWindow(hwnd, 600, 800);

        // Start polling for state updates
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += async (_, _) => await PollStateAsync();
        _pollTimer.Start();

        // Initial state poll
        _ = PollStateAsync();
    }

    /// <summary>
    /// Polls the tray process for current sync state via named pipe.
    /// </summary>
    private async Task PollStateAsync()
    {
        try
        {
            var state = await _stateClient.GetStateAsync();
            if (state is not null)
            {
                StateText.Text = state.SyncState;
                QueueText.Text = $"{state.PendingCount} pending";

                PauseResumeButton.Content = state.SyncState == "Paused" ? "Resume" : "Pause";
            }
        }
        catch
        {
            StateText.Text = "Tray not running";
        }
    }

    /// <summary>
    /// Handles the Browse button for the NAS server directory.
    /// </summary>
    private async void OnBrowseServer(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");

        // Initialize with HWND for proper window parenting
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ServerDirBox.Text = folder.Path;
        }
    }

    /// <summary>
    /// Handles the Browse button for the sync root directory.
    /// </summary>
    private async void OnBrowseSyncRoot(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            SyncRootBox.Text = folder.Path;
        }
    }

    /// <summary>
    /// Saves configuration changes and signals the tray process to restart the engine.
    /// </summary>
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.ServerDirectory = ServerDirBox.Text.Trim();
        _settings.SyncRootPath = SyncRootBox.Text.Trim();
        _settings.Save();

        // Signal tray to restart
        try
        {
            await _stateClient.SendCommandAsync("restart");
        }
        catch
        {
            // Tray may not be running
        }

        // Show confirmation
        SaveButton.Content = "Saved ✓";
        await Task.Delay(2000);
        SaveButton.Content = "Apply & Restart";
    }

    /// <summary>
    /// Sends a pause/resume command to the tray process.
    /// </summary>
    private async void OnPauseResume(object sender, RoutedEventArgs e)
    {
        string command = PauseResumeButton.Content?.ToString() == "Pause" ? "pause" : "resume";

        try
        {
            await _stateClient.SendCommandAsync(command);
        }
        catch
        {
            StateText.Text = "Tray not running";
        }
    }

    /// <summary>
    /// Sends a full scan command to the tray process.
    /// </summary>
    private async void OnFullScan(object sender, RoutedEventArgs e)
    {
        try
        {
            await _stateClient.SendCommandAsync("scan");
            ScanButton.Content = "Scanning...";
            await Task.Delay(3000);
            ScanButton.Content = "Full Scan";
        }
        catch
        {
            StateText.Text = "Tray not running";
        }
    }

    /// <summary>
    /// Resizes the window to the specified dimensions using Win32 SetWindowPos.
    /// </summary>
    private static void ResizeWindow(IntPtr hwnd, int width, int height)
    {
        // Get DPI scale factor
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;

        int scaledWidth = (int)(width * scale);
        int scaledHeight = (int)(height * scale);

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, scaledWidth, scaledHeight, 0x0006); // SWP_NOMOVE | SWP_NOZORDER
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
