namespace NasSync.Core;

/// <summary>
/// Defines the contract for the bidirectional sync engine that orchestrates
/// file synchronization between local placeholders and the remote NAS.
/// Handles change detection, conflict resolution, hydration, and dehydration.
/// </summary>
public interface ISyncEngine : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for this sync engine instance.
    /// </summary>
    string SyncEngineId { get; }

    /// <summary>
    /// Gets the current sync state.
    /// </summary>
    SyncState State { get; }

    /// <summary>
    /// Gets the local sync root path where placeholder files reside.
    /// </summary>
    string SyncRootPath { get; }

    /// <summary>
    /// Event raised when the sync state changes.
    /// </summary>
    event EventHandler<SyncStateChangedEventArgs> StateChanged;

    /// <summary>
    /// Event raised when a sync operation (upload/download) starts or completes.
    /// </summary>
    event EventHandler<SyncOperationEventArgs> SyncOperation;

    /// <summary>
    /// Event raised when a file conflict is detected and requires user resolution.
    /// </summary>
    event EventHandler<ConflictDetectedEventArgs> ConflictDetected;

    /// <summary>
    /// Starts the sync engine — registers the sync root, creates placeholders,
    /// begins file system watching, and initiates bidirectional sync.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous start operation.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the sync engine gracefully — pauses file system watching,
    /// completes in-flight operations, and optionally disconnects the sync root.
    /// </summary>
    /// <param name="unregisterSyncRoot">Whether to unregister the sync root from Windows.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous stop operation.</returns>
    Task StopAsync(bool unregisterSyncRoot = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses sync operations without unregistering the sync root.
    /// Placeholders remain visible but no data transfers occur.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes sync operations after a pause.
    /// </summary>
    void Resume();

    /// <summary>
    /// Triggers a full sync scan — compares local and remote file trees,
    /// queues necessary uploads/downloads, and resolves any pending conflicts.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous full scan operation.</returns>
    Task FullScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current sync queue status — number of pending, in-progress,
    /// and completed operations.
    /// </summary>
    /// <returns>The current sync queue status snapshot.</returns>
    SyncQueueStatus GetQueueStatus();
}

/// <summary>
/// Represents the overall state of the sync engine.
/// </summary>
public enum SyncState
{
    /// <summary>The engine has not been started yet.</summary>
    Idle,

    /// <summary>The engine is starting up (registering sync root, scanning).</summary>
    Starting,

    /// <summary>The engine is actively syncing files.</summary>
    Syncing,

    /// <summary>The engine is running but idle (no pending changes).</summary>
    Idle_Synced,

    /// <summary>The engine is paused by user request.</summary>
    Paused,

    /// <summary>The engine is offline (NAS unreachable), cached files still available.</summary>
    Offline,

    /// <summary>The engine encountered a critical error.</summary>
    Error
}

/// <summary>
/// Describes the type of a sync operation.
/// </summary>
public enum SyncOperationType
{
    /// <summary>Downloading a file from NAS to local (hydration).</summary>
    Download,

    /// <summary>Uploading a local file to NAS.</summary>
    Upload,

    /// <summary>Creating a placeholder file locally.</summary>
    CreatePlaceholder,

    /// <summary>Dehydrating a file (releasing local cache).</summary>
    Dehydrate,

    /// <summary>Deleting a file on both sides.</summary>
    Delete,

    /// <summary>Renaming/moving a file on both sides.</summary>
    Rename
}

/// <summary>
/// Describes the status of a sync operation.
/// </summary>
public enum SyncOperationStatus
{
    /// <summary>The operation is queued and waiting to start.</summary>
    Queued,

    /// <summary>The operation is currently in progress.</summary>
    InProgress,

    /// <summary>The operation completed successfully.</summary>
    Completed,

    /// <summary>The operation failed.</summary>
    Failed,

    /// <summary>The operation was cancelled.</summary>
    Cancelled
}

/// <summary>
/// Event arguments for sync state changes.
/// </summary>
public class SyncStateChangedEventArgs(SyncState oldState, SyncState newState, string? message = null) : EventArgs
{
    /// <summary>Gets the previous sync state.</summary>
    public SyncState OldState { get; } = oldState;

    /// <summary>Gets the new sync state.</summary>
    public SyncState NewState { get; } = newState;

    /// <summary>Gets an optional message describing the reason for the change.</summary>
    public string? Message { get; } = message;
}

/// <summary>
/// Event arguments for sync operation lifecycle events.
/// </summary>
public class SyncOperationEventArgs(
    SyncOperationType operationType,
    SyncOperationStatus status,
    string localPath,
    string remotePath,
    long bytesTransferred = 0,
    long totalBytes = 0,
    Exception? error = null) : EventArgs
{
    /// <summary>Gets the type of sync operation.</summary>
    public SyncOperationType OperationType { get; } = operationType;

    /// <summary>Gets the current status of the operation.</summary>
    public SyncOperationStatus Status { get; } = status;

    /// <summary>Gets the local file path involved in the operation.</summary>
    public string LocalPath { get; } = localPath;

    /// <summary>Gets the remote file path involved in the operation.</summary>
    public string RemotePath { get; } = remotePath;

    /// <summary>Gets the number of bytes transferred so far.</summary>
    public long BytesTransferred { get; } = bytesTransferred;

    /// <summary>Gets the total bytes to transfer (0 if unknown).</summary>
    public long TotalBytes { get; } = totalBytes;

    /// <summary>Gets the error if the operation failed.</summary>
    public Exception? Error { get; } = error;

    /// <summary>Gets the transfer progress as a percentage (0.0 to 1.0).</summary>
    public double Progress => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes : 0;
}

/// <summary>
/// Event arguments for conflict detection.
/// </summary>
public class ConflictDetectedEventArgs(
    string localPath,
    string remotePath,
    DateTimeOffset localModifiedTime,
    DateTimeOffset remoteModifiedTime) : EventArgs
{
    /// <summary>Gets the local file path that has the conflict.</summary>
    public string LocalPath { get; } = localPath;

    /// <summary>Gets the remote file path that has the conflict.</summary>
    public string RemotePath { get; } = remotePath;

    /// <summary>Gets the last modification time of the local file.</summary>
    public DateTimeOffset LocalModifiedTime { get; } = localModifiedTime;

    /// <summary>Gets the last modification time of the remote file.</summary>
    public DateTimeOffset RemoteModifiedTime { get; } = remoteModifiedTime;
}

/// <summary>
/// Snapshot of the current sync queue status.
/// </summary>
/// <param name="PendingCount">Number of operations waiting to start.</param>
/// <param name="InProgressCount">Number of operations currently in progress.</param>
/// <param name="CompletedCount">Number of recently completed operations.</param>
/// <param name="FailedCount">Number of failed operations.</param>
public record SyncQueueStatus(
    int PendingCount,
    int InProgressCount,
    int CompletedCount,
    int FailedCount);
