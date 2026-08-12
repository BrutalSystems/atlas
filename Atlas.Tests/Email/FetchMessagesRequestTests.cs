using Atlas.Email.Models;
using Xunit;

namespace Atlas.Tests.Email;

/// <summary>
/// Covers the CollectMessages opt-out on FetchMessagesRequest (see FetchMessagesRequest.cs).
///
/// This does NOT exercise the provider-side guarding logic in GoogleMailProvider,
/// OutlookMailProvider, or ImapMailProvider -- those hard-code a live HttpClient/ImapClient with
/// no seam for injecting a fake transport, so driving FetchMessagesAsync in a unit test would mean
/// hitting real Gmail/Graph/IMAP endpoints. That behavioral coverage (filter runs for every
/// message; returned enumerable is empty when CollectMessages=false and fully populated at the
/// default) lives in sift's FilterEngineTests against a stub IMailProvider instead. See the report
/// for the tradeoff.
/// </summary>
public class FetchMessagesRequestTests
{
    [Fact]
    public void CollectMessages_defaults_to_true()
    {
        var request = new FetchMessagesRequest
        {
            Since = DateTimeOffset.UtcNow,
        };

        Assert.True(request.CollectMessages);
    }

    [Fact]
    public void CollectMessages_can_be_opted_out_via_object_initializer()
    {
        var request = new FetchMessagesRequest
        {
            Since = DateTimeOffset.UtcNow,
            CollectMessages = false,
        };

        Assert.False(request.CollectMessages);
    }
}
