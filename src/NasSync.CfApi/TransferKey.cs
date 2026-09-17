namespace NasSync.CfApi;

/// <summary>
/// Managed wrapper for the native <c>CF_TRANSFER_KEY</c> structure.
/// Identifies a pending data transfer request from the platform.
/// Used with <see cref="HydrationDataProvider"/> to deliver data via <c>CfExecute</c>.
/// </summary>
/// <param name="Value">The internal transfer key value from the native API.</param>
public readonly record struct TransferKey(long Value)
{
    /// <summary>
    /// Gets whether this transfer key represents a valid (non-empty) transfer.
    /// </summary>
    public bool IsValid => Value != 0;

    /// <summary>
    /// An empty/invalid transfer key sentinel value.
    /// </summary>
    public static readonly TransferKey Empty = new(0);
}
