using NasSync.Adapters;

namespace NasSync.Core;

/// <summary>
/// A test/development <see cref="INasAdapter"/> implementation that uses a local
/// directory as the "NAS" data source. Enables end-to-end testing of the sync engine
/// without requiring a real NAS device or network connectivity.
///
/// <para>
/// All file operations are performed against the configured server directory using
/// standard <c>System.IO</c> APIs. Remote paths use forward slashes (<c>/</c>) and
/// are mapped to local paths with backslashes (<c>\</c>).
/// </para>
///
/// <para>
/// This adapter is modeled after the Cloud Mirror sample's <c>FakeCloudProvider</c>
/// and serves as the reference implementation for the <see cref="INasAdapter"/> contract.
/// </para>
/// </summary>
public sealed class LocalFolderAdapter : INasAdapter
{
    private readonly string _serverDirectory;
    private ConnectionState _state = ConnectionState.Disconnected;

    /// <summary>Buffer size for chunked file transfers (64 KB).</summary>
    private const int BUFFER_SIZE = 64 * 1024;

    /// <inheritdoc />
    public string AdapterId { get; }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public ConnectionState State => _state;

    /// <inheritdoc />
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <inheritdoc />
    public event EventHandler<RemoteFileChangedEventArgs>? RemoteFileChanged;

    /// <summary>
    /// Creates a new LocalFolderAdapter backed by the specified directory.
    /// </summary>
    /// <param name="serverDirectory">
    /// The full path to the local directory that acts as the NAS.
    /// Must exist before calling <see cref="ConnectAsync"/>.
    /// </param>
    /// <param name="adapterId">Unique identifier for this adapter instance.</param>
    /// <param name="displayName">Human-readable display name.</param>
    public LocalFolderAdapter(string serverDirectory, string adapterId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        _serverDirectory = Path.GetFullPath(serverDirectory);
        AdapterId = adapterId;
        DisplayName = displayName;
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_serverDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Server directory does not exist: {_serverDirectory}");
        }

        var oldState = _state;
        _state = ConnectionState.Connected;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, _state));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var oldState = _state;
        _state = ConnectionState.Disconnected;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, _state));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteFileEntry>> ListFilesAsync(
        string remotePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        string localPath = MapToLocalPath(remotePath);
        var results = new List<RemoteFileEntry>();

        if (!Directory.Exists(localPath))
        {
            return Task.FromResult<IReadOnlyList<RemoteFileEntry>>(results);
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Enumerate directories
        foreach (string dir in Directory.GetDirectories(localPath, "*", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirInfo = new DirectoryInfo(dir);
            results.Add(new RemoteFileEntry(
                Path: MapToRemotePath(dir),
                Name: dirInfo.Name,
                IsDirectory: true,
                Size: 0,
                LastModifiedUtc: dirInfo.LastWriteTimeUtc,
                CreatedUtc: dirInfo.CreationTimeUtc));
        }

        // Enumerate files
        foreach (string file in Directory.GetFiles(localPath, "*", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(file);
            results.Add(new RemoteFileEntry(
                Path: MapToRemotePath(file),
                Name: fileInfo.Name,
                IsDirectory: false,
                Size: fileInfo.Length,
                LastModifiedUtc: fileInfo.LastWriteTimeUtc,
                CreatedUtc: fileInfo.CreationTimeUtc));
        }

        return Task.FromResult<IReadOnlyList<RemoteFileEntry>>(results);
    }

    /// <inheritdoc />
    public async Task DownloadFileAsync(
        string remotePath, Stream outputStream, long offset = 0, long length = -1,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        string localPath = MapToLocalPath(remotePath);

        await using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, useAsync: true);

        if (offset > 0)
        {
            fileStream.Seek(offset, SeekOrigin.Begin);
        }

        long remaining = length > 0 ? length : fileStream.Length - offset;
        byte[] buffer = new byte[BUFFER_SIZE];
        long totalRead = 0;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int toRead = (int)Math.Min(buffer.Length, remaining);
            int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);

            if (bytesRead == 0)
            {
                break; // End of file
            }

            await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

            totalRead += bytesRead;
            remaining -= bytesRead;
            progress?.Report(totalRead);
        }
    }

    /// <inheritdoc />
    public async Task UploadFileAsync(
        Stream localStream, string remotePath, bool overwrite = true,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        string localPath = MapToLocalPath(remotePath);

        // Ensure parent directory exists
        string? parentDir = Path.GetDirectoryName(localPath);
        if (parentDir is not null)
        {
            Directory.CreateDirectory(parentDir);
        }

        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var fileStream = new FileStream(localPath, mode, FileAccess.Write, FileShare.None, BUFFER_SIZE, useAsync: true);

        byte[] buffer = new byte[BUFFER_SIZE];
        long totalWritten = 0;
        int bytesRead;

        while ((bytesRead = await localStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

            totalWritten += bytesRead;
            progress?.Report(totalWritten);
        }
    }

    /// <inheritdoc />
    public Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        string localPath = MapToLocalPath(remotePath);
        Directory.CreateDirectory(localPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string remotePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        string localPath = MapToLocalPath(remotePath);

        if (Directory.Exists(localPath))
        {
            Directory.Delete(localPath, recursive);
        }
        else if (File.Exists(localPath))
        {
            File.Delete(localPath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        string localSource = MapToLocalPath(sourcePath);
        string localDest = MapToLocalPath(destinationPath);

        if (Directory.Exists(localSource))
        {
            Directory.Move(localSource, localDest);
        }
        else
        {
            // Ensure parent directory exists
            string? parentDir = Path.GetDirectoryName(localDest);
            if (parentDir is not null)
            {
                Directory.CreateDirectory(parentDir);
            }

            File.Move(localSource, localDest);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<StorageQuota> GetStorageQuotaAsync(CancellationToken cancellationToken = default)
    {
        var driveInfo = new DriveInfo(Path.GetPathRoot(_serverDirectory) ?? "C:");

        return Task.FromResult(new StorageQuota(
            TotalBytes: driveInfo.TotalSize,
            UsedBytes: driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
            AvailableBytes: driveInfo.AvailableFreeSpace));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_state == ConnectionState.Connected)
        {
            _state = ConnectionState.Disconnected;
        }

        return ValueTask.CompletedTask;
    }

    // =========================================================================
    // Path mapping
    // =========================================================================

    /// <summary>
    /// Maps a remote path (forward slashes) to a local file system path.
    /// Example: "/Documents/report.pdf" → "C:\Server\Documents\report.pdf"
    /// </summary>
    private string MapToLocalPath(string remotePath)
    {
        // Normalize: remove leading slash, replace / with \
        string relative = remotePath.TrimStart('/').Replace('/', '\\');
        return string.IsNullOrEmpty(relative)
            ? _serverDirectory
            : Path.Combine(_serverDirectory, relative);
    }

    /// <summary>
    /// Maps a local file system path to a remote path (forward slashes).
    /// Example: "C:\Server\Documents\report.pdf" → "/Documents/report.pdf"
    /// </summary>
    private string MapToRemotePath(string localPath)
    {
        string relative = Path.GetRelativePath(_serverDirectory, localPath);
        return "/" + relative.Replace('\\', '/');
    }
}
