namespace NasSync.Adapters.Smb;

/// <summary>
/// SMB/CIFS protocol adapter implementation for accessing NAS file shares
/// via Windows network paths (\\server\share). Leverages the built-in
/// Windows SMB client for file operations.
/// </summary>
public sealed class SmbAdapter : INasAdapter
{
    private readonly SmbAdapterConfiguration _config;
    private ConnectionState _state = ConnectionState.Disconnected;

    /// <inheritdoc />
    public string AdapterId => _config.AdapterId;

    /// <inheritdoc />
    public string DisplayName => _config.DisplayName;

    /// <inheritdoc />
    public ConnectionState State => _state;

    /// <inheritdoc />
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <inheritdoc />
    public event EventHandler<RemoteFileChangedEventArgs>? RemoteFileChanged;

    /// <summary>
    /// Creates a new SMB adapter with the specified configuration.
    /// </summary>
    /// <param name="config">SMB adapter configuration including server and share details.</param>
    public SmbAdapter(SmbAdapterConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Verify SMB share is accessible, establish connection monitoring
        throw new NotImplementedException("SMB adapter connection not yet implemented.");
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Clean up SMB connection resources
        throw new NotImplementedException("SMB adapter disconnection not yet implemented.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteFileEntry>> ListFilesAsync(
        string remotePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        // TODO: Enumerate files via System.IO against the UNC path
        throw new NotImplementedException("SMB file listing not yet implemented.");
    }

    /// <inheritdoc />
    public Task DownloadFileAsync(
        string remotePath, Stream outputStream, long offset = 0, long length = -1,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        // TODO: Read file via System.IO FileStream with range support
        throw new NotImplementedException("SMB file download not yet implemented.");
    }

    /// <inheritdoc />
    public Task UploadFileAsync(
        Stream localStream, string remotePath, bool overwrite = true,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        // TODO: Write file via System.IO FileStream to UNC path
        throw new NotImplementedException("SMB file upload not yet implemented.");
    }

    /// <inheritdoc />
    public Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        // TODO: Create directory via System.IO.Directory
        throw new NotImplementedException("SMB directory creation not yet implemented.");
    }

    /// <inheritdoc />
    public Task DeleteAsync(string remotePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        // TODO: Delete file/directory via System.IO
        throw new NotImplementedException("SMB delete not yet implemented.");
    }

    /// <inheritdoc />
    public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        // TODO: Move/rename via System.IO
        throw new NotImplementedException("SMB move not yet implemented.");
    }

    /// <inheritdoc />
    public Task<StorageQuota> GetStorageQuotaAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Query disk free space via DriveInfo or P/Invoke GetDiskFreeSpaceEx
        throw new NotImplementedException("SMB storage quota not yet implemented.");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // TODO: Clean up resources
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Configuration for the SMB/CIFS adapter.
/// </summary>
public sealed class SmbAdapterConfiguration
{
    /// <summary>Gets or sets the unique adapter instance identifier.</summary>
    public required string AdapterId { get; init; }

    /// <summary>Gets or sets the display name for this connection.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets or sets the NAS server hostname or IP address.</summary>
    public required string ServerAddress { get; init; }

    /// <summary>Gets or sets the SMB share name on the NAS.</summary>
    public required string ShareName { get; init; }

    /// <summary>Gets or sets the optional username for SMB authentication.</summary>
    public string? Username { get; init; }

    /// <summary>Gets or sets the optional password for SMB authentication.</summary>
    public string? Password { get; init; }

    /// <summary>Gets or sets the optional domain for SMB authentication.</summary>
    public string? Domain { get; init; }

    /// <summary>
    /// Builds the UNC path for this SMB connection (\\server\share).
    /// </summary>
    public string UncPath => $@"\\{ServerAddress}\{ShareName}";
}
