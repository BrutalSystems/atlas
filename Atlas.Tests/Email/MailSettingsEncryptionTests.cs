using System.Security.Cryptography;
using Atlas.Email.Settings;
using Xunit;

namespace Atlas.Tests.Email;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MailEncryptionCollection
{
    public const string Name = "mail-encryption";
}

/// <summary>Covers the configured mail encryption key (MailEncryption:Key) and the legacy built-in
/// key it replaces. The key override is static, so this class runs alone and resets it after each
/// test.
///
/// The test host runs as Development (ASPNETCORE_ENVIRONMENT unset), where an unset key falls back
/// to the legacy key. "No key in Production throws" can't be exercised here, because Env's
/// environment is fixed for the process.</summary>
[Collection(MailEncryptionCollection.Name)]
public sealed class MailSettingsEncryptionTests : IDisposable
{
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public void Dispose() => MailSettings.ConfigureEncryption(null);

    [Fact]
    public void ConfiguredKey_RoundTrips_AndMarksTheValue()
    {
        MailSettings.ConfigureEncryption(Key);

        var cipher = MailSettings.Encrypt("secret");

        Assert.StartsWith("v2:", cipher);
        Assert.Equal("secret", MailSettings.Decrypt(cipher));
    }

    [Fact]
    public void Settings_RoundTripThroughEncryptedJson_WithAConfiguredKey()
    {
        MailSettings.ConfigureEncryption(Key);
        var settings = new GmailApiSettings { Username = "a@example.com", ClientId = "id", ClientSecret = "shh" };

        var restored = Assert.IsType<GmailApiSettings>(MailSettings.FromEncryptedJson(settings.ToEncryptedJson()));

        Assert.Equal("a@example.com", restored.Username);
        Assert.Equal("shh", restored.ClientSecret);
    }

    [Fact]
    public void WithoutAKey_Development_UsesTheLegacyFormat()
    {
        // Unchanged behaviour for local dev databases and test suites with no key configured.
        var cipher = MailSettings.Encrypt("secret");

        Assert.DoesNotContain(":", cipher);
        Assert.Equal("secret", MailSettings.Decrypt(cipher));
    }

    [Fact]
    public void LegacyValue_IsRefused_OnceAKeyIsConfigured()
    {
        var legacy = MailSettings.Encrypt("secret");   // no key configured yet: legacy format
        MailSettings.ConfigureEncryption(Key);

        var ex = Assert.Throws<InvalidOperationException>(() => MailSettings.Decrypt(legacy));
        Assert.Contains(MailSettings.AllowLegacyKeySetting, ex.Message);
    }

    [Fact]
    public void LegacyValue_IsReadable_WhenMigrating_AndReEncryptsAsV2()
    {
        var legacy = MailSettings.Encrypt("secret");
        MailSettings.ConfigureEncryption(Key, allowLegacyKey: true);

        var plain = MailSettings.Decrypt(legacy);
        var reEncrypted = MailSettings.Encrypt(plain);

        Assert.Equal("secret", plain);
        Assert.StartsWith("v2:", reEncrypted);
        Assert.Equal("secret", MailSettings.Decrypt(reEncrypted));
    }

    [Fact]
    public void V2Value_IsRefused_WhenNoKeyIsConfigured()
    {
        MailSettings.ConfigureEncryption(Key);
        var cipher = MailSettings.Encrypt("secret");
        MailSettings.ConfigureEncryption(null);

        Assert.Throws<InvalidOperationException>(() => MailSettings.Decrypt(cipher));
    }

    [Fact]
    public void AShortKey_IsRejected()
    {
        MailSettings.ConfigureEncryption("too-short");

        Assert.Throws<InvalidOperationException>(() => MailSettings.Encrypt("secret"));
        Assert.Throws<InvalidOperationException>(MailSettings.EnsureEncryptionKeyConfigured);
    }

    [Fact]
    public void AnExplicitKey_StillWorks_RegardlessOfConfiguration()
    {
        MailSettings.ConfigureEncryption(Key);
        var explicitKey = "an explicit key passed by the caller";

        var cipher = MailSettings.Encrypt("secret", explicitKey);

        Assert.Equal("secret", MailSettings.Decrypt(cipher, explicitKey));
    }
}
