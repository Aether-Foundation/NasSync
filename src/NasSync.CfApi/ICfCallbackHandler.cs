namespace NasSync.CfApi;

/// <summary>
/// Defines the callback interface that the CfAPI wrapper invokes when
/// the cldflt.sys minifilter driver requests operations from the sync engine.
/// Implement this interface in the sync engine to handle hydration, dehydration,
/// and file lifecycle events.
///
/// <para>
/// Callbacks are invoked on threads managed by the Cloud Filter platform.
/// Implementations should be thread-safe and handle exceptions gracefully —
/// exceptions must not propagate to native code.
/// </para>
/// </summary>
public interface ICfCallbackHandler
{
    /// <summary>
    /// Called when an application opens a placeholder file and needs data to be downloaded.
    /// The sync engine must download the requested byte range and provide it via
    /// <see cref="HydrationDataProvider.ProvideDataAsync"/>.
    ///
    /// <para>
    /// This callback is invoked synchronously by the platform. The calling application's
    /// I/O is blocked until data is provided. Use <c>.GetAwaiter().GetResult()</c> to
    /// bridge async download code into this synchronous callback.
    /// </para>
    /// </summary>
    /// <param name="filePath">The full path of the file being hydrated.</param>
    /// <param name="offset">The byte offset to start reading from.</param>
    /// <param name="length">The number of bytes to provide.</param>
    /// <param name="transferKey">The transfer key for providing data via CfExecute.</param>
    /// <param name="volumeGuidName">The volume GUID from the callback info.</param>
    /// <param name="fileId">The NTFS file ID from the callback info.</param>
    /// <param name="cancellationToken">Token to cancel the hydration operation.</param>
    /// <returns>A task representing the asynchronous data fetch operation.</returns>
    Task FetchDataAsync(
        string filePath,
        long offset,
        long length,
        TransferKey transferKey,
        Guid volumeGuidName,
        long fileId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Called when the platform needs to populate a directory with placeholder entries.
    /// The sync engine should list the remote directory contents and create placeholders.
    /// </summary>
    /// <param name="directoryPath">The full path of the directory to populate.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous placeholder creation operation.</returns>
    Task FetchPlaceholdersAsync(string directoryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a file hydration is cancelled (e.g., user closes the app before download completes).
    /// </summary>
    /// <param name="filePath">The full path of the file whose hydration was cancelled.</param>
    /// <param name="transferKey">The transfer key of the cancelled operation.</param>
    /// <returns>A task representing any cleanup operations.</returns>
    Task CancelFetchDataAsync(string filePath, TransferKey transferKey);

    /// <summary>
    /// Called when a file is about to be dehydrated (local cache released).
    /// </summary>
    /// <param name="filePath">The full path of the file being dehydrated.</param>
    /// <returns>A task for any pre-dehydration operations (e.g., flush pending uploads).</returns>
    Task NotifyDehydrateAsync(string filePath);

    /// <summary>
    /// Called after a file has been successfully dehydrated.
    /// </summary>
    /// <param name="filePath">The full path of the dehydrated file.</param>
    /// <returns>A task for any post-dehydration cleanup.</returns>
    Task NotifyDehydrateCompletionAsync(string filePath);

    /// <summary>
    /// Called when a file under the sync root is deleted.
    /// </summary>
    /// <param name="filePath">The full path of the deleted file.</param>
    /// <returns>A task for propagating the deletion to the remote NAS.</returns>
    Task NotifyDeleteAsync(string filePath);

    /// <summary>
    /// Called when a file under the sync root is renamed or moved.
    /// </summary>
    /// <param name="sourcePath">The original path of the file.</param>
    /// <param name="destinationPath">The new path of the file.</param>
    /// <returns>A task for propagating the rename to the remote NAS.</returns>
    Task NotifyRenameAsync(string sourcePath, string destinationPath);

    /// <summary>
    /// Called when a file open operation completes.
    /// Useful for tracking file access patterns and triggering post-open sync.
    /// </summary>
    /// <param name="filePath">The full path of the file that was opened.</param>
    /// <returns>A task for any post-open operations.</returns>
    Task NotifyFileOpenCompletionAsync(string filePath);
}
