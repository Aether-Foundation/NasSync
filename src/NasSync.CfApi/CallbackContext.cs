namespace NasSync.CfApi;

/// <summary>
/// State object passed as the context pointer to native CfAPI callbacks.
/// Pinned via <see cref="System.Runtime.InteropServices.GCHandle"/> to prevent
/// garbage collection while the sync root is connected.
///
/// <para>
/// The static callback dispatchers in <see cref="CfSyncRootManager"/> receive
/// this object via <c>GCHandle.FromIntPtr(contextPtr).Target</c> and use it
/// to access the callback handler, path resolver, and hydration provider.
/// </para>
/// </summary>
internal sealed class CallbackContext
{
    /// <summary>
    /// Gets the callback handler that processes sync events (hydration, deletion, rename, etc.).
    /// </summary>
    public required ICfCallbackHandler Handler { get; init; }

    /// <summary>
    /// Gets the path resolver that maps NTFS file IDs to file system paths.
    /// </summary>
    public required PathResolver PathResolver { get; init; }

    /// <summary>
    /// Gets the hydration data provider used to deliver file data to the platform via CfExecute.
    /// </summary>
    public required HydrationDataProvider HydrationProvider { get; init; }

    /// <summary>
    /// Gets the full path to the sync root directory.
    /// </summary>
    public required string SyncRootPath { get; init; }
}
