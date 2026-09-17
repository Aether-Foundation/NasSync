using NasSync.Core;

namespace NasSync.Tray;

/// <summary>
/// Manages loading and caching of status-aware tray icons from embedded resources.
/// Returns the appropriate icon variant based on the current <see cref="SyncState"/>.
/// </summary>
internal static class TrayIconManager
{
    private static readonly Dictionary<string, Icon> IconCache = new();

    /// <summary>
    /// Gets the tray icon appropriate for the given sync state.
    /// Falls back to <see cref="SystemIcons.Application"/> if embedded icons are unavailable.
    /// </summary>
    /// <param name="state">The current sync engine state.</param>
    /// <returns>An icon representing the sync state.</returns>
    public static Icon GetIcon(SyncState state)
    {
        string variant = state switch
        {
            SyncState.Idle_Synced => "connected",
            SyncState.Syncing or SyncState.Starting => "syncing",
            SyncState.Paused or SyncState.Offline => "offline",
            SyncState.Error => "error",
            _ => "connected",
        };

        return LoadIcon(variant);
    }

    /// <summary>
    /// Loads an icon from embedded resources, caching it for subsequent calls.
    /// </summary>
    private static Icon LoadIcon(string variant)
    {
        if (IconCache.TryGetValue(variant, out Icon? cached))
        {
            return cached;
        }

        string resourceName = $"NasSync.Tray.resources.icons.nassync-{variant}.ico";
        var stream = typeof(TrayIconManager).Assembly.GetManifestResourceStream(resourceName);

        if (stream is not null)
        {
            var icon = new Icon(stream, 32, 32);
            IconCache[variant] = icon;
            return icon;
        }

        // Fallback: generate a simple colored icon programmatically
        var fallback = GenerateFallbackIcon(variant);
        IconCache[variant] = fallback;
        return fallback;
    }

    /// <summary>
    /// Generates a simple fallback icon with a colored dot when embedded resources are unavailable.
    /// </summary>
    private static Icon GenerateFallbackIcon(string variant)
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Cloud shape (simple circle approximation)
        var cloudColor = variant switch
        {
            "connected" => Color.FromArgb(0, 120, 212),    // Windows blue
            "syncing" => Color.FromArgb(0, 150, 230),      // Lighter blue
            "offline" => Color.FromArgb(160, 160, 160),    // Gray
            "error" => Color.FromArgb(220, 50, 50),        // Red
            _ => Color.FromArgb(0, 120, 212),
        };

        g.Clear(Color.Transparent);
        g.FillEllipse(new SolidBrush(cloudColor), 4, 6, 24, 18);

        // Status dot
        var dotColor = variant switch
        {
            "connected" => Color.FromArgb(0, 200, 80),     // Green
            "syncing" => Color.FromArgb(0, 180, 255),      // Cyan
            "offline" => Color.FromArgb(200, 200, 200),    // Light gray
            "error" => Color.FromArgb(255, 60, 60),        // Bright red
            _ => Color.FromArgb(0, 200, 80),
        };

        g.FillEllipse(new SolidBrush(dotColor), 20, 20, 10, 10);

        IntPtr hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        return icon;
    }
}
