using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Helpers;

namespace Atlas.Email.Settings;

/// <summary>
/// Base class for mail provider settings.
/// </summary>
public abstract class MailSettings
{
    /// <summary>
    /// Gets or sets the tenant ID this mail account belongs to.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the account ID this mail account belongs to.
    /// </summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username for authentication.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the encrypted password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The OAuth scopes the provider granted, space-separated, as its token response gave them. Null for IMAP and
    /// for accounts connected before Atlas recorded it (those have the mail scopes only). See <see cref="MailScopes"/>.
    /// </summary>
    public string? GrantedScopes { get; set; }

    /// <summary>
    /// Deserializes and decrypts mail settings from encrypted JSON.
    /// </summary>
    public static MailSettings FromEncryptedJson(string encryptedJson, string? encryptionKey = null)
    {
        if (string.IsNullOrWhiteSpace(encryptedJson))
        {
            throw new ArgumentException("Encrypted JSON cannot be null or empty.", nameof(encryptedJson));
        }

        var decryptedJson = Decrypt(encryptedJson, encryptionKey);

        using var jsonDoc = JsonDocument.Parse(decryptedJson);
        var root = jsonDoc.RootElement;

        if (root.TryGetProperty("$type", out var typeProperty))
        {
            var typeName = typeProperty.GetString();

            if (typeName?.Contains("ImapSettings") == true)
            {
                return JsonSerializer.Deserialize<ImapSettings>(decryptedJson)
                       ?? throw new InvalidOperationException("Failed to deserialize ImapSettings.");
            }
            else if (typeName?.Contains("GmailApiSettings") == true)
            {
                return JsonSerializer.Deserialize<GmailApiSettings>(decryptedJson)
                       ?? throw new InvalidOperationException("Failed to deserialize GmailApiSettings.");
            }
            else if (typeName?.Contains("OutlookSettings") == true)
            {
                return JsonSerializer.Deserialize<OutlookSettings>(decryptedJson)
                       ?? throw new InvalidOperationException("Failed to deserialize OutlookSettings.");
            }
            else if (typeName?.Contains("GoogleSettings") == true)
            {
                return JsonSerializer.Deserialize<GmailApiSettings>(decryptedJson)
                       ?? throw new InvalidOperationException("Failed to deserialize GmailApiSettings.");
            }
        }

        // Try to infer type from properties
        if (root.TryGetProperty("Server", out _))
        {
            return JsonSerializer.Deserialize<ImapSettings>(decryptedJson)
                   ?? throw new InvalidOperationException("Failed to deserialize ImapSettings.");
        }
        else if (root.TryGetProperty("AccessToken", out _) && root.TryGetProperty("TenantId", out _))
        {
            // Outlook has AccessToken + TenantId
            return JsonSerializer.Deserialize<OutlookSettings>(decryptedJson)
                   ?? throw new InvalidOperationException("Failed to deserialize OutlookSettings.");
        }
        else if (root.TryGetProperty("ClientId", out _) && root.TryGetProperty("AccessToken", out _))
        {
            // Gmail has ClientId + AccessToken (but no TenantId)
            return JsonSerializer.Deserialize<GmailApiSettings>(decryptedJson)
                   ?? throw new InvalidOperationException("Failed to deserialize GmailApiSettings.");
        }

        throw new InvalidOperationException("Unable to determine mail settings type from encrypted JSON.");
    }

    /// <summary>
    /// Serializes and encrypts the mail settings to an encrypted JSON string.
    /// </summary>
    public string ToEncryptedJson(string? encryptionKey = null)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        var json = JsonSerializer.Serialize(this, this.GetType(), options);

        using var jsonDoc = JsonDocument.Parse(json);
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("$type", this.GetType().Name);

            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        var jsonWithType = Encoding.UTF8.GetString(stream.ToArray());
        return Encrypt(jsonWithType, encryptionKey);
    }

    /// <summary>Marks a value encrypted with a configured key. Values without it were written with
    /// the legacy built-in key. ':' never occurs in base64, so the two formats can't be confused.</summary>
    private const string ConfiguredKeyPrefix = "v2:";

    public static string Encrypt(string plainText, string? encryptionKey = null)
    {
        if (encryptionKey != null) return EncryptWith(plainText, DeriveKey(encryptionKey));

        var source = ResolveKeySource();
        var cipherText = EncryptWith(plainText, DeriveKey(source));
        return source == LegacyKey ? cipherText : ConfiguredKeyPrefix + cipherText;
    }

    private static string EncryptWith(string plainText, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var msEncrypt = new System.IO.MemoryStream();

        msEncrypt.Write(aes.IV, 0, aes.IV.Length);

        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        using (var swEncrypt = new System.IO.StreamWriter(csEncrypt))
        {
            swEncrypt.Write(plainText);
        }

        return Convert.ToBase64String(msEncrypt.ToArray());
    }

    public static string Decrypt(string cipherText, string? encryptionKey = null)
    {
        var configuredFormat = cipherText.StartsWith(ConfiguredKeyPrefix, StringComparison.Ordinal);
        var body = configuredFormat ? cipherText[ConfiguredKeyPrefix.Length..] : cipherText;

        if (encryptionKey != null) return DecryptWith(body, DeriveKey(encryptionKey));

        var source = ResolveKeySource();
        if (configuredFormat)
        {
            if (source == LegacyKey)
                throw new InvalidOperationException(
                    $"This value was encrypted with a configured key, but {KeySetting} is not set.");
            return DecryptWith(body, DeriveKey(source));
        }

        // Legacy format. Allowed where the legacy key is the current key (Development/Test without a
        // configured key), or when migrating. The caller's next save re-encrypts it as v2.
        if (source != LegacyKey && !AllowLegacyKey())
            throw new InvalidOperationException(
                $"This value was encrypted with the legacy built-in key. Set {AllowLegacyKeySetting}=true "
                + "to read it; it is re-encrypted with the configured key the next time it's saved.");
        return DecryptWith(body, DeriveKey(LegacyKey));
    }

    private static string DecryptWith(string cipherText, byte[] key)
    {
        var buffer = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = key;

        var iv = new byte[aes.IV.Length];
        Array.Copy(buffer, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var msDecrypt = new System.IO.MemoryStream(buffer, iv.Length, buffer.Length - iv.Length);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new System.IO.StreamReader(csDecrypt);

        return srDecrypt.ReadToEnd();
    }

    /// <summary>The configuration key holding the encryption key (env var <c>MailEncryption__Key</c>).
    /// Any random string of at least <see cref="MinimumKeyLength"/> characters; it is hashed to the
    /// 256-bit AES key. Changing it makes everything already stored unreadable, unless
    /// <see cref="AllowLegacyKeySetting"/> covers the old key.</summary>
    public const string KeySetting = "MailEncryption:Key";

    /// <summary>When true, <see cref="Decrypt"/> reads values written with the legacy built-in key (those
    /// without the <c>v2:</c> prefix) even though a key is configured. Callers write settings back
    /// after use, so each value is re-encrypted with the configured key the next time it's saved.
    /// Turn it off once that's done.</summary>
    public const string AllowLegacyKeySetting = "MailEncryption:AllowLegacyKey";

    public const int MinimumKeyLength = 32;

    /// <summary>The key every Atlas.Email value was encrypted with before a key could be configured.
    /// It is in this public source, so it protects nothing. Kept only so Development/Test keep
    /// working without configuration and so old values can be migrated (see
    /// <see cref="AllowLegacyKeySetting"/>).</summary>
    private const string LegacyKey = "SiftDefaultEncryptionKey2024!ChangeInProduction";

    private static string? _configuredKey;
    private static bool? _allowLegacyKey;

    /// <summary>Overrides the key read from configuration. For tests and for hosts that load the key
    /// from somewhere other than <see cref="Env"/>'s configuration. Null goes back to configuration.</summary>
    public static void ConfigureEncryption(string? key, bool? allowLegacyKey = null)
    {
        _configuredKey = key;
        _allowLegacyKey = allowLegacyKey;
    }

    /// <summary>Throws unless a usable key is configured, or this is Development/Test (which fall back
    /// to the legacy key). Call at startup so a missing key fails the deploy, not the first sync.</summary>
    public static void EnsureEncryptionKeyConfigured() => _ = ResolveKeySource();

    private static string ResolveKeySource()
    {
        var key = _configuredKey ?? Env.GetConfiguration()[KeySetting];
        if (!string.IsNullOrEmpty(key))
        {
            if (key.Length < MinimumKeyLength)
                throw new InvalidOperationException(
                    $"{KeySetting} must be at least {MinimumKeyLength} characters.");
            return key;
        }

        if (Env.IsDevelopment || Env.IsTest)
            return LegacyKey;

        throw new InvalidOperationException(
            $"{KeySetting} (env MailEncryption__Key) is not set. Atlas.Email will not encrypt mail "
            + "credentials with the built-in key outside Development/Test.");
    }

    private static bool AllowLegacyKey() =>
        _allowLegacyKey ?? string.Equals(Env.GetConfiguration()[AllowLegacyKeySetting], "true",
            StringComparison.OrdinalIgnoreCase);

    private static byte[] DeriveKey(string keySource) => SHA256.HashData(Encoding.UTF8.GetBytes(keySource));
}
