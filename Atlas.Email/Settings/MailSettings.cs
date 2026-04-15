using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

    public static string Encrypt(string plainText, string? encryptionKey = null)
    {
        var key = DeriveKey(encryptionKey);

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
        var key = DeriveKey(encryptionKey);
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

    private static byte[] DeriveKey(string? encryptionKey)
    {
        var keySource = encryptionKey ?? "SiftDefaultEncryptionKey2024!ChangeInProduction";

        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(keySource));
    }
}
