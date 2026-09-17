using System.Collections.Concurrent;
using NasSync.Adapters;

namespace NasSync.Core;

/// <summary>
/// Watches the sync root directory for local file changes and uploads them to the adapter.
/// Implements debouncing (500ms), anti-echo filtering (30s TTL), and file-release waiting
/// to handle the realities of file system notifications during active saves.
///
/// <para>
/// Change flow:
/// 1. FileSystemWatcher fires event → stored in pending queue with timestamp
/// 2. Processing timer (200ms) drains entries older than 500ms (debounce)
/// 3. Anti-echo check: skip if the engine recently synced this file
/// 4. Wait for file lock release (retry every 100ms for 5s)
/// 5. Upload via adapter
/// </para>
/// </summary>
internal sealed class FileChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly INasAdapter _adapter;
    private readonly string _syncRootPath;
    private readonly ConcurrentDictionary<string, PendingChange> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentlySynced = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _processTimer;

    /// <summary>Debounce delay — collapse rapid successive events.</summary>
    private static readonly TimeSpan DEBOUNCE_DELAY = TimeSpan.FromMilliseconds(500);

    /// <summary>Anti-echo TTL — ignore events for recently synced files.</summary>
    private static readonly TimeSpan SYNC_EXPIRY = TimeSpan.FromSeconds(30);

    /// <summary>Processing interval — how often to check for pending changes.</summary>
    private static readonly TimeSpan PROCESS_INTERVAL = TimeSpan.FromMilliseconds(200);

    /// <summary>Maximum time to wait for a file lock to be released.</summary>
    private static readonly TimeSpan FILE_LOCK_TIMEOUT = TimeSpan.FromSeconds(5);

    /// <summary>Retry interval for file lock waiting.</summary>
    private static readonly TimeSpan FILE_LOCK_RETRY = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets the number of pending (unprocessed) changes.</summary>
    public int PendingCount => _pendingChanges.Count;

    /// <summary>Fired when an upload operation starts.</summary>
    public event EventHandler<SyncOperationEventArgs>? UploadStarted;

    /// <summary>Fired when an upload operation completes (success or failure).</summary>
    public event EventHandler<SyncOperationEventArgs>? UploadCompleted;

    /// <summary>
    /// Creates a new FileChangeWatcher for the specified sync root.
    /// </summary>
    /// <param name="syncRootPath">The directory to watch for changes.</param>
    /// <param name="adapter">The adapter to upload changes to.</param>
    public FileChangeWatcher(string syncRootPath, INasAdapter adapter)
    {
        _syncRootPath = syncRootPath;
        _adapter = adapter;

        _watcher = new FileSystemWatcher(syncRootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size,
        };

        _watcher.Created += OnFileSystemEvent;
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnRenamedEvent;

        _processTimer = new Timer(ProcessPendingChanges, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Starts watching for file changes and processing the pending queue.
    /// </summary>
    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
        _processTimer.Change(PROCESS_INTERVAL, PROCESS_INTERVAL);
    }

    /// <summary>
    /// Stops watching and processing. Pending changes are preserved for when Start is called again.
    /// </summary>
    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
        _processTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Marks a file as recently synced by the engine. File system events for this path
    /// will be ignored for the next 30 seconds (anti-echo).
    /// </summary>
    /// <param name="fullPath">The full path of the recently synced file.</param>
    public void MarkRecentlySynced(string fullPath)
    {
        _recentlySynced[fullPath] = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileSystemEvent;
        _watcher.Changed -= OnFileSystemEvent;
        _watcher.Deleted -= OnFileSystemEvent;
        _watcher.Renamed -= OnRenamedEvent;
        _watcher.Dispose();
        _processTimer.Dispose();
    }

    // =========================================================================
    // Event handlers
    // =========================================================================

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        var changeType = e.ChangeType switch
        {
            WatcherChangeTypes.Created => ChangeType.Created,
            WatcherChangeTypes.Changed => ChangeType.Changed,
            WatcherChangeTypes.Deleted => ChangeType.Deleted,
            _ => ChangeType.Changed,
        };

        _pendingChanges[e.FullPath] = new PendingChange(e.FullPath, changeType, DateTimeOffset.UtcNow, null);
    }

    private void OnRenamedEvent(object sender, RenamedEventArgs e)
    {
        _pendingChanges[e.FullPath] = new PendingChange(e.FullPath, ChangeType.Renamed, DateTimeOffset.UtcNow, e.OldFullPath);
    }

    // =========================================================================
    // Processing
    // =========================================================================

    /// <summary>
    /// Timer callback that processes pending changes older than the debounce delay.
    /// </summary>
    private async void ProcessPendingChanges(object? state)
    {
        var now = DateTimeOffset.UtcNow;

        // Clean up expired anti-echo entries
        foreach (var kvp in _recentlySynced)
        {
            if (now - kvp.Value > SYNC_EXPIRY)
            {
                _recentlySynced.TryRemove(kvp.Key, out _);
            }
        }

        // Process entries that have been pending long enough (debounced)
        foreach (var kvp in _pendingChanges)
        {
            if (now - kvp.Value.Timestamp < DEBOUNCE_DELAY)
            {
                continue; // Too recent, wait for debounce
            }

            if (!_pendingChanges.TryRemove(kvp.Key, out PendingChange change))
            {
                continue; // Already removed by another thread
            }

            // Anti-echo: skip if the engine recently synced this file
            if (_recentlySynced.ContainsKey(change.FullPath))
            {
                continue;
            }

            await ProcessChangeAsync(change);
        }
    }

    /// <summary>
    /// Processes a single file change by uploading, deleting, or renaming on the adapter.
    /// </summary>
    private async Task ProcessChangeAsync(PendingChange change)
    {
        string relativePath = Path.GetRelativePath(_syncRootPath, change.FullPath);
        string remotePath = "/" + relativePath.Replace('\\', '/');

        try
        {
            switch (change.ChangeType)
            {
                case ChangeType.Created:
                case ChangeType.Changed:
                    await UploadFileAsync(change.FullPath, remotePath);
                    break;

                case ChangeType.Deleted:
                    UploadStarted?.Invoke(this, new SyncOperationEventArgs(
                        SyncOperationType.Delete, SyncOperationStatus.InProgress,
                        change.FullPath, remotePath));

                    await _adapter.DeleteAsync(remotePath);

                    UploadCompleted?.Invoke(this, new SyncOperationEventArgs(
                        SyncOperationType.Delete, SyncOperationStatus.Completed,
                        change.FullPath, remotePath));
                    break;

                case ChangeType.Renamed:
                    if (change.OldPath is not null)
                    {
                        string oldRelative = Path.GetRelativePath(_syncRootPath, change.OldPath);
                        string oldRemote = "/" + oldRelative.Replace('\\', '/');

                        UploadStarted?.Invoke(this, new SyncOperationEventArgs(
                            SyncOperationType.Rename, SyncOperationStatus.InProgress,
                            change.FullPath, remotePath));

                        await _adapter.MoveAsync(oldRemote, remotePath);

                        UploadCompleted?.Invoke(this, new SyncOperationEventArgs(
                            SyncOperationType.Rename, SyncOperationStatus.Completed,
                            change.FullPath, remotePath));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FileChangeWatcher] Error processing {change.ChangeType} for {change.FullPath}: {ex.Message}");

            UploadCompleted?.Invoke(this, new SyncOperationEventArgs(
                SyncOperationType.Upload, SyncOperationStatus.Failed,
                change.FullPath, remotePath, error: ex));
        }
    }

    /// <summary>
    /// Uploads a file to the adapter, waiting for the file lock to be released first.
    /// </summary>
    private async Task UploadFileAsync(string fullPath, string remotePath)
    {
        // Skip directories
        if (Directory.Exists(fullPath))
        {
            await _adapter.CreateDirectoryAsync(remotePath);
            return;
        }

        if (!File.Exists(fullPath))
        {
            return; // File was deleted before we could upload
        }

        var fileInfo = new FileInfo(fullPath);

        UploadStarted?.Invoke(this, new SyncOperationEventArgs(
            SyncOperationType.Upload, SyncOperationStatus.InProgress,
            fullPath, remotePath, 0, fileInfo.Length));

        // Wait for the file to be released by the writing application
        await WaitForFileReleaseAsync(fullPath);

        await using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        await _adapter.UploadFileAsync(stream, remotePath, overwrite: true);

        // Mark as recently synced to prevent echo
        MarkRecentlySynced(fullPath);

        UploadCompleted?.Invoke(this, new SyncOperationEventArgs(
            SyncOperationType.Upload, SyncOperationStatus.Completed,
            fullPath, remotePath, fileInfo.Length, fileInfo.Length));
    }

    /// <summary>
    /// Waits for a file's write lock to be released, retrying every 100ms for up to 5 seconds.
    /// This handles the common case where an application is still writing when FSW fires.
    /// </summary>
    private static async Task WaitForFileReleaseAsync(string fullPath)
    {
        var deadline = DateTimeOffset.UtcNow + FILE_LOCK_TIMEOUT;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var testStream = new FileStream(
                    fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return; // File is available
            }
            catch (IOException)
            {
                await Task.Delay(FILE_LOCK_RETRY);
            }
        }

        // Timeout — proceed anyway, the upload will fail gracefully if still locked
    }

    // =========================================================================
    // Internal types
    // =========================================================================

    private enum ChangeType
    {
        Created,
        Changed,
        Deleted,
        Renamed,
    }

    private sealed record PendingChange(
        string FullPath,
        ChangeType ChangeType,
        DateTimeOffset Timestamp,
        string? OldPath);
}
