using Atlas.Email.Abstractions;
using Atlas.Email.Models;
using Atlas.Email.Security;
using Xunit;

namespace Atlas.Tests.Email;

/// <summary>
/// Covers the ownership guard used by the OAuth controllers before they will accept a
/// caller-supplied accountId (BrutalSystems/sift#5).
///
/// The rule under test is that a null or empty value on EITHER side of EITHER comparison is a
/// REJECTION, never a match. That is not defensive padding: UserContext.UserId is null on an
/// otherwise-authenticated request whenever Brokenhip cannot resolve the user
/// (UserIdMiddleware adds the userId claim only when resolution succeeds, then continues), and
/// BaseDbContext's query filter degrades to allow-all on exactly the same input
/// (`GetUserId() == null || e.UserId == GetUserId()`). A `null == null` match here would make
/// this guard vanish precisely when it is the only one left standing.
/// </summary>
public class MailAccountOwnershipTests
{
    private static IMailAccountRecord Record(string? tenantId, string? userId) =>
        new FakeRecord { Id = "acct-1", TenantId = tenantId, UserId = userId };

    [Fact]
    public void Accepts_a_record_owned_by_the_caller()
    {
        Assert.True(MailAccountOwnership.IsOwnedBy(Record("t1", "u1"), "t1", "u1"));
    }

    [Fact]
    public void Rejects_a_record_in_another_tenant()
    {
        Assert.False(MailAccountOwnership.IsOwnedBy(Record("t2", "u1"), "t1", "u1"));
    }

    [Fact]
    public void Rejects_a_record_owned_by_another_user_in_the_same_tenant()
    {
        Assert.False(MailAccountOwnership.IsOwnedBy(Record("t1", "u2"), "t1", "u1"));
    }

    [Fact]
    public void Rejects_a_missing_record()
    {
        Assert.False(MailAccountOwnership.IsOwnedBy(null, "t1", "u1"));
    }

    // The core of sift#5: every way a null can appear must fail closed.
    [Theory]
    [InlineData(null, "u1", "t1", "u1")]   // record has no tenant
    [InlineData("t1", null, "t1", "u1")]   // record has no user
    [InlineData("t1", "u1", null, "u1")]   // caller has no tenant
    [InlineData("t1", "u1", "t1", null)]   // caller has no user  <-- the Brokenhip-outage case
    [InlineData(null, null, null, null)]   // null == null must NOT be a match
    [InlineData("", "u1", "t1", "u1")]
    [InlineData("t1", "", "t1", "u1")]
    [InlineData("t1", "u1", "", "u1")]
    [InlineData("t1", "u1", "t1", "")]
    public void Rejects_when_any_side_is_null_or_empty(
        string? recordTenant, string? recordUser, string? callerTenant, string? callerUser)
    {
        Assert.False(MailAccountOwnership.IsOwnedBy(
            Record(recordTenant, recordUser), callerTenant, callerUser));
    }

    [Fact]
    public void Comparison_is_ordinal_and_case_sensitive()
    {
        Assert.False(MailAccountOwnership.IsOwnedBy(Record("T1", "U1"), "t1", "u1"));
    }

    private sealed class FakeRecord : IMailAccountRecord
    {
        public string? Id { get; set; }
        public string? TenantId { get; set; }
        public string Email { get; set; } = string.Empty;
        public MailProviderType ProviderType { get; set; }
        public string? EncryptedSettings { get; set; }
        public bool IsActive { get; set; }
        public string? Name { get; set; }
        public string? UserId { get; set; }
    }
}
