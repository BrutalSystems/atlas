using System.Text.Json.Serialization;
using Atlas.Models;

namespace Atlas.Email.Models;

/// <summary>
/// Represents a mail message for processing. Not persisted directly — used as an in-memory DTO.
/// </summary>
public class MailMessage : BaseModel
{
    public string MessageId { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;

    public string FromDomain { get; set; } = string.Empty;

    public List<string> To { get; set; } = new();

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool HasAttachments { get; set; }

    public DateTimeOffset ReceivedDate { get; set; }

    public bool IsRead { get; set; }

    public List<string> Flags { get; set; } = [];

    public string? SourceLink { get; set; }

    public List<EmailFolder> Folders { get; set; } = [];
}

public class EmailFolder
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public object? ProviderFolder { get; set; }
}
