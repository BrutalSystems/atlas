namespace Atlas.Email.Models;

public class FetchMessagesRequest
{
    public DateTimeOffset Since { get; set; }
    public DateTimeOffset? Until { get; set; }
    public Func<MailMessage, int, int, Task<bool>>? Filter { get; set; }
    public int MaxCount { get; set; } = 1000;
    public string? Folder { get; set; }
}
