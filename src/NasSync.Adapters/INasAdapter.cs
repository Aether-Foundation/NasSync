namespace NasSync.Adapters;

/// <summary>
/// Defines the contract for a NAS system adapter that provides file operations
/// and metadata queries against a specific NAS platform (e.g., Synology, QNAP, TrueNAS).
/// Each adapter implementation handles the platform-specific communication details.
/// </summary>
public interface INasAdapter : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for this adapter instance (e.g., "synology-home", "qnap-office").
    /// </summary>
    string AdapterId { get; }

    /// <summary>
    /// Gets the display name for this NAS connection (e.g., "Home NAS", "Office NAS").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the current connection state of the adapter.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Event raised when the connection state changes.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

    /// <summary>
    /// Event raised when a remote file change is detected on the NAS.
    /// </summary>
    event EventHandler<RemoteFileChangedEventArgs> RemoteFileChanged;

    /// <summary>
    /// Establishes a connection to the NAS device.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous connection operation.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the NAS device gracefully.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous disconnection operation.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all files and directories at the specified remote path.
    /// </summary>
    /// <param name="remotePath">The remote directory path to list.</param>
    /// <param name="recursive">Whether to list recursively.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A collection of file/directory metadata entries.</returns>
    Task<IReadOnlyList<RemoteFileEntry>> ListFilesAsync(
        string remotePath,
        bool recursive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a file from the NAS to a local stream.
    /// Supports partial/range reads for progressive hydration.
    /// </summary>
    /// <param name="remotePath">The remote file path to download.</param>
    /// <param name="outputStream">The local stream to write the downloaded data to.</param>
    /// <param name="offset">Byte offset to start reading from (0 for beginning).</param>
    /// <param name="length">Number of bytes to read (-1 for entire remaining file).</param>
    /// <param name="progress">Optional progress reporter for download tracking.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous download operation.</returns>
    Task DownloadFileAsync(
        string remotePath,
        Stream outputStream,
        long offset = 0,
        long length = -1,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file from a local stream to the NAS.
    /// </summary>
    /// <param name="localStream">The local stream to read the upload data from.</param>
    /// <param name="remotePath">The remote file path to upload to.</param>
    /// <param name="overwrite">Whether to overwrite an existing file at the remote path.</param>
    /// <param name="progress">Optional progress reporter for upload tracking.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous upload operation.</returns>
    Task UploadFileAsync(
        Stream localStream,
        string remotePath,
        bool overwrite = true,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a directory on the NAS at the specified remote path.
    /// </summary>
    /// <param name="remotePath">The remote directory path to create.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file or directory on the NAS at the specified remote path.
    /// </summary>
    /// <param name="remotePath">The remote path to delete.</param>
    /// <param name="recursive">Whether to delete directories recursively.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteAsync(string remotePath, bool recursive = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames/moves a file or directory on the NAS.
    /// </summary>
    /// <param name="sourcePath">The current remote path.</param>
    /// <param name="destinationPath">The new remote path.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves storage quota information from the NAS.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Storage quota information including total, used, and available space.</returns>
    Task<StorageQuota> GetStorageQuotaAsync(CancellationToken cancellationToken = default);
}
