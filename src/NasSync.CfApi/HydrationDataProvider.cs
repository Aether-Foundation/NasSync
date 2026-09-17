using System.Runtime.InteropServices;
using NasSync.CfApi.Interop;

namespace NasSync.CfApi;

/// <summary>
/// Delivers downloaded file data back to the Cloud Filter platform via <c>CfExecute</c>.
/// Used inside FETCH_DATA callbacks: the sync engine downloads the requested range from
/// the NAS adapter and hands the bytes to this provider, which builds the native
/// <c>CF_OPERATION_INFO</c> / <c>CF_OPERATION_PARAMETERS</c> pair and executes the
/// TRANSFER_DATA operation.
///
/// <para>
/// This class is internal because it works directly with native transfer keys and
/// operation structures. External consumers use <see cref="ISyncEngine"/>.
/// </para>
/// </summary>
internal sealed class HydrationDataProvider
{
    /// <summary>NTSTATUS success value (STATUS_SUCCESS).</summary>
    private const int STATUS_SUCCESS = 0;

    /// <summary>NTSTATUS for "object name not found" — used when the remote file is missing.</summary>
    private const int STATUS_OBJECT_NAME_NOT_FOUND = unchecked((int)0xC0000034);

    /// <summary>
    /// Provides a chunk of data to satisfy a pending FETCH_DATA callback.
    /// For progressive hydration this may be called once per requested range.
    /// </summary>
    /// <param name="request">The fetch request captured from the callback info.</param>
    /// <param name="data">Buffer containing the downloaded bytes.</param>
    /// <param name="offset">Byte offset within the file where <paramref name="data"/> belongs.</param>
    /// <exception cref="CfApiException">Thrown if the native CfExecute call fails.</exception>
    internal unsafe void ProvideData(FetchDataRequest request, ReadOnlySpan<byte> data, long offset)
    {
        var opInfo = BuildOperationInfo(request);

        var opParams = new CfNativeTypes.CF_OPERATION_PARAMETERS_TRANSFER_DATA
        {
            ParamSize = (uint)sizeof(CfNativeTypes.CF_OPERATION_PARAMETERS_TRANSFER_DATA),
            Flags = 0,
            CompletionStatus = STATUS_SUCCESS,
            Offset = offset,
            Length = data.Length,
        };

        fixed (byte* bufferPtr = data)
        {
            opParams.Buffer = (IntPtr)bufferPtr;

            int hr = CfNativeMethods.CfExecute(ref opInfo, &opParams);

            if (hr != 0)
            {
                throw new CfApiException("CfExecute", hr,
                    $"Failed to transfer {data.Length} bytes at offset {offset} for '{request.FilePath}'.");
            }
        }
    }

    /// <summary>
    /// Reports an error to the platform when a FETCH_DATA callback cannot be satisfied
    /// (e.g., the NAS is unreachable or the file no longer exists on the remote).
    /// The platform fails the application's I/O with the mapped error.
    /// </summary>
    /// <param name="request">The fetch request captured from the callback info.</param>
    /// <param name="ntStatus">NTSTATUS to report; defaults to STATUS_OBJECT_NAME_NOT_FOUND.</param>
    internal unsafe void ReportError(FetchDataRequest request, int ntStatus = STATUS_OBJECT_NAME_NOT_FOUND)
    {
        var opInfo = BuildOperationInfo(request);

        var opParams = new CfNativeTypes.CF_OPERATION_PARAMETERS_TRANSFER_DATA
        {
            ParamSize = (uint)sizeof(CfNativeTypes.CF_OPERATION_PARAMETERS_TRANSFER_DATA),
            Flags = 0,
            CompletionStatus = ntStatus,
            Buffer = IntPtr.Zero,
            Offset = 0,
            Length = 0,
        };

        // Best-effort — the platform completes the app's I/O with an error.
        _ = CfNativeMethods.CfExecute(ref opInfo, &opParams);
    }

    /// <summary>
    /// Reports transfer progress to the Shell so File Explorer can display download UI.
    /// Non-fatal: failures are ignored.
    /// </summary>
    /// <param name="request">The fetch request whose progress is being reported.</param>
    /// <param name="total">Total bytes expected for the transfer.</param>
    /// <param name="completed">Bytes completed so far.</param>
    internal void ReportProgress(FetchDataRequest request, long total, long completed)
    {
        int hr = CfNativeMethods.CfReportProviderProgress(
            request.ConnectionKey,
            request.TransferKey,
            total,
            Math.Clamp(completed, 0, total));

        _ = hr; // Progress reporting is best-effort.
    }

    /// <summary>
    /// Builds the <c>CF_OPERATION_INFO</c> header for a TRANSFER_DATA operation,
    /// echoing back the opaque keys captured from the FETCH_DATA callback.
    /// </summary>
    private static unsafe CfNativeTypes.CF_OPERATION_INFO BuildOperationInfo(FetchDataRequest request)
    {
        return new CfNativeTypes.CF_OPERATION_INFO
        {
            StructSize = (uint)sizeof(CfNativeTypes.CF_OPERATION_INFO),
            Type = CfNativeTypes.CF_OPERATION_TYPE.TRANSFER_DATA,
            ConnectionKey = request.ConnectionKey,
            TransferKey = request.TransferKey,
            CorrelationVector = new IntPtr(request.CorrelationVector),
            SyncStatus = IntPtr.Zero,
            RequestKey = request.RequestKey,
        };
    }

    /// <summary>
    /// Reads a PCWSTR (NUL-terminated wide string) from an unmanaged pointer.
    /// Returns null for a null pointer. Used to decode VolumeDosName / NormalizedPath /
    /// TargetPath fields of native callback structures.
    /// </summary>
    /// <param name="ptr">Pointer to the wide string, possibly <see cref="IntPtr.Zero"/>.</param>
    /// <returns>The decoded string, or null.</returns>
    internal static string? ReadPcwstr(IntPtr ptr)
    {
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUni(ptr);
    }
}
