namespace Atlas.Email.Models;

public class MailFolderStats
{
    public string FolderName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
}
