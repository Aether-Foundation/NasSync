using NasSync.Adapters;
using NasSync.CfApi;
using NasSync.CfApi.Interop;

namespace NasSync.Core;

/// <summary>
/// The main sync engine orchestrator. Implements both <see cref="ISyncEngine"/>
/// (public API for lifecycle management) and <see cref="ICfCallbackHandler"/>
/// (native callback handling from the Cloud Filter platform).
///
/// <para>
/// On <see cref="StartAsync"/>, the engine:
/// 1. Connects to the NAS adapter
/// 2. Registers a sync root with the Windows Cloud Filter platform
/// 3. Lists files from the adapter and creates placeholder files
/// 4. Registers FileId mappings for callback path resolution
/// 5. Connects callbacks to begin receiving hydration requests
/// </para>
///
/// <para>
/// When an application opens a placeholder file, the platform invokes
/// <see cref="FetchDataAsync"/>, which downloads the requested byte range
/// from the adapter and delivers it to the platform via <c>CfExecute</c>.
/// </para>
/// </summary>
public sealed class SyncEngine : ISyncEngine, ICfCallbackHandler
{
    private readonly SyncEngineConfiguration _config;
    private readonly INasAdapter _adapter;
    private readonly CfSyncRootManager _syncRootManager;
    private readonly PlaceholderManager _placeholderManager;
    private readonly CancellationTokenSource _cts = new();

    private SyncState _state = SyncState.Idle;

    /// <inheritdoc />
    public string SyncEngineId => _config.EngineId;

    /// <inheritdoc />
    public SyncState State => _state;

    /// <inheritdoc />
    public string SyncRootPath => _config.SyncRootPath;

    /// <inheritdoc />
    public event EventHandler<SyncStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<SyncOperationEventArgs>? SyncOperation;

    /// <inheritdoc />
    public event EventHandler<ConflictDetectedEventArgs>? ConflictDetected;

    /// <summary>
    /// Creates a new SyncEngine with the specified configuration and adapter.
    /// </summary>
    /// <param name="config">Sync engine configuration (paths, IDs, display name).</param>
    /// <param name="adapter">The NAS adapter providing file data.</param>
    public SyncEngine(SyncEngineConfiguration config, INasAdapter adapter)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _syncRootManager = new CfSyncRootManager();
        _placeholderManager = new PlaceholderManager(config.SyncRootPath);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_state != SyncState.Idle)
        {
            throw new InvalidOperationException($"Cannot start engine in state {_state}.");
        }

        TransitionState(SyncState.Starting);

        try
        {
            // Step 1: Connect to the adapter
            await _adapter.ConnectAsync(cancellationToken);

            // Step 2: Register the sync root
            var registrationInfo = new SyncRootRegistrationInfo
            {
                ProviderId = _config.ProviderId,
                AccountId = _config.AccountId,
                SyncRootPath = _config.SyncRootPath,
                DisplayName = _config.DisplayName,
                IconResource = _config.IconResource,
                HydrationPolicy = HydrationPolicy.Progressive,
                HydrationPolicyModifier = HydrationPolicyModifier.AutoDehydrationAllowed,
                PopulationPolicy = PopulationPolicy.Full,
                AllowPinning = true,
                Version = "1.0",
            };

            await _syncRootManager.RegisterAsync(registrationInfo, cancellationToken);

            // Step 3: Populate placeholders from the adapter
            await PopulatePlaceholdersAsync(cancellationToken);

            // Step 4: Connect callbacks (engine is the handler)
            await _syncRootManager.ConnectAsync(this, cancellationToken);

            TransitionState(SyncState.Idle_Synced);
        }
        catch (Exception ex)
        {
            TransitionState(SyncState.Error, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(bool unregisterSyncRoot = false, CancellationToken cancellationToken = default)
    {
        if (_state == SyncState.Idle)
        {
            return;
        }

        _cts.Cancel();

        if (_syncRootManager.IsConnected)
        {
            await _syncRootManager.DisconnectAsync();
        }

        if (unregisterSyncRoot && _syncRootManager.IsRegistered)
        {
            await _syncRootManager.UnregisterAsync(cancellationToken);
        }

        await _adapter.DisconnectAsync(cancellationToken);

        TransitionState(SyncState.Idle);
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (_state == SyncState.Idle_Synced || _state == SyncState.Syncing)
        {
            TransitionState(SyncState.Paused);
        }
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (_state == SyncState.Paused)
        {
            TransitionState(SyncState.Idle_Synced);
        }
    }

    /// <inheritdoc />
    public Task FullScanAsync(CancellationToken cancellationToken = default)
    {
        return PopulatePlaceholdersAsync(cancellationToken);
    }

    /// <inheritdoc />
    public SyncQueueStatus GetQueueStatus()
    {
        // Phase 3: simple status, no queue yet
        return new SyncQueueStatus(0, 0, 0, 0);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_syncRootManager.IsConnected)
        {
            await _syncRootManager.DisconnectAsync();
        }

        _syncRootManager.Dispose();
        await _adapter.DisposeAsync();
        _cts.Dispose();
    }

    // =========================================================================
    // ICfCallbackHandler implementation
    // =========================================================================

    /// <summary>
    /// Handles FETCH_DATA callbacks — downloads file data from the adapter and
    /// delivers it to the platform via <see cref="HydrationDataProvider.ProvideDataAsync"/>.
    /// This is the critical hydration path.
    /// </summary>
    public async Task FetchDataAsync(
        string filePath,
        long offset,
        long length,
        TransferKey transferKey,
        Guid volumeGuidName,
        long fileId,
        CancellationToken cancellationToken)
    {
        if (_state == SyncState.Paused)
        {
            return;
        }

        // Compute the remote path from the local file path
        string relativePath = Path.GetRelativePath(_config.SyncRootPath, filePath);
        string remotePath = "/" + relativePath.Replace('\\', '/');

        SyncOperation?.Invoke(this, new SyncOperationEventArgs(
            SyncOperationType.Download, SyncOperationStatus.InProgress,
            filePath, remotePath, 0, length));

        try
        {
            // Report progress to the Shell
            _placeholderManager.ReportProgress(length, 0);

            // Download from adapter
            using var ms = new MemoryStream();
            await _adapter.DownloadFileAsync(remotePath, ms, offset, length, null, cancellationToken);
            byte[] data = ms.ToArray();

            // Deliver to platform via CfExecute
            var nativeKey = new CfNativeTypes.CF_TRANSFER_KEY { Internal = transferKey.Value };

            // Access HydrationDataProvider through the sync root manager's callback context
            var hydrationProvider = new HydrationDataProvider();
            await hydrationProvider.ProvideDataAsync(volumeGuidName, fileId, nativeKey, data, offset);

            // Report completion
            _placeholderManager.ReportProgress(length, length);

            SyncOperation?.Invoke(this, new SyncOperationEventArgs(
                SyncOperationType.Download, SyncOperationStatus.Completed,
                filePath, remotePath, data.Length, length));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncEngine] FetchData error for {filePath}: {ex.Message}");

            // Report error to platform
            try
            {
                var nativeKey = new CfNativeTypes.CF_TRANSFER_KEY { Internal = transferKey.Value };
                var hydrationProvider = new HydrationDataProvider();
                hydrationProvider.ReportError(volumeGuidName, fileId, nativeKey, 0x00000002); // ERROR_FILE_NOT_FOUND
            }
            catch
            {
                // Best effort
            }

            SyncOperation?.Invoke(this, new SyncOperationEventArgs(
                SyncOperationType.Download, SyncOperationStatus.Failed,
                filePath, remotePath, 0, length, ex));
        }
    }

    /// <summary>
    /// Handles FETCH_PLACEHOLDERS callbacks — lists the remote directory and creates
    /// placeholder entries for on-demand directory population.
    /// </summary>
    public async Task FetchPlaceholdersAsync(string directoryPath, CancellationToken cancellationToken)
    {
        string relativePath = Path.GetRelativePath(_config.SyncRootPath, directoryPath);
        string remotePath = "/" + relativePath.Replace('\\', '/');

        try
        {
            var entries = await _adapter.ListFilesAsync(remotePath, recursive: false, cancellationToken);
            var placeholderEntries = MapToPlaceholderEntries(entries);

            if (placeholderEntries.Count > 0)
            {
                await _placeholderManager.CreatePlaceholdersAsync(placeholderEntries, markInSync: true, cancellationToken);
                await RegisterFileIdMappingsAsync(placeholderEntries);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncEngine] FetchPlaceholders error for {directoryPath}: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task CancelFetchDataAsync(string filePath, TransferKey transferKey)
    {
        System.Diagnostics.Debug.WriteLine($"[SyncEngine] CancelFetchData: {filePath}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyDehydrateAsync(string filePath)
    {
        System.Diagnostics.Debug.WriteLine($"[SyncEngine] NotifyDehydrate: {filePath}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyDehydrateCompletionAsync(string filePath)
    {
        System.Diagnostics.Debug.WriteLine($"[SyncEngine] NotifyDehydrateCompletion: {filePath}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyDeleteAsync(string filePath)
    {
        System.Diagnostics.Debug.WriteLine($"[SyncEngine] NotifyDelete: {filePath}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyRenameAsync(string sourcePath, string destinationPath)
    {
        System.Diagnostics.Debug.WriteLine($"[SyncEngine] NotifyRename: {sourcePath} -> {destinationPath}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyFileOpenCompletionAsync(string filePath)
    {
        System.Diagnostics.Debug.WriteLine($"[SyncEngine] NotifyFileOpenCompletion: {filePath}");
        return Task.CompletedTask;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Lists all files from the adapter and creates placeholder entries under the sync root.
    /// Registers FileId mappings for each created placeholder.
    /// </summary>
    private async Task PopulatePlaceholdersAsync(CancellationToken cancellationToken)
    {
        TransitionState(SyncState.Syncing);

        // List all files recursively from the adapter
        var allEntries = await _adapter.ListFilesAsync("/", recursive: true, cancellationToken);

        // Sort: directories first, then files (placeholders must be created in order)
        var sorted = allEntries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Path)
            .ToList();

        var placeholderEntries = MapToPlaceholderEntries(sorted);

        if (placeholderEntries.Count > 0)
        {
            await _placeholderManager.CreatePlaceholdersAsync(placeholderEntries, markInSync: true, cancellationToken);
            await RegisterFileIdMappingsAsync(placeholderEntries);
        }
    }

    /// <summary>
    /// Maps adapter file entries to placeholder creation entries.
    /// Converts remote paths (forward slash) to relative paths (backslash).
    /// </summary>
    private List<PlaceholderEntry> MapToPlaceholderEntries(IReadOnlyList<RemoteFileEntry> entries)
    {
        var result = new List<PlaceholderEntry>(entries.Count);

        foreach (var entry in entries)
        {
            // Convert remote path "/Documents/report.pdf" to relative "Documents\report.pdf"
            string relativePath = entry.Path.TrimStart('/').Replace('/', '\\');

            result.Add(new PlaceholderEntry(
                RelativePath: relativePath,
                IsDirectory: entry.IsDirectory,
                FileSize: entry.IsDirectory ? 0 : entry.Size,
                LastModifiedUtc: entry.LastModifiedUtc,
                CreatedUtc: entry.CreatedUtc));
        }

        return result;
    }

    /// <summary>
    /// Queries FileId for each created placeholder and registers it with the PathResolver.
    /// This enables callback dispatchers to resolve file paths from FileId values.
    /// </summary>
    private Task RegisterFileIdMappingsAsync(List<PlaceholderEntry> entries)
    {
        int registered = 0;

        foreach (var entry in entries)
        {
            string fullPath = Path.Combine(_config.SyncRootPath, entry.RelativePath);

            try
            {
                long fileId = PlaceholderManager.GetFileId(fullPath);
                _syncRootManager.PathResolver.RegisterMapping(fileId, fullPath);
                registered++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SyncEngine] Failed to register FileId for {fullPath}: {ex.Message}");
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"[SyncEngine] Registered {registered}/{entries.Count} FileId mappings.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Transitions the engine state and fires the <see cref="StateChanged"/> event.
    /// </summary>
    private void TransitionState(SyncState newState, string? message = null)
    {
        if (_state == newState) return;

        var oldState = _state;
        _state = newState;
        StateChanged?.Invoke(this, new SyncStateChangedEventArgs(oldState, newState, message));
    }
}
