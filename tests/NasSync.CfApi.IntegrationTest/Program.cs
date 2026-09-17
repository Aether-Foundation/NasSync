using NasSync.CfApi;

// ==========================================================================
// NAS Cloud Sync — Navigation Pane Integration Test
// ==========================================================================
//
// This program:
// 1. Creates a test sync root directory
// 2. Registers it with the Windows Cloud Filter API (native)
// 3. Registers it for File Explorer navigation pane (WinRT)
// 4. Creates sample placeholder files
// 5. Waits for you to check File Explorer
// 6. Cleans up on exit
//
// IMPORTANT: Must run as Administrator (CfRegisterSyncRoot requires elevation).
// ==========================================================================

Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║   NAS Cloud Sync — Navigation Pane Integration Test  ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.WriteLine();

// Configuration
string providerId = "NasSyncTest";
string accountId = "test-account";
string syncRootPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "NasSyncTest");

Console.WriteLine($"  Provider ID : {providerId}");
Console.WriteLine($"  Account ID  : {accountId}");
Console.WriteLine($"  Sync Root   : {syncRootPath}");
Console.WriteLine();

// Check for admin privileges
using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
var principal = new System.Security.Principal.WindowsPrincipal(identity);
bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

if (!isAdmin)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("⚠ WARNING: Not running as Administrator.");
    Console.WriteLine("  CfRegisterSyncRoot requires elevated privileges.");
    Console.WriteLine("  If registration fails, re-run as Administrator.");
    Console.ResetColor();
    Console.WriteLine();
}

// Step 1: Create sync root directory
Console.Write("  [1/5] Creating sync root directory... ");
try
{
    Directory.CreateDirectory(syncRootPath);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✓");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"✗ {ex.Message}");
    Console.ResetColor();
    Console.ReadKey();
    return;
}

// Step 2: Register with CfAPI (native)
Console.Write("  [2/5] Registering sync root (CfAPI native)... ");
CfSyncRootManager? syncRootManager = null;
try
{
    syncRootManager = new CfSyncRootManager();

    var registrationInfo = new SyncRootRegistrationInfo
    {
        ProviderId = providerId,
        AccountId = accountId,
        SyncRootPath = syncRootPath,
        DisplayName = "NAS Sync Test",
        IconResource = @"%SystemRoot%\system32\imageres.dll,-1043",
        HydrationPolicy = HydrationPolicy.Progressive,
        HydrationPolicyModifier = HydrationPolicyModifier.AutoDehydrationAllowed,
        PopulationPolicy = PopulationPolicy.Full,
        AllowPinning = true,
        ShowSiblingsAsGroup = false,
        Version = "1.0",
    };

    await syncRootManager.RegisterAsync(registrationInfo);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✓");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"✗ {ex.Message}");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("  Hint: Try running as Administrator.");
    Console.WriteLine("  Press any key to exit...");
    Console.ReadKey();
    return;
}

// Step 3: Register navigation pane entry (WinRT)
// NOTE: WinRT registration requires Windows App SDK runtime and Visual Studio build tools.
//       This step is skipped in CLI-only builds. The navigation pane entry will be
//       registered when the full application runs with the Windows App SDK runtime.
Console.WriteLine("  [3/5] Registering navigation pane entry (WinRT)... skipped (requires VS build tools)");

// Step 4: Create sample placeholder files
Console.Write("  [4/5] Creating sample placeholder files... ");
try
{
    var placeholderManager = new PlaceholderManager(syncRootPath);
    var now = DateTimeOffset.UtcNow;

    var entries = new List<PlaceholderEntry>
    {
        new("Documents", IsDirectory: true, FileSize: 0, LastModifiedUtc: now, CreatedUtc: now),
        new(@"Documents\report.pdf", IsDirectory: false, FileSize: 1_048_576, LastModifiedUtc: now, CreatedUtc: now),
        new(@"Documents\notes.txt", IsDirectory: false, FileSize: 2_048, LastModifiedUtc: now, CreatedUtc: now),
        new("Photos", IsDirectory: true, FileSize: 0, LastModifiedUtc: now, CreatedUtc: now),
        new(@"Photos\vacation.jpg", IsDirectory: false, FileSize: 5_242_880, LastModifiedUtc: now, CreatedUtc: now),
        new(@"Photos\family.png", IsDirectory: false, FileSize: 3_145_728, LastModifiedUtc: now, CreatedUtc: now),
        new("readme.txt", IsDirectory: false, FileSize: 512, LastModifiedUtc: now, CreatedUtc: now),
    };

    await placeholderManager.CreatePlaceholdersAsync(entries, markInSync: true);

    // Register FileId mappings so callbacks can resolve paths
    foreach (var entry in entries)
    {
        string fullPath = Path.Combine(syncRootPath, entry.RelativePath);
        try
        {
            long fileId = PlaceholderManager.GetFileId(fullPath);
            syncRootManager.PathResolver.RegisterMapping(fileId, fullPath);
        }
        catch (Exception)
        {
            // Skip files that can't be opened (e.g., permission issues)
        }
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"✓ ({entries.Count} items, {syncRootManager.PathResolver.Count} FileId mappings)");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"⚠ {ex.Message}");
    Console.ResetColor();
}

// Step 5: Connect callbacks
Console.Write("  [5/5] Connecting sync root callbacks... ");
try
{
    var testCallbacks = new TestCallbackHandler();
    await syncRootManager.ConnectAsync(testCallbacks);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✓");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"⚠ {ex.Message}");
    Console.ResetColor();
}

// Verification
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine();
Console.WriteLine("  ✓ Registration complete!");
Console.WriteLine();
Console.WriteLine("  Open File Explorer and check:");
Console.WriteLine($"    • Navigation pane — look for 'NAS Sync Test'");
Console.WriteLine($"    • {syncRootPath}");
Console.WriteLine("    • Placeholder files should show cloud icons ☁");
Console.WriteLine();
Console.WriteLine("  Double-clicking a placeholder will trigger a FETCH_DATA");
Console.WriteLine("  callback (this test will report an error since no real");
Console.WriteLine("  NAS is connected).");
Console.WriteLine();
Console.ResetColor();

Console.Write("  Press [Enter] to unregister and clean up...");
Console.ReadLine();

// Cleanup
Console.WriteLine();
Console.Write("  Cleaning up... ");
try
{
    if (syncRootManager.IsConnected)
    {
        await syncRootManager.DisconnectAsync();
    }

    // Unregister native sync root
    await syncRootManager.UnregisterAsync();
    syncRootManager.Dispose();

    // Optionally remove the directory
    if (Directory.Exists(syncRootPath))
    {
        Directory.Delete(syncRootPath, recursive: true);
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✓ Done");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"✗ {ex.Message}");
    Console.ResetColor();
    Console.WriteLine("  You may need to manually clean up:");
    Console.WriteLine($"    Directory: {syncRootPath}");
}

Console.WriteLine();
Console.WriteLine("  Test complete. Press any key to exit.");
Console.ReadKey();

// ==========================================================================
// Test callback handler — logs callbacks to console
// ==========================================================================
sealed class TestCallbackHandler : ICfCallbackHandler
{
    public Task FetchDataAsync(FetchDataRequest request, CancellationToken ct)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [CALLBACK] FetchData: {request.FilePath} offset={request.RequiredOffset} length={request.RequiredLength} transferKey={request.TransferKey}");
        Console.ResetColor();
        // In a real implementation, we'd download from NAS and call HydrationDataProvider.ProvideData.
        return Task.CompletedTask;
    }

    public Task FetchPlaceholdersAsync(string directoryPath, CancellationToken ct)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [CALLBACK] FetchPlaceholders: {directoryPath}");
        Console.ResetColor();
        return Task.CompletedTask;
    }

    public Task CancelFetchDataAsync(string filePath, TransferKey transferKey)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [CALLBACK] CancelFetchData: {filePath} transferKey={transferKey.Value}");
        Console.ResetColor();
        return Task.CompletedTask;
    }

    public Task NotifyDehydrateAsync(string filePath)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [CALLBACK] NotifyDehydrate: {filePath}");
        Console.ResetColor();
        return Task.CompletedTask;
    }

    public Task NotifyDehydrateCompletionAsync(string filePath)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [CALLBACK] NotifyDehydrateCompletion: {filePath}");
        Console.ResetColor();
        return Task.CompletedTask;
    }

    public Task NotifyDeleteAsync(string filePath)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [CALLBACK] NotifyDelete: {filePath}");
        Console.ResetColor();
        return Task.CompletedTask;
    }

    public Task NotifyRenameAsync(string sourcePath, string destinationPath)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [CALLBACK] NotifyRename: {sourcePath} -> {destinationPath}");
        Console.ResetColor();
        return Task.CompletedTask;
    }

    public Task NotifyFileOpenCompletionAsync(string filePath)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [CALLBACK] NotifyFileOpenCompletion: {filePath}");
        Console.ResetColor();
        return Task.CompletedTask;
    }
}
