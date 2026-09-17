namespace NasSync.CfApi;

/// <summary>
/// Captures everything needed to satisfy a FETCH_DATA callback via <c>CfExecute</c>.
///
/// <para>
/// When the platform invokes FETCH_DATA it supplies a <c>CF_CALLBACK_INFO</c> carrying
/// an opaque connection key, transfer key, and request key. To return data the provider
/// must echo all three back inside a <c>CF_OPERATION_INFO</c>. This immutable record
/// bundles them (plus the requested byte range and the resolved local path) so the sync
/// engine can hand them to <see cref="HydrationDataProvider"/> without poking at native
/// structs.
/// </para>
/// </summary>
/// <param name="FilePath">Normalized full local path of the placeholder being hydrated.</param>
/// <param name="RequiredOffset">First byte offset the application requires.</param>
/// <param name="RequiredLength">Number of required bytes starting at <paramref name="RequiredOffset"/>.</param>
/// <param name="OptionalOffset">First byte offset of optional (speculative) data, or -1 if none.</param>
/// <param name="OptionalLength">Number of optional bytes, or 0 if none.</param>
/// <param name="ConnectionKey">Opaque CF_CONNECTION_KEY echoed back into CfExecute.</param>
/// <param name="TransferKey">Opaque CF_TRANSFER_KEY echoed back into CfExecute.</param>
/// <param name="RequestKey">Opaque CF_REQUEST_KEY echoed back into CfExecute.</param>
/// <param name="CorrelationVector">Pointer to the correlation vector (opaque, passed through), or 0.</param>
public readonly record struct FetchDataRequest(
    string FilePath,
    long RequiredOffset,
    long RequiredLength,
    long OptionalOffset,
    long OptionalLength,
    long ConnectionKey,
    long TransferKey,
    long RequestKey,
    long CorrelationVector);
