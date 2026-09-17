using NasSync.CfApi.Interop;

namespace NasSync.CfApi;

/// <summary>
/// Provides data transfer operations for hydrating placeholder files.
/// Used within FETCH_DATA callbacks to deliver downloaded bytes from the NAS
/// to the cldflt.sys minifilter driver via the CfExecute function.
///
/// <para>
/// Typical flow:
/// 1. Platform invokes FETCH_DATA callback with offset, length, and transfer key
/// 2. Sync engine downloads the requested byte range from NAS
/// 3. <c>ProvideData</c> delivers the bytes to the platform via CfExecute
/// 4. Platform hydrates the placeholder with the provided data
/// </para>
/// </summary>
public sealed class HydrationDataProvider
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
    public unsafe Task ProvideDataAsync(
        Guid volumeGuidName,
        long fileId,
        CfNativeTypes.CF_TRANSFER_KEY transferKey,
        ReadOnlyMemory<byte> data,
        long offset)
    {
        fixed (byte* bufferPtr = data.Span)
        {
            int hr = CfNativeMethods.CfExecute(
                in volumeGuidName,
                in fileId,
                in transferKey,
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
    public void ReportError(
        Guid volumeGuidName,
        long fileId,
        CfNativeTypes.CF_TRANSFER_KEY transferKey,
        int errorCode)
    {
        // CfExecute with a null buffer and specific error code signals failure to the platform
        unsafe
        {
            CfNativeMethods.CfExecute(
                in volumeGuidName,
                in fileId,
                in transferKey,
                null, // null buffer indicates error
                0,
                0,
                CfNativeTypes.CF_EXECUTE_FLAGS.NONE,
                out long usn);
        }
    }

    /// <summary>
    /// Extracts the transfer key from a FETCH_DATA callback for use with ProvideDataAsync.
    /// The transfer key uniquely identifies the pending data transfer request.
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
    /// Must be called after ProvideDataAsync completes or when a fetch is cancelled
    /// to free platform resources associated with the transfer.
    /// </summary>
    /// <param name="volumeGuidName">Volume GUID from the callback info.</param>
    /// <param name="fileId">File system file ID from the callback info.</param>
    /// <param name="transferKey">The transfer key to release.</param>
    public void ReleaseTransferData(
        Guid volumeGuidName,
        long fileId,
        CfNativeTypes.CF_TRANSFER_KEY transferKey)
    {
        int hr = CfNativeMethods.CfReleaseTransferData(
            in volumeGuidName,
            in fileId,
            in transferKey);

        // Release failures are non-fatal
        _ = hr;
    }
}
