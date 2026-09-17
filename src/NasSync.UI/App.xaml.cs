using Microsoft.UI.Xaml;

namespace NasSync.UI;

/// <summary>
/// WinUI 3 application entry point. Creates and activates the main settings window.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// Gets the HWND of the main window for use with Win32 interop (folder pickers, etc.).
    /// </summary>
    public static IntPtr WindowHandle { get; private set; }

    /// <summary>
    /// Initializes a new instance of the App class.
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched. Creates the main window.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();

        // Capture HWND for folder picker initialization
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);

        _window.Activate();
    }
}
