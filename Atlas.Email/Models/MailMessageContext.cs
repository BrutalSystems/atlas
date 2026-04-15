namespace Atlas.Email.Models;

public class MailMessageContext
{
    public MailMessageContext(string messageId)
    {
        MessageId = messageId;
    }

    public MailMessageContext(MailMessage mailMessage, object? sourceFolder, List<object?> otherFolders)
    {
        MailMessage = mailMessage;
        MessageId = mailMessage.Id ?? string.Empty;
        SourceFolder = sourceFolder;
        OtherFolders = otherFolders;
    }

    public string MessageId { get; set; } = string.Empty;

    public MailMessage? MailMessage { get; set; }

    public object? SourceFolder { get; set; }

    public List<object?> OtherFolders { get; set; } = [];
}
