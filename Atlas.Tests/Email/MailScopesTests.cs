using Atlas.Email.Settings;
using Xunit;

namespace Atlas.Tests.Email;

/// <summary>
/// Contacts are an opt-in on top of the mail scopes, and whether a mailbox has them is read from the scopes the
/// provider actually granted, not from what was asked for: Google lets a person untick a scope on its screen.
/// </summary>
[Collection(MailEncryptionCollection.Name)]
public sealed class MailScopesTests : IDisposable
{
    public void Dispose() => MailSettings.ConfigureEncryption(null);

    private static readonly string[] GoogleContactScopes = MailScopes.GoogleContacts.Split(' ');

    [Fact]
    public void Mail_only_unless_contacts_are_asked_for()
    {
        Assert.Equal(MailScopes.GoogleMail, MailScopes.Google(false));
        Assert.Equal(MailScopes.MicrosoftMail, MailScopes.Microsoft(false));
        Assert.EndsWith(MailScopes.GoogleContacts, MailScopes.Google(true));
        Assert.EndsWith(MailScopes.MicrosoftContacts, MailScopes.Microsoft(true));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("contacts", 1)]
    [InlineData(" contacts , contacts ", 1)]
    public void Parses_known_features(string? features, int count) =>
        Assert.Equal(count, MailScopes.ParseFeatures(features)!.Count);

    [Theory]
    [InlineData("calendar")]
    [InlineData("contacts,calendar")]
    [InlineData("Contacts")]
    public void Refuses_unknown_features(string features) => Assert.Null(MailScopes.ParseFeatures(features));

    [Fact]
    public void Google_needs_every_contacts_scope()
    {
        var all = new GmailApiSettings { GrantedScopes = $"{MailScopes.GoogleMail} {MailScopes.GoogleContacts}" };
        var some = new GmailApiSettings { GrantedScopes = $"{MailScopes.GoogleMail} {GoogleContactScopes[0]}" };

        Assert.True(MailScopes.HasContacts(all));
        Assert.False(MailScopes.HasContacts(some));
    }

    [Theory]
    [InlineData("Contacts.Read Mail.Read User.Read")]
    [InlineData("https://graph.microsoft.com/Contacts.Read https://graph.microsoft.com/Mail.Read")]
    [InlineData("contacts.read mail.read")]
    public void Microsoft_contacts_in_any_form(string granted) =>
        Assert.True(MailScopes.HasContacts(new OutlookSettings { GrantedScopes = granted }));

    [Fact]
    public void Unknown_or_imap_has_no_contacts()
    {
        Assert.False(MailScopes.HasContacts(new GmailApiSettings()));
        Assert.False(MailScopes.HasContacts(new OutlookSettings { GrantedScopes = MailScopes.MicrosoftMail }));
        Assert.False(MailScopes.HasContacts(new ImapSettings()));
        Assert.False(MailScopes.HasContacts(null));
    }

    [Fact]
    public void Granted_scopes_survive_encryption()
    {
        MailSettings.ConfigureEncryption("a-test-key-that-is-at-least-32-characters-long", allowLegacyKey: false);
        var settings = new GmailApiSettings { ClientId = "c", AccessToken = "a", GrantedScopes = MailScopes.Google(true) };

        var back = MailSettings.FromEncryptedJson(settings.ToEncryptedJson());

        Assert.True(MailScopes.HasContacts(back));
    }
}
