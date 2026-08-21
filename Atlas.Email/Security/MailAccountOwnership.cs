using System.Diagnostics.CodeAnalysis;
using Atlas.Email.Abstractions;

namespace Atlas.Email.Security;

/// <summary>
/// Decides whether a caller-supplied mail account id may be acted on by the current caller.
///
/// The OAuth controllers accept an accountId from the request and later overwrite that account's
/// stored credentials. Nothing used to verify the caller owned it (BrutalSystems/sift#5), which
/// made a cross-tenant account takeover reachable through the anonymous callback.
/// </summary>
internal static class MailAccountOwnership
{
    /// <summary>
    /// True only when <paramref name="record"/> demonstrably belongs to the caller identified by
    /// <paramref name="callerTenantId"/> and <paramref name="callerUserId"/>.
    ///
    /// A null or empty value on EITHER side of EITHER comparison is a rejection, never a match.
    /// This is the load-bearing part. Both ambient guards in the stack fail OPEN on a null:
    /// BaseDbContext's query filter is `(GetUserId() == null || e.UserId == GetUserId())`, and
    /// consumers' UserHasAccess overrides return true when UserContext.TenantId is null. And null
    /// is reachable on an authenticated request -- sift's UserIdMiddleware adds the `userId` claim
    /// only when Brokenhip resolves the user, and otherwise lets the request continue. Treating
    /// null == null as a match would make this check disappear in exactly the conditions where it
    /// is the only guard still standing.
    /// </summary>
    /// <remarks>
    /// <c>[NotNullWhen(true)]</c> lets callers dereference the record after a successful check
    /// without a redundant null test -- this guard replaced the <c>if (account == null)</c> the
    /// compiler's flow analysis previously relied on.
    /// </remarks>
    public static bool IsOwnedBy(
        [NotNullWhen(true)] IMailAccountRecord? record, string? callerTenantId, string? callerUserId)
    {
        if (record is null)
            return false;

        if (string.IsNullOrEmpty(callerTenantId) || string.IsNullOrEmpty(callerUserId))
            return false;

        if (string.IsNullOrEmpty(record.TenantId) || string.IsNullOrEmpty(record.UserId))
            return false;

        return string.Equals(record.TenantId, callerTenantId, StringComparison.Ordinal)
            && string.Equals(record.UserId, callerUserId, StringComparison.Ordinal);
    }
}
