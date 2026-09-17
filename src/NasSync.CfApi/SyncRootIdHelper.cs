using System.Security.Principal;

namespace NasSync.CfApi;

/// <summary>
/// Shared helper for constructing sync root IDs in the standard format
/// expected by the Windows Cloud Files platform.
///
/// <para>
/// Format: <c>[ProviderId]![UserSid]![AccountId]</c>
/// </para>
///
/// <para>
/// Both <see cref="CfSyncRootManager"/> and <see cref="WinRtRegistration"/>
/// use this helper to ensure consistent sync root IDs across native and WinRT registration.
/// </para>
/// </summary>
internal static class SyncRootIdHelper
{
    /// <summary>
    /// Builds a sync root ID from provider and account identifiers.
    /// </summary>
    /// <param name="providerId">The storage provider identifier (e.g., "NasSync").</param>
    /// <param name="accountId">The account identifier for this NAS connection.</param>
    /// <returns>A sync root ID in the format <c>[ProviderId]![UserSid]![AccountId]</c>.</returns>
    public static string Build(string providerId, string accountId)
    {
        string userSid = WindowsIdentity.GetCurrent().User?.Value ?? "S-1-5-0";
        return $"{providerId}!{userSid}!{accountId}";
    }
}
