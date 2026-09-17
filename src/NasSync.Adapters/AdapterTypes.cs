namespace NasSync.Adapters;

/// <summary>
/// Represents the connection state of a NAS adapter.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// The adapter is disconnected from the NAS.
    /// </summary>
    Disconnected,

    /// <summary>
    /// The adapter is in the process of connecting to the NAS.
    /// </summary>
    Connecting,

    /// <summary>
    /// The adapter is connected and operational.
    /// </summary>
    Connected,

    /// <summary>
    /// The adapter lost connection and is attempting to reconnect.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// The adapter encountered an unrecoverable error.
    /// </summary>
    Error
}

/// <summary>
/// Represents metadata for a file or directory on the remote NAS.
/// </summary>
/// <param name="Path">The full remote path of the entry.</param>
/// <param name="Name">The file/directory name (last segment of the path).</param>
/// <param name="IsDirectory">Whether this entry is a directory.</param>
/// <param name="Size">File size in bytes (0 for directories).</param>
/// <param name="LastModifiedUtc">Last modification time in UTC.</param>
/// <param name="CreatedUtc">Creation time in UTC.</param>
/// <param name="Checksum">Optional content checksum (e.g., MD5 or SHA256) for change detection.</param>
public record RemoteFileEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long Size,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset CreatedUtc,
    string? Checksum = null);

/// <summary>
/// Represents storage quota information from the NAS.
/// </summary>
/// <param name="TotalBytes">Total storage capacity in bytes.</param>
/// <param name="UsedBytes">Currently used storage in bytes.</param>
/// <param name="AvailableBytes">Available free storage in bytes.</param>
public record StorageQuota(long TotalBytes, long UsedBytes, long AvailableBytes);

/// <summary>
/// Event arguments for connection state changes.
/// </summary>
/// <param name="OldState">The previous connection state.</param>
/// <param name="NewState">The new connection state.</param>
/// <param name="ErrorMessage">Optional error message if the state change was due to an error.</param>
public class ConnectionStateChangedEventArgs(
    ConnectionState oldState,
    ConnectionState newState,
    string? errorMessage = null) : EventArgs
{
    /// <summary>Gets the previous connection state.</summary>
    public ConnectionState OldState { get; } = oldState;

    /// <summary>Gets the new connection state.</summary>
    public ConnectionState NewState { get; } = newState;

    /// <summary>Gets the optional error message.</summary>
    public string? ErrorMessage { get; } = errorMessage;
}

/// <summary>
/// Describes the type of change detected on a remote file.
/// </summary>
public enum RemoteFileChangeType
{
    /// <summary>A new file or directory was created.</summary>
    Created,

    /// <summary>An existing file or directory was modified.</summary>
    Modified,

    /// <summary>A file or directory was deleted.</summary>
    Deleted,

    /// <summary>A file or directory was renamed/moved.</summary>
    Renamed
}

/// <summary>
/// Event arguments for remote file change notifications.
/// </summary>
public class RemoteFileChangedEventArgs(
    RemoteFileChangeType changeType,
    string remotePath,
    string? oldRemotePath = null,
    RemoteFileEntry? entry = null) : EventArgs
{
    /// <summary>Gets the type of change.</summary>
    public RemoteFileChangeType ChangeType { get; } = changeType;

    /// <summary>Gets the affected remote path.</summary>
    public string RemotePath { get; } = remotePath;

    /// <summary>Gets the previous remote path (for renames).</summary>
    public string? OldRemotePath { get; } = oldRemotePath;

    /// <summary>Gets the file entry metadata (null for deletions).</summary>
    public RemoteFileEntry? Entry { get; } = entry;
}
