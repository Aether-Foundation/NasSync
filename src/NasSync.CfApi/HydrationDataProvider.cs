using NasSync.CfApi.Interop;

namespace NasSync.CfApi;

/// <summary>
/// Provides data transfer operations for hydrating placeholder files.
/// Used within FETCH_DATA callbacks to deliver downloaded bytes from the NAS
/// to the cldflt.sys minifilter driver via the CfExecute function.
///
/// <para>
/// This class is internal because it works directly with native transfer keys
/// and callback structures. The sync engine uses it internally during callback
/// processing. External consumers should use the <see cref="ISyncEngine"/> interface.
/// </para>
/// </summary>
internal sealed class HydrationDataProvider
{
    /// <summary>
    /// Provides a chunk of data to satisfy a pending FETCH_DATA callback.
    /// This is the primary mechanism for delivering downloaded file data to the platform.
    ///
    /// <para>
    /// For progressive hydration, this method may be called multiple times for the same file
    /// with different offset/length pairs as the application reads through the file.
    /// </para>
    /// </summary>
    /// <param name="volumeGuidName">Volume GUID from the callback info.</param>
    /// <param name="fileId">File system file ID from the callback info.</param>
    /// <param name="transferKey">Transfer key from the callback info.</param>
    /// <param name="data">Buffer containing the downloaded data bytes.</param>
    /// <param name="offset">Byte offset within the file where this data belongs.</param>
    /// <returns>A task representing the asynchronous data delivery operation.</returns>
    /// <exception cref="CfApiException">Thrown if the native CfExecute call fails.</exception>
    internal unsafe Task ProvideDataAsync(
        Guid volumeGuidName,
        long fileId,
        CfNativeTypes.CF_TRANSFER_KEY transferKey,
        ReadOnlyMemory<byte> data,
        long offset)
    {
        fixed (byte* bufferPtr = data.Span)
        {
            int hr = CfNativeMethods.CfExecute(
                ref volumeGuidName,
                ref fileId,
                ref transferKey,
                bufferPtr,
                data.Length,
                offset,
                CfNativeTypes.CF_EXECUTE_FLAGS.NONE,
                out long usn);

            if (hr != 0)
            {
                throw new CfApiException("CfExecute", hr,
                    $"Failed to provide {data.Length} bytes at offset {offset} for file ID {fileId}.");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports an error to the platform when a FETCH_DATA callback cannot be satisfied.
    /// Use this when the NAS is unreachable or the file no longer exists on the remote.
    /// </summary>
    /// <param name="volumeGuidName">Volume GUID from the callback info.</param>
    /// <param name="fileId">File system file ID from the callback info.</param>
    /// <param name="transferKey">Transfer key from the callback info.</param>
    /// <param name="errorCode">The Win32 error code to report (e.g., ERROR_FILE_NOT_FOUND).</param>
    internal void ReportError(
        Guid volumeGuidName,
        long fileId,
        CfNativeTypes.CF_TRANSFER_KEY transferKey,
        int errorCode)
    {
        // CfExecute with a null buffer signals failure to the platform
        unsafe
        {
            CfNativeMethods.CfExecute(
                ref volumeGuidName,
                ref fileId,
                ref transferKey,
                null,
                0,
                0,
                CfNativeTypes.CF_EXECUTE_FLAGS.NONE,
                out long usn);
        }
    }

    /// <summary>
    /// Extracts the transfer key from a FETCH_DATA callback for use with ProvideDataAsync.
    /// </summary>
    /// <param name="callbackInfoPtr">Pointer to the native CF_CALLBACK structure.</param>
    /// <returns>The transfer key for the pending transfer.</returns>
    /// <exception cref="CfApiException">Thrown if the native CfGetTransferKey call fails.</exception>
    internal static unsafe CfNativeTypes.CF_TRANSFER_KEY GetTransferKey(
        CfNativeTypes.CF_CALLBACK* callbackInfoPtr)
    {
        int hr = CfNativeMethods.CfGetTransferKey(callbackInfoPtr, out CfNativeTypes.CF_TRANSFER_KEY key);

        if (hr != 0)
        {
            throw new CfApiException("CfGetTransferKey", hr,
                "Failed to extract transfer key from callback info.");
        }

        return key;
    }

    /// <summary>
    /// Releases transfer data associated with a completed or cancelled transfer.
    /// </summary>
    /// <param name="volumeGuidName">Volume GUID from the callback info.</param>
    /// <param name="fileId">File system file ID from the callback info.</param>
    /// <param name="transferKey">The transfer key to release.</param>
    internal void ReleaseTransferData(
        Guid volumeGuidName,
        long fileId,
        CfNativeTypes.CF_TRANSFER_KEY transferKey)
    {
        CfNativeMethods.CfReleaseTransferData(
            ref volumeGuidName,
            ref fileId,
            ref transferKey);
    }
}
