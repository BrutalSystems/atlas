namespace Atlas.Settings;

public class EmailSettings
{
    public string? FromName { get; set; }

    public string? FromEmail { get; set; }
    
    public string? SmtpServer { get; set; }

    public int? SmtpPort { get; set; } = 587;

    public string? SmtpUser { get; set; }

    public string? SmtpPassword { get; set; }

    public bool? SmtpUseSsl { get; set; } = true;  
}